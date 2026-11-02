using System;
using System.Windows.Forms;

namespace ActiveCanvasEngine
{
    // Lifetime: the engine hooks into its PictureBox (mouse, resize, handle events) and into the whole
    // application (the mouse-wheel message filter), and holds bitmaps (the drawing buffer, background images,
    // icons, OSM tiles). Dispose() undoes all of that. It runs automatically when the PictureBox is disposed
    // (e.g. the form closes); call it yourself before attaching a new engine to the same PictureBox:
    //
    //     engine.Dispose();
    //     engine = new ActiveCanvas(pictureBox1);
    public partial class ActiveCanvas : IDisposable
    {
        #region Dispose

        private bool _disposed;

        /// <summary>True once Dispose() has run - the instance is detached and can't be used any more.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// Detaches the engine from its PictureBox and frees what it holds: unsubscribes the mouse/resize/handle
        /// events, removes the application-wide mouse-wheel filter, cancels OSM tile downloads and frees the
        /// cached tiles (the disk cache stays), disposes the background images (all three planes), the icons'
        /// images and the drawing buffer. The PictureBox itself is NOT disposed - it belongs to your form and
        /// can get a new engine. Brushes/pens you passed in (AddPoint, AddPolygon, ...) stay yours, as before.
        /// Safe to call more than once. Call it on the UI thread (the wheel filter is registered there).
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _pictureBox.MouseDown -= OnMouseDown;
            _pictureBox.MouseMove -= OnMouseMove;
            _pictureBox.MouseUp -= OnMouseUp;
            _pictureBox.MouseLeave -= OnMouseLeave;
            _pictureBox.Resize -= OnPictureBoxResize;
            _pictureBox.HandleCreated -= OnPictureBoxHandleCreated;
            _pictureBox.Disposed -= OnPictureBoxDisposed;

            Application.RemoveMessageFilter(_wheelFilter);

            _osmEnabled = false;
            ResetOsmState(); // cancels downloads in flight, late tiles are dropped (generation changes)

            DisposeBackgroundImages();
            foreach (var marker in Images) marker.Image.Dispose(); // the engine owns icon images (see AddImage); a shared one is fine to dispose twice

            if (!_pictureBox.IsDisposed && !_pictureBox.Disposing)
            {
                if (ReferenceEquals(_pictureBox.Image, _buffer)) _pictureBox.Image = null;
                _pictureBox.Cursor = Cursors.Default;
            }

            _buffer?.Dispose();
            _buffer = null; // Render()/ShowBusyOverlay() do nothing from now on

            GC.SuppressFinalize(this);
        }

        private void OnPictureBoxResize(object? sender, EventArgs e) => DeferredRebuildBuffer();

        private void OnPictureBoxHandleCreated(object? sender, EventArgs e) => RebuildBuffer();

        private void OnPictureBoxDisposed(object? sender, EventArgs e) => Dispose();

        #endregion
    }
}
