using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // Camera: view presets (top/front/side/isometric), world<->screen coordinate conversion
        // (Project/ScreenToWorld), zoom, hit-testing (what's under a given screen position), shape
        // geometry helpers, selection, and the interactive drawing/placing API.

        #region View presets

        /// <summary>Top view (X-Y plane) - default, suitable for 2D scenes.</summary>
        public void ShowTopView() => SetView(0f, 90f);

        /// <summary>Front view (X-Z plane, looking along the Y axis).</summary>
        public void ShowFrontView() => SetView(0f, 0f);

        /// <summary>Side view (Y-Z plane, looking along the X axis).</summary>
        public void ShowSideView() => SetView(90f, 0f);

        /// <summary>An angled "isometric" view - shows the shape in space best.</summary>
        public void ShowIsometricView() => SetView(35f, 25f);

        public void SetView(float yawDeg, float pitchDeg)
        {
            _yawDeg = yawDeg;
            _pitchDeg = ClampPitch(pitchDeg);
            Render();
        }

        #endregion

        #region Projection, hit-testing and selection

        /// <summary>Projects a world-space point to a screen position according to the current rotation/zoom/pan.</summary>
        public PointF Project(Vector3 world) => Project(world, out _);

        private PointF Project(Vector3 world, out float depth)
        {
            var rotated = RotateToView(world);
            depth = rotated.Y; // only for draw-order sorting (painter's algorithm), not real perspective

            float screenX = rotated.X * _pixelsPerMeter + _panOffsetPixels.X;
            float screenY = -rotated.Z * _pixelsPerMeter + _panOffsetPixels.Y;
            return new PointF(screenX, screenY);
        }

        /// <summary>Applies only the camera's rotation (yaw then pitch) to a world-space point, without zoom/pan.
        /// Shared by Project() and ZoomToFullExtent() - the "raw" X/Z here are exactly what Project() later
        /// scales by _pixelsPerMeter and offsets by _panOffsetPixels.</summary>
        private Vector3 RotateToView(Vector3 world)
        {
            var afterYaw = Vector3.Transform(world, Matrix4x4.CreateRotationZ(DegToRad(_yawDeg)));
            return Vector3.Transform(afterYaw, Matrix4x4.CreateRotationX(DegToRad(_pitchDeg)));
        }

        /// <summary>Finds the clickable point under the given screen position (within tolerance), or null.
        /// Points with Selectable = false are ignored - they can't be clicked or dragged.</summary>
        public Point3D? HitTest(PointF screenPos) => FindNearest(screenPos, p => p.Selectable);

        /// <summary>Like HitTest, but respects HoverEnabled instead of Selectable - used for mouse hover,
        /// so even a non-clickable ("read-only") point can show a hover effect if its HoverEnabled allows it.</summary>
        private Point3D? HitTestForHover(PointF screenPos) => FindNearest(screenPos, p => p.HoverEnabled);

        private Point3D? FindNearest(PointF screenPos, Func<Point3D, bool> filter)
        {
            Point3D? best = null;
            float bestDist = float.MaxValue;

            foreach (var p in Points)
            {
                if (!filter(p)) continue;

                float radius = p.Size ?? PointScreenRadius;
                float tolerance = radius + 3f;

                var screen = Project(p.World);
                float dist = (float)Math.Sqrt(
                    Math.Pow(screen.X - screenPos.X, 2) +
                    Math.Pow(screen.Y - screenPos.Y, 2));

                if (dist <= tolerance && dist < bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Finds the icon/image under the given screen position (a simple test against the icon's rectangle).</summary>
        private ImageMarker? HitTestImage(PointF screenPos) => FindNearestImage(screenPos, im => im.Selectable);

        private ImageMarker? HitTestImageForHover(PointF screenPos) => FindNearestImage(screenPos, im => im.HoverEnabled);

        private ImageMarker? FindNearestImage(PointF screenPos, Func<ImageMarker, bool> filter)
        {
            // The most recently added one (drawn on top) wins on overlap - same principle as polygons.
            for (int i = Images.Count - 1; i >= 0; i--)
            {
                var im = Images[i];
                if (!filter(im)) continue;

                var size = im.ComputeScreenSize(_pixelsPerMeter);
                var screen = Project(im.World);
                var rect = new RectangleF(screen.X - size.Width / 2f, screen.Y - size.Height / 2f, size.Width, size.Height);

                if (rect.Contains(screenPos))
                    return im;
            }
            return null;
        }

        /// <summary>Finds the shape marker under the given screen position (a simple test against its bounding rectangle).</summary>
        private ShapeMarker? HitTestShape(PointF screenPos) => FindNearestShape(screenPos, sh => sh.Selectable);

        private ShapeMarker? HitTestShapeForHover(PointF screenPos) => FindNearestShape(screenPos, sh => sh.HoverEnabled);

        private ShapeMarker? FindNearestShape(PointF screenPos, Func<ShapeMarker, bool> filter)
        {
            for (int i = Shapes.Count - 1; i >= 0; i--)
            {
                var sh = Shapes[i];
                if (!filter(sh)) continue;

                var size = sh.ComputeScreenSize(_pixelsPerMeter);
                var screen = Project(sh.World);
                var rect = new RectangleF(screen.X - size.Width / 2f, screen.Y - size.Height / 2f, size.Width, size.Height);

                if (rect.Contains(screenPos))
                    return sh;
            }
            return null;
        }

        /// <summary>Finds the polyline whose line (not a vertex) was hit within a pixel tolerance.
        /// On overlap between several lines, the most recently added one wins (the one drawn on top).</summary>
        private Polyline? HitTestPolyline(PointF screenPos, float tolerancePixels = PolylineHitTolerance)
        {
            for (int idx = Polylines.Count - 1; idx >= 0; idx--)
            {
                var poly = Polylines[idx];
                if (!poly.Selectable || poly.Vertices.Count < 2) continue;

                var screenPoints = ProjectVertices(poly.Vertices);
                for (int i = 0; i < screenPoints.Length - 1; i++)
                {
                    if (DistancePointToSegment(screenPos, screenPoints[i], screenPoints[i + 1]) <= tolerancePixels)
                        return poly;
                }
            }
            return null;
        }

        /// <summary>Finds the polygon whose filled area was hit (a point-in-polygon test).
        /// On overlap between several areas, the most recently added one wins (the one drawn on top).</summary>
        private Polygon? HitTestPolygon(PointF screenPos)
        {
            for (int idx = Polygons.Count - 1; idx >= 0; idx--)
            {
                var poly = Polygons[idx];
                if (!poly.Selectable || poly.Vertices.Count < 3) continue;

                if (IsPointInPolygon(screenPos, ProjectVertices(poly.Vertices)))
                    return poly;
            }
            return null;
        }

        /// <summary>Distance from a point to segment a-b (in pixels) - perpendicular projection, clamped to the endpoints.</summary>
        private static float DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lengthSq = dx * dx + dy * dy;

            if (lengthSq < 0.0001f)
                return (float)Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));

            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq;
            t = Math.Max(0f, Math.Min(1f, t));

            float projX = a.X + t * dx, projY = a.Y + t * dy;
            return (float)Math.Sqrt(Math.Pow(p.X - projX, 2) + Math.Pow(p.Y - projY, 2));
        }

        /// <summary>Classic "ray casting" point-in-polygon test.</summary>
        private static bool IsPointInPolygon(PointF p, PointF[] polygon)
        {
            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                bool crosses = (polygon[i].Y > p.Y) != (polygon[j].Y > p.Y);
                if (crosses)
                {
                    float xIntersect = (polygon[j].X - polygon[i].X) * (p.Y - polygon[i].Y)
                        / (polygon[j].Y - polygon[i].Y) + polygon[i].X;
                    if (p.X < xIntersect)
                        inside = !inside;
                }
                j = i;
            }
            return inside;
        }

        /// <summary>Builds a GraphicsPath for the given point shape, centered on screen with the given radius.
        /// Used consistently for the fill, outline, hover glow, and selection ring - so the shape is always consistent.</summary>
        private static GraphicsPath BuildShapePath(PointShape shape, PointF center, float radius)
        {
            var path = new GraphicsPath();
            switch (shape)
            {
                case PointShape.Square:
                    path.AddPolygon(SquarePoints(center, radius));
                    break;
                case PointShape.Triangle:
                    path.AddPolygon(TrianglePoints(center, radius));
                    break;
                case PointShape.Diamond:
                    path.AddPolygon(DiamondPoints(center, radius));
                    break;
                case PointShape.Star:
                    path.AddPolygon(StarPoints(center, radius));
                    break;
                case PointShape.Cross:
                    path.AddPolygon(CrossPoints(center, radius));
                    break;
                default: // Circle
                    path.AddEllipse(center.X - radius, center.Y - radius, radius * 2, radius * 2);
                    break;
            }
            return path;
        }

        private static PointF[] SquarePoints(PointF c, float r) => new[]
        {
            new PointF(c.X - r, c.Y - r), new PointF(c.X + r, c.Y - r),
            new PointF(c.X + r, c.Y + r), new PointF(c.X - r, c.Y + r)
        };

        private static PointF[] DiamondPoints(PointF c, float r) => new[]
        {
            new PointF(c.X, c.Y - r), new PointF(c.X + r, c.Y),
            new PointF(c.X, c.Y + r), new PointF(c.X - r, c.Y)
        };

        private static PointF[] TrianglePoints(PointF c, float r) => new[]
        {
            new PointF(c.X, c.Y - r),
            new PointF(c.X + r * 0.866f, c.Y + r * 0.5f),
            new PointF(c.X - r * 0.866f, c.Y + r * 0.5f)
        };

        private static PointF[] CrossPoints(PointF c, float r)
        {
            float t = r * 0.38f; // half-thickness of a cross arm
            return new[]
            {
                new PointF(c.X - t, c.Y - r), new PointF(c.X + t, c.Y - r),
                new PointF(c.X + t, c.Y - t), new PointF(c.X + r, c.Y - t),
                new PointF(c.X + r, c.Y + t), new PointF(c.X + t, c.Y + t),
                new PointF(c.X + t, c.Y + r), new PointF(c.X - t, c.Y + r),
                new PointF(c.X - t, c.Y + t), new PointF(c.X - r, c.Y + t),
                new PointF(c.X - r, c.Y - t), new PointF(c.X - t, c.Y - t)
            };
        }

        private static PointF[] StarPoints(PointF c, float outerRadius)
        {
            float innerRadius = outerRadius * 0.45f;
            var pts = new PointF[10];

            for (int i = 0; i < 10; i++)
            {
                // Start with a point up top (-90°) and alternate outer/inner radius every 36°.
                float angleDeg = -90f + i * 36f;
                float angleRad = angleDeg * (float)Math.PI / 180f;
                float r = (i % 2 == 0) ? outerRadius : innerRadius;
                pts[i] = new PointF(c.X + r * (float)Math.Cos(angleRad), c.Y + r * (float)Math.Sin(angleRad));
            }
            return pts;
        }

        /// <summary>
        /// Programmatically sets what's selected - a point, polyline, polygon, or image (e.g. from a click
        /// in a list in the host Form, not just a click on the canvas). Call with null to clear the selection.
        /// This is the single place that changes Selected - the internal click handling uses it too, so the
        /// selection is always consistent and SelectionChanged fires. At most one thing is ever selected at a time.
        /// </summary>
        public void Select(object? shape)
        {
            if (ReferenceEquals(shape, _selectedShape)) return;

            foreach (var p in Points) p.SetSelected(false);
            foreach (var pl in Polylines) pl.SetSelected(false);
            foreach (var pg in Polygons) pg.SetSelected(false);
            foreach (var im in Images) im.SetSelected(false);
            foreach (var sh in Shapes) sh.SetSelected(false);

            switch (shape)
            {
                case Point3D p: p.SetSelected(true); break;
                case Polyline pl: pl.SetSelected(true); break;
                case Polygon pg: pg.SetSelected(true); break;
                case ImageMarker im: im.SetSelected(true); break;
                case ShapeMarker sh: sh.SetSelected(true); break;
            }

            _selectedShape = shape;
            Render();
            SelectionChanged?.Invoke(this, shape);
        }

        /// <summary>Convenience overload of Select() for a point (kept for readability at the call site).</summary>
        public void SelectPoint(Point3D? point) => Select(point);

        /// <summary>Convenience overload of Select() for a polyline.</summary>
        public void SelectPolyline(Polyline? polyline) => Select(polyline);

        /// <summary>Convenience overload of Select() for a polygon.</summary>
        public void SelectPolygon(Polygon? polygon) => Select(polygon);

        /// <summary>Convenience overload of Select() for an image/icon.</summary>
        public void SelectImage(ImageMarker? image) => Select(image);

        /// <summary>Convenience overload of Select() for a shape marker.</summary>
        public void SelectShapeMarker(ShapeMarker? shape) => Select(shape);

        /// <summary>
        /// Starts interactive drawing of a new polyline - every left click on the canvas (away from an
        /// existing point) adds a vertex, a click on an existing point "snaps" to it (a shared vertex). A
        /// double-click or FinishDrawing() finishes the drawing (needs at least 2 vertices), CancelDrawing()
        /// cancels it. The in-progress line is drawn continuously (it's a real Polyline, added to Polylines
        /// right away), including a "rubber band" line following the cursor.
        /// </summary>
        public void StartDrawingPolyline(string label = "", Pen? linePen = null)
        {
            CancelDrawing();

            _drawingPolyline = new Polyline().WithLabel(label);
            if (linePen != null) _drawingPolyline.WithLinePen(linePen);
            Polylines.Add(_drawingPolyline);

            _pictureBox.Cursor = DrawingCursor;
            DrawingStarted?.Invoke(this, EventArgs.Empty);
            Render();
        }

        /// <summary>Like StartDrawingPolyline, but the result is a closed Polygon (needs at least 3 vertices).</summary>
        public void StartDrawingPolygon(string label = "", Brush? fillBrush = null, Pen? outlinePen = null)
        {
            CancelDrawing();

            _drawingPolygon = new Polygon().WithLabel(label);
            if (fillBrush != null) _drawingPolygon.WithFillBrush(fillBrush);
            if (outlinePen != null) _drawingPolygon.WithOutlinePen(outlinePen);
            Polygons.Add(_drawingPolygon);

            _pictureBox.Cursor = DrawingCursor;
            DrawingStarted?.Invoke(this, EventArgs.Empty);
            Render();
        }

        /// <summary>
        /// Finishes drawing and keeps the created shape. If it doesn't have enough vertices (2 for a
        /// polyline, 3 for a polygon), behaves like CancelDrawing() - an incomplete/meaningless shape is discarded.
        /// </summary>
        public void FinishDrawing()
        {
            if (_drawingPolyline != null)
            {
                if (_drawingPolyline.Vertices.Count < 2) { CancelDrawing(); return; }

                var finished = _drawingPolyline;
                _drawingPolyline = null;
                _drawingOwnedVertices.Clear();
                _drawingPreviewScreenPos = null;
                _pictureBox.Cursor = Cursors.Default;
                Render();
                PolylineDrawingFinished?.Invoke(this, finished);
                return;
            }

            if (_drawingPolygon != null)
            {
                if (_drawingPolygon.Vertices.Count < 3) { CancelDrawing(); return; }

                var finished = _drawingPolygon;
                _drawingPolygon = null;
                _drawingOwnedVertices.Clear();
                _drawingPreviewScreenPos = null;
                _pictureBox.Cursor = Cursors.Default;
                Render();
                PolygonDrawingFinished?.Invoke(this, finished);
            }
        }

        /// <summary>Cancels drawing/placement/measuring in progress - removes the in-progress shape as well
        /// as the vertices the engine created for it (vertices "snapped" onto existing points are left
        /// untouched), and clears any measurement currently shown.</summary>
        public void CancelDrawing()
        {
            if (!IsDrawing && !_measuringActive && _measureStart == null) return;

            if (_drawingPolyline != null) { Polylines.Remove(_drawingPolyline); _drawingPolyline = null; }
            if (_drawingPolygon != null) { Polygons.Remove(_drawingPolygon); _drawingPolygon = null; }
            _placingPointOptions = null;
            _placingImageOptions = null;

            foreach (var v in _drawingOwnedVertices) Points.Remove(v);
            _drawingOwnedVertices.Clear();
            _drawingPreviewScreenPos = null;

            _measuringActive = false;
            _measureStart = null;
            _measureEnd = null;
            _measurePreviewScreenPos = null;

            _pictureBox.Cursor = Cursors.Default;

            Render();
            DrawingCancelled?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Starts (or restarts) the measuring tool - click one point, then a second point, to draw a line
        /// between them and see the distance (a live "rubber band" with a running distance label follows
        /// the cursor after the first click). Mutually exclusive with drawing/placing modes - calling this
        /// cancels any of those, and vice versa.
        ///
        /// Calling this again (e.g. from a toggle button, the same way you'd re-trigger point placement)
        /// discards any previous measurement and starts fresh from the first click. The finished
        /// measurement (line + distance label) stays visible after the second click until you call this
        /// again or CancelDrawing() - IsMeasuring itself goes back to false once the second point is placed.
        /// </summary>
        public void StartMeasuring()
        {
            CancelDrawing();

            _measuringActive = true;
            _pictureBox.Cursor = DrawingCursor;
            Render();
        }

        private void AddMeasurePoint(PointF screenPos)
        {
            var world = ScreenToWorld(screenPos, depth: 0f); // the plane passing through the center of the current view

            if (_measureStart == null)
            {
                _measureStart = world;
                Render();
                return;
            }

            _measureEnd = world;
            _measuringActive = false; // tool goes idle - StartMeasuring() again for another measurement
            _measurePreviewScreenPos = null;
            _pictureBox.Cursor = Cursors.Default;
            Render();

            MeasurementCompleted?.Invoke(this, Vector3.Distance(_measureStart.Value, _measureEnd.Value));
        }

        /// <summary>
        /// Starts a mode in which every left click on the canvas (away from an existing point) immediately
        /// creates and places a new, complete point - no collecting vertices like StartDrawingPolyline.
        /// Configure the look the same way as with AddPoint(). If continuous = false, the mode ends itself
        /// after the first placement (typically "place one point and done"); default true = click as many
        /// times as you like until you call CancelDrawing() (e.g. on Escape from the Form).
        /// </summary>
        public void StartPlacingPoints(string label = "", Brush? brush = null, Pen? pen = null, float? size = null,
            PointShape shape = PointShape.Circle, bool selectable = true, bool draggable = true,
            bool hoverEnabled = true, bool continuous = true)
        {
            CancelDrawing();
            _placingPointOptions = new PointPlacementOptions
            {
                Label = label, Brush = brush, Pen = pen, Size = size, Shape = shape,
                Selectable = selectable, Draggable = draggable, HoverEnabled = hoverEnabled, Continuous = continuous
            };
            _pictureBox.Cursor = DrawingCursor;
            DrawingStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Like StartPlacingPoints, just places icons/images instead (see AddImage() for the meaning of the parameters).</summary>
        public void StartPlacingImages(Image image, string label = "", float width = 24f, float? height = null,
            bool scaleWithZoom = false, bool selectable = true, bool draggable = true, bool hoverEnabled = true,
            bool continuous = true)
        {
            CancelDrawing();
            _placingImageOptions = new ImagePlacementOptions
            {
                Image = image, Label = label, Width = width, Height = height, ScaleWithZoom = scaleWithZoom,
                Selectable = selectable, Draggable = draggable, HoverEnabled = hoverEnabled, Continuous = continuous
            };
            _pictureBox.Cursor = DrawingCursor;
            DrawingStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Creates and places a point according to the current StartPlacingPoints() settings at the given screen position.</summary>
        private void PlacePointAt(PointF screenPos)
        {
            var opts = _placingPointOptions!;

            // ScreenToWorld already returns a LOCAL-space coordinate (no offset needed - that's the point
            // of it). AddPoint expects RAW coordinates and applies SetLocalOrigin()'s offset itself, so we
            // convert back to raw here first - otherwise the offset would be applied twice.
            var raw = ToRaw(ScreenToWorld(screenPos, depth: 0f));

            var p = AddPoint(raw.X, raw.Y, raw.Z, opts.Label, opts.Brush, opts.Pen, opts.Size, opts.Shape,
                opts.Selectable, opts.Draggable, opts.HoverEnabled);

            PointPlaced?.Invoke(this, p);
            if (!opts.Continuous)
            {
                _placingPointOptions = null;
                _pictureBox.Cursor = Cursors.Default;
            }
        }

        /// <summary>Creates and places an icon according to the current StartPlacingImages() settings at the given screen position.</summary>
        private void PlaceImageAt(PointF screenPos)
        {
            var opts = _placingImageOptions!;

            // Same reasoning as PlacePointAt - ScreenToWorld returns local space, AddImage expects raw.
            var raw = ToRaw(ScreenToWorld(screenPos, depth: 0f));

            var marker = AddImage(opts.Image, raw.X, raw.Y, raw.Z, opts.Label, opts.Width, opts.Height,
                opts.Selectable, opts.Draggable, opts.HoverEnabled);
            marker.WithScaleWithZoom(opts.ScaleWithZoom);
            Render();

            ImagePlaced?.Invoke(this, marker);
            if (!opts.Continuous)
            {
                _placingImageOptions = null;
                _pictureBox.Cursor = Cursors.Default;
            }
        }

        /// <summary>Adds a vertex at the given screen position to the shape currently being drawn - either by
        /// "snapping" to an existing point under the cursor, or by creating a new one on the current view plane.</summary>
        private void AddDrawingVertex(PointF screenPos)
        {
            var vertex = HitTest(screenPos);
            if (vertex == null)
            {
                var world = ScreenToWorld(screenPos, depth: 0f); // the plane passing through the center of the current view
                vertex = new Point3D(world.X, world.Y, world.Z);
                Points.Add(vertex);
                _drawingOwnedVertices.Add(vertex);
            }

            _drawingPolyline?.Vertices.Add(vertex);
            _drawingPolygon?.Vertices.Add(vertex);

            Render();
            DrawingVertexAdded?.Invoke(this, vertex);
        }

        /// <summary>
        /// The exact opposite of Project() - computes a world-space point from a screen position. Since a
        /// 2D screen is missing one dimension (view depth), it needs to be supplied (typically the point's
        /// depth before a drag starts, so it stays in the same "layer" and, e.g. in top view, keeps its Z height).
        /// </summary>
        private Vector3 ScreenToWorld(PointF screen, float depth)
        {
            float rotatedX = (screen.X - _panOffsetPixels.X) / _pixelsPerMeter;
            float rotatedZ = -(screen.Y - _panOffsetPixels.Y) / _pixelsPerMeter;
            var rotated = new Vector3(rotatedX, depth, rotatedZ);

            // The exact opposite of Project(): first undo the pitch rotation, then the yaw rotation.
            var afterYaw = Vector3.Transform(rotated, Matrix4x4.CreateRotationX(DegToRad(-_pitchDeg)));
            var world = Vector3.Transform(afterYaw, Matrix4x4.CreateRotationZ(DegToRad(-_yawDeg)));
            return world;
        }

        public void ZoomAt(PointF screenAnchor, float factor)
        {
            var rawBefore = new PointF(
                (screenAnchor.X - _panOffsetPixels.X) / _pixelsPerMeter,
                (screenAnchor.Y - _panOffsetPixels.Y) / _pixelsPerMeter);

            _pixelsPerMeter = Math.Max(_minPixelsPerMeter, Math.Min(_maxPixelsPerMeter, _pixelsPerMeter * factor));

            _panOffsetPixels = new PointF(
                screenAnchor.X - rawBefore.X * _pixelsPerMeter,
                screenAnchor.Y - rawBefore.Y * _pixelsPerMeter);

            Render();
        }

        /// <summary>
        /// Sets zoom and pan so that every item currently on the canvas - points, polyline/polygon
        /// vertices, icons, and the background image if one is loaded - is visible at once, with a small
        /// margin around the edges. Does nothing if the scene is completely empty.
        /// </summary>
        /// <param name="marginFraction">Extra breathing room around the content, as a fraction of the
        /// content's own size (0.1 = 10% margin on each side). Use 0 to fit exactly, edge to edge.</param>
        public void ZoomToFullExtent(float marginFraction = 0.1f)
        {
            if (_buffer == null) return;

            var rawPoints = new List<(float x, float z)>();

            foreach (var p in Points) rawPoints.Add(ProjectRawXZ(p.World));
            foreach (var im in Images) rawPoints.Add(ProjectRawXZ(im.World));
            foreach (var sh in Shapes) rawPoints.Add(ProjectRawXZ(sh.World));

            // Include the background image's 4 corners too, if one is loaded - the current camera
            // rotation can make any of them the extreme point, not just the "top-left" one.
            if (_backgroundImage != null)
            {
                rawPoints.Add(ProjectRawXZ(_bgWorldOrigin));
                rawPoints.Add(ProjectRawXZ(_bgWorldOrigin + new Vector3(_bgWorldWidth, 0, 0)));
                rawPoints.Add(ProjectRawXZ(_bgWorldOrigin + new Vector3(0, -_bgWorldHeight, 0)));
                rawPoints.Add(ProjectRawXZ(_bgWorldOrigin + new Vector3(_bgWorldWidth, -_bgWorldHeight, 0)));
            }

            if (rawPoints.Count == 0) return; // nothing to fit to - leave the current view untouched

            float minX = rawPoints.Min(p => p.x);
            float maxX = rawPoints.Max(p => p.x);
            float minZ = rawPoints.Min(p => p.z);
            float maxZ = rawPoints.Max(p => p.z);

            // Guard against a degenerate (single point, or all points on one line) extent, so we don't
            // end up dividing by ~0 and zooming in to infinity.
            float spanX = Math.Max(maxX - minX, 0.01f);
            float spanZ = Math.Max(maxZ - minZ, 0.01f);

            int viewW = _buffer.Width;
            int viewH = _buffer.Height;

            float marginMultiplier = 1f + Math.Max(0f, marginFraction) * 2f;
            float ppmX = viewW / (spanX * marginMultiplier);
            float ppmZ = viewH / (spanZ * marginMultiplier);

            _pixelsPerMeter = Math.Max(_minPixelsPerMeter, Math.Min(_maxPixelsPerMeter, Math.Min(ppmX, ppmZ)));

            // Center the content's bounding box in the middle of the view - same screenX/screenY formulas
            // as Project(), just solved backwards for the pan offset instead of the screen position.
            float centerX = (minX + maxX) / 2f;
            float centerZ = (minZ + maxZ) / 2f;

            _panOffsetPixels = new PointF(
                viewW / 2f - centerX * _pixelsPerMeter,
                viewH / 2f + centerZ * _pixelsPerMeter);

            Render();
        }

        /// <summary>Rotation-only projection (no zoom/pan) - the raw X/Z that ZoomToFullExtent fits a
        /// bounding box around, before deciding on a scale and pan offset.</summary>
        private (float x, float z) ProjectRawXZ(Vector3 world)
        {
            var rotated = RotateToView(world);
            return (rotated.X, rotated.Z);
        }

        /// <summary>
        /// Approximate world-space rectangle currently visible in the viewport (at world Z = 0). Useful
        /// inside a CustomBackgroundPaint handler to skip drawing entities that are off-screen anyway -
        /// important for large vector backgrounds (e.g. a parsed DXF drawing) with many entities.
        /// Returns RectangleF.Empty before the canvas has a size yet.
        /// </summary>
        public RectangleF GetVisibleWorldBounds()
        {
            if (_buffer == null) return RectangleF.Empty;

            var corners = new[]
            {
                ScreenToWorld(new PointF(0, 0), 0f),
                ScreenToWorld(new PointF(_buffer.Width, 0), 0f),
                ScreenToWorld(new PointF(0, _buffer.Height), 0f),
                ScreenToWorld(new PointF(_buffer.Width, _buffer.Height), 0f),
            };

            float minX = corners.Min(c => c.X);
            float maxX = corners.Max(c => c.X);
            float minY = corners.Min(c => c.Y);
            float maxY = corners.Max(c => c.Y);

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        private static float DegToRad(float deg) => deg * (float)Math.PI / 180f;
        // ±90° is completely safe mathematically (no division by zero anywhere in Project/ScreenToWorld),
        // and ShowTopView/ShowFrontView need it to be exact - otherwise a small "slip" appears between Y and Z
        // when dragging a point (almost-but-not-quite 90° is no longer a clean orthogonal top view).
        private static float ClampPitch(float deg) => Math.Max(-90f, Math.Min(90f, deg));

        #endregion
    }
}
