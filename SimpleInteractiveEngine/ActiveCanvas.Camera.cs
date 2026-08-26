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
            var afterYaw = Vector3.Transform(world, Matrix4x4.CreateRotationZ(DegToRad(_yawDeg)));
            var rotated = Vector3.Transform(afterYaw, Matrix4x4.CreateRotationX(DegToRad(_pitchDeg)));

            depth = rotated.Y; // only for draw-order sorting (painter's algorithm), not real perspective

            float screenX = rotated.X * _pixelsPerMeter + _panOffsetPixels.X;
            float screenY = -rotated.Z * _pixelsPerMeter + _panOffsetPixels.Y;
            return new PointF(screenX, screenY);
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

            switch (shape)
            {
                case Point3D p: p.SetSelected(true); break;
                case Polyline pl: pl.SetSelected(true); break;
                case Polygon pg: pg.SetSelected(true); break;
                case ImageMarker im: im.SetSelected(true); break;
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
                Render();
                PolygonDrawingFinished?.Invoke(this, finished);
            }
        }

        /// <summary>Cancels drawing/placement in progress - removes the in-progress shape as well as the
        /// vertices the engine created for it (vertices "snapped" onto existing points are left untouched).</summary>
        public void CancelDrawing()
        {
            if (!IsDrawing) return;

            if (_drawingPolyline != null) { Polylines.Remove(_drawingPolyline); _drawingPolyline = null; }
            if (_drawingPolygon != null) { Polygons.Remove(_drawingPolygon); _drawingPolygon = null; }
            _placingPointOptions = null;
            _placingImageOptions = null;

            foreach (var v in _drawingOwnedVertices) Points.Remove(v);
            _drawingOwnedVertices.Clear();
            _drawingPreviewScreenPos = null;

            Render();
            DrawingCancelled?.Invoke(this, EventArgs.Empty);
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
            DrawingStarted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Creates and places a point according to the current StartPlacingPoints() settings at the given screen position.</summary>
        private void PlacePointAt(PointF screenPos)
        {
            var opts = _placingPointOptions!;
            var world = ScreenToWorld(screenPos, depth: 0f); // the plane passing through the center of the current view

            var p = AddPoint(world.X, world.Y, world.Z, opts.Label, opts.Brush, opts.Pen, opts.Size, opts.Shape,
                opts.Selectable, opts.Draggable, opts.HoverEnabled);

            PointPlaced?.Invoke(this, p);
            if (!opts.Continuous) _placingPointOptions = null;
        }

        /// <summary>Creates and places an icon according to the current StartPlacingImages() settings at the given screen position.</summary>
        private void PlaceImageAt(PointF screenPos)
        {
            var opts = _placingImageOptions!;
            var world = ScreenToWorld(screenPos, depth: 0f);

            var marker = AddImage(opts.Image, world.X, world.Y, world.Z, opts.Label, opts.Width, opts.Height,
                opts.Selectable, opts.Draggable, opts.HoverEnabled);
            marker.WithScaleWithZoom(opts.ScaleWithZoom);
            Render();

            ImagePlaced?.Invoke(this, marker);
            if (!opts.Continuous) _placingImageOptions = null;
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

        private static float DegToRad(float deg) => deg * (float)Math.PI / 180f;
        // ±90° is completely safe mathematically (no division by zero anywhere in Project/ScreenToWorld),
        // and ShowTopView/ShowFrontView need it to be exact - otherwise a small "slip" appears between Y and Z
        // when dragging a point (almost-but-not-quite 90° is no longer a clean orthogonal top view).
        private static float ClampPitch(float deg) => Math.Max(-90f, Math.Min(90f, deg));

        #endregion
    }
}
