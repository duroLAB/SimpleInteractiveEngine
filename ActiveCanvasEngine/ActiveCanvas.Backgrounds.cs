using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;

namespace ActiveCanvasEngine
{
    public partial class ActiveCanvas
    {
        // Background images in up to three planes - one image per plane:
        //
        //   Ground - the X-Y plane (plan view, ShowTopView)        - SetBackgroundImage / GeoTIFF
        //   Front  - the X-Z plane (front view, ShowFrontView)     - SetFrontBackgroundImage
        //   Side   - the Y-Z plane (side view, ShowSideView)       - SetSideBackgroundImage
        //
        // All of them go through the same projection as everything else: in their own view they lie flat in
        // front of you, in a 3D view the vertical ones stand in the scene like walls, and in a view that looks
        // along a plane (e.g. the front image in the top view) the image collapses into a line and is skipped.
        // Each one can be hidden (SetBackgroundVisible) and made semi-transparent (SetBackgroundOpacity).
        //
        //     engine.SetBackgroundImage(planBitmap, 0, 40, 60);                     // plan, top-left at (0, 40)
        //     engine.SetFrontBackgroundImage(elevationBitmap, minX: 0, minZ: 0, widthMeters: 60);  // at Y = 0
        //     engine.SetBackgroundOpacity(ActiveCanvas.BackgroundPlane.Front, 0.5f);

        #region Background planes

        /// <summary>The plane a background image lies in.</summary>
        public enum BackgroundPlane
        {
            /// <summary>X-Y plane (plan / top view) - the classic background image, GeoTIFF.</summary>
            Ground,
            /// <summary>X-Z plane (front view / elevation, see ShowFrontView).</summary>
            Front,
            /// <summary>Y-Z plane (side view, see ShowSideView).</summary>
            Side
        }

        private sealed class BackgroundSlot
        {
            public Image? Image;
            public Vector3 RawTopLeft;   // RAW coordinates - follows the current local origin, see SetLocalOrigin
            public Vector3 Right;        // unit vector along the image's width
            public Vector3 Down;         // unit vector along the image's height
            public float Width;
            public float Height;
            public bool Visible = true;
            public float Opacity = 1f;
        }

        private readonly BackgroundSlot[] _backgrounds = { new BackgroundSlot(), new BackgroundSlot(), new BackgroundSlot() };

        private BackgroundSlot Slot(BackgroundPlane plane) => _backgrounds[(int)plane];

        /// <summary>
        /// Sets the background image of the FRONT plane (X-Z, a vertical plane at Y = atY) - e.g. an elevation
        /// drawing. It covers X from minX to minX + widthMeters and Z from minZ to minZ + height, and reads the
        /// right way round in ShowFrontView. Coordinates are RAW (SetLocalOrigin is applied automatically).
        /// The engine takes ownership of the image and disposes it on replacement/close.
        /// </summary>
        /// <param name="heightMeters">If omitted, computed from the image's aspect ratio.</param>
        public void SetFrontBackgroundImage(Image image, float minX, float minZ, float widthMeters,
            float? heightMeters = null, float atY = 0f)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            float height = heightMeters ?? AspectHeight(image, widthMeters);

            SetBackgroundSlot(BackgroundPlane.Front, image,
                rawTopLeft: new Vector3(minX, atY, minZ + height),
                right: Vector3.UnitX, down: -Vector3.UnitZ, widthMeters, height);
        }

        /// <summary>
        /// Sets the background image of the SIDE plane (Y-Z, a vertical plane at X = atX). It covers Y from
        /// minY to minY + widthMeters and Z from minZ to minZ + height. In ShowSideView world +Y points to the
        /// LEFT, so the image's left edge lies at the higher Y - that way it reads the right way round (not
        /// mirrored) in the side view. Coordinates are RAW (SetLocalOrigin is applied automatically).
        /// </summary>
        /// <param name="heightMeters">If omitted, computed from the image's aspect ratio.</param>
        public void SetSideBackgroundImage(Image image, float minY, float minZ, float widthMeters,
            float? heightMeters = null, float atX = 0f)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            float height = heightMeters ?? AspectHeight(image, widthMeters);

            SetBackgroundSlot(BackgroundPlane.Side, image,
                rawTopLeft: new Vector3(atX, minY + widthMeters, minZ + height),
                right: -Vector3.UnitY, down: -Vector3.UnitZ, widthMeters, height);
        }

        /// <summary>True if the given plane has a background image (whether or not it's currently visible).</summary>
        public bool HasBackgroundImage(BackgroundPlane plane) => Slot(plane).Image != null;

        /// <summary>Removes (and disposes) the background image of one plane.</summary>
        public void ClearBackgroundImage(BackgroundPlane plane)
        {
            var slot = Slot(plane);
            slot.Image?.Dispose();
            slot.Image = null;
            Render();
        }

        /// <summary>Removes the background images of all three planes.</summary>
        public void ClearAllBackgroundImages()
        {
            foreach (var slot in _backgrounds)
            {
                slot.Image?.Dispose();
                slot.Image = null;
            }
            Render();
        }

        /// <summary>Whether the plane's background image is drawn (default true). Hiding keeps the image.</summary>
        public bool IsBackgroundVisible(BackgroundPlane plane) => Slot(plane).Visible;

        /// <summary>Shows/hides the plane's background image without removing it - e.g. to hide an elevation
        /// in the 3D view.</summary>
        public void SetBackgroundVisible(BackgroundPlane plane, bool visible)
        {
            var slot = Slot(plane);
            if (slot.Visible == visible) return;
            slot.Visible = visible;
            Render();
        }

        /// <summary>Opacity of the plane's background image, 0..1 (default 1 = fully opaque).</summary>
        public float GetBackgroundOpacity(BackgroundPlane plane) => Slot(plane).Opacity;

        /// <summary>Sets the opacity of the plane's background image, 0..1 - e.g. 0.5 so an elevation standing
        /// in the 3D view doesn't hide what's behind it.</summary>
        public void SetBackgroundOpacity(BackgroundPlane plane, float opacity)
        {
            var slot = Slot(plane);
            float clamped = Math.Clamp(opacity, 0f, 1f);
            if (slot.Opacity == clamped) return;
            slot.Opacity = clamped;
            Render();
        }

        /// <summary>
        /// Loads an image file fully into memory (the file isn't kept locked) and, if its larger side exceeds
        /// maxPixelDimension, returns a downscaled copy - handy for SetFrontBackgroundImage/SetSideBackgroundImage.
        /// Has no dependency on engine state, so it's safe to call from a background thread (Task.Run).
        /// </summary>
        public static Image LoadImageFile(string filePath, int? maxPixelDimension = 4000)
        {
            Bitmap original;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var loaded = Image.FromStream(stream))
            {
                original = new Bitmap(loaded);
            }

            if (maxPixelDimension.HasValue && Math.Max(original.Width, original.Height) > maxPixelDimension.Value)
            {
                var smaller = DownscaleForDisplay(original, maxPixelDimension.Value);
                original.Dispose();
                return smaller;
            }
            return original;
        }

        private static float AspectHeight(Image image, float width) => width * image.Height / image.Width;

        /// <summary>Shared by all three planes (SetBackgroundImage included).</summary>
        private void SetBackgroundSlot(BackgroundPlane plane, Image image, Vector3 rawTopLeft, Vector3 right,
            Vector3 down, float width, float height)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            if (width <= 0f) throw new ArgumentOutOfRangeException(nameof(width), "Image width must be positive.");

            var slot = Slot(plane);
            if (!ReferenceEquals(slot.Image, image)) slot.Image?.Dispose();

            slot.Image = image;
            slot.RawTopLeft = rawTopLeft;
            slot.Right = right;
            slot.Down = down;
            slot.Width = width;
            slot.Height = height;

            Render();
        }

        /// <summary>True if the plane's image should be drawn and counted by ZoomToFullExtent.</summary>
        private bool IsBackgroundDrawn(BackgroundPlane plane)
        {
            var slot = Slot(plane);
            if (slot.Image == null || !slot.Visible || slot.Opacity <= 0f) return false;
            if (plane == BackgroundPlane.Ground && IsBackgroundImageHidden) return false; // under the OSM layer
            return true;
        }

        /// <summary>The image's 4 corners in LOCAL space: top-left, top-right, bottom-left, bottom-right.</summary>
        private Vector3[] BackgroundLocalCorners(BackgroundSlot slot)
        {
            var topLeft = ToLocal(slot.RawTopLeft);
            var right = slot.Right * slot.Width;
            var down = slot.Down * slot.Height;
            return new[] { topLeft, topLeft + right, topLeft + down, topLeft + right + down };
        }

        private static readonly BackgroundPlane[] AllBackgroundPlanes =
            { BackgroundPlane.Ground, BackgroundPlane.Front, BackgroundPlane.Side };

        /// <summary>Draws the visible background images - ground first, then the vertical ones.</summary>
        private void DrawBackgroundImages(Graphics g)
        {
            foreach (var plane in AllBackgroundPlanes)
            {
                if (!IsBackgroundDrawn(plane)) continue;

                var slot = Slot(plane);
                var corners = BackgroundLocalCorners(slot);

                // Orthographic projection keeps parallel lines parallel -> the rectangle always projects to a
                // parallelogram, so 3 corners are enough (DrawImage fills in the fourth).
                var topLeft = Project(corners[0]);
                var topRight = Project(corners[1]);
                var bottomLeft = Project(corners[2]);

                // Looking along the plane (e.g. the front image in the top view) - it's just a line. Skip it:
                // there's nothing to see, and GDI+ can throw on a degenerate parallelogram.
                float area = Math.Abs((topRight.X - topLeft.X) * (bottomLeft.Y - topLeft.Y)
                                    - (topRight.Y - topLeft.Y) * (bottomLeft.X - topLeft.X));
                if (area < 1f) continue;

                var destination = new[] { topLeft, topRight, bottomLeft };
                var image = slot.Image!;

                if (slot.Opacity >= 1f)
                {
                    g.DrawImage(image, destination);
                }
                else
                {
                    using var attributes = new ImageAttributes();
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = slot.Opacity });
                    g.DrawImage(image, destination, new RectangleF(0, 0, image.Width, image.Height),
                        GraphicsUnit.Pixel, attributes);
                }
            }
        }

        /// <summary>Adds the corners of every drawn background image to ZoomToFullExtent's point list.</summary>
        private void AddBackgroundExtentPoints(List<(float x, float z)> rawPoints)
        {
            foreach (var plane in AllBackgroundPlanes)
            {
                if (!IsBackgroundDrawn(plane)) continue;
                foreach (var corner in BackgroundLocalCorners(Slot(plane)))
                    rawPoints.Add(ProjectRawXZ(corner));
            }
        }

        private void DisposeBackgroundImages()
        {
            foreach (var slot in _backgrounds)
            {
                slot.Image?.Dispose();
                slot.Image = null;
            }
        }

        #endregion
    }
}
