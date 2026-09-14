using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SimpleDrawingEngine
{
    /// <summary>
    /// Helpers for building fill patterns (dot grids, hatching) usable directly with any
    /// WithFillBrush()/WithBrush() call across the engine - Polygon, ShapeMarker, Point3D, all of them
    /// just take a plain System.Drawing.Brush, and both HatchBrush and TextureBrush (built here) are Brush
    /// subclasses, so nothing in the engine itself needed to change.
    ///
    /// Two of the three common "material hatching" styles (crosshatch grid, diagonal lines) are built
    /// directly into GDI+ via HatchBrush - no helper needed, just use it directly:
    ///
    ///     shape.WithFillBrush(new HatchBrush(HatchStyle.DiagonalCross, Color.Black, Color.Transparent));
    ///     shape.WithFillBrush(new HatchBrush(HatchStyle.ForwardDiagonal, Color.Black, Color.Transparent));
    ///
    /// A regular grid of dots/circles isn't one of the built-in HatchStyle values though, so
    /// CreateDotPatternBrush() below builds one as a small tiled TextureBrush instead.
    /// </summary>
    public static class HatchBrushes
    {
        /// <summary>
        /// Builds a TextureBrush that tiles a regular grid of filled circles - the "dot pattern" style
        /// that has no built-in HatchStyle equivalent. Works with any WithFillBrush()/WithBrush() call:
        ///
        ///     zone.WithFillBrush(HatchBrushes.CreateDotPatternBrush());
        ///
        /// The caller owns the returned brush (same rule as any other custom Brush passed into the
        /// engine) - dispose it yourself if you replace it later; the engine itself doesn't dispose
        /// custom brushes on your behalf.
        /// </summary>
        /// <param name="spacing">Distance between dot centers, in pixels (or world units if the item using
        /// it has WithScaleWithZoom(true) - the brush itself doesn't know or care, it's just pixels of
        /// whatever surface it ends up filling).</param>
        /// <param name="dotRadius">Radius of each dot, in the same unit as spacing.</param>
        /// <param name="dotColor">Fill color of the dots.</param>
        /// <param name="backgroundColor">Color of the space between dots - use Color.Transparent (default)
        /// to let whatever is drawn underneath show through.</param>
        public static TextureBrush CreateDotPatternBrush(float spacing = 20f, float dotRadius = 3f,
            Color? dotColor = null, Color? backgroundColor = null)
        {
            Color dot = dotColor ?? Color.Black;
            Color background = backgroundColor ?? Color.Transparent;

            int tileSize = Math.Max(1, (int)Math.Ceiling(spacing));
            var tile = new Bitmap(tileSize, tileSize);

            using (var g = Graphics.FromImage(tile))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using (var backBrush = new SolidBrush(background))
                    g.FillRectangle(backBrush, 0, 0, tileSize, tileSize);

                // One dot centered in the tile - TextureBrush repeats this tile edge-to-edge, which is
                // exactly what turns "one dot" into "a regular grid of dots" with spacing = tileSize.
                using var dotBrush = new SolidBrush(dot);
                float cx = tileSize / 2f, cy = tileSize / 2f;
                g.FillEllipse(dotBrush, cx - dotRadius, cy - dotRadius, dotRadius * 2, dotRadius * 2);
            }

            // WrapMode.Tile is the default for TextureBrush, but stated explicitly here for clarity -
            // it's exactly what makes the single tile repeat seamlessly into a grid.
            return new TextureBrush(tile) { WrapMode = WrapMode.Tile };
        }
    }
}
