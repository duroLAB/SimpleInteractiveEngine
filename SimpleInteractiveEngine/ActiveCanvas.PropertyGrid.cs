using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // Support for showing an item's properties in a WinForms PropertyGrid. The grid is never bound
        // directly to Point3D/Polyline/Polygon/ImageMarker - those intentionally have private setters
        // (see the With... method design throughout this engine) and some of their properties (Vector3,
        // Brush, Pen) don't have a nice built-in PropertyGrid editor anyway. Instead, each type gets a
        // small view-model wrapper below with plain public properties; editing one calls the matching
        // With... method and re-renders, so the grid stays a thin, honest view onto the real object.

        #region Property grid

        /// <summary>
        /// Creates a ready-to-use PropertyGrid bound to the given item (Point3D / Polyline / Polygon /
        /// ImageMarker) - editing a value in the grid updates the item directly and re-renders the canvas.
        /// Drop the returned control into your own dialog, typically from PointDoubleClicked or a similar event:
        ///
        ///     engine.PointDoubleClicked += (s, p) =>
        ///     {
        ///         using var dlg = new Form { Text = "Point properties", Width = 320, Height = 420 };
        ///         dlg.Controls.Add(engine.CreatePropertyGrid(p, hiddenProperties: new[] { "Id", "HoverEnabled" }));
        ///         dlg.ShowDialog();
        ///     };
        ///
        /// hiddenProperties is purely a display-level filter - it hides the named properties (by their
        /// PropertyGrid name, e.g. "Id", "Draggable", "ShowLabel") without touching the underlying view-model
        /// class, so the same wrapper types work for both a "full" and a "simplified" dialog.
        ///
        /// Returns null if the item's type isn't recognized.
        /// </summary>
        public PropertyGrid? CreatePropertyGrid(object item, IEnumerable<string>? hiddenProperties = null)
        {
            object? viewModel = item switch
            {
                Point3D p => new Point3DProperties(p, this),
                Polyline pl => new PolylineProperties(pl, this),
                Polygon pg => new PolygonProperties(pg, this),
                ImageMarker im => new ImageMarkerProperties(im, this),
                _ => null
            };

            if (viewModel == null) return null;

            var hiddenSet = hiddenProperties?.ToArray();
            object gridSource = (hiddenSet != null && hiddenSet.Length > 0)
                ? new FilteredPropertyView(viewModel, hiddenSet)
                : viewModel;

            return new PropertyGrid
            {
                Dock = DockStyle.Fill,
                ToolbarVisible = false,
                PropertySort = PropertySort.Categorized,
                SelectedObject = gridSource
            };
        }

        #endregion
    }

    /// <summary>
    /// Wraps any object and hides the named properties from a PropertyGrid, without changing the
    /// wrapped object's class. Purely a display-level filter - works with any of the *_Properties
    /// view-models below (or any other object), matched by property name (e.g. "Id", "Draggable").
    /// </summary>
    internal class FilteredPropertyView : ICustomTypeDescriptor
    {
        private readonly object _target;
        private readonly HashSet<string> _hidden;

        public FilteredPropertyView(object target, IEnumerable<string> hiddenPropertyNames)
        {
            _target = target;
            _hidden = new HashSet<string>(hiddenPropertyNames);
        }

        public PropertyDescriptorCollection GetProperties() => GetProperties(null);

        public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        {
            var visible = TypeDescriptor.GetProperties(_target, attributes ?? Array.Empty<Attribute>(), true)
                .Cast<PropertyDescriptor>()
                .Where(p => !_hidden.Contains(p.Name))
                .ToArray();
            return new PropertyDescriptorCollection(visible);
        }

        // Everything else just delegates to the wrapped object, unchanged - GetPropertyOwner is the key
        // one: it tells the PropertyGrid to actually read/write values on _target, not on this wrapper.
        public object GetPropertyOwner(PropertyDescriptor? pd) => _target;
        public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(_target, true);
        public string? GetClassName() => TypeDescriptor.GetClassName(_target, true);
        public string? GetComponentName() => TypeDescriptor.GetComponentName(_target, true);
        public TypeConverter GetConverter() => TypeDescriptor.GetConverter(_target, true);
        public EventDescriptor? GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(_target, true);
        public PropertyDescriptor? GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(_target, true);
        public object? GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(_target, editorBaseType, true);
        public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(_target, true);
        public EventDescriptorCollection GetEvents(Attribute[]? attributes) => TypeDescriptor.GetEvents(_target, attributes ?? Array.Empty<Attribute>(), true);
    }

    #region Property grid view-models

    /// <summary>PropertyGrid view-model for Point3D - see ActiveCanvas.CreatePropertyGrid().</summary>
    public class Point3DProperties
    {
        private readonly ActiveCanvas.Point3D _point;
        private readonly ActiveCanvas _engine;

        internal Point3DProperties(ActiveCanvas.Point3D point, ActiveCanvas engine)
        {
            _point = point;
            _engine = engine;
        }

        [Category("Position")]
        [Description("Raw (real-world CRS) coordinate - the engine's local offset, if any (SetLocalOrigin), is handled transparently here.")]
        public float X { get => _engine.ToRaw(_point.World).X; set => Move(value, Y, Z); }

        [Category("Position")]
        [Description("Raw (real-world CRS) coordinate - the engine's local offset, if any (SetLocalOrigin), is handled transparently here.")]
        public float Y { get => _engine.ToRaw(_point.World).Y; set => Move(X, value, Z); }

        [Category("Position")]
        [Description("Raw (real-world CRS) coordinate - the engine's local offset, if any (SetLocalOrigin), is handled transparently here.")]
        public float Z { get => _engine.ToRaw(_point.World).Z; set => Move(X, Y, value); }

        [Category("Appearance")]
        public string Label { get => _point.Label; set { _point.WithLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        public bool ShowLabel { get => _point.ShowLabel; set { _point.WithShowLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        public ActiveCanvas.PointShape Shape { get => _point.Shape; set { _point.WithShape(value); _engine.Render(); } }

        [Category("Appearance")]
        public float Size { get => _point.Size ?? _engine.PointScreenRadius; set { _point.WithSize(value); _engine.Render(); } }

        [Category("Appearance")]
        [Description("Fill color. Only editable here if the point uses a plain solid color (no custom gradient/hatch brush).")]
        public Color Color
        {
            get => (_point.Brush as SolidBrush)?.Color ?? _engine.DefaultPointColor;
            set { _point.WithBrush(new SolidBrush(value)); _engine.Render(); }
        }

        [Category("Behavior")]
        public bool Selectable { get => _point.Selectable; set { _point.WithSelectable(value); _engine.Render(); } }

        [Category("Behavior")]
        public bool Draggable { get => _point.Draggable; set { _point.WithDraggable(value); _engine.Render(); } }

        [Category("Behavior")]
        public bool HoverEnabled { get => _point.HoverEnabled; set { _point.WithHover(value); _engine.Render(); } }

        [Category("Identity"), ReadOnly(true)]
        public Guid Id => _point.Id;

        private void Move(float x, float y, float z)
        {
            _point.MoveTo(_engine.ToLocal(new Vector3(x, y, z)));
            _engine.Render();
        }
    }

    /// <summary>PropertyGrid view-model for Polyline - see ActiveCanvas.CreatePropertyGrid().</summary>
    public class PolylineProperties
    {
        private readonly ActiveCanvas.Polyline _line;
        private readonly ActiveCanvas _engine;

        internal PolylineProperties(ActiveCanvas.Polyline line, ActiveCanvas engine)
        {
            _line = line;
            _engine = engine;
        }

        [Category("Appearance")]
        public string Label { get => _line.Label; set { _line.WithLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        public bool ShowLabel { get => _line.ShowLabel; set { _line.WithShowLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        public Color LineColor
        {
            get => _line.LinePen?.Color ?? _engine.DefaultLineColor;
            set { _line.WithLinePen(new Pen(value, LineWidth)); _engine.Render(); }
        }

        [Category("Appearance")]
        public float LineWidth
        {
            get => _line.LinePen?.Width ?? 2f;
            set { _line.WithLinePen(new Pen(LineColor, value)); _engine.Render(); }
        }

        [Category("Behavior")]
        [Description("If false, the line itself can't be clicked (its vertex points are unaffected).")]
        public bool Selectable { get => _line.Selectable; set { _line.WithSelectable(value); _engine.Render(); } }

        [Category("Identity"), ReadOnly(true)]
        public Guid Id => _line.Id;

        [Category("Identity"), ReadOnly(true)]
        public int VertexCount => _line.Vertices.Count;
    }

    /// <summary>PropertyGrid view-model for Polygon - see ActiveCanvas.CreatePropertyGrid().</summary>
    public class PolygonProperties
    {
        private readonly ActiveCanvas.Polygon _polygon;
        private readonly ActiveCanvas _engine;

        internal PolygonProperties(ActiveCanvas.Polygon polygon, ActiveCanvas engine)
        {
            _polygon = polygon;
            _engine = engine;
        }

        [Category("Appearance")]
        public string Label { get => _polygon.Label; set { _polygon.WithLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        public bool ShowLabel { get => _polygon.ShowLabel; set { _polygon.WithShowLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        [Description("Fill color, including transparency (the default fill is semi-transparent).")]
        public Color FillColor
        {
            get => (_polygon.FillBrush as SolidBrush)?.Color ?? _engine.DefaultPolygonFillColor;
            set { _polygon.WithFillBrush(new SolidBrush(value)); _engine.Render(); }
        }

        [Category("Appearance")]
        public Color OutlineColor
        {
            get => _polygon.OutlinePen?.Color ?? _engine.DefaultPolygonOutlineColor;
            set { _polygon.WithOutlinePen(new Pen(value, OutlineWidth)); _engine.Render(); }
        }

        [Category("Appearance")]
        public float OutlineWidth
        {
            get => _polygon.OutlinePen?.Width ?? 1.5f;
            set { _polygon.WithOutlinePen(new Pen(OutlineColor, value)); _engine.Render(); }
        }

        [Category("Behavior")]
        [Description("If false, the filled area can't be clicked (its vertex points are unaffected).")]
        public bool Selectable { get => _polygon.Selectable; set { _polygon.WithSelectable(value); _engine.Render(); } }

        [Category("Identity"), ReadOnly(true)]
        public Guid Id => _polygon.Id;

        [Category("Identity"), ReadOnly(true)]
        public int VertexCount => _polygon.Vertices.Count;
    }

    /// <summary>PropertyGrid view-model for ImageMarker - see ActiveCanvas.CreatePropertyGrid().</summary>
    public class ImageMarkerProperties
    {
        private readonly ActiveCanvas.ImageMarker _marker;
        private readonly ActiveCanvas _engine;

        internal ImageMarkerProperties(ActiveCanvas.ImageMarker marker, ActiveCanvas engine)
        {
            _marker = marker;
            _engine = engine;
        }

        [Category("Position")]
        [Description("Raw (real-world CRS) coordinate - the engine's local offset, if any (SetLocalOrigin), is handled transparently here.")]
        public float X { get => _engine.ToRaw(_marker.World).X; set => Move(value, Y, Z); }

        [Category("Position")]
        [Description("Raw (real-world CRS) coordinate - the engine's local offset, if any (SetLocalOrigin), is handled transparently here.")]
        public float Y { get => _engine.ToRaw(_marker.World).Y; set => Move(X, value, Z); }

        [Category("Position")]
        [Description("Raw (real-world CRS) coordinate - the engine's local offset, if any (SetLocalOrigin), is handled transparently here.")]
        public float Z { get => _engine.ToRaw(_marker.World).Z; set => Move(X, Y, value); }

        [Category("Appearance")]
        public string Label { get => _marker.Label; set { _marker.WithLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        public bool ShowLabel { get => _marker.ShowLabel; set { _marker.WithShowLabel(value); _engine.Render(); } }

        [Category("Appearance")]
        [Description("Width in pixels (fixed on-screen size), or in meters if ScaleWithZoom is true.")]
        public float Width { get => _marker.Width; set { _marker.WithSize(value, _marker.Height); _engine.Render(); } }

        [Category("Appearance")]
        [Description("If true, the icon grows/shrinks with zoom like a real-world object; if false, it keeps a fixed pixel size like a map pin.")]
        public bool ScaleWithZoom { get => _marker.ScaleWithZoom; set { _marker.WithScaleWithZoom(value); _engine.Render(); } }

        [Category("Behavior")]
        public bool Selectable { get => _marker.Selectable; set { _marker.WithSelectable(value); _engine.Render(); } }

        [Category("Behavior")]
        public bool Draggable { get => _marker.Draggable; set { _marker.WithDraggable(value); _engine.Render(); } }

        [Category("Identity"), ReadOnly(true)]
        public Guid Id => _marker.Id;

        private void Move(float x, float y, float z)
        {
            _marker.MoveTo(_engine.ToLocal(new Vector3(x, y, z)));
            _engine.Render();
        }
    }

    #endregion
}