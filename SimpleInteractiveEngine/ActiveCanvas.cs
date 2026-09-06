using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    /// <summary>
    /// Universal, simple interactive drawing engine for WinForms - covers both the 2D and 3D case
    /// with the same code.
    ///
    /// Key idea: 2D isn't a different problem than 3D, it's just a special case of it. "Top view"
    /// (ShowTopView) is mathematically the exact same orthographic projection as free 3D rotation,
    /// just with a fixed angle. Thanks to this the whole engine has ONE Project()/HitTest()/Render() -
    /// no duplication, no two places where the same thing can break (e.g. a forgotten redraw after zoom).
    ///
    /// Usage (pure 2D screen):
    ///     var engine = new ActiveCanvas(pictureBox1);   // default = top view, rotation disabled
    ///     engine.AddPoint(5, 3, 0, "A");
    ///     engine.SetBackgroundImage(mapBitmap, worldX: 0, worldY: 0, worldWidthMeters: 50);
    ///
    /// Usage (3D screen):
    ///     var engine = new ActiveCanvas(pictureBox1) { AllowFreeRotate = true };
    ///     engine.ShowIsometricView();
    ///
    /// Mouse controls:
    ///     left button (no drag)         -> select point (PointClicked event)
    ///     left button + drag            -> free rotation (only if AllowFreeRotate = true)
    ///     right/middle button + drag    -> pan
    ///     mouse wheel                   -> zoom (works independently of focus, via IMessageFilter)
    ///
    /// Coordinates: X = right, Y = forward/backward (depth), Z = up (height). Everything in meters.
    /// For pure 2D data just leave Z = 0.
    ///
    /// The class is split (partial) into several files by responsibility - see
    /// ActiveCanvas.Types.cs, .PublicApi.cs, .Camera.cs, .Input.cs, .Rendering.cs.
    /// This file is the "core": data, configuration, state, and constructor.
    /// </summary>
    public partial class ActiveCanvas
    {
        #region Data and configuration
        public List<Point3D> Points { get; } = new List<Point3D>();
        public List<Polyline> Polylines { get; } = new List<Polyline>();
        public List<Polygon> Polygons { get; } = new List<Polygon>();
        public List<ImageMarker> Images { get; } = new List<ImageMarker>();
        public List<ShapeMarker> Shapes { get; } = new List<ShapeMarker>();
        public List<ShapeConnector> Connectors { get; } = new List<ShapeConnector>();
        public float PointScreenRadius { get; set; } = 6f;

        /// <summary>Default point color, used when the point has no custom Brush (see Point3D.WithBrush).</summary>
        public Color DefaultPointColor { get; set; } = Color.SteelBlue;

        /// <summary>Color of the currently selected (clicked) point.</summary>
        public Color SelectedPointColor { get; set; } = Color.OrangeRed;

        /// <summary>Default polyline color, used when it has no custom LinePen (see Polyline.WithLinePen).</summary>
        public Color DefaultLineColor { get; set; } = Color.SteelBlue;

        /// <summary>Default (semi-transparent) polygon fill color, used when it has no custom FillBrush.</summary>
        public Color DefaultPolygonFillColor { get; set; } = Color.FromArgb(60, Color.SteelBlue);

        /// <summary>Default polygon outline color, used when it has no custom OutlinePen.</summary>
        public Color DefaultPolygonOutlineColor { get; set; } = Color.SteelBlue;

        /// <summary>Half-length of the ground grid (Z = 0), in meters. Set to 0 to disable the grid.</summary>
        public float GroundGridExtent { get; set; } = 10f;

        /// <summary>
        /// If false (default), the left mouse button is used EXCLUSIVELY for selecting a point - no
        /// rotation by dragging. This is the correct setting for pure 2D screens. Turn it on for 3D
        /// screens where the user should be able to freely rotate the scene.
        /// </summary>
        public bool AllowFreeRotate { get; set; } = false;

        /// <summary>Global switch to disable dragging for the whole canvas (e.g. a read-only screen). If you
        /// only need to lock a specific point/icon (not the whole canvas), use Point3D.WithDraggable(false) /
        /// ImageMarker.WithDraggable(false) - that's independent of selection (Selectable).</summary>
        public bool AllowDragPoints { get; set; } = true;
        #endregion

        #region Camera state, UI wiring and events
        // ---- Background bitmap (map/drawing), lying in the world plane at a given Z ----
        private Image? _backgroundImage;
        private Vector3 _bgWorldOrigin;
        private float _bgWorldWidth;
        private float _bgWorldHeight;

        // ---- Camera ----
        // Default = top view (pure 2D). For 3D scenes call ShowIsometricView() or set your own.
        private float _yawDeg = 0f;
        private float _pitchDeg = 90f;
        private float _pixelsPerMeter = 10f;
        private readonly float _minPixelsPerMeter = 0.5f;
        private readonly float _maxPixelsPerMeter = 800f;
        private PointF _panOffsetPixels = PointF.Empty; // in pixels, also includes centering

        // ---- UI wiring ----
        private readonly PictureBox _pictureBox;
        private Bitmap? _buffer;

        private bool _isRotating;
        private bool _isPanning;
        private Point _dragStart;
        private const float DragThreshold = 3f; // px - how far the mouse must move for it to not be a "click"

        private Point3D? _draggingPoint;
        private float _draggingDepth;
        private Point3D? _hoverPoint;

        private ImageMarker? _draggingImage;
        private float _draggingImageDepth;
        private ImageMarker? _hoverImage;

        private ShapeMarker? _draggingShape;
        private float _draggingShapeDepth;
        private ShapeMarker? _hoverShape;
        private ShapeConnector? _hoverConnector;

        /// <summary>Whatever is currently selected - Point3D, Polyline, Polygon, ImageMarker, or null. Always at most one thing at a time.</summary>
        private object? _selectedShape;

        public float PixelsPerMeter => _pixelsPerMeter;
        public float YawDegrees => _yawDeg;
        public float PitchDegrees => _pitchDeg;

        /// <summary>Whatever is currently selected (Point3D / Polyline / Polygon / ImageMarker), or null.
        /// The typed SelectedPoint / SelectedPolyline / SelectedPolygon / SelectedImage below are more convenient.</summary>
        public object? SelectedShape => _selectedShape;

        /// <summary>The currently selected point, if a point is what's selected (otherwise null). No need to iterate over Points.</summary>
        public Point3D? SelectedPoint => _selectedShape as Point3D;

        /// <summary>The currently selected polyline, if that's what's selected (otherwise null).</summary>
        public Polyline? SelectedPolyline => _selectedShape as Polyline;

        /// <summary>The currently selected polygon, if that's what's selected (otherwise null).</summary>
        public Polygon? SelectedPolygon => _selectedShape as Polygon;

        /// <summary>The currently selected icon/image, if that's what's selected (otherwise null).</summary>
        public ImageMarker? SelectedImage => _selectedShape as ImageMarker;

        /// <summary>The currently selected shape marker (circle/ellipse/rectangle/rounded rectangle), if that's what's selected (otherwise null).</summary>
        public ShapeMarker? SelectedShapeMarker => _selectedShape as ShapeMarker;

        /// <summary>The currently selected connector, if that's what's selected (otherwise null).</summary>
        public ShapeConnector? SelectedConnector => _selectedShape as ShapeConnector;

        // ---- Interactive drawing of a new polyline/polygon by clicking on the canvas ----
        private Polyline? _drawingPolyline;
        private Polygon? _drawingPolygon;
        private readonly HashSet<Point3D> _drawingOwnedVertices = new(); // points created by drawing (not snapped to an existing one) - removed on CancelDrawing
        private PointF? _drawingPreviewScreenPos; // cursor position, for the "rubber band" line after the last vertex

        // ---- Interactive placement of points/icons with a single click (no collecting multiple vertices) ----
        private sealed class PointPlacementOptions
        {
            public string Label = "";
            public Brush? Brush;
            public Pen? Pen;
            public float? Size;
            public PointShape Shape;
            public bool Selectable = true;
            public bool Draggable = true;
            public bool HoverEnabled = true;
            public bool Continuous = true;
        }

        private sealed class ImagePlacementOptions
        {
            public Image Image = null!;
            public string Label = "";
            public float Width = 24f;
            public float? Height;
            public bool ScaleWithZoom;
            public bool Selectable = true;
            public bool Draggable = true;
            public bool HoverEnabled = true;
            public bool Continuous = true;
        }

        private PointPlacementOptions? _placingPointOptions;
        private ImagePlacementOptions? _placingImageOptions;

        /// <summary>True if interactive drawing of a polyline/polygon, or placement of points/icons, is
        /// currently in progress (StartDrawingPolyline/Polygon, StartPlacingPoints/Images). A click on the
        /// canvas behaves differently than a normal selection during this - see CancelDrawing() to cancel any of these modes.</summary>
        public bool IsDrawing => _drawingPolyline != null || _drawingPolygon != null
            || _placingPointOptions != null || _placingImageOptions != null;

        /// <summary>True if the StartPlacingPoints() mode is currently active.</summary>
        public bool IsPlacingPoints => _placingPointOptions != null;

        /// <summary>True if the StartPlacingImages() mode is currently active.</summary>
        public bool IsPlacingImages => _placingImageOptions != null;

        // ---- Measuring tool - click two points to draw a line between them and see the distance ----
        private bool _measuringActive;
        private Vector3? _measureStart;
        private Vector3? _measureEnd;
        private PointF? _measurePreviewScreenPos;

        /// <summary>True while the measuring tool is actively waiting for a click (StartMeasuring() was
        /// called and the second point hasn't been placed yet). Becomes false again once the measurement
        /// is finished - the result stays visible until StartMeasuring() is called again.</summary>
        public bool IsMeasuring => _measuringActive;

        /// <summary>The current measurement's distance in meters, once both points are placed - null before that.</summary>
        public float? MeasuredDistance => (_measureStart != null && _measureEnd != null)
            ? Vector3.Distance(_measureStart.Value, _measureEnd.Value)
            : (float?)null;

        /// <summary>Fires once the measuring tool's second point is placed, with the measured distance (meters).</summary>
        public event EventHandler<float>? MeasurementCompleted;

        public event EventHandler<Point3D>? PointClicked;

        /// <summary>Click on the polyline's line itself (not on one of its vertices - that's handled by PointClicked).</summary>
        public event EventHandler<Polyline>? PolylineClicked;

        /// <summary>Click inside the polygon's area (not on one of its vertices - that's handled by PointClicked).</summary>
        public event EventHandler<Polygon>? PolygonClicked;

        /// <summary>Click on an icon/image.</summary>
        public event EventHandler<ImageMarker>? ImageClicked;

        /// <summary>Click on a shape marker (circle/ellipse/rectangle/rounded rectangle).</summary>
        public event EventHandler<ShapeMarker>? ShapeClicked;

        /// <summary>Click on a connector's line.</summary>
        public event EventHandler<ShapeConnector>? ConnectorClicked;

        /// <summary>Double-click on a point - fires in addition to PointClicked (which fires on both clicks
        /// of the double-click). Typical use: open a properties dialog (see CreatePropertyGrid()).</summary>
        public event EventHandler<Point3D>? PointDoubleClicked;

        /// <summary>Double-click on a polyline's line. See PointDoubleClicked.</summary>
        public event EventHandler<Polyline>? PolylineDoubleClicked;

        /// <summary>Double-click inside a polygon's area. See PointDoubleClicked.</summary>
        public event EventHandler<Polygon>? PolygonDoubleClicked;

        /// <summary>Double-click on an icon/image. See PointDoubleClicked.</summary>
        public event EventHandler<ImageMarker>? ImageDoubleClicked;

        /// <summary>Double-click on a shape marker. See PointDoubleClicked.</summary>
        public event EventHandler<ShapeMarker>? ShapeDoubleClicked;

        /// <summary>Double-click on a connector's line. See PointDoubleClicked.</summary>
        public event EventHandler<ShapeConnector>? ConnectorDoubleClicked;

        /// <summary>
        /// Fires during Render(), right after the background image layer - lets the host draw an arbitrary
        /// vector background (e.g. a parsed DXF drawing) directly into the canvas, respecting the current
        /// pan/zoom/rotation. Use Project() (already public) to convert your own world-space coordinates to
        /// screen positions; use GetVisibleWorldBounds() to cull entities outside the current viewport.
        ///
        ///     engine.CustomBackgroundPaint += (s, e) =>
        ///     {
        ///         var visible = engine.GetVisibleWorldBounds();
        ///         foreach (var line in dxfDocument.Lines.Where(l => visible.IntersectsWith(l.Bounds)))
        ///         {
        ///             var p1 = engine.Project(new Vector3(line.X1, line.Y1, 0));
        ///             var p2 = engine.Project(new Vector3(line.X2, line.Y2, 0));
        ///             e.Graphics.DrawLine(myPen, p1, p2);
        ///         }
        ///     };
        ///
        /// Drawn before the ground grid and all engine entities (points/polylines/polygons/icons stay
        /// visible and interactive on top of it), and after the raster background image, if any is set.
        /// </summary>
        public event EventHandler<CanvasPaintEventArgs>? CustomBackgroundPaint;

        /// <summary>Fires repeatedly while a point is being dragged (e.g. for live-updating a properties panel).</summary>
        public event EventHandler<Point3D>? PointMoved;

        /// <summary>Fires once, on mouse release - when dragging is definitively finished (as opposed to
        /// PointMoved, which fires repeatedly during the drag). Useful e.g. for saving to a DB.</summary>
        public event EventHandler<Point3D>? PointDragCompleted;

        /// <summary>Fires when the selection changes - by clicking a point/line/area, clicking empty space
        /// (clears the selection), or programmatically via Select()/SelectPoint(). The argument is a
        /// Point3D/Polyline/Polygon, or null.</summary>
        public event EventHandler<object?>? SelectionChanged;

        /// <summary>Fires on StartDrawingPolyline()/StartDrawingPolygon().</summary>
        public event EventHandler? DrawingStarted;

        /// <summary>Fires after every vertex added while drawing (a click on the canvas in drawing mode).</summary>
        public event EventHandler<Point3D>? DrawingVertexAdded;

        /// <summary>Fires when drawing a polyline is successfully finished (FinishDrawing(), at least 2 vertices).</summary>
        public event EventHandler<Polyline>? PolylineDrawingFinished;

        /// <summary>Fires when drawing a polygon is successfully finished (FinishDrawing(), at least 3 vertices).</summary>
        public event EventHandler<Polygon>? PolygonDrawingFinished;

        /// <summary>Fires on CancelDrawing() (or on FinishDrawing() without enough vertices).</summary>
        public event EventHandler? DrawingCancelled;

        /// <summary>Fires for every point placed while StartPlacingPoints() is active.</summary>
        public event EventHandler<Point3D>? PointPlaced;

        /// <summary>Fires for every icon placed while StartPlacingImages() is active.</summary>
        public event EventHandler<ImageMarker>? ImagePlaced;

        // PictureBox can never receive focus (ControlStyles.Selectable = false), so MouseWheel
        // never fires on it directly. We handle this via IMessageFilter - see the end of the file.
        private readonly WheelMessageFilter _wheelFilter;

        public ActiveCanvas(PictureBox pictureBox)
        {
            _pictureBox = pictureBox ?? throw new ArgumentNullException(nameof(pictureBox));

            _pictureBox.SizeMode = PictureBoxSizeMode.Normal;
            _pictureBox.BackColor = Color.White;

            _pictureBox.MouseDown += OnMouseDown;
            _pictureBox.MouseMove += OnMouseMove;
            _pictureBox.MouseUp += OnMouseUp;
            _pictureBox.MouseLeave += OnMouseLeave;
            _pictureBox.Resize += (s, e) => DeferredRebuildBuffer();

            _wheelFilter = new WheelMessageFilter(this);
            Application.AddMessageFilter(_wheelFilter);
            _pictureBox.Disposed += (s, e) =>
            {
                Application.RemoveMessageFilter(_wheelFilter);
                _backgroundImage?.Dispose();
            };

            if (_pictureBox.IsHandleCreated)
                RebuildBuffer();
            else
                _pictureBox.HandleCreated += (s, e) => RebuildBuffer();
        }
        #endregion
    }

    /// <summary>Event args for ActiveCanvas.CustomBackgroundPaint - just the Graphics to draw into.
    /// Convert your own world-space coordinates via the engine's public Project() method.</summary>
    public class CanvasPaintEventArgs : EventArgs
    {
        public Graphics Graphics { get; }
        internal CanvasPaintEventArgs(Graphics graphics) => Graphics = graphics;
    }
}
