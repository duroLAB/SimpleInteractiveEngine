using System.Drawing;
using System.Drawing.Drawing2D;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // A simple "please wait" overlay drawn directly on the canvas - useful around any operation that
        // might take a noticeable moment (loading a large background image, parsing a big DXF file...).
        // Just two calls: ShowBusyOverlay() before the slow work, HideBusyOverlay() after.

        #region Busy overlay

        /// <summary>
        /// Draws a "please wait" overlay over the current canvas content and forces it to paint
        /// immediately - safe to call right before a slow SYNCHRONOUS operation, since it doesn't wait for
        /// the normal message loop to get around to repainting (Control.Update() forces it right away).
        /// For genuinely long operations, prefer running the work on a background thread (see
        /// SetBackgroundImageFromGeoTiffAsync for an example) so the UI - and this overlay - stays
        /// responsive instead of just showing a static frozen message.
        /// </summary>
        public void ShowBusyOverlay(string message = "Please wait...")
        {
            if (_buffer == null) return;

            using (var g = Graphics.FromImage(_buffer))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using var dimBrush = new SolidBrush(Color.FromArgb(150, Color.Black));
                g.FillRectangle(dimBrush, 0, 0, _buffer.Width, _buffer.Height);

                using var font = new Font("Segoe UI", 11f, FontStyle.Bold);
                using var textBrush = new SolidBrush(Color.White);
                var size = g.MeasureString(message, font);

                float boxPadding = 16f;
                var boxRect = new RectangleF(
                    (_buffer.Width - size.Width) / 2f - boxPadding,
                    (_buffer.Height - size.Height) / 2f - boxPadding,
                    size.Width + boxPadding * 2,
                    size.Height + boxPadding * 2);

                using var boxBrush = new SolidBrush(Color.FromArgb(210, Color.FromArgb(40, 40, 40)));
                using var boxPath = RoundedRect(boxRect, 10f);
                g.FillPath(boxBrush, boxPath);

                g.DrawString(message, font, textBrush,
                    (_buffer.Width - size.Width) / 2f, (_buffer.Height - size.Height) / 2f);
            }

            // Invalidate() alone would only repaint whenever the message loop next gets around to it - since
            // the slow operation is about to block that very loop, force it to happen right now instead.
            _pictureBox.Invalidate();
            _pictureBox.Update();
        }

        /// <summary>Removes the busy overlay by simply re-rendering the real canvas content over it.</summary>
        public void HideBusyOverlay() => Render();

        #endregion
    }
}