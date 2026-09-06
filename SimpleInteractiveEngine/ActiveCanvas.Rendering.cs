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
        // All drawing into the internal Bitmap - layer by layer (background, grid, axes,
        // areas, lines, points, icons, labels, scale bar), plus geometric helper functions for shapes.

        #region Visual constants
        // All the "magic numbers" for appearance in one place, so they can be tuned
        // without hunting through the drawing methods below.
        private static readonly Font LabelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        private static readonly Font ScaleBarFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        private const int HoverHaloAlpha = 55;
        private const int HoverRingAlpha = 230;
        private const int SelectionHighlightAlpha = 140;
        private const int LabelBackdropAlpha = 190;
        private const float HoverHaloExtraRadius = 7f;
        private const float HoverGrowRadius = 1.5f;
        private const float SelectionRingExtraRadius = 4f;
        private const float PolylineHitTolerance = 6f;
        #endregion

        #region Rendering

        /// <summary>
        /// Manually forces the bitmap to be rebuilt according to the PictureBox's current size. Under
        /// normal circumstances there's no need to call this - the engine handles it itself via
        /// PictureBox.Resize. A safety net for unusual containers (e.g. nested SplitContainer/TabControl
        /// layouts), where the Resize event might not arrive reliably in the right order.
        /// </summary>
        public void RefreshCanvasSize() => RebuildBuffer();

        /// <summary>
        /// Defers RebuildBuffer() until "after" the current layout pass finishes. Creating a new Bitmap
        /// and assigning Image directly INSIDE the PictureBox.Resize event can interfere with an
        /// in-progress Dock layout (the same mechanism that's currently recomputing sizes) - so it's
        /// better done on the next message-loop tick, once Windows has definitively finished the resize.
        /// </summary>
        private void DeferredRebuildBuffer()
        {
            if (_pictureBox.IsDisposed) return;

            if (_pictureBox.IsHandleCreated)
                _pictureBox.BeginInvoke(new Action(RebuildBuffer));
            else
                RebuildBuffer();
        }

        private void RebuildBuffer()
        {
            int w = Math.Max(1, _pictureBox.ClientSize.Width);
            int h = Math.Max(1, _pictureBox.ClientSize.Height);

            bool firstTime = _buffer == null;

            _buffer?.Dispose();
            _buffer = new Bitmap(w, h);
            _pictureBox.Image = _buffer;

            if (firstTime)
                _panOffsetPixels = new PointF(w / 2f, h / 2f); // center the coordinate origin

            Render();
        }

        public void Render()
        {
            if (_buffer == null) return;

            using (var g = Graphics.FromImage(_buffer))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.White);

                DrawBackgroundImage(g);
                CustomBackgroundPaint?.Invoke(this, new CanvasPaintEventArgs(g));
                DrawGroundGrid(g);
                DrawAxes(g);
                DrawPolygonFills(g);
                DrawPolylineSegments(g);
                DrawShapeMarkers(g);
                DrawPoints(g);
                DrawImages(g);
                DrawPolylineLabels(g);
                DrawPolygonLabels(g);
                DrawImageLabels(g);
                DrawShapeMarkerLabels(g);
                DrawDrawingPreview(g);
                DrawMeasurement(g);
                DrawScaleBar(g, _buffer.Width, _buffer.Height);
            }

            _pictureBox.Invalidate();
        }

        private void DrawBackgroundImage(Graphics g)
        {
            if (_backgroundImage == null) return;

            // In top view, screenY = -y*ppm holds, so increasing world Y goes UP on screen.
            // The image must therefore stretch from the top edge (worldOrigin) towards MINUS Y,
            // otherwise it would be vertically flipped.
            //
            // Orthographic projection preserves parallelism -> the background rectangle always projects
            // to exactly a parallelogram, so 3 corners are enough (DrawImage fills in the fourth).
            var topLeft = Project(_bgWorldOrigin);
            var topRight = Project(_bgWorldOrigin + new Vector3(_bgWorldWidth, 0, 0));
            var bottomLeft = Project(_bgWorldOrigin + new Vector3(0, -_bgWorldHeight, 0));

            g.DrawImage(_backgroundImage, new[] { topLeft, topRight, bottomLeft });
        }

        private void DrawGroundGrid(Graphics g)
        {
            if (GroundGridExtent <= 0) return;

            using var pen = new Pen(Color.Gainsboro, 1f);
            int n = (int)GroundGridExtent;

            for (int i = -n; i <= n; i++)
            {
                var p1 = Project(new Vector3(i, -n, 0));
                var p2 = Project(new Vector3(i, n, 0));
                g.DrawLine(pen, p1, p2);

                var p3 = Project(new Vector3(-n, i, 0));
                var p4 = Project(new Vector3(n, i, 0));
                g.DrawLine(pen, p3, p4);
            }
        }

        private void DrawAxes(Graphics g)
        {
            // In pure 2D view (pitch = 90°) the Z axis (height) is practically invisible - that's correct,
            // in top view there's no point drawing "up/down" as a line.
            using var xPen = new Pen(Color.Firebrick, 2f);
            using var yPen = new Pen(Color.ForestGreen, 2f);
            using var zPen = new Pen(Color.SteelBlue, 2f);

            var origin = Project(Vector3.Zero);
            float axisLen = Math.Max(1f, GroundGridExtent * 0.3f);

            g.DrawLine(xPen, origin, Project(new Vector3(axisLen, 0, 0)));
            g.DrawLine(yPen, origin, Project(new Vector3(0, axisLen, 0)));
            g.DrawLine(zPen, origin, Project(new Vector3(0, 0, axisLen)));
        }

        private void DrawPolygonFills(Graphics g)
        {
            using var defaultFillBrush = new SolidBrush(DefaultPolygonFillColor);
            using var defaultOutlinePen = new Pen(DefaultPolygonOutlineColor, 1.5f);
            using var selectionPen = new Pen(SelectedPointColor, 2.5f) { DashStyle = DashStyle.Dash };

            foreach (var poly in Polygons)
            {
                if (poly.Vertices.Count < 3) continue;
                var screenPoints = ProjectVertices(poly.Vertices);

                g.FillPolygon(poly.FillBrush ?? defaultFillBrush, screenPoints);
                g.DrawPolygon(poly.OutlinePen ?? defaultOutlinePen, screenPoints);

                if (poly.Selected)
                    g.DrawPolygon(selectionPen, screenPoints);
            }
        }

        private void DrawPolygonLabels(Graphics g)
        {
            foreach (var poly in Polygons)
            {
                if (!poly.ShowLabel || string.IsNullOrEmpty(poly.Label)) continue;
                DrawLabelWithBackdrop(g, poly.Label, Project(poly.LabelPosition ?? poly.ComputeCentroid()));
            }
        }

        private void DrawPolylineSegments(Graphics g)
        {
            using var defaultPen = new Pen(DefaultLineColor, 2f);
            using var selectionPen = new Pen(Color.FromArgb(SelectionHighlightAlpha, SelectedPointColor), 6f)
                { StartCap = LineCap.Round, EndCap = LineCap.Round };

            foreach (var poly in Polylines)
            {
                if (poly.Vertices.Count < 2) continue;
                var pen = poly.LinePen ?? defaultPen;
                var screenPoints = ProjectVertices(poly.Vertices); // project vertices once, not twice for shared points

                for (int i = 0; i < screenPoints.Length - 1; i++)
                {
                    // Selection highlight - a thicker, semi-transparent "backing" under the actual line,
                    // works universally regardless of LinePen (same principle as point hover).
                    if (poly.Selected)
                        g.DrawLine(selectionPen, screenPoints[i], screenPoints[i + 1]);

                    g.DrawLine(pen, screenPoints[i], screenPoints[i + 1]);
                }
            }
        }

        private void DrawPolylineLabels(Graphics g)
        {
            foreach (var poly in Polylines)
            {
                if (!poly.ShowLabel || string.IsNullOrEmpty(poly.Label)) continue;
                DrawLabelWithBackdrop(g, poly.Label, Project(poly.LabelPosition ?? poly.ComputeMidpoint()));
            }
        }

        /// <summary>Projects a list of vertices to screen space once (shared by both drawing and segment hit-testing).</summary>
        private PointF[] ProjectVertices(List<Point3D> vertices)
        {
            var result = new PointF[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
                result[i] = Project(vertices[i].World);
            return result;
        }

        /// <summary>
        /// Shared label rendering for both polylines and polygons - text centered on the given screen
        /// position, with a subtle semi-transparent backdrop so it's readable over any background.
        /// </summary>
        private void DrawLabelWithBackdrop(Graphics g, string label, PointF screen)
        {
            using var textBrush = new SolidBrush(Color.Black);
            var size = g.MeasureString(label, LabelFont);

            var backRect = new RectangleF(screen.X - size.Width / 2f - 3f, screen.Y - size.Height / 2f - 1f,
                size.Width + 6f, size.Height + 2f);
            using (var backBrush = new SolidBrush(Color.FromArgb(LabelBackdropAlpha, Color.White)))
                g.FillRectangle(backBrush, backRect);

            g.DrawString(label, LabelFont, textBrush, screen.X - size.Width / 2f, screen.Y - size.Height / 2f);
        }

        /// <summary>Scale bar in the bottom-right corner - screen-space, independent of the camera's rotation/pan.</summary>
        private void DrawScaleBar(Graphics g, int canvasWidth, int canvasHeight)
        {
            if (_pixelsPerMeter <= 0 || canvasWidth < 60 || canvasHeight < 40) return;

            const float targetPixelWidth = 90f;
            float niceMeters = NiceRoundNumber(targetPixelWidth / _pixelsPerMeter);
            float barWidth = niceMeters * _pixelsPerMeter;
            string label = FormatMeters(niceMeters);

            var textSize = g.MeasureString(label, ScaleBarFont);

            const float margin = 14f;
            const float barHeight = 6f;
            float padding = 8f;

            float boxWidth = Math.Max(barWidth, textSize.Width) + padding * 2;
            float boxHeight = textSize.Height + barHeight + padding * 2.2f;
            float boxRight = canvasWidth - margin;
            float boxBottom = canvasHeight - margin;
            var boxRect = new RectangleF(boxRight - boxWidth, boxBottom - boxHeight, boxWidth, boxHeight);

            // A subtle semi-transparent backdrop - so the scale bar is readable even on a dark/colorful background.
            using (var path = RoundedRect(boxRect, 6f))
            using (var backBrush = new SolidBrush(Color.FromArgb(200, Color.White)))
                g.FillPath(backBrush, path);

            float barLeft = boxRight - padding - barWidth;
            float barY = boxRect.Bottom - padding;

            using var barPen = new Pen(Color.FromArgb(210, Color.Black), 1.5f);
            g.DrawLine(barPen, barLeft, barY, boxRight - padding, barY);
            g.DrawLine(barPen, barLeft, barY - 4, barLeft, barY + 4);
            g.DrawLine(barPen, boxRight - padding, barY - 4, boxRight - padding, barY + 4);

            using var textBrush = new SolidBrush(Color.FromArgb(220, Color.Black));
            g.DrawString(label, ScaleBarFont, textBrush,
                boxRect.X + (boxWidth - textSize.Width) / 2f, boxRect.Y + padding * 0.4f);
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Rounds to a "nice" number for the scale bar: 1, 2, 5, 10, 20, 50, 100, ...</summary>
        private static float NiceRoundNumber(float value)
        {
            if (value <= 0) return 1f;
            float exponent = (float)Math.Floor(Math.Log10(value));
            float fraction = value / (float)Math.Pow(10, exponent);

            float niceFraction = fraction switch
            {
                < 1.5f => 1f,
                < 3f => 2f,
                < 7f => 5f,
                _ => 10f
            };

            return niceFraction * (float)Math.Pow(10, exponent);
        }

        private static string FormatMeters(float meters)
        {
            if (meters >= 1000f) return $"{meters / 1000f:0.##} km";
            if (meters < 1f) return $"{meters * 100f:0} cm";
            return $"{meters:0.##} m";
        }

        private void DrawPoints(Graphics g)
        {
            using var textBrush = new SolidBrush(Color.Black);

            // Simple "painter's algorithm" - draw the farther points first, then the closer ones.
            // In pure 2D view all points have the same Z=0 depth, so the order is stable based on insertion.
            var ordered = Points
                .Select(p => (Point: p, Screen: Project(p.World, out float depth), Depth: depth))
                .OrderByDescending(t => t.Depth);

            foreach (var (p, screen, _) in ordered)
            {
                bool isHover = p.HoverEnabled && ReferenceEquals(p, _hoverPoint);
                float radius = p.Size ?? PointScreenRadius;

                if (isHover)
                {
                    // A subtle "halo" glow - derived from the point's own color (SolidBrush), otherwise DefaultPointColor.
                    // Works equally well with any color/custom brush, since we don't use a fixed hue.
                    // Same shape as the point itself, just bigger - so a star gets a star-shaped glow, not a circular one.
                    Color haloColor = (p.Brush as SolidBrush)?.Color ?? DefaultPointColor;
                    float haloRadius = radius + HoverHaloExtraRadius;
                    using var haloPath = BuildShapePath(p.Shape, screen, haloRadius);
                    using var haloBrush = new SolidBrush(Color.FromArgb(HoverHaloAlpha, haloColor));
                    g.FillPath(haloBrush, haloPath);
                    radius += HoverGrowRadius;
                }

                using var shapePath = BuildShapePath(p.Shape, screen, radius);

                // The point's custom Brush/Pen is NEVER overridden - selection is signaled by a separate
                // ring below, not by changing the fill color. This works universally with the default
                // color as well as with any custom brush.
                if (p.Brush != null)
                {
                    g.FillPath(p.Brush, shapePath);
                }
                else
                {
                    using var fillBrush = new SolidBrush(DefaultPointColor);
                    g.FillPath(fillBrush, shapePath);
                }

                if (p.Pen != null)
                    g.DrawPath(p.Pen, shapePath);

                if (isHover)
                {
                    // An extra thin white outline for a clean, modern "highlight" - contrasts with any color.
                    using var ringPen = new Pen(Color.FromArgb(HoverRingAlpha, Color.White), 1.5f);
                    g.DrawPath(ringPen, shapePath);
                }

                if (p.Selected)
                {
                    // A persistent selection outline, separate from the hover highlight - works independently
                    // of any custom Brush/Pen, so the selection is always visible. Same shape as the point.
                    float selRadius = radius + SelectionRingExtraRadius;
                    using var selectionPath = BuildShapePath(p.Shape, screen, selRadius);
                    using var selectionPen = new Pen(SelectedPointColor, 2f);
                    g.DrawPath(selectionPen, selectionPath);
                }

                if (p.ShowLabel && !string.IsNullOrEmpty(p.Label))
                {
                    var offset = p.LabelOffset ?? new PointF(radius + 2, -6);
                    g.DrawString(p.Label, ScaleBarFont, textBrush, screen.X + offset.X, screen.Y + offset.Y);
                }
            }
        }

        private void DrawImages(Graphics g)
        {
            // Same "painter's algorithm" as for points - farther icons are drawn first.
            var ordered = Images
                .Select(im => (Image: im, Screen: Project(im.World, out float depth), Depth: depth))
                .OrderByDescending(t => t.Depth);

            foreach (var (im, screen, _) in ordered)
            {
                var size = im.ComputeScreenSize(_pixelsPerMeter);
                var rect = new RectangleF(screen.X - size.Width / 2f, screen.Y - size.Height / 2f, size.Width, size.Height);
                bool isHover = im.HoverEnabled && ReferenceEquals(im, _hoverImage);

                if (isHover)
                {
                    // Same principle as the point hover glow - a subtle extra semi-transparent rectangle.
                    var haloRect = RectangleF.Inflate(rect, HoverHaloExtraRadius, HoverHaloExtraRadius);
                    using var haloBrush = new SolidBrush(Color.FromArgb(HoverHaloAlpha, DefaultPointColor));
                    g.FillRectangle(haloBrush, haloRect);
                }

                g.DrawImage(im.Image, rect);

                if (isHover)
                {
                    using var ringPen = new Pen(Color.FromArgb(HoverRingAlpha, Color.White), 1.5f);
                    g.DrawRectangle(ringPen, rect.X, rect.Y, rect.Width, rect.Height);
                }

                if (im.Selected)
                {
                    var selRect = RectangleF.Inflate(rect, SelectionRingExtraRadius, SelectionRingExtraRadius);
                    using var selectionPen = new Pen(SelectedPointColor, 2f);
                    g.DrawRectangle(selectionPen, selRect.X, selRect.Y, selRect.Width, selRect.Height);
                }
            }
        }

        private void DrawImageLabels(Graphics g)
        {
            foreach (var im in Images)
            {
                if (!im.ShowLabel || string.IsNullOrEmpty(im.Label)) continue;

                var size = im.ComputeScreenSize(_pixelsPerMeter);
                var screen = Project(im.World);
                var offset = im.LabelOffset ?? new PointF(size.Width / 2f + 2, -size.Height / 2f);
                DrawLabelWithBackdrop(g, im.Label, new PointF(screen.X + offset.X, screen.Y + offset.Y));
            }
        }

        private void DrawShapeMarkers(Graphics g)
        {
            // Same "painter's algorithm" as points/images - farther shapes are drawn first.
            var ordered = Shapes
                .Select(sh => (Shape: sh, Screen: Project(sh.World, out float depth), Depth: depth))
                .OrderByDescending(t => t.Depth);

            foreach (var (sh, screen, _) in ordered)
            {
                var size = sh.ComputeScreenSize(_pixelsPerMeter);
                var rect = new RectangleF(screen.X - size.Width / 2f, screen.Y - size.Height / 2f, size.Width, size.Height);
                float cornerRadius = sh.ComputeScreenCornerRadius(_pixelsPerMeter);
                bool isHover = sh.HoverEnabled && ReferenceEquals(sh, _hoverShape);

                if (isHover)
                {
                    // Same principle as point/image hover glow - a subtle extra semi-transparent outline of the same shape.
                    var haloRect = RectangleF.Inflate(rect, HoverHaloExtraRadius, HoverHaloExtraRadius);
                    Color haloColor = (sh.Brush as SolidBrush)?.Color ?? DefaultPointColor;
                    using var haloPath = BuildMarkerShapePath(sh.ShapeType, haloRect, cornerRadius + HoverHaloExtraRadius);
                    using var haloBrush = new SolidBrush(Color.FromArgb(HoverHaloAlpha, haloColor));
                    g.FillPath(haloBrush, haloPath);
                }

                using var shapePath = BuildMarkerShapePath(sh.ShapeType, rect, cornerRadius);

                if (sh.Brush != null)
                {
                    g.FillPath(sh.Brush, shapePath);
                }
                else
                {
                    using var fillBrush = new SolidBrush(DefaultPointColor);
                    g.FillPath(fillBrush, shapePath);
                }

                if (sh.Pen != null)
                    g.DrawPath(sh.Pen, shapePath);

                if (isHover)
                {
                    using var ringPen = new Pen(Color.FromArgb(HoverRingAlpha, Color.White), 1.5f);
                    g.DrawPath(ringPen, shapePath);
                }

                if (sh.Selected)
                {
                    var selRect = RectangleF.Inflate(rect, SelectionRingExtraRadius, SelectionRingExtraRadius);
                    using var selPath = BuildMarkerShapePath(sh.ShapeType, selRect, cornerRadius + SelectionRingExtraRadius);
                    using var selectionPen = new Pen(SelectedPointColor, 2f);
                    g.DrawPath(selectionPen, selPath);
                }
            }
        }

        private void DrawShapeMarkerLabels(Graphics g)
        {
            foreach (var sh in Shapes)
            {
                if (!sh.ShowLabel || string.IsNullOrEmpty(sh.Label)) continue;

                var size = sh.ComputeScreenSize(_pixelsPerMeter);
                var screen = Project(sh.World);
                var offset = sh.LabelOffset ?? new PointF(size.Width / 2f + 2, -size.Height / 2f);
                DrawLabelWithBackdrop(g, sh.Label, new PointF(screen.X + offset.X, screen.Y + offset.Y));
            }
        }

        /// <summary>Builds the GraphicsPath for a shape marker's outline - used consistently for the fill,
        /// outline, hover glow, and selection ring, same principle as BuildShapePath for point shapes.</summary>
        private static GraphicsPath BuildMarkerShapePath(MarkerShapeType type, RectangleF rect, float cornerRadius)
        {
            switch (type)
            {
                case MarkerShapeType.Rectangle:
                    var rectPath = new GraphicsPath();
                    rectPath.AddRectangle(rect);
                    return rectPath;

                case MarkerShapeType.RoundedRectangle:
                    // Clamp so the corner radius never exceeds half the shorter side (AddArc would otherwise misbehave).
                    float maxRadius = Math.Min(rect.Width, rect.Height) / 2f;
                    return RoundedRect(rect, Math.Max(0f, Math.Min(cornerRadius, maxRadius)));

                case MarkerShapeType.Ellipse:
                case MarkerShapeType.Circle:
                default:
                    var ellipsePath = new GraphicsPath();
                    ellipsePath.AddEllipse(rect);
                    return ellipsePath;
            }
        }

        /// <summary>A "rubber band" line from the last placed vertex to the cursor during interactive
        /// drawing - and for a polygon, an indication of closing back to the first vertex.</summary>
        private void DrawDrawingPreview(Graphics g)
        {
            if (!IsDrawing || _drawingPreviewScreenPos == null) return;

            var vertices = _drawingPolyline?.Vertices ?? _drawingPolygon?.Vertices;
            if (vertices == null || vertices.Count == 0) return;

            var cursor = _drawingPreviewScreenPos.Value;
            var lastScreen = Project(vertices[^1].World);

            using var previewPen = new Pen(Color.FromArgb(160, Color.Gray), 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLine(previewPen, lastScreen, cursor);

            if (_drawingPolygon != null && vertices.Count >= 2)
            {
                var firstScreen = Project(vertices[0].World);
                using var closePen = new Pen(Color.FromArgb(90, Color.Gray), 1f) { DashStyle = DashStyle.Dot };
                g.DrawLine(closePen, cursor, firstScreen);
            }
        }

        /// <summary>Draws the measuring tool's line (live preview after the first click, solid once
        /// finished) with a running/final distance label - reuses FormatMeters/DrawLabelWithBackdrop from
        /// the scale bar so the number formatting is consistent everywhere in the engine.</summary>
        private void DrawMeasurement(Graphics g)
        {
            if (_measureStart == null) return;

            var startScreen = Project(_measureStart.Value);

            Vector3 endWorld;
            PointF endScreen;
            bool isFinished = _measureEnd != null;

            if (isFinished)
            {
                endWorld = _measureEnd!.Value;
                endScreen = Project(endWorld);
            }
            else if (_measurePreviewScreenPos != null)
            {
                endScreen = _measurePreviewScreenPos.Value;
                endWorld = ScreenToWorld(endScreen, depth: 0f); // approximate - only used for the live label
            }
            else
            {
                return; // first point placed, but the cursor hasn't moved yet
            }

            using var linePen = new Pen(Color.DeepPink, 2f) { DashStyle = isFinished ? DashStyle.Solid : DashStyle.Dash };
            g.DrawLine(linePen, startScreen, endScreen);

            const float markerRadius = 4f;
            using var markerBrush = new SolidBrush(Color.DeepPink);
            g.FillEllipse(markerBrush, startScreen.X - markerRadius, startScreen.Y - markerRadius, markerRadius * 2, markerRadius * 2);
            g.FillEllipse(markerBrush, endScreen.X - markerRadius, endScreen.Y - markerRadius, markerRadius * 2, markerRadius * 2);

            float distance = Vector3.Distance(_measureStart.Value, endWorld);
            var midScreen = new PointF((startScreen.X + endScreen.X) / 2f, (startScreen.Y + endScreen.Y) / 2f);
            DrawLabelWithBackdrop(g, FormatMeters(distance), midScreen);
        }

        #endregion
    }
}
