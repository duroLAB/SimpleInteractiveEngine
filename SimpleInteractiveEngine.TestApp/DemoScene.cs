using System.Drawing;
using System.Drawing.Drawing2D;

namespace SimpleDrawingEngine
{
    /// <summary>
    /// A demonstration of creating every primitive the engine supports. Call it e.g. from Form_Load:
    ///     DemoScene.Build(_engine);
    /// </summary>
    public static class DemoScene
    {
        public static void Build(ActiveCanvas engine)
        {
            // =====================================================================
            //  1. POINT - the simplest case
            // =====================================================================
            var basicPoint = engine.AddPoint(0, 0, 0, "Basic point");

            // A point with a custom look (brush, pen, size) - all via AddPoint's constructor/parameters
            var styledPoint = engine.AddPoint(4, 2, 0, "Custom look",
                brush: new SolidBrush(Color.MediumPurple),
                pen: new Pen(Color.Black, 1.5f),
                size: 9f);

            // A point that can be selected (a click could e.g. open a dialog), but can't be dragged - locked position
            var lockedPoint = engine.AddPoint(4, -2, 0, "Locked point",
                selectable: true, draggable: false);

            // A purely visual marker - can't be clicked, dragged, or show hover
            var decorativePoint = new ActiveCanvas.Point3D(-3, 3, 0, "Decoration")
                .WithBrush(new SolidBrush(Color.LightGray))
                .WithSelectable(false)
                .WithHover(false);
            engine.Points.Add(decorativePoint);

            // A point with a custom shape - a star instead of a circle
            var starPoint = engine.AddPoint(2, 6, 0, "Star",
                brush: new SolidBrush(Color.Gold), shape: ActiveCanvas.PointShape.Star, size: 10f);

            // =====================================================================
            //  2. POLYLINE - a line with vertices that can be dragged like ordinary points
            // =====================================================================

            // Fastest - directly from coordinates
            var route = engine.AddPolyline(
                new[] { (0f, 0f, 0f), (3f, 4f, 0f), (7f, 3f, 0f), (9f, 6f, 0f) },
                label: "Route A",
                linePen: new Pen(Color.ForestGreen, 3f));

            // Or from custom points (full control over each vertex's look)
            var v1 = new ActiveCanvas.Point3D(-5, -2, 0, "Start").WithBrush(new SolidBrush(Color.OrangeRed));
            var v2 = new ActiveCanvas.Point3D(-2, -4, 0, "End").WithBrush(new SolidBrush(Color.OrangeRed));
            var customRoute = engine.AddPolyline(new[] { v1, v2 }, label: "Route B");
            customRoute.WithLabelPosition(-3.5f, -3f, 0f); // custom label position instead of the midpoint

            // =====================================================================
            //  3. POLYGON - a closed area with a semi-transparent fill
            // =====================================================================

            // Default (semi-transparent blue) fill
            var zone = engine.AddPolygon(
                new[] { (10f, 0f, 0f), (15f, 0f, 0f), (15f, 4f, 0f), (10f, 4f, 0f) },
                label: "Zone A");

            // With a custom fill/outline color, and square vertices (a more technical look for
            // areas/drawings) - we create the vertices separately so we can set their shape.
            var rv1 = new ActiveCanvas.Point3D(10, -6, 0).WithShape(ActiveCanvas.PointShape.Square).WithSize(6f);
            var rv2 = new ActiveCanvas.Point3D(14, -6, 0).WithShape(ActiveCanvas.PointShape.Square).WithSize(6f);
            var rv3 = new ActiveCanvas.Point3D(12, -2, 0).WithShape(ActiveCanvas.PointShape.Square).WithSize(6f);
            var restrictedZone = engine.AddPolygon(new[] { rv1, rv2, rv3 },
                label: "No entry",
                fillBrush: new SolidBrush(Color.FromArgb(70, Color.OrangeRed)),
                outlinePen: new Pen(Color.OrangeRed, 2f));

            // =====================================================================
            //  4. IMAGE MARKER - an icon/image at a point in the world
            // =====================================================================

            // Fixed size in pixels (like a map pin) - independent of zoom
            var pinIcon = CreatePinIcon(Color.Crimson);
            var poi = engine.AddImage(pinIcon, 6, 8, 0, "Warehouse", width: 28);

            // Real-world size (grows with zoom) - e.g. a building's footprint
            var floorPlanIcon = CreatePinIcon(Color.SteelBlue);
            var building = engine.AddImage(floorPlanIcon, -6, 6, 0, "Hall 1", width: 4f);
            building.WithScaleWithZoom(true);

            // An icon that can only be selected (e.g. to show info), but not moved
            var fixedIcon = CreatePinIcon(Color.Goldenrod);
            engine.AddImage(fixedIcon, 0, 8, 0, "Substation", width: 24, draggable: false);
        }

        /// <summary>Generates a simple "pin" icon (circle + point) directly in code, so the example is
        /// runnable without depending on an external image file.</summary>
        private static Bitmap CreatePinIcon(Color color)
        {
            var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(color);
            using var outline = new Pen(Color.White, 2f);

            var points = new[]
            {
                new PointF(16, 30),
                new PointF(6, 14),
                new PointF(26, 14)
            };
            g.FillPolygon(brush, points);
            g.FillEllipse(brush, 4, 0, 24, 24);
            g.DrawEllipse(outline, 5, 1, 22, 22);

            return bmp;
        }
    }
}
