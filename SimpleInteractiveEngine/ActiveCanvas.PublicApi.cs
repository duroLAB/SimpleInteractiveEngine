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
        // Public API for adding/removing scene content: points, lines, areas, icons,
        // and the background image.

        #region Local coordinate origin

        private Vector3 _localOrigin = Vector3.Zero;

        /// <summary>
        /// Sets a local coordinate origin - a pure offset, no axis flipping or rotation, so it works with
        /// any coordinate reference system (S-JTSK, UTM, whatever). After calling this, every subsequent
        /// AddPoint/AddPolyline/AddPolygon/AddImage/SetBackgroundImage call takes its x/y/z as RAW
        /// coordinates (e.g. real S-JTSK/UTM values), and the engine internally stores (raw - origin)
        /// instead. This keeps internal numbers close to zero, which matters because Vector3 uses float:
        /// raw coordinates in the hundreds of thousands (typical for most real-world CRSs) leave only a
        /// few centimeters of precision, while small, offset numbers keep sub-millimeter precision.
        ///
        /// The engine's own World values (e.g. Point3D.World, what you see while dragging) are always in
        /// this LOCAL space - convert back to raw coordinates explicitly with ToRaw() wherever you need
        /// the real-world value again (e.g. saving to a database). This engine never guesses or flips
        /// axes for you - that part is CRS-specific and stays entirely in your own code.
        ///
        /// Call this once, before adding any content - changing it afterwards does not move what's
        /// already on the canvas (their stored World values stay as they are).
        /// </summary>
        public void SetLocalOrigin(float x, float y, float z = 0f) => _localOrigin = new Vector3(x, y, z);

        /// <summary>Converts raw (real-world CRS) coordinates to this engine's local space (raw - origin).</summary>
        public Vector3 ToLocal(Vector3 raw) => raw - _localOrigin;

        /// <summary>Converts this engine's local-space coordinates back to raw (real-world CRS) coordinates (local + origin).</summary>
        public Vector3 ToRaw(Vector3 local) => local + _localOrigin;

        #endregion

        #region Public API - points, edges, polylines, polygons

        /// <summary>
        /// Adds a point. For pure 2D usage leave z = 0. Optionally sets its look/behavior right away -
        /// or use the returned instance with the fluent With... methods (see the Point3D XML comment).
        /// x/y/z are RAW coordinates - if SetLocalOrigin() was called, the offset is applied automatically.
        /// </summary>
        public Point3D AddPoint(float x, float y, float z = 0f, string label = "",
            Brush? brush = null, Pen? pen = null, float? size = null, PointShape shape = PointShape.Circle,
            bool selectable = true, bool draggable = true, bool hoverEnabled = true, Guid? id = null)
        {
            var local = ToLocal(new Vector3(x, y, z));
            var p = new Point3D(local.X, local.Y, local.Z, label, id)
                .WithShape(shape)
                .WithSelectable(selectable)
                .WithDraggable(draggable)
                .WithHover(hoverEnabled);

            if (brush != null) p.WithBrush(brush);
            if (pen != null) p.WithPen(pen);
            if (size.HasValue) p.WithSize(size.Value);

            Points.Add(p);
            Render();
            return p;
        }

        /// <summary>
        /// Convenient shortcut for a simple connection between two points - in reality just AddPolyline
        /// with two vertices (Polyline can do everything a separate "Edge" class would, and more).
        /// </summary>
        public Polyline AddEdge(Point3D a, Point3D b, string label = "", Pen? linePen = null, Guid? id = null)
            => AddPolyline(new[] { a, b }, label, linePen, id);

        /// <summary>
        /// Adds a polyline from existing points (e.g. created via AddPoint with a custom look). Points
        /// are automatically added to Points as well if they aren't there already - so they get
        /// hit-testing/drag/hover exactly as if you'd added them directly.
        /// </summary>
        public Polyline AddPolyline(IEnumerable<Point3D> vertices, string label = "", Pen? linePen = null, Guid? id = null)
        {
            var poly = new Polyline(id).WithLabel(label);
            if (linePen != null) poly.WithLinePen(linePen);

            foreach (var v in vertices)
            {
                poly.Vertices.Add(v);
                if (!Points.Contains(v))
                    Points.Add(v);
            }

            Polylines.Add(poly);
            Render();
            return poly;
        }

        /// <summary>Convenience overload - creates vertices directly from coordinates (default point look).</summary>
        /// <summary>Convenience overload - creates vertices directly from raw coordinates (default point look).
        /// Same offset handling as AddPoint - see SetLocalOrigin().</summary>
        public Polyline AddPolyline(IEnumerable<(float x, float y, float z)> worldPoints, string label = "", Pen? linePen = null)
        {
            var vertices = worldPoints.Select(c => ToLocal(new Vector3(c.x, c.y, c.z)))
                .Select(v => new Point3D(v.X, v.Y, v.Z)).ToList();
            return AddPolyline(vertices, label, linePen);
        }

        /// <summary>
        /// Adds a closed polygon from existing points (at least 3 vertices). Just like with AddPolyline,
        /// vertices are automatically added to Points too - hit-test/drag/hover for free.
        /// Has a semi-transparent fill by default (DefaultPolygonFillColor).
        /// </summary>
        public Polygon AddPolygon(IEnumerable<Point3D> vertices, string label = "", Brush? fillBrush = null, Pen? outlinePen = null, Guid? id = null)
        {
            var poly = new Polygon(id).WithLabel(label);
            if (fillBrush != null) poly.WithFillBrush(fillBrush);
            if (outlinePen != null) poly.WithOutlinePen(outlinePen);

            foreach (var v in vertices)
            {
                poly.Vertices.Add(v);
                if (!Points.Contains(v))
                    Points.Add(v);
            }

            Polygons.Add(poly);
            Render();
            return poly;
        }

        /// <summary>Convenience overload - creates vertices directly from raw coordinates (default point look).
        /// Same offset handling as AddPoint - see SetLocalOrigin().</summary>
        public Polygon AddPolygon(IEnumerable<(float x, float y, float z)> worldPoints, string label = "", Brush? fillBrush = null, Pen? outlinePen = null)
        {
            var vertices = worldPoints.Select(c => ToLocal(new Vector3(c.x, c.y, c.z)))
                .Select(v => new Point3D(v.X, v.Y, v.Z)).ToList();
            return AddPolygon(vertices, label, fillBrush, outlinePen);
        }

        /// <summary>
        /// Adds an icon/image at a point in the world. By default it has a fixed size in pixels
        /// (independent of zoom, like a map pin) - for a real-world size call WithScaleWithZoom(true)
        /// on the returned instance. The engine takes ownership of the image and disposes it on Clear()/replacement.
        /// x/y/z are RAW coordinates - if SetLocalOrigin() was called, the offset is applied automatically.
        /// </summary>
        public ImageMarker AddImage(Image image, float x, float y, float z = 0f, string label = "",
            float width = 24f, float? height = null, bool selectable = true, bool draggable = true,
            bool hoverEnabled = true, Guid? id = null)
        {
            var local = ToLocal(new Vector3(x, y, z));
            var marker = new ImageMarker(image, local.X, local.Y, local.Z, id)
                .WithSize(width, height)
                .WithLabel(label)
                .WithSelectable(selectable)
                .WithDraggable(draggable)
                .WithHover(hoverEnabled);

            Images.Add(marker);
            Render();
            return marker;
        }

        public void Clear()
        {
            Points.Clear();
            Polylines.Clear();
            Polygons.Clear();
            foreach (var im in Images) im.Image.Dispose();
            Images.Clear();
            _selectedShape = null;
            _drawingPolyline = null;
            _drawingPolygon = null;
            _drawingOwnedVertices.Clear();
            _drawingPreviewScreenPos = null;
            Render();
        }

        /// <summary>
        /// Finds any item by its Id (Point3D, Polyline, Polygon, or ImageMarker).
        /// Handy when you have a Guid from the host application (e.g. from a database) and need to find
        /// the matching item on the canvas, or vice versa - every item's Id can be read directly from its .Id property.
        /// </summary>
        public object? FindById(Guid id)
        {
            return (object?)Points.FirstOrDefault(p => p.Id == id)
                ?? (object?)Polylines.FirstOrDefault(pl => pl.Id == id)
                ?? Polygons.FirstOrDefault(pg => pg.Id == id)
                ?? (object?)Images.FirstOrDefault(im => im.Id == id);
        }

        /// <summary>
        /// Sets a background bitmap (e.g. a map or drawing) that lies in the world plane at a given
        /// height Z, stretching from a given corner by worldWidthMeters x worldHeightMeters. It pans and
        /// rotates together with the rest of the scene, since it's drawn through the same projection as everything else.
        /// worldX/worldY are RAW coordinates - if SetLocalOrigin() was called, the offset is applied automatically
        /// (this is also what makes SetBackgroundImageFromGeoTiff's positioning line up with points added via
        /// AddPoint/AddPolyline/etc. once a local origin is in use).
        /// </summary>
        /// <param name="image">The background image. The engine takes ownership and disposes it itself on replacement/close.</param>
        /// <param name="worldX">Raw X coordinate of the image's top-left corner (meters).</param>
        /// <param name="worldY">Raw Y coordinate of the image's top-left corner (meters). The image
        /// stretches from it towards minus Y (in top view that's the "down" direction on screen).</param>
        /// <param name="worldWidthMeters">Image width in the world (meters).</param>
        /// <param name="worldHeightMeters">World-space height; if omitted, it's computed from the image's aspect ratio.</param>
        /// <param name="worldZ">Raw height of the plane the background lies in (0 = ground).</param>
        public void SetBackgroundImage(Image image, float worldX, float worldY, float worldWidthMeters,
            float? worldHeightMeters = null, float worldZ = 0f)
        {
            _backgroundImage?.Dispose();
            _backgroundImage = image ?? throw new ArgumentNullException(nameof(image));

            _bgWorldOrigin = ToLocal(new Vector3(worldX, worldY, worldZ));
            _bgWorldWidth = worldWidthMeters;
            _bgWorldHeight = worldHeightMeters ?? worldWidthMeters * image.Height / image.Width;

            Render();
        }

        public void ClearBackgroundImage()
        {
            _backgroundImage?.Dispose();
            _backgroundImage = null;
            Render();
        }

        #endregion
    }
}