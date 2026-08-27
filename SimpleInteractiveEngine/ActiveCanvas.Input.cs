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
        // Mouse handling (pan/drag/rotate/hover/select/draw) and wheel zoom via
        // IMessageFilter (PictureBox can never receive focus, so a normal MouseWheel event never fires on it).

        #region Mouse handling

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _dragStart = e.Location;
                _pictureBox.Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (IsPlacingPoints)
                {
                    PlacePointAt(e.Location);
                    return;
                }

                if (IsPlacingImages)
                {
                    PlaceImageAt(e.Location);
                    return;
                }

                if (IsDrawing)
                {
                    if (e.Clicks >= 2)
                        FinishDrawing();
                    else
                        AddDrawingVertex(e.Location);
                    return;
                }

                var hit = HitTest(e.Location);
                if (hit != null)
                {
                    Select(hit);
                    PointClicked?.Invoke(this, hit);

                    if (AllowDragPoints && hit.Draggable)
                    {
                        _draggingPoint = hit;
                        Project(hit.World, out _draggingDepth); // remember the point's original "depth"
                        _pictureBox.Cursor = Cursors.Hand;
                    }
                    return; // the scene doesn't rotate on a point - it's either dragged or just selected
                }

                var hitImage = HitTestImage(e.Location);
                if (hitImage != null)
                {
                    Select(hitImage);
                    ImageClicked?.Invoke(this, hitImage);

                    if (AllowDragPoints && hitImage.Draggable)
                    {
                        _draggingImage = hitImage;
                        Project(hitImage.World, out _draggingImageDepth);
                        _pictureBox.Cursor = Cursors.Hand;
                    }
                    return;
                }

                var hitLine = HitTestPolyline(e.Location);
                if (hitLine != null)
                {
                    Select(hitLine);
                    PolylineClicked?.Invoke(this, hitLine);
                    _isRotating = false;
                    _dragStart = e.Location;
                    return;
                }

                var hitPolygon = HitTestPolygon(e.Location);
                if (hitPolygon != null)
                {
                    Select(hitPolygon);
                    PolygonClicked?.Invoke(this, hitPolygon);
                    _isRotating = false;
                    _dragStart = e.Location;
                    return;
                }

                // click on empty space - clear the selection and prepare for possible free rotation (handled in MouseMove)
                if (_selectedShape != null)
                    Select(null);

                _isRotating = false;
                _dragStart = e.Location;
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_drawingPolyline != null || _drawingPolygon != null)
            {
                _drawingPreviewScreenPos = e.Location;
                Render(); // the rubber-band line must follow the cursor even without a button pressed
            }

            if (_draggingPoint != null)
            {
                _draggingPoint.MoveTo(ScreenToWorld(e.Location, _draggingDepth));
                Render();
                PointMoved?.Invoke(this, _draggingPoint);
                return;
            }

            if (_draggingImage != null)
            {
                _draggingImage.MoveTo(ScreenToWorld(e.Location, _draggingImageDepth));
                Render();
                return;
            }

            if (_isPanning)
            {
                _panOffsetPixels = new PointF(
                    _panOffsetPixels.X + (e.X - _dragStart.X),
                    _panOffsetPixels.Y + (e.Y - _dragStart.Y));
                _dragStart = e.Location;
                Render();
                return;
            }

            if (AllowFreeRotate && e.Button == MouseButtons.Left)
            {
                float dx = e.X - _dragStart.X;
                float dy = e.Y - _dragStart.Y;

                if (!_isRotating && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
                    _isRotating = true;

                if (_isRotating)
                {
                    _yawDeg += dx * 0.4f;
                    _pitchDeg = ClampPitch(_pitchDeg - dy * 0.4f);
                    _dragStart = e.Location;
                    Render();
                }
                return;
            }

            // Nothing is being dragged or rotated - just an ordinary mouse move, check hover over a point/icon
            // (works during drawing too - hover thus indicates which point a vertex would "snap" to).
            UpdateHover(e.Location);
        }

        private void OnMouseLeave(object? sender, EventArgs e)
        {
            bool changed = false;
            if (_hoverPoint != null) { _hoverPoint = null; changed = true; }
            if (_hoverImage != null) { _hoverImage = null; changed = true; }
            if (_drawingPreviewScreenPos != null) { _drawingPreviewScreenPos = null; changed = true; }

            if (changed)
            {
                if (!IsDrawing)
                    _pictureBox.Cursor = Cursors.Default;
                Render();
            }
        }

        private void UpdateHover(Point location)
        {
            var hitPoint = HitTestForHover(location);
            var hitImage = hitPoint == null ? HitTestImageForHover(location) : null; // a point takes priority

            if (hitPoint == _hoverPoint && hitImage == _hoverImage) return;

            _hoverPoint = hitPoint;
            _hoverImage = hitImage;

            // While drawing/placing, DrawingCursor stays put regardless of what's hovered underneath.
            if (!IsDrawing)
            {
                bool selectable = (hitPoint?.Selectable ?? false) || (hitImage?.Selectable ?? false);
                _pictureBox.Cursor = selectable ? Cursors.Hand : Cursors.Default;
            }
            Render();
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
            {
                _isPanning = false;
                _pictureBox.Cursor = Cursors.Default;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (_draggingPoint != null)
                {
                    var finished = _draggingPoint;
                    _draggingPoint = null;
                    _pictureBox.Cursor = Cursors.Default;
                    PointDragCompleted?.Invoke(this, finished);
                }

                if (_draggingImage != null)
                {
                    _draggingImage = null;
                    _pictureBox.Cursor = Cursors.Default;
                }

                _isRotating = false;
            }
        }

        #endregion

        #region Wheel zoom via IMessageFilter (independent of focus)

        private class WheelMessageFilter : IMessageFilter
        {
            private const int WM_MOUSEWHEEL = 0x020A;
            private readonly ActiveCanvas _engine;

            public WheelMessageFilter(ActiveCanvas engine) => _engine = engine;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;

                var pb = _engine._pictureBox;
                if (!pb.IsHandleCreated || !pb.Visible) return false;

                long lParam = m.LParam.ToInt64();
                int screenX = unchecked((short)(lParam & 0xFFFF));
                int screenY = unchecked((short)((lParam >> 16) & 0xFFFF));
                var pbPoint = pb.PointToClient(new Point(screenX, screenY));

                if (!pb.ClientRectangle.Contains(pbPoint)) return false;

                long wParam = m.WParam.ToInt64();
                int delta = unchecked((short)((wParam >> 16) & 0xFFFF));
                float factor = delta > 0 ? 1.15f : 1f / 1.15f;

                _engine.ZoomAt(pbPoint, factor);
                return true;
            }
        }
        #endregion
    }
}