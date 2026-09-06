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
        // Nested data types: PointShape, Point3D, Polyline, Polygon, ImageMarker.
        // These are "values" - their look/behavior is configured via the constructor + fluent With... methods.

        #region Nested types (Point3D, Polyline, Polygon, ImageMarker)

        /// <summary>Shape used to draw a point. Also applies to Polyline/Polygon vertices, since those are
        /// the same shared Point3D objects.</summary>
        public enum PointShape
        {
            Circle,
            Square,
            Triangle,
            Diamond,
            Star,
            Cross
        }

        /// <summary>
        /// A point in space (meters). For 2D usage just leave Z = 0.
        /// Look and behavior are configured exclusively via the constructor or fluent With... methods:
        ///
        ///     var p = engine.AddPoint(1, 2, 0, "A")
        ///         .WithBrush(new SolidBrush(Color.Purple))
        ///         .WithPen(new Pen(Color.Black, 1f))
        ///         .WithShape(ActiveCanvas.PointShape.Square)
        ///         .WithSize(9f)
        ///         .WithSelectable(false)
        ///         .WithHover(false);
        ///
        /// Note on Brush/Pen: the point only borrows them for drawing, it doesn't dispose them - if you
        /// create them yourself (not via DefaultPointColor), take care of disposing them yourself (typically
        /// keep them as long-lived, reusable objects rather than creating a new one per point).
        /// </summary>
        public class Point3D
        {
            /// <summary>Unique identifier of the point - either auto-generated, or supplied by the host
            /// application (e.g. a DB record ID), so the engine<->data link is direct.</summary>
            public Guid Id { get; }

            public Vector3 World { get; private set; }
            public string Label { get; private set; }
            public bool Selected { get; private set; }

            public Brush? Brush { get; private set; }
            public Pen? Pen { get; private set; }
            public float? Size { get; private set; }
            public PointShape Shape { get; private set; } = PointShape.Circle;
            public bool Selectable { get; private set; } = true;
            public bool Draggable { get; private set; } = true;
            public bool HoverEnabled { get; private set; } = true;
            public bool ShowLabel { get; private set; } = true;

            /// <summary>Custom label offset in pixels from the point. If null, uses the default (up and to the right of the point).</summary>
            public PointF? LabelOffset { get; private set; }

            public Point3D(float x, float y, float z = 0f, string label = "", Guid? id = null)
            {
                Id = id ?? Guid.NewGuid();
                World = new Vector3(x, y, z);
                Label = label;
            }

            public Point3D WithBrush(Brush brush) { Brush = brush; return this; }
            public Point3D WithPen(Pen pen) { Pen = pen; return this; }

            /// <summary>Point shape - circle (default), square, triangle, diamond, star, or cross.</summary>
            public Point3D WithShape(PointShape shape) { Shape = shape; return this; }

            /// <summary>Custom point radius in pixels. If not called, uses the engine's PointScreenRadius.</summary>
            public Point3D WithSize(float radiusPixels) { Size = radiusPixels; return this; }

            /// <summary>If false, the point can't be clicked or selected (HitTest ignores it) - a purely visual marker.
            /// Does not affect dragging - use the separate WithDraggable() for that.</summary>
            public Point3D WithSelectable(bool selectable) { Selectable = selectable; return this; }

            /// <summary>If false, the point can be clicked/selected, but can't be dragged with the mouse - locks its position.
            /// Independent of Selectable, so you can e.g. just block moving it without losing clickability.</summary>
            public Point3D WithDraggable(bool draggable) { Draggable = draggable; return this; }

            /// <summary>If false, the point never shows a hover effect (glow on mouse-over).</summary>
            public Point3D WithHover(bool enabled) { HoverEnabled = enabled; return this; }

            public Point3D WithLabel(string label) { Label = label; return this; }

            /// <summary>Hides/shows the point's label without having to change the text itself.</summary>
            public Point3D WithShowLabel(bool visible) { ShowLabel = visible; return this; }

            /// <summary>Custom label position as a pixel offset from the point's center (e.g. (0,-14) = above the point).</summary>
            public Point3D WithLabelOffset(float dxPixels, float dyPixels) { LabelOffset = new PointF(dxPixels, dyPixels); return this; }

            // The nested class has access to private members - the engine (ActiveCanvas) uses these
            // internal mutations when selecting and when dragging the point with the mouse.
            internal void SetSelected(bool selected) => Selected = selected;
            internal void MoveTo(Vector3 world) => World = world;
        }

        /// <summary>
        /// A polyline - its "joints" (vertices) are plain Point3D objects, also added to the main
        /// Points list, so they automatically get hit-testing, drag-and-drop and hover completely for
        /// free, exactly like any other point. The line just connects and draws them.
        /// </summary>
        public class Polyline
        {
            public Guid Id { get; }
            public List<Point3D> Vertices { get; } = new List<Point3D>();
            public Pen? LinePen { get; private set; }
            public string Label { get; private set; } = "";
            public bool ShowLabel { get; private set; } = true;
            public bool Selected { get; private set; }
            public bool Selectable { get; private set; } = true;

            /// <summary>Custom world-space label position. If null, the line's midpoint is used automatically.</summary>
            public Vector3? LabelPosition { get; private set; }

            public Polyline(Guid? id = null) { Id = id ?? Guid.NewGuid(); }

            public Polyline WithLinePen(Pen pen) { LinePen = pen; return this; }
            public Polyline WithLabel(string label) { Label = label; return this; }
            public Polyline WithShowLabel(bool visible) { ShowLabel = visible; return this; }
            public Polyline WithLabelPosition(float x, float y, float z = 0f) { LabelPosition = new Vector3(x, y, z); return this; }

            /// <summary>If false, the line itself can't be clicked (its vertices are unaffected - they have their own Selectable).</summary>
            public Polyline WithSelectable(bool selectable) { Selectable = selectable; return this; }

            /// <summary>The line's world-space midpoint - used as the label position if no custom one is set (WithLabelPosition).</summary>
            public Vector3 ComputeMidpoint()
            {
                if (Vertices.Count == 0) return Vector3.Zero;
                Vector3 sum = Vector3.Zero;
                foreach (var v in Vertices) sum += v.World;
                return sum / Vertices.Count;
            }

            internal void SetSelected(bool selected) => Selected = selected;
        }

        /// <summary>
        /// A closed polygon - its vertices are again shared Point3D objects (same principle as Polyline),
        /// so they can be dragged with the mouse the same way. It additionally has a filled area
        /// (semi-transparent by default).
        /// </summary>
        public class Polygon
        {
            public Guid Id { get; }
            public List<Point3D> Vertices { get; } = new List<Point3D>();
            public Brush? FillBrush { get; private set; }
            public Pen? OutlinePen { get; private set; }
            public string Label { get; private set; } = "";
            public bool ShowLabel { get; private set; } = true;
            public bool Selected { get; private set; }
            public bool Selectable { get; private set; } = true;

            /// <summary>Custom world-space label position. If null, the polygon's centroid is used automatically.</summary>
            public Vector3? LabelPosition { get; private set; }

            public Polygon(Guid? id = null) { Id = id ?? Guid.NewGuid(); }

            public Polygon WithFillBrush(Brush brush) { FillBrush = brush; return this; }
            public Polygon WithOutlinePen(Pen pen) { OutlinePen = pen; return this; }
            public Polygon WithLabel(string label) { Label = label; return this; }
            public Polygon WithShowLabel(bool visible) { ShowLabel = visible; return this; }
            public Polygon WithLabelPosition(float x, float y, float z = 0f) { LabelPosition = new Vector3(x, y, z); return this; }

            /// <summary>If false, the filled area can't be clicked (its vertices are unaffected - they have their own Selectable).</summary>
            public Polygon WithSelectable(bool selectable) { Selectable = selectable; return this; }

            /// <summary>Simple centroid (average of the vertices) - used as the label position if no custom one is set.</summary>
            public Vector3 ComputeCentroid()
            {
                if (Vertices.Count == 0) return Vector3.Zero;
                Vector3 sum = Vector3.Zero;
                foreach (var v in Vertices) sum += v.World;
                return sum / Vertices.Count;
            }

            internal void SetSelected(bool selected) => Selected = selected;
        }

        /// <summary>
        /// An image/icon placed at a point in the world - behaves like a Point3D (drag-and-drop, hover,
        /// selection), just a bitmap is drawn instead of a circle. By default it has a fixed size in
        /// pixels (like a map icon, doesn't grow with zoom) - for a "real" size in the world (e.g. an
        /// object's footprint, which should grow with zoom) use WithScaleWithZoom(true) and WithSize in meters.
        /// </summary>
        public class ImageMarker
        {
            public Guid Id { get; }
            public Vector3 World { get; private set; }
            public Image Image { get; private set; }
            public float Width { get; private set; } = 24f;
            public float? Height { get; private set; }
            public bool ScaleWithZoom { get; private set; }
            public bool Selectable { get; private set; } = true;
            public bool Draggable { get; private set; } = true;
            public bool HoverEnabled { get; private set; } = true;
            public bool Selected { get; private set; }
            public string Label { get; private set; } = "";
            public bool ShowLabel { get; private set; } = true;
            public PointF? LabelOffset { get; private set; }

            public ImageMarker(Image image, float x, float y, float z = 0f, Guid? id = null)
            {
                Id = id ?? Guid.NewGuid();
                Image = image ?? throw new ArgumentNullException(nameof(image));
                World = new Vector3(x, y, z);
            }

            /// <summary>Icon size. In pixels (default), or in meters if ScaleWithZoom = true.
            /// If height is omitted, it's computed from the image's aspect ratio.</summary>
            public ImageMarker WithSize(float width, float? height = null) { Width = width; Height = height; return this; }

            /// <summary>If true, the icon grows/shrinks with zoom (Width/Height are then in meters).
            /// If false (default), it always has the same on-screen size regardless of zoom (like a map pin).</summary>
            public ImageMarker WithScaleWithZoom(bool scale) { ScaleWithZoom = scale; return this; }

            /// <summary>If false, the icon can be clicked/selected, but can't be dragged with the mouse - locks its position.</summary>
            public ImageMarker WithDraggable(bool draggable) { Draggable = draggable; return this; }

            public ImageMarker WithSelectable(bool selectable) { Selectable = selectable; return this; }
            public ImageMarker WithHover(bool enabled) { HoverEnabled = enabled; return this; }
            public ImageMarker WithLabel(string label) { Label = label; return this; }
            public ImageMarker WithShowLabel(bool visible) { ShowLabel = visible; return this; }
            public ImageMarker WithLabelOffset(float dxPixels, float dyPixels) { LabelOffset = new PointF(dxPixels, dyPixels); return this; }

            /// <summary>Computes the actual on-screen size (in pixels) at the given current zoom level.</summary>
            internal SizeF ComputeScreenSize(float pixelsPerMeter)
            {
                float scale = ScaleWithZoom ? pixelsPerMeter : 1f;
                float w = Width * scale;
                float aspect = Image.Width > 0 ? (float)Image.Height / Image.Width : 1f;
                float h = (Height ?? Width * aspect) * scale;
                return new SizeF(w, h);
            }

            internal void SetSelected(bool selected) => Selected = selected;
            internal void MoveTo(Vector3 world) => World = world;
        }

        /// <summary>Shape drawn by a ShapeMarker.</summary>
        public enum MarkerShapeType
        {
            Circle,
            Ellipse,
            Rectangle,
            RoundedRectangle,

            /// <summary>Arbitrary outline defined by ShapeMarker.WithCustomPolygon() - a user-defined set of points.</summary>
            CustomPolygon
        }

        /// <summary>
        /// A simple vector shape (circle/ellipse/rectangle/rounded rectangle) anchored at a single point -
        /// no vertices, no per-corner editing. Behaves like ImageMarker: drag-and-drop moves the whole
        /// shape at once, and it can either keep a fixed on-screen size regardless of zoom, or scale with
        /// zoom like a real-world object (WithScaleWithZoom).
        /// </summary>
        public class ShapeMarker
        {
            public Guid Id { get; }
            public Vector3 World { get; private set; }
            public MarkerShapeType ShapeType { get; private set; }
            public float Width { get; private set; } = 40f;
            public float? Height { get; private set; }
            public float CornerRadius { get; private set; } = 8f; // only used for RoundedRectangle
            public bool ScaleWithZoom { get; private set; }
            public Brush? Brush { get; private set; }
            public Pen? Pen { get; private set; }
            public bool Selectable { get; private set; } = true;
            public bool Draggable { get; private set; } = true;
            public bool HoverEnabled { get; private set; } = true;
            public bool Selected { get; private set; }
            public string Label { get; private set; } = "";
            public bool ShowLabel { get; private set; } = true;
            public PointF? LabelOffset { get; private set; }

            /// <summary>Outline points for ShapeType.CustomPolygon, relative to the shape's own center and
            /// normalized to roughly -0.5..0.5 in both axes - Width/Height then scale this to the actual size.</summary>
            public IReadOnlyList<PointF>? CustomPoints { get; private set; }

            public ShapeMarker(float x, float y, float z = 0f, MarkerShapeType shapeType = MarkerShapeType.Circle, Guid? id = null)
            {
                Id = id ?? Guid.NewGuid();
                World = new Vector3(x, y, z);
                ShapeType = shapeType;
            }

            public ShapeMarker WithShapeType(MarkerShapeType shapeType) { ShapeType = shapeType; return this; }

            /// <summary>
            /// Defines a custom outline as a set of points relative to the shape's own center, normalized
            /// so the shape roughly spans -0.5..0.5 in both axes (Width/Height then scale it to the actual
            /// size) - e.g. a diamond: (0,-0.5), (0.5,0), (0,0.5), (-0.5,0). Needs at least 3 points.
            /// Automatically switches ShapeType to CustomPolygon.
            /// </summary>
            public ShapeMarker WithCustomPolygon(IEnumerable<PointF> relativePoints)
            {
                CustomPoints = relativePoints.ToList();
                ShapeType = MarkerShapeType.CustomPolygon;
                return this;
            }

            /// <summary>Size. In pixels (default), or in meters if ScaleWithZoom = true. If height is
            /// omitted, the shape is as tall as it is wide (a circle instead of an ellipse, a square
            /// instead of a rectangle).</summary>
            public ShapeMarker WithSize(float width, float? height = null) { Width = width; Height = height; return this; }

            /// <summary>Corner radius for RoundedRectangle (same unit as Width - pixels, or meters if ScaleWithZoom). Ignored for other shapes.</summary>
            public ShapeMarker WithCornerRadius(float radius) { CornerRadius = radius; return this; }

            /// <summary>If true, the shape grows/shrinks with zoom (Width/Height/CornerRadius are then in
            /// meters). If false (default), it always has the same on-screen size regardless of zoom.</summary>
            public ShapeMarker WithScaleWithZoom(bool scale) { ScaleWithZoom = scale; return this; }

            public ShapeMarker WithBrush(Brush brush) { Brush = brush; return this; }
            public ShapeMarker WithPen(Pen pen) { Pen = pen; return this; }

            /// <summary>If false, the shape can be clicked/selected, but can't be dragged with the mouse - locks its position.</summary>
            public ShapeMarker WithDraggable(bool draggable) { Draggable = draggable; return this; }

            public ShapeMarker WithSelectable(bool selectable) { Selectable = selectable; return this; }
            public ShapeMarker WithHover(bool enabled) { HoverEnabled = enabled; return this; }
            public ShapeMarker WithLabel(string label) { Label = label; return this; }
            public ShapeMarker WithShowLabel(bool visible) { ShowLabel = visible; return this; }
            public ShapeMarker WithLabelOffset(float dxPixels, float dyPixels) { LabelOffset = new PointF(dxPixels, dyPixels); return this; }

            /// <summary>Computes the actual on-screen size (in pixels) at the given current zoom level.</summary>
            internal SizeF ComputeScreenSize(float pixelsPerMeter)
            {
                float scale = ScaleWithZoom ? pixelsPerMeter : 1f;
                return new SizeF(Width * scale, (Height ?? Width) * scale);
            }

            /// <summary>Computes the actual on-screen corner radius (RoundedRectangle) at the given current zoom level.</summary>
            internal float ComputeScreenCornerRadius(float pixelsPerMeter)
                => CornerRadius * (ScaleWithZoom ? pixelsPerMeter : 1f);

            internal void SetSelected(bool selected) => Selected = selected;
            internal void MoveTo(Vector3 world) => World = world;
        }
        #endregion
    }
}
