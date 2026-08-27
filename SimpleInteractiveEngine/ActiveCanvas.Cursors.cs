using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // Custom cursor shown during drawing/placement mode (StartDrawingPolyline/Polygon,
        // StartPlacingPoints/Images) - a modern crosshair with a precisely centered hotspot.

        #region Drawing-mode cursor

        /// <summary>Cursor shown while a drawing/placement mode is active. Defaults to a built-in
        /// crosshair; assign your own Cursor here to customize it.</summary>
        public Cursor DrawingCursor { get; set; } = CreateCrosshairCursor();

        /// <summary>
        /// Builds a modern crosshair cursor: a white "halo" behind a black line for visibility on any
        /// background, a small gap around the center for precision, and a dot marking the exact click
        /// point. Uses CreateIconIndirect so the cursor's hotspot lands exactly on that center dot -
        /// Cursor(bitmap.GetHicon()) alone does not guarantee this.
        /// </summary>
        private static Cursor CreateCrosshairCursor()
        {
            const int size = 32;
            const int center = size / 2;
            const int gap = 4;       // empty space around the center, so the crosshair doesn't cover the exact point
            const int armLength = 11;
            const int dotRadius = 2;

            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using var haloPen = new Pen(Color.FromArgb(210, Color.White), 3f);
                using var linePen = new Pen(Color.FromArgb(235, Color.Black), 1.4f);

                DrawCrossArms(g, haloPen, center, gap, armLength);
                DrawCrossArms(g, linePen, center, gap, armLength);

                using var dotHalo = new SolidBrush(Color.FromArgb(210, Color.White));
                using var dotBrush = new SolidBrush(Color.FromArgb(235, Color.Black));
                g.FillEllipse(dotHalo, center - dotRadius - 1, center - dotRadius - 1, (dotRadius + 1) * 2, (dotRadius + 1) * 2);
                g.FillEllipse(dotBrush, center - dotRadius, center - dotRadius, dotRadius * 2, dotRadius * 2);
            }

            return CreateCursorFromBitmap(bmp, center, center);
        }

        private static void DrawCrossArms(Graphics g, Pen pen, int center, int gap, int armLength)
        {
            g.DrawLine(pen, center, center - gap - armLength, center, center - gap);
            g.DrawLine(pen, center, center + gap, center, center + gap + armLength);
            g.DrawLine(pen, center - gap - armLength, center, center - gap, center);
            g.DrawLine(pen, center + gap, center, center + gap + armLength, center);
        }

        /// <summary>Converts a Bitmap into a Cursor with an exact hotspot, via the Win32 CreateIconIndirect
        /// API (a plain Cursor(bitmap.GetHicon()) always centers the hotspot on the whole image's bounding
        /// box, which isn't precise enough for a crosshair-style cursor).</summary>
        private static Cursor CreateCursorFromBitmap(Bitmap bitmap, int hotspotX, int hotspotY)
        {
            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                var info = new NativeMethods.IconInfo();
                NativeMethods.GetIconInfo(hIcon, ref info);
                info.fIcon = false;
                info.xHotspot = hotspotX;
                info.yHotspot = hotspotY;

                IntPtr hCursor = NativeMethods.CreateIconIndirect(ref info);

                if (info.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmMask);
                if (info.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(info.hbmColor);

                return new Cursor(hCursor);
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct IconInfo
            {
                public bool fIcon;
                public int xHotspot;
                public int yHotspot;
                public IntPtr hbmMask;
                public IntPtr hbmColor;
            }

            [DllImport("user32.dll")]
            public static extern bool GetIconInfo(IntPtr hIcon, ref IconInfo pIconInfo);

            [DllImport("user32.dll")]
            public static extern IntPtr CreateIconIndirect(ref IconInfo icon);

            [DllImport("user32.dll")]
            public static extern bool DestroyIcon(IntPtr hIcon);

            [DllImport("gdi32.dll")]
            public static extern bool DeleteObject(IntPtr hObject);
        }

        #endregion
    }
}
