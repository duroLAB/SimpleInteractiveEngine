using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ActiveCanvasEngine
{
    public partial class ActiveCanvas
    {
        // Removing single items - with everything that depends on them, so the scene never ends up with a
        // polyline pointing at a deleted point or a connector hanging on a deleted shape:
        //
        //   Point3D        - also taken out of every polyline/polygon; a polyline left with < 2 vertices or a
        //                    polygon left with < 3 is removed as well
        //   Polyline/Polygon - its own vertices go too (unless removeVertices: false, or another line/area uses them)
        //   ShapeMarker    - its connectors go too
        //   ImageMarker, ShapeConnector - just the item itself
        //
        //     engine.ItemRemoved += (s, item) => repository.Delete(((dynamic)item).Id);
        //     this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) engine.RemoveSelected(); };

        #region Removing items

        /// <summary>
        /// Fires once for every item removed by Remove()/RemoveSelected() - including the ones removed along
        /// with it (a polyline's own vertices, a shape's connectors, a polygon left with too few vertices).
        /// Fires after the item is gone from the canvas. Not fired by Clear().
        /// </summary>
        public event EventHandler<object>? ItemRemoved;

        /// <summary>
        /// Removes an item (Point3D, Polyline, Polygon, ImageMarker, ShapeMarker or ShapeConnector) together
        /// with whatever depends on it - see the overview at the top of ActiveCanvas.Remove.cs. Clears the
        /// selection/hover/drag if it pointed at a removed item. Returns false if the item isn't on this canvas.
        /// A line/area that is still being drawn is cancelled instead (same as CancelDrawing()).
        /// An ImageMarker's image is NOT disposed, so the marker can be added back to Images later - dispose
        /// marker.Image yourself when you're sure it's no longer needed.
        /// </summary>
        /// <param name="removeVertices">For a Polyline/Polygon: also remove its vertices that no other
        /// polyline/polygon uses (default). Pass false to keep them as standalone points.</param>
        public bool Remove(object item, bool removeVertices = true)
        {
            if (item == null) return false;

            if (ReferenceEquals(item, _drawingPolyline) || ReferenceEquals(item, _drawingPolygon))
            {
                CancelDrawing();
                return true;
            }

            var removed = new List<object>();
            bool found = item switch
            {
                Point3D point => RemovePointCore(point, removeVertices, removed),
                Polyline polyline => RemovePolylineCore(polyline, removeVertices, removed),
                Polygon polygon => RemovePolygonCore(polygon, removeVertices, removed),
                ImageMarker image => RemoveFromList(Images, image, removed),
                ShapeMarker shape => RemoveShapeCore(shape, removed),
                ShapeConnector connector => RemoveFromList(Connectors, connector, removed),
                _ => false
            };

            if (!found) return false;

            AfterRemove(removed);
            return true;
        }

        /// <summary>Removes whatever is currently selected (see Remove). Returns false if nothing is selected.</summary>
        public bool RemoveSelected(bool removeVertices = true)
            => _selectedShape != null && Remove(_selectedShape, removeVertices);

        private bool RemovePointCore(Point3D point, bool removeVertices, List<object> removed)
        {
            bool found = Points.Remove(point);

            foreach (var polyline in Polylines)
                if (polyline.Vertices.RemoveAll(v => ReferenceEquals(v, point)) > 0) found = true;
            foreach (var polygon in Polygons)
                if (polygon.Vertices.RemoveAll(v => ReferenceEquals(v, point)) > 0) found = true;

            if (!found) return false;

            _drawingOwnedVertices.Remove(point);
            removed.Add(point);

            // A line/area that no longer makes sense goes too (but never the one still being drawn).
            foreach (var polyline in Polylines.Where(pl => pl.Vertices.Count < 2 && !ReferenceEquals(pl, _drawingPolyline)).ToList())
                RemovePolylineCore(polyline, removeVertices, removed);
            foreach (var polygon in Polygons.Where(pg => pg.Vertices.Count < 3 && !ReferenceEquals(pg, _drawingPolygon)).ToList())
                RemovePolygonCore(polygon, removeVertices, removed);

            return true;
        }

        private bool RemovePolylineCore(Polyline polyline, bool removeVertices, List<object> removed)
        {
            if (!Polylines.Remove(polyline)) return false;
            removed.Add(polyline);
            if (removeVertices) RemoveUnusedVertices(polyline.Vertices, removed);
            return true;
        }

        private bool RemovePolygonCore(Polygon polygon, bool removeVertices, List<object> removed)
        {
            if (!Polygons.Remove(polygon)) return false;
            removed.Add(polygon);
            if (removeVertices) RemoveUnusedVertices(polygon.Vertices, removed);
            return true;
        }

        /// <summary>Removes the given vertices from Points unless another polyline/polygon still uses them.
        /// The removed line/area keeps its own Vertices list untouched, so it could be added back later.</summary>
        private void RemoveUnusedVertices(IEnumerable<Point3D> vertices, List<object> removed)
        {
            foreach (var vertex in vertices.Distinct().ToList())
            {
                bool stillUsed = Polylines.Any(pl => pl.Vertices.Any(v => ReferenceEquals(v, vertex)))
                              || Polygons.Any(pg => pg.Vertices.Any(v => ReferenceEquals(v, vertex)));
                if (stillUsed) continue;

                if (Points.Remove(vertex))
                {
                    _drawingOwnedVertices.Remove(vertex);
                    removed.Add(vertex);
                }
            }
        }

        private bool RemoveShapeCore(ShapeMarker shape, List<object> removed)
        {
            if (!Shapes.Remove(shape)) return false;
            removed.Add(shape);

            // A connector can't exist without both of its shapes.
            foreach (var connector in Connectors.Where(c => ReferenceEquals(c.From, shape) || ReferenceEquals(c.To, shape)).ToList())
                RemoveFromList(Connectors, connector, removed);

            return true;
        }

        private static bool RemoveFromList<T>(List<T> list, T item, List<object> removed) where T : class
        {
            if (!list.Remove(item)) return false;
            removed.Add(item);
            return true;
        }

        /// <summary>Clears selection/hover/drag state pointing at removed items, re-renders once, then fires
        /// ItemRemoved for each removed item.</summary>
        private void AfterRemove(List<object> removed)
        {
            bool WasRemoved(object? candidate) => candidate != null && removed.Any(r => ReferenceEquals(r, candidate));

            // A removed item keeps no "selected" look, in case it's ever added back.
            foreach (var item in removed)
            {
                switch (item)
                {
                    case Point3D p: p.SetSelected(false); break;
                    case Polyline pl: pl.SetSelected(false); break;
                    case Polygon pg: pg.SetSelected(false); break;
                    case ImageMarker im: im.SetSelected(false); break;
                    case ShapeMarker sh: sh.SetSelected(false); break;
                    case ShapeConnector c: c.SetSelected(false); break;
                }
            }

            if (WasRemoved(_hoverPoint)) _hoverPoint = null;
            if (WasRemoved(_hoverImage)) _hoverImage = null;
            if (WasRemoved(_hoverShape)) _hoverShape = null;
            if (WasRemoved(_hoverConnector)) _hoverConnector = null;

            if (WasRemoved(_draggingPoint) || WasRemoved(_draggingImage) || WasRemoved(_draggingShape))
            {
                _draggingPoint = null;
                _draggingImage = null;
                _draggingShape = null;
                if (!IsDrawing && !_measuringActive) _pictureBox.Cursor = Cursors.Default;
            }

            if (WasRemoved(_selectedShape))
                Select(null); // fires SelectionChanged and renders
            else
                Render();

            foreach (var item in removed)
                ItemRemoved?.Invoke(this, item);
        }

        #endregion
    }
}
