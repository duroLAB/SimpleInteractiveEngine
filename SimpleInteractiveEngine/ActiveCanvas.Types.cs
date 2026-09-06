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

        /// <summary>Preset label position relative to a ShapeMarker's bounds - a convenient alternative to
        /// computing a pixel offset by hand (WithLabelOffset still works and takes priority if set).</summary>
        public enum LabelAnchor
        {
            TopLeft, Top, TopRight,
            Left, Center, Right,
            BottomLeft, Bottom, BottomRight
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

            /// <summary>Width in meters by default (ScaleWithZoom = true) - matches Point3D/Polyline/Polygon,
            /// which are always in meters. Switch to WithScaleWithZoom(false) for a fixed pixel size instead
            /// (e.g. an icon-style marker that should stay readable at any zoom level).</summary>
            public float Width { get; private set; } = 1f;
            public float? Height { get; private set; }

            /// <summary>Only used when ShapeType is RoundedRectangle. Same unit as Width (meters by default).</summary>
            public float CornerRadius { get; private set; } = 0.15f;

            /// <summary>True by default: Width/Height/CornerRadius are in meters and the shape scales with
            /// zoom, like a real-world object. Set to false for a fixed on-screen pixel size instead (like a map pin).</summary>
            public bool ScaleWithZoom { get; private set; } = true;
            public Brush? Brush { get; private set; }
            public Pen? Pen { get; private set; }
            public bool Selectable { get; private set; } = true;
            public bool Draggable { get; private set; } = true;
            public bool HoverEnabled { get; private set; } = true;
            public bool Selected { get; private set; }
            public string Label { get; private set; } = "";
            public bool ShowLabel { get; private set; } = true;
            public PointF? LabelOffset { get; private set; }

            /// <summary>Preset label position relative to the shape's bounds, used only when LabelOffset
            /// isn't explicitly set (WithLabelOffset always takes priority when present).</summary>
            public LabelAnchor LabelAnchor { get; private set; } = LabelAnchor.TopRight;

            /// <summary>Text drawn INSIDE the shape's own bounds (not a floating label next to it) -
            /// automatically word-wraps to fit, and honors explicit "\n" line breaks, since it's drawn via
            /// GDI+'s own multi-line text layout. Leave empty (default) for no inner text.</summary>
            public string InnerText { get; private set; } = "";
            public Color InnerTextColor { get; private set; } = Color.Black;
            public Font? InnerTextFont { get; private set; }

            /// <summary>If true, InnerText's font size is automatically picked (within MinInnerTextFontSize..
            /// MaxInnerTextFontSize) to best fill the shape's currently visible on-screen area - recalculated
            /// every frame, so it adapts as you zoom or resize the shape. InnerTextFont, if set, still
            /// supplies the font family/style - just not its size, which becomes dynamic.</summary>
            public bool AutoScaleInnerTextFont { get; private set; }
            public float MinInnerTextFontSize { get; private set; } = 6f;
            public float MaxInnerTextFontSize { get; private set; } = 24f;

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

            /// <summary>Preset label position (see LabelAnchor). Ignored once WithLabelOffset() is used - that always wins.</summary>
            public ShapeMarker WithLabelAnchor(LabelAnchor anchor) { LabelAnchor = anchor; return this; }

            /// <summary>Sets the text drawn inside the shape's own bounds. Wraps automatically and supports
            /// explicit "\n" line breaks - this is separate from Label (the floating label next to the shape).</summary>
            public ShapeMarker WithInnerText(string text) { InnerText = text; return this; }

            public ShapeMarker WithInnerTextColor(Color color) { InnerTextColor = color; return this; }

            /// <summary>Custom font for the inner text. If not set, uses the engine's default label font/size.
            /// If AutoScaleInnerTextFont is on, only this font's family/style are used - its size is picked dynamically.</summary>
            public ShapeMarker WithInnerTextFont(Font font) { InnerTextFont = font; return this; }

            /// <summary>Turns on (or off) automatic font sizing for InnerText, so it grows/shrinks to best
            /// fill the shape's current on-screen area instead of staying at one fixed size. Bounded by
            /// minSize/maxSize so it never becomes unreadably small or absurdly large.</summary>
            public ShapeMarker WithAutoScaleInnerText(bool enabled, float minSize = 6f, float maxSize = 24f)
            {
                AutoScaleInnerTextFont = enabled;
                MinInnerTextFontSize = minSize;
                MaxInnerTextFontSize = maxSize;
                return this;
            }

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

            /// <summary>Size in meters by default (ScaleWithZoom = true), or in pixels if you've called
            /// WithScaleWithZoom(false). If height is omitted, the shape is as tall as it is wide (a circle
            /// instead of an ellipse, a square instead of a rectangle).</summary>
            public ShapeMarker WithSize(float width, float? height = null) { Width = width; Height = height; return this; }

            /// <summary>Corner radius for RoundedRectangle (same unit as Width - meters by default, pixels if ScaleWithZoom is false). Ignored for other shapes.</summary>
            public ShapeMarker WithCornerRadius(float radius) { CornerRadius = radius; return this; }

            /// <summary>True by default: Width/Height/CornerRadius are in meters and the shape grows/shrinks
            /// with zoom, like a real-world object - consistent with Point3D/Polyline/Polygon, which are
            /// always in meters. Set to false for a fixed on-screen pixel size instead (e.g. an icon-style
            /// marker that should stay the same visible size at any zoom level, like a map pin).</summary>
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

        /// <summary>Routing strategy for ShapeConnector.</summary>
        public enum ConnectorRouting
        {
            /// <summary>A single direct line from the start attachment point to the end attachment point.</summary>
            Straight,

            /// <summary>An "elbow" connector - first a horizontal segment (change in X), then a vertical
            /// segment (change in Y) to reach the target. Common in flowchart/diagram-style connectors.</summary>
            Orthogonal
        }

        /// <summary>
        /// A line connecting two ShapeMarkers - has no position of its own, it derives its endpoints from
        /// the two shapes' current positions (and a chosen attachment point on each) every time it's drawn,
        /// so it automatically follows drag-and-drop of either shape with no extra bookkeeping needed.
        /// </summary>
        public class ShapeConnector
        {
            public Guid Id { get; }
            public ShapeMarker From { get; }
            public ShapeMarker To { get; }
            public LabelAnchor FromAnchor { get; private set; } = LabelAnchor.Center;
            public LabelAnchor ToAnchor { get; private set; } = LabelAnchor.Center;
            public ConnectorRouting Routing { get; private set; } = ConnectorRouting.Straight;
            public Pen? Pen { get; private set; }
            public bool ShowArrow { get; private set; } = true;
            public float ArrowSize { get; private set; } = 10f;
            public string Label { get; private set; } = "";
            public bool ShowLabel { get; private set; } = true;

            /// <summary>Custom world-space label position. If null, a sensible point along the route is used automatically.</summary>
            public Vector3? LabelPosition { get; private set; }

            public bool Selectable { get; private set; } = true;
            public bool HoverEnabled { get; private set; } = true;
            public bool Selected { get; private set; }

            public ShapeConnector(ShapeMarker from, ShapeMarker to, Guid? id = null)
            {
                Id = id ?? Guid.NewGuid();
                From = from ?? throw new ArgumentNullException(nameof(from));
                To = to ?? throw new ArgumentNullException(nameof(to));
            }

            /// <summary>Which point on each shape the line attaches to (e.g. Right on the "from" shape, Left on the "to" shape).</summary>
            public ShapeConnector WithAnchors(LabelAnchor fromAnchor, LabelAnchor toAnchor) { FromAnchor = fromAnchor; ToAnchor = toAnchor; return this; }

            public ShapeConnector WithRouting(ConnectorRouting routing) { Routing = routing; return this; }
            public ShapeConnector WithPen(Pen pen) { Pen = pen; return this; }

            /// <summary>Shows (or hides) an arrowhead at the "To" end, pointing in the direction of travel.</summary>
            public ShapeConnector WithArrow(bool show, float size = 10f) { ShowArrow = show; ArrowSize = size; return this; }

            public ShapeConnector WithLabel(string label) { Label = label; return this; }
            public ShapeConnector WithShowLabel(bool visible) { ShowLabel = visible; return this; }
            public ShapeConnector WithLabelPosition(float x, float y, float z = 0f) { LabelPosition = new Vector3(x, y, z); return this; }

            /// <summary>If false, the connector's line can't be clicked/selected.</summary>
            public ShapeConnector WithSelectable(bool selectable) { Selectable = selectable; return this; }
            public ShapeConnector WithHover(bool enabled) { HoverEnabled = enabled; return this; }

            internal void SetSelected(bool selected) => Selected = selected;
        }
        #endregion
    }
}
