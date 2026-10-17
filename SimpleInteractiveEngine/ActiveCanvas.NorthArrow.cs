using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // A north arrow for 2D (top) views whose "up" isn't north. Typical case: a background from AutoCAD
        // drawn in its own local coordinates - a building drawn as an upright rectangle that in reality
        // faces south-east. Tell the engine where the drawing's "up" really points (WorldUpBearingDegrees)
        // and an arrow in the top-right corner shows true north. It also follows a rotated 2D view.
        //
        //     engine.WorldUpBearingDegrees = 135f;   // the drawing's top actually faces south-east
        //
        // While the OSM layer is on, the world is Web Mercator (+Y is exactly north) and the view is locked
        // north-up by default, so the arrow stays hidden there.

        #region North arrow

        /// <summary>When the north arrow is drawn (see NorthArrow). Only ever in the top (2D) view.</summary>
        public enum NorthArrowMode
        {
            /// <summary>Never drawn.</summary>
            Never,
            /// <summary>In the top view, whenever north does NOT point straight up on screen - because the drawing
            /// itself is rotated (WorldUpBearingDegrees) and/or the 2D view is rotated.</summary>
            WhenRotated,
            /// <summary>Always in the top view, even when north points straight up (bearing 0, OSM map). Default.</summary>
            Always
        }

        private NorthArrowMode _northArrow = NorthArrowMode.Always;
        private float _worldUpBearingDegrees;

        /// <summary>When to draw the north arrow (top-right corner, top view only). Default Always - set
        /// WhenRotated to show it only when north isn't straight up, or Never to hide it.</summary>
        public NorthArrowMode NorthArrow
        {
            get => _northArrow;
            set
            {
                if (value == _northArrow) return;
                _northArrow = value;
                Render();
            }
        }

        /// <summary>
        /// Real-world compass bearing of the world +Y axis - i.e. where "up" in your drawing actually points,
        /// in degrees clockwise from true north: 0 = up is north (default), 90 = east, 135 = south-east,
        /// 180 = south, 270 = west. Drives the north arrow, ToTrueNorth/FromTrueNorth and - with a geo
        /// anchor (SetGeoAnchor) - how the OSM map is fitted under the drawing. The drawing's own
        /// coordinates are never changed. Ignored while the OSM layer is on WITHOUT a geo anchor (the world
        /// is then Web Mercator, whose +Y is exactly north).
        /// </summary>
        public float WorldUpBearingDegrees
        {
            get => _worldUpBearingDegrees;
            set
            {
                if (value == _worldUpBearingDegrees) return;
                _worldUpBearingDegrees = value;
                if (IsNorthUpLocked) SetRotationKeepingCenter(NorthUpYaw, 90f); // map shown - keep north up
                Render();
            }
        }

        // Without a geo anchor the OSM layer makes the world Web Mercator, whose +Y is exactly north.
        private float EffectiveWorldUpBearing => (_osmEnabled && !_geoAnchorSet) ? 0f : _worldUpBearingDegrees;

        /// <summary>
        /// Converts RAW drawing coordinates to coordinates aligned with true north - X = east, Y = north,
        /// Z unchanged - by rotating them around the drawing's origin (raw 0,0) according to
        /// WorldUpBearingDegrees. With a bearing of 0 (or while the OSM layer is on) it returns the input as is.
        ///
        ///     engine.WorldUpBearingDegrees = 60f;
        ///     var real = engine.ToTrueNorth(engine.ToRaw(point.World));   // (East, North, Z)
        ///
        /// Only a rotation - if the drawing origin should also land on a real coordinate (e.g. S-JTSK),
        /// add that offset yourself.
        /// </summary>
        public Vector3 ToTrueNorth(Vector3 raw)
        {
            double b = EffectiveWorldUpBearing * Math.PI / 180.0;
            double cos = Math.Cos(b), sin = Math.Sin(b);

            // Drawing +Y points at bearing B, drawing +X at bearing B + 90 deg.
            double east = raw.X * cos + raw.Y * sin;
            double north = -raw.X * sin + raw.Y * cos;
            return new Vector3((float)east, (float)north, raw.Z);
        }

        /// <summary>The exact opposite of ToTrueNorth(): (East, North, Z) back to RAW drawing coordinates.</summary>
        public Vector3 FromTrueNorth(Vector3 trueNorth)
        {
            double b = EffectiveWorldUpBearing * Math.PI / 180.0;
            double cos = Math.Cos(b), sin = Math.Sin(b);

            double x = trueNorth.X * cos - trueNorth.Y * sin;
            double y = trueNorth.X * sin + trueNorth.Y * cos;
            return new Vector3((float)x, (float)y, trueNorth.Z);
        }

        /// <summary>True when the drawing is rotated against north (non-zero bearing, OSM layer off) - the
        /// East/North rows in the property grid are only shown then.</summary>
        internal bool HasTrueNorthRotation => Math.Abs(EffectiveWorldUpBearing % 360f) > 1e-4f;

        private const float NorthArrowRadius = 24f;   // backdrop circle, px
        private const float NorthArrowMargin = 14f;   // same margin as the scale bar

        /// <summary>Direction of true north as a world-space unit vector (Z = 0). With bearing B of world +Y,
        /// north lies B degrees counter-clockwise from +Y.</summary>
        private Vector3 NorthWorldDirection()
        {
            double b = EffectiveWorldUpBearing * Math.PI / 180.0;
            return new Vector3((float)-Math.Sin(b), (float)Math.Cos(b), 0f);
        }

        private void DrawNorthArrow(Graphics g)
        {
            if (_buffer == null || _northArrow == NorthArrowMode.Never) return;
            if (Math.Abs(_pitchDeg - 90f) > 0.5f) return;              // top (2D) view only
            if (_buffer.Width < 120 || _buffer.Height < 120) return;   // too small a canvas to spare the corner

            // Where true north points on screen, measured at the view center - covers both a rotated
            // drawing (WorldUpBearingDegrees) and a rotated 2D view (yaw) in one go.
            var screenCenter = new PointF(_buffer.Width / 2f, _buffer.Height / 2f);
            if (!TryScreenToGround(screenCenter, out var ground))
                ground = ScreenToWorld(screenCenter, 0f);

            const float probePixels = 40f;
            var p0 = Project(ground);
            var p1 = Project(ground + NorthWorldDirection() * (probePixels / _pixelsPerMeter));
            float dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            if (dx * dx + dy * dy < 6f * 6f) return; // degenerate - shouldn't happen in the top view

            float angleDeg = (float)(Math.Atan2(dx, -dy) * 180.0 / Math.PI); // clockwise from "up"
            if (_northArrow == NorthArrowMode.WhenRotated && Math.Abs(angleDeg) <= 0.5f) return; // north is up - nothing to show

            var center = new PointF(
                _buffer.Width - NorthArrowMargin - NorthArrowRadius,
                NorthArrowMargin + NorthArrowRadius);

            var oldState = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Backdrop - same semi-transparent style as the scale bar, so it reads on any background.
            var circle = new RectangleF(center.X - NorthArrowRadius, center.Y - NorthArrowRadius,
                NorthArrowRadius * 2f, NorthArrowRadius * 2f);
            using (var backBrush = new SolidBrush(Color.FromArgb(200, LabelBackdropColor)))
                g.FillEllipse(backBrush, circle);
            using (var ringPen = new Pen(Color.FromArgb(90, LabelTextColor), 1f))
                g.DrawEllipse(ringPen, circle);

            // Needle: a diamond split into a red north half and a neutral south half, rotated as a whole.
            const float needleLength = 13f;
            const float needleHalfWidth = 5f;
            var northHalf = new[] { new PointF(0, -needleLength), new PointF(needleHalfWidth, 0), new PointF(-needleHalfWidth, 0) };
            var southHalf = new[] { new PointF(0, needleLength), new PointF(needleHalfWidth, 0), new PointF(-needleHalfWidth, 0) };

            using (var transform = new Matrix())
            {
                transform.Translate(center.X, center.Y);
                transform.Rotate(angleDeg);
                transform.TransformPoints(northHalf);
                transform.TransformPoints(southHalf);
            }

            using (var northBrush = new SolidBrush(Color.Crimson))
                g.FillPolygon(northBrush, northHalf);
            using (var southBrush = new SolidBrush(Color.FromArgb(150, LabelTextColor)))
                g.FillPolygon(southBrush, southHalf);

            // "N" beyond the needle tip, kept upright so it's always readable.
            const float letterDistance = 18f;
            double angleRad = angleDeg * Math.PI / 180.0;
            var letterCenter = new PointF(
                center.X + (float)(Math.Sin(angleRad) * letterDistance),
                center.Y - (float)(Math.Cos(angleRad) * letterDistance));

            var letterSize = g.MeasureString("N", LabelFont);
            using (var textBrush = new SolidBrush(LabelTextColor))
                g.DrawString("N", LabelFont, textBrush,
                    letterCenter.X - letterSize.Width / 2f, letterCenter.Y - letterSize.Height / 2f);

            g.Restore(oldState);
        }

        #endregion
    }
}
