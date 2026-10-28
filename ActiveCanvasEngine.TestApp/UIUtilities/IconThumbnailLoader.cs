using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace IconPicker
{
    /// <summary>
    /// Pomocná trieda na načítanie zmenšeného (štvorcového) náhľadu obrázka alebo ikony zo súboru.
    /// Podporuje PNG, BMP, ICO, JPG a GIF (formáty, ktoré vie prečítať GDI+ cez System.Drawing).
    /// </summary>
    internal static class IconThumbnailLoader
    {
        /// <summary>
        /// Načíta súbor a vráti nový bitmapový náhľad veľkosti size x size.
        /// V prípade chyby (poškodený súbor, nepodporovaný formát...) vráti null.
        /// </summary>
        public static Image LoadThumbnail(string filePath, int size)
        {
            try
            {
                if (string.Equals(Path.GetExtension(filePath), ".ico", StringComparison.OrdinalIgnoreCase))
                {
                    // Pre .ico požiadame rovno o variantu s požadovanou veľkosťou (ak v súbore existuje),
                    // inak Windows vyberie najbližšiu a nižšie ju doškálujeme.
                    using (var icon = new Icon(filePath, new Size(size, size)))
                    using (var iconBitmap = icon.ToBitmap())
                    {
                        return ResizeToSquare(iconBitmap, size);
                    }
                }

                using (var original = Image.FromFile(filePath))
                {
                    return ResizeToSquare(original, size);
                }
            }
            catch
            {
                // Poškodený / nepodporovaný súbor jednoducho preskočíme.
                return null;
            }
        }

        private static Bitmap ResizeToSquare(Image source, int size)
        {
            var bmp = new Bitmap(size, size);
            bmp.SetResolution(source.HorizontalResolution <= 0 ? 96 : source.HorizontalResolution,
                               source.VerticalResolution <= 0 ? 96 : source.VerticalResolution);

            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Zachováme pomer strán a náhľad vycentrujeme, aby sa nedeformoval.
                float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
                int drawWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
                int drawHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
                int offsetX = (size - drawWidth) / 2;
                int offsetY = (size - drawHeight) / 2;

                g.DrawImage(source, new Rectangle(offsetX, offsetY, drawWidth, drawHeight));
            }

            return bmp;
        }
    }
}
