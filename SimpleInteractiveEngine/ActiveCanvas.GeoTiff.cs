using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // Optional convenience for setting a georeferenced (Geo)TIFF as the background image without
        // having to work out worldWidthMeters/worldX/worldY by hand. Purely a parameter-computation
        // helper on top of the existing SetBackgroundImage() - it doesn't touch the rendering pipeline
        // or add any new drawing concept, so it stays out of the way if you don't use it.

        #region GeoTIFF background convenience

        /// <summary>
        /// Loads a (Geo)TIFF as the background image and, if georeferencing info is available, uses it to
        /// position and size it automatically - no need to guess worldWidthMeters by hand.
        ///
        /// Looks for georeferencing in this order:
        ///   1. A sidecar world file next to the image (.tfw, or the image's own extension with a trailing
        ///      'w', e.g. "site.tif" + "site.tfw") - the common ESRI/QGIS convention.
        ///   2. Embedded GeoTIFF tags (ModelPixelScaleTag / ModelTiepointTag) inside the TIFF itself.
        ///
        /// Assumes the file's coordinate reference system is in linear meters (e.g. a local UTM zone) -
        /// if it's in degrees (plain lat/long), the resulting "meters" will be wrong, since this engine
        /// has no projection/reprojection support.
        ///
        /// Falls back to fallbackWorldWidthMeters (positioned at world origin) if no georeferencing is
        /// found at all, so the call never fails outright - you just get an unpositioned image, same as
        /// calling SetBackgroundImage() directly would.
        ///
        /// The file is fully decoded up front (via a FileStream, not Image.FromFile) so it isn't kept
        /// locked open afterwards - important for larger TIFFs that might otherwise decode lazily while
        /// panning/zooming. If maxPixelDimension is set and the image's larger side exceeds it, a
        /// downscaled copy is used for actual drawing instead - the screen never shows more detail than a
        /// few thousand pixels anyway, so holding the full original resolution in memory is usually wasted
        /// RAM for a large orthophoto/scan. World-space size is always computed from the ORIGINAL pixel
        /// dimensions first, so georeferencing accuracy is unaffected by the downscale.
        /// </summary>
        /// <returns>True if georeferencing was found and used, false if the fallback was used instead.</returns>
        /// <param name="fallbackWorldWidthMeters">Used when no georeferencing is found and fallbackWorldWidthProvider is null.</param>
        /// <param name="fallbackWorldWidthProvider">Called ONLY if no georeferencing is found (lazily - the
        /// image is decoded just once either way), letting you e.g. prompt the user for a width instead of
        /// using a fixed fallback. Its return value overrides fallbackWorldWidthMeters when provided.</param>
        public bool SetBackgroundImageFromGeoTiff(string filePath, float worldZ = 0f,
            float fallbackWorldWidthMeters = 100f, Func<float>? fallbackWorldWidthProvider = null,
            int? maxPixelDimension = 4000)
        {
            Bitmap original;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var loaded = Image.FromStream(stream))
            {
                original = new Bitmap(loaded); // force a full, independent decode - releases the file afterwards
            }

            bool hasGeoreferencing = TryReadWorldFile(filePath, original, out var geo)
                || TryReadEmbeddedGeoTiffTags(original, out geo);

            Image forDrawing = original;
            if (maxPixelDimension.HasValue && Math.Max(original.Width, original.Height) > maxPixelDimension.Value)
            {
                forDrawing = DownscaleForDisplay(original, maxPixelDimension.Value);
                original.Dispose(); // only the downscaled copy is kept from here on
            }

            if (hasGeoreferencing)
            {
                SetBackgroundImage(forDrawing, geo.x, geo.y, geo.width, geo.height, worldZ);
            }
            else
            {
                float width = fallbackWorldWidthProvider?.Invoke() ?? fallbackWorldWidthMeters;
                SetBackgroundImage(forDrawing, 0f, 0f, width, worldZ: worldZ);
            }

            return hasGeoreferencing;
        }

        private static Bitmap DownscaleForDisplay(Bitmap source, int maxDimension)
        {
            float scale = (float)maxDimension / Math.Max(source.Width, source.Height);
            int w = Math.Max(1, (int)(source.Width * scale));
            int h = Math.Max(1, (int)(source.Height * scale));

            var resized = new Bitmap(w, h);
            using var g = Graphics.FromImage(resized);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, w, h);
            return resized;
        }

        private static bool TryReadWorldFile(string imagePath, Image image, out (float x, float y, float width, float height) geo)
        {
            geo = default;

            string? worldFilePath = FindWorldFile(imagePath);
            if (worldFilePath == null) return false;

            var lines = File.ReadAllLines(worldFilePath);
            if (lines.Length < 6) return false;

            var values = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (!double.TryParse(lines[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }

            // World file line order: pixel size X, rotation, rotation, pixel size Y (negative),
            // X of the upper-left pixel's CENTER, Y of the upper-left pixel's CENTER.
            double pixelSizeX = values[0];
            double pixelSizeY = values[3];
            double centerX = values[4];
            double centerY = values[5];

            // Shift from "center of the top-left pixel" to "the pixel's actual top-left corner",
            // which is what SetBackgroundImage's worldX/worldY expect.
            double worldX = centerX - pixelSizeX / 2.0;
            double worldY = centerY - pixelSizeY / 2.0; // pixelSizeY is negative, so this moves up

            geo = (
                (float)worldX,
                (float)worldY,
                (float)(pixelSizeX * image.Width),
                (float)(Math.Abs(pixelSizeY) * image.Height));
            return true;
        }

        private static string? FindWorldFile(string imagePath)
        {
            string dir = Path.GetDirectoryName(imagePath) ?? "";
            string nameNoExt = Path.GetFileNameWithoutExtension(imagePath);
            string ext = Path.GetExtension(imagePath);

            // Classic ESRI convention: first and last letter of the original extension, plus a trailing
            // 'w' (tif -> tfw, jpg -> jgw, png -> pgw...).
            string classicWorldExt = ext.Length >= 2 ? "." + ext[1] + ext[^1] + "w" : ".tfw";

            string[] candidates =
            {
                Path.Combine(dir, nameNoExt + classicWorldExt),
                Path.Combine(dir, nameNoExt + ext + "w"),
                Path.Combine(dir, nameNoExt + ".tfw"),
                Path.Combine(dir, nameNoExt + ".wld"),
            };

            foreach (var candidate in candidates)
                if (File.Exists(candidate)) return candidate;

            return null;
        }

        private static bool TryReadEmbeddedGeoTiffTags(Image image, out (float x, float y, float width, float height) geo)
        {
            geo = default;

            const int ModelPixelScaleTag = 33550;
            const int ModelTiepointTag = 33922;

            if (Array.IndexOf(image.PropertyIdList, ModelPixelScaleTag) < 0) return false;
            if (Array.IndexOf(image.PropertyIdList, ModelTiepointTag) < 0) return false;

            double[] scale = ReadDoubleArray(image.GetPropertyItem(ModelPixelScaleTag)!.Value!);
            double[] tie = ReadDoubleArray(image.GetPropertyItem(ModelTiepointTag)!.Value!);

            // ModelPixelScaleTag = [scaleX, scaleY, scaleZ]; ModelTiepointTag = [i, j, k, X, Y, Z] for the
            // first tie point. Assumes the common case where that tie point is the raster's top-left pixel
            // (i = j = 0), which covers the large majority of GeoTIFFs written by GIS software.
            if (scale.Length < 2 || tie.Length < 6) return false;

            geo = (
                (float)tie[3],
                (float)tie[4],
                (float)(scale[0] * image.Width),
                (float)(scale[1] * image.Height));
            return true;
        }

        private static double[] ReadDoubleArray(byte[] bytes)
        {
            int count = bytes.Length / 8;
            var result = new double[count];
            for (int i = 0; i < count; i++)
                result[i] = BitConverter.ToDouble(bytes, i * 8);
            return result;
        }

        #endregion
    }
}