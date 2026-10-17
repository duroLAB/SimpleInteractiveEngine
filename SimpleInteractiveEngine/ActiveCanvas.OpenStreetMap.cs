using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // Optional OpenStreetMap (or any other XYZ / "slippy map") tile layer drawn underneath everything
        // else. Completely opt-in: nothing in this file runs until EnableOpenStreetMap() is called, and
        // the engine stays CRS-agnostic otherwise.
        //
        // The one rule while the layer is on: the engine's RAW world coordinates are Web Mercator meters
        // (EPSG:3857) - the projection every OSM-style tile server uses. Use LatLonToWebMercator() /
        // LatLonToLocal() / CreatePointLatLon() to place content by GPS (WGS84) coordinates, and
        // SetLocalOriginLatLon() first so the float-based internal coordinates stay precise.
        //
        //     engine.SetLocalOriginLatLon(48.1437, 17.1078);           // once, before adding content
        //     engine.OsmUserAgent = "MyApp/1.0 (+https://example.com)"; // required by the OSM tile policy
        //     engine.EnableOpenStreetMap();
        //     engine.ZoomToLatLon(48.1437, 17.1078, zoomLevel: 16);
        //
        // Tiles are downloaded in the background (max 2 parallel requests, as the OSM tile usage policy
        // asks), kept in a small in-memory LRU cache and in an on-disk cache, and while a tile is still
        // loading, an already cached lower-zoom tile is stretched in its place so the map never flashes empty.

        #region OpenStreetMap - Web Mercator math (static, no engine state)

        /// <summary>Earth radius used by Web Mercator (EPSG:3857), in meters.</summary>
        public const double WebMercatorEarthRadius = 6378137.0;

        /// <summary>Full width (and height) of the Web Mercator world square, in Web Mercator meters (~40 075 km).</summary>
        public const double WebMercatorWorldSize = 2.0 * Math.PI * WebMercatorEarthRadius;

        /// <summary>Latitude limit of Web Mercator - the map is a square, so it stops at about +-85.05 deg.</summary>
        public const double WebMercatorMaxLatitude = 85.0511287798066;

        /// <summary>Converts WGS84 latitude/longitude (degrees, as from GPS or OSM) to Web Mercator
        /// (EPSG:3857) meters - the RAW world coordinates the engine uses while the OSM layer is on.</summary>
        public static (double X, double Y) LatLonToWebMercator(double latitude, double longitude)
        {
            double lat = Math.Clamp(latitude, -WebMercatorMaxLatitude, WebMercatorMaxLatitude);
            double x = WebMercatorEarthRadius * longitude * Math.PI / 180.0;
            double y = WebMercatorEarthRadius * Math.Log(Math.Tan(Math.PI / 4.0 + lat * Math.PI / 360.0));
            return (x, y);
        }

        /// <summary>The exact opposite of LatLonToWebMercator().</summary>
        public static (double Latitude, double Longitude) WebMercatorToLatLon(double x, double y)
        {
            double lon = x / WebMercatorEarthRadius * 180.0 / Math.PI;
            double lat = (2.0 * Math.Atan(Math.Exp(y / WebMercatorEarthRadius)) - Math.PI / 2.0) * 180.0 / Math.PI;
            return (lat, lon);
        }

        #endregion

        #region OpenStreetMap - geo helpers bound to this canvas (local origin aware)

        /// <summary>
        /// Sets the local origin (see SetLocalOrigin) to a GPS position, rounded to whole Web Mercator
        /// meters. Call it once, before adding content, with a point roughly in the middle of your area of
        /// interest - Web Mercator values are in the millions, and without an origin the float-based
        /// internal coordinates would only be precise to about half a meter.
        /// </summary>
        public void SetLocalOriginLatLon(double latitude, double longitude)
        {
            var (mx, my) = LatLonToWebMercator(latitude, longitude);
            var (x, y) = MercatorToRaw(mx, my);
            SetLocalOrigin((float)Math.Round(x), (float)Math.Round(y), 0f);
        }

        /// <summary>Converts a GPS position straight to this engine's LOCAL coordinates (Web Mercator minus
        /// the local origin), computed in double precision - more precise than going through the raw float
        /// overloads of AddPoint for large Web Mercator values.</summary>
        public Vector3 LatLonToLocal(double latitude, double longitude, float z = 0f)
        {
            var (mx, my) = LatLonToWebMercator(latitude, longitude);
            var (x, y) = MercatorToRaw(mx, my); // identity unless a geo anchor is set (SetGeoAnchor)
            return new Vector3((float)(x - _localOrigin.X), (float)(y - _localOrigin.Y), z - _localOrigin.Z);
        }

        /// <summary>The exact opposite of LatLonToLocal() - e.g. Point3D.World back to a GPS position.</summary>
        public (double Latitude, double Longitude) LocalToLatLon(Vector3 local)
        {
            var (mx, my) = RawToMercator(local.X + (double)_localOrigin.X, local.Y + (double)_localOrigin.Y);
            return WebMercatorToLatLon(mx, my);
        }

        /// <summary>
        /// Like CreatePoint(), but from a GPS position - creates a standalone Point3D in local space,
        /// WITHOUT adding it to Points. Use it to build vertices for AddPolyline/AddPolygon (which add
        /// their vertices to Points automatically), or add it yourself via Points.Add(...).
        /// </summary>
        public Point3D CreatePointLatLon(double latitude, double longitude, float z = 0f, string label = "", Guid? id = null)
        {
            var local = LatLonToLocal(latitude, longitude, z);
            return new Point3D(local.X, local.Y, local.Z, label, id);
        }

        /// <summary>
        /// GPS position of the ground (raw Z = 0) under a screen position - e.g. for a "lat/lon under the
        /// cursor" status bar. Only meaningful when the world is in Web Mercator (always true while the OSM
        /// layer is on). Returns null if the ground plane can't be hit (a pure front/side view).
        /// </summary>
        public (double Latitude, double Longitude)? ScreenToLatLon(PointF screen)
        {
            if (!TryScreenToGround(screen, out var local)) return null;
            return LocalToLatLon(local);
        }

        /// <summary>
        /// Centers the view on a GPS position at a given OSM zoom level (0 = whole world ... 19 = single
        /// buildings; 1 tile pixel = 1 screen pixel at that level). Works with or without the tile layer.
        /// Does nothing before the canvas has a size (same as ZoomToFullExtent).
        /// </summary>
        public void ZoomToLatLon(double latitude, double longitude, double zoomLevel = 16)
        {
            if (_buffer == null) return;

            _pixelsPerMeter = ClampPixelsPerMeter((float)(256.0 * Math.Pow(2.0, zoomLevel) * MercatorUnitsPerWorldUnit / WebMercatorWorldSize));

            var (x, z) = ProjectRawXZ(LatLonToLocal(latitude, longitude));
            _panOffsetPixels = new PointF(
                _buffer.Width / 2f - x * _pixelsPerMeter,
                _buffer.Height / 2f + z * _pixelsPerMeter);

            Render();
        }

        /// <summary>
        /// Where a screen-space ray hits the ground plane (raw Z = 0), in local coordinates. Works in any
        /// view - in top view it's the same as ScreenToWorld(screen, 0); in a tilted 3D view it's the
        /// ground point actually seen at that pixel. False if the view looks along the ground (front/side view).
        /// </summary>
        private bool TryScreenToGround(PointF screen, out Vector3 local)
        {
            var a = ScreenToWorld(screen, 0f);
            var direction = ScreenToWorld(screen, 1f) - a; // the (unit) viewing direction - orthographic, same for every pixel

            if (Math.Abs(direction.Z) < 0.05f) // closer than ~3 deg to looking along the ground
            {
                local = default;
                return false;
            }

            float groundZ = -_localOrigin.Z; // raw Z = 0
            float t = (groundZ - a.Z) / direction.Z;
            local = a + direction * t;
            return true;
        }

        #endregion

        #region OpenStreetMap - geo anchor (a drawing in its own coordinates, placed on the map)

        private bool _geoAnchorSet;
        private double _anchorMercX, _anchorMercY;   // Web Mercator position of the anchor point
        private double _anchorScale = 1.0;           // Web Mercator units per real meter at the anchor = 1/cos(latitude)
        private double _anchorDrawX, _anchorDrawY;   // the anchor point in RAW drawing coordinates

        /// <summary>
        /// Places a drawing that lives in its own coordinates (e.g. from AutoCAD, in meters) onto the map:
        /// the drawing point (drawingX, drawingY) - by default its origin - lies at the given GPS position,
        /// and the drawing's "up" points at WorldUpBearingDegrees. From then on the world coordinates stay
        /// the DRAWING's (nothing you've added moves, X/Y stay as they are, distances stay real meters), and
        /// the OSM tiles are rotated/scaled into the drawing instead; with OsmLockNorthUp the view turns so
        /// that north is up while the map is shown (the drawing then appears rotated) and turns back after.
        ///
        ///     engine.WorldUpBearingDegrees = 60f;                    // drawing "up" faces ENE
        ///     engine.SetGeoAnchor(48.1437, 17.1078);                 // drawing 0,0 = this GPS position
        ///     engine.EnableOpenStreetMap();
        ///
        /// Without a geo anchor, the world coordinates themselves are Web Mercator while the map is on (the
        /// mode for data that already is in GPS/Web Mercator - see CreatePointLatLon). The anchor uses the
        /// Web Mercator scale at the anchor point for the whole drawing - accurate to ~0.1 % within a few km.
        /// </summary>
        public void SetGeoAnchor(double latitude, double longitude, float drawingX = 0f, float drawingY = 0f)
        {
            var (mx, my) = LatLonToWebMercator(latitude, longitude);
            _anchorMercX = mx;
            _anchorMercY = my;
            _anchorScale = 1.0 / Math.Cos(Math.Clamp(latitude, -WebMercatorMaxLatitude, WebMercatorMaxLatitude) * Math.PI / 180.0);
            _anchorDrawX = drawingX;
            _anchorDrawY = drawingY;
            _geoAnchorSet = true;

            if (IsNorthUpLocked) SetRotationKeepingCenter(NorthUpYaw, 90f);
            Render();
        }

        /// <summary>Removes the geo anchor - the world coordinates are treated as Web Mercator again while
        /// the map is on.</summary>
        public void ClearGeoAnchor()
        {
            if (!_geoAnchorSet) return;
            _geoAnchorSet = false;

            if (IsNorthUpLocked) SetRotationKeepingCenter(NorthUpYaw, 90f);
            Render();
        }

        /// <summary>True if SetGeoAnchor() has placed the drawing on the map.</summary>
        public bool HasGeoAnchor => _geoAnchorSet;

        /// <summary>RAW world (drawing) coordinates -> Web Mercator. Identity without a geo anchor.</summary>
        private (double X, double Y) RawToMercator(double x, double y)
        {
            if (!_geoAnchorSet) return (x, y);

            double b = _worldUpBearingDegrees * Math.PI / 180.0;
            double cos = Math.Cos(b), sin = Math.Sin(b);
            double dx = x - _anchorDrawX, dy = y - _anchorDrawY;

            double east = dx * cos + dy * sin;      // same rotation as ToTrueNorth()
            double north = -dx * sin + dy * cos;
            return (_anchorMercX + east * _anchorScale, _anchorMercY + north * _anchorScale);
        }

        /// <summary>Web Mercator -> RAW world (drawing) coordinates. Identity without a geo anchor.</summary>
        private (double X, double Y) MercatorToRaw(double mx, double my)
        {
            if (!_geoAnchorSet) return (mx, my);

            double b = _worldUpBearingDegrees * Math.PI / 180.0;
            double cos = Math.Cos(b), sin = Math.Sin(b);
            double east = (mx - _anchorMercX) / _anchorScale;
            double north = (my - _anchorMercY) / _anchorScale;

            double x = east * cos - north * sin;     // same rotation as FromTrueNorth()
            double y = east * sin + north * cos;
            return (x + _anchorDrawX, y + _anchorDrawY);
        }

        /// <summary>How many Web Mercator units one world unit is: 1 without an anchor (the world IS Web
        /// Mercator), 1/cos(latitude) with one (the world is real meters).</summary>
        private double MercatorUnitsPerWorldUnit => _geoAnchorSet ? _anchorScale : 1.0;

        /// <summary>The view yaw that puts true north straight up on screen (0 unless the drawing is rotated).</summary>
        private float NorthUpYaw => -EffectiveWorldUpBearing;

        #endregion

        #region OpenStreetMap - configuration

        private string _osmTileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
        private string _osmCacheFolderName = "";

        /// <summary>
        /// Tile URL with {z}, {x}, {y} placeholders (and optionally {s} for a/b/c subdomains). Default is
        /// the standard OSM server. Any other XYZ tile source in Web Mercator works too - just respect its
        /// terms and set OsmAttribution accordingly. Changing it drops the in-memory tiles of the old source.
        /// </summary>
        public string OsmTileUrlTemplate
        {
            get => _osmTileUrlTemplate;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Tile URL template must not be empty.", nameof(value));
                if (value == _osmTileUrlTemplate) return;
                _osmTileUrlTemplate = value;
                _osmCacheFolderName = "";
                ResetOsmState();
                Render();
            }
        }

        /// <summary>
        /// HTTP User-Agent sent with every tile request. The OSM tile usage policy REQUIRES an identifying
        /// User-Agent (requests without one get blocked) - set it to your own application's name/version
        /// and a contact URL, e.g. "MyApp/2.1 (+https://example.com/myapp)".
        /// </summary>
        public string OsmUserAgent { get; set; } = "ActiveCanvas/1.0.1 (+https://github.com/duroLAB/SimpleInteractiveEngine)";

        /// <summary>Attribution text drawn in the bottom-left corner while the layer is shown - required by
        /// the OSM license. Change it together with OsmTileUrlTemplate if you use a different tile source.</summary>
        public string OsmAttribution { get; set; } = "© OpenStreetMap contributors";

        /// <summary>Highest zoom level the tile server provides (19 for the standard OSM server). Zooming in
        /// further just stretches tiles of this level.</summary>
        public int OsmMaxZoom { get; set; } = 19;

        /// <summary>Opacity of the tile layer, 0..1 (e.g. 0.6 to make your own content stand out more).</summary>
        public float OsmOpacity { get; set; } = 1f;

        /// <summary>Folder for the on-disk tile cache (one subfolder per tile source). Null disables disk
        /// caching. Default: %LOCALAPPDATA%\ActiveCanvas\TileCache.</summary>
        public string? OsmDiskCacheDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ActiveCanvas", "TileCache");

        /// <summary>How long a tile on disk is used without asking the server again (OSM asks for at least
        /// 7 days). An older tile is still used as a fallback when the server can't be reached (offline).</summary>
        public TimeSpan OsmDiskCacheMaxAge { get; set; } = TimeSpan.FromDays(7);

        /// <summary>How many decoded tiles to keep in memory (~256 KB each). Always at least twice the
        /// number of tiles currently on screen, so a big window doesn't thrash.</summary>
        public int OsmMemoryCacheTiles { get; set; } = 256;

        /// <summary>True while the tile layer is on (between EnableOpenStreetMap and DisableOpenStreetMap).</summary>
        public bool IsOpenStreetMapEnabled => _osmEnabled;

        private bool _osmLockNorthUp = true;
        private bool _osmHidesBackgroundImage = true;

        /// <summary>
        /// If true (default), the view is locked to the top view with north up while the tile layer is on:
        /// EnableOpenStreetMap() switches to it (keeping the spot in the middle of the screen in place),
        /// SetView()/ShowIsometricView()/... are forced back to it, and dragging doesn't rotate the scene
        /// even with AllowFreeRotate = true. A map reads the way people expect it to, and tiles are made
        /// for exactly this view. Set to false to allow rotated/tilted views over the map.
        /// </summary>
        public bool OsmLockNorthUp
        {
            get => _osmLockNorthUp;
            set
            {
                if (value == _osmLockNorthUp) return;
                _osmLockNorthUp = value;
                if (IsNorthUpLocked)
                {
                    SetRotationKeepingCenter(NorthUpYaw, 90f);
                    Render();
                }
            }
        }

        /// <summary>True while the view is actually locked north-up (the tile layer is on and OsmLockNorthUp
        /// is true) - e.g. to disable "3D view"/rotation buttons in the host UI meanwhile.</summary>
        public bool IsNorthUpLocked => _osmEnabled && _osmLockNorthUp;

        /// <summary>
        /// If true (default), the background image (SetBackgroundImage / GeoTIFF) is hidden while the tile
        /// layer is on - it's only hidden, not removed, and shows again after DisableOpenStreetMap(). Set to
        /// false to draw it on top of the tiles (e.g. a georeferenced orthophoto in Web Mercator over the map).
        /// </summary>
        public bool OsmHidesBackgroundImage
        {
            get => _osmHidesBackgroundImage;
            set
            {
                if (value == _osmHidesBackgroundImage) return;
                _osmHidesBackgroundImage = value;
                Render();
            }
        }

        /// <summary>True while the background image is hidden because of the tile layer.</summary>
        private bool IsBackgroundImageHidden => _osmEnabled && _osmHidesBackgroundImage;

        /// <summary>Tile zoom level used by the last render (0..OsmMaxZoom), e.g. for a status bar.</summary>
        public int OsmCurrentZoom => _osmCurrentZoom;

        #endregion

        #region OpenStreetMap - state

        private readonly record struct TileKey(int Z, int X, int Y);

        // Shared by every canvas in the process: the OSM policy limits connections per APPLICATION.
        private static readonly HttpClient OsmHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim OsmDownloadGate = new SemaphoreSlim(2, 2);

        private const int OsmMaxTilesPerFrame = 300;           // safety cap - a very flat 3D view could otherwise "see" thousands
        private const int OsmFallbackLevels = 4;               // how many zoom levels up to look for a stand-in tile
        private static readonly TimeSpan OsmRetryDelay = TimeSpan.FromSeconds(30);

        private bool _osmEnabled;
        private int _osmCurrentZoom;
        private int _osmGeneration;                            // bumped on enable/disable/source change - late tiles of an old generation are dropped
        private CancellationTokenSource? _osmCts;
        private bool _osmRenderQueued;

        // Tiles the last render wanted - read by background downloads to skip tiles scrolled away meanwhile.
        // Always replaced as a whole (never mutated), so reading it from another thread is safe.
        private volatile HashSet<TileKey> _osmWanted = new HashSet<TileKey>();

        private readonly ConcurrentDictionary<TileKey, byte> _osmPending = new();          // download in progress
        private readonly ConcurrentDictionary<TileKey, DateTime> _osmFailedUntil = new();  // don't retry before (UTC)

        // In-memory LRU cache, UI thread only. Most recently used at the front.
        private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, Bitmap Bitmap)>> _osmCache = new();
        private readonly LinkedList<(TileKey Key, Bitmap Bitmap)> _osmLru = new();

        #endregion

        #region OpenStreetMap - public on/off

        /// <summary>
        /// Turns the tile layer on. Without a geo anchor the engine treats its raw world coordinates as Web
        /// Mercator meters: tiles are positioned accordingly, the scale bar and the measuring tool switch to
        /// real ground distances (Web Mercator stretches distances by 1/cos(latitude) - about 1.5x in Central
        /// Europe), and you can zoom out all the way to the whole world. With a geo anchor (SetGeoAnchor) the
        /// world stays the drawing's own coordinates and the tiles are fitted into it instead.
        /// </summary>
        public void EnableOpenStreetMap()
        {
            if (_osmEnabled) return;
            _osmEnabled = true;
            _osmGeneration++;
            _osmCts = new CancellationTokenSource();

            _yawBeforeOsm = _yawDeg;
            if (_osmLockNorthUp)
                SetRotationKeepingCenter(NorthUpYaw, 90f);

            Render();
        }

        private float _yawBeforeOsm;

        /// <summary>Turns the tile layer off and frees the tiles held in memory (the disk cache stays).</summary>
        public void DisableOpenStreetMap()
        {
            if (!_osmEnabled) return;
            _osmEnabled = false;
            ResetOsmState();

            // Turn back to the orientation from before the map (e.g. the drawing's "up" on top again).
            if (_osmLockNorthUp)
                SetRotationKeepingCenter(_yawBeforeOsm, _pitchDeg);

            Render();
        }

        /// <summary>
        /// Changes the camera rotation so that the ground point currently in the middle of the screen stays
        /// in the middle (a plain SetView() rotates around the world origin, which can throw the area you
        /// were looking at far off-screen). Doesn't render - the caller does.
        /// </summary>
        private void SetRotationKeepingCenter(float yawDeg, float pitchDeg)
        {
            if (_buffer == null)
            {
                _yawDeg = yawDeg;
                _pitchDeg = ClampPitch(pitchDeg);
                return;
            }

            var center = new PointF(_buffer.Width / 2f, _buffer.Height / 2f);
            bool hasGround = TryScreenToGround(center, out var groundPoint); // e.g. false in a pure front view

            _yawDeg = yawDeg;
            _pitchDeg = ClampPitch(pitchDeg);

            if (!hasGround) return;

            var (x, z) = ProjectRawXZ(groundPoint);
            _panOffsetPixels = new PointF(
                center.X - x * _pixelsPerMeter,
                center.Y + z * _pixelsPerMeter);
        }

        /// <summary>Cancels downloads in flight and drops all in-memory tiles - used on disable, source
        /// change and when the PictureBox is disposed.</summary>
        private void ResetOsmState()
        {
            _osmGeneration++;
            _osmCts?.Cancel(); // not disposed on purpose - background tasks may still be holding its token
            _osmCts = _osmEnabled ? new CancellationTokenSource() : null;

            _osmWanted = new HashSet<TileKey>();
            _osmPending.Clear();
            _osmFailedUntil.Clear();

            foreach (var entry in _osmLru) entry.Bitmap.Dispose();
            _osmLru.Clear();
            _osmCache.Clear();
        }

        #endregion

        #region OpenStreetMap - rendering

        /// <summary>Draws the visible tiles (or stretched lower-zoom stand-ins while they load) and requests
        /// the missing ones. Called from Render(), before the background image.</summary>
        private void DrawOsmTiles(Graphics g)
        {
            if (!_osmEnabled || _buffer == null) return;

            if (!TryGetVisibleMercatorRect(out double minX, out double minY, out double maxX, out double maxY))
            {
                _osmWanted = new HashSet<TileKey>();
                return;
            }

            int maxZoom = Math.Clamp(OsmMaxZoom, 0, 24);
            double screenPixelsPerMercatorUnit = _pixelsPerMeter / MercatorUnitsPerWorldUnit;
            int z = Math.Clamp((int)Math.Round(Math.Log2(WebMercatorWorldSize * screenPixelsPerMercatorUnit / 256.0)), 0, maxZoom);

            (int x0, int x1, int y0, int y1) range;
            while (true)
            {
                range = TileRange(z, minX, minY, maxX, maxY);
                long count = (long)Math.Max(0, range.x1 - range.x0 + 1) * Math.Max(0, range.y1 - range.y0 + 1);
                if (count <= OsmMaxTilesPerFrame || z == 0) break;
                z--;
            }
            _osmCurrentZoom = z;

            // Center-first, so the tiles in the middle of the screen are downloaded first.
            double cx = (range.x0 + range.x1) / 2.0, cy = (range.y0 + range.y1) / 2.0;
            var needed = new List<TileKey>();
            for (int ty = range.y0; ty <= range.y1; ty++)
                for (int tx = range.x0; tx <= range.x1; tx++)
                    needed.Add(new TileKey(z, tx, ty));
            needed.Sort((a, b) =>
                ((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy)).CompareTo((b.X - cx) * (b.X - cx) + (b.Y - cy) * (b.Y - cy)));

            _osmWanted = new HashSet<TileKey>(needed);

            var oldInterpolation = g.InterpolationMode;
            var oldPixelOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using (var attributes = new ImageAttributes())
            {
                // TileFlipXY stops GDI+ from blending the tile's edge pixels with "nothing" -
                // without it, thin light seams show up between neighbouring tiles.
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                if (OsmOpacity < 1f)
                    attributes.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(OsmOpacity, 0f, 1f) });

                foreach (var key in needed)
                {
                    if (TryGetCachedTile(key, out var bitmap))
                    {
                        DrawTile(g, bitmap, new RectangleF(0, 0, bitmap.Width, bitmap.Height), key, attributes);
                        continue;
                    }

                    RequestOsmTile(key);
                    DrawFallbackTile(g, key, attributes);
                }
            }

            g.InterpolationMode = oldInterpolation;
            g.PixelOffsetMode = oldPixelOffset;

            TrimOsmMemoryCache(needed.Count);
        }

        /// <summary>While a tile is loading, stretch the matching part of an already cached parent tile
        /// (one to OsmFallbackLevels zoom levels up) over its place - blurry for a moment instead of empty.</summary>
        private void DrawFallbackTile(Graphics g, TileKey key, ImageAttributes attributes)
        {
            for (int up = 1; up <= OsmFallbackLevels && key.Z - up >= 0; up++)
            {
                var parentKey = new TileKey(key.Z - up, key.X >> up, key.Y >> up);
                if (!TryGetCachedTile(parentKey, out var parent)) continue;

                int mask = (1 << up) - 1;
                float part = (float)parent.Width / (1 << up);
                var src = new RectangleF((key.X & mask) * part, (key.Y & mask) * part, part, part);
                DrawTile(g, parent, src, key, attributes);
                return;
            }
        }

        /// <summary>Draws (part of) a bitmap over a tile's square in the world - through Project(), like the
        /// background image, so it pans/zooms/rotates together with everything else.</summary>
        private void DrawTile(Graphics g, Bitmap bitmap, RectangleF src, TileKey key, ImageAttributes attributes)
        {
            double size = WebMercatorWorldSize / (1 << key.Z);
            double left = -WebMercatorWorldSize / 2.0 + key.X * size;
            double top = WebMercatorWorldSize / 2.0 - key.Y * size;

            var destination = new[]
            {
                Project(MercatorToLocal(left, top)),
                Project(MercatorToLocal(left + size, top)),
                Project(MercatorToLocal(left, top - size))
            };

            g.DrawImage(bitmap, destination, src, GraphicsUnit.Pixel, attributes);
        }

        /// <summary>Raw Web Mercator (double) -> local space on the ground plane, subtracting the origin in
        /// double precision. Neighbouring tiles share corner values exactly, so no gaps appear between them.</summary>
        private Vector3 MercatorToLocal(double mx, double my)
        {
            var (x, y) = MercatorToRaw(mx, my); // with a geo anchor this rotates/scales the tile into the drawing
            return new Vector3((float)(x - _localOrigin.X), (float)(y - _localOrigin.Y), -_localOrigin.Z);
        }

        /// <summary>Bounding box (raw Web Mercator meters) of the ground area seen on screen.</summary>
        private bool TryGetVisibleMercatorRect(out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue;
            maxX = maxY = double.MinValue;

            var corners = new[]
            {
                new PointF(0, 0), new PointF(_buffer!.Width, 0),
                new PointF(0, _buffer.Height), new PointF(_buffer.Width, _buffer.Height)
            };

            foreach (var corner in corners)
            {
                if (!TryScreenToGround(corner, out var local)) return false;
                var (x, y) = RawToMercator(local.X + (double)_localOrigin.X, local.Y + (double)_localOrigin.Y);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return true;
        }

        /// <summary>Tile index range covering a Web Mercator rectangle at zoom z, clipped to the world.
        /// Empty (x0 > x1 or y0 > y1) if the rectangle lies completely outside the map.</summary>
        private static (int x0, int x1, int y0, int y1) TileRange(int z, double minX, double minY, double maxX, double maxY)
        {
            double n = 1 << z;
            double half = WebMercatorWorldSize / 2.0;

            double fx0 = Math.Floor((minX + half) / WebMercatorWorldSize * n);
            double fx1 = Math.Floor((maxX + half) / WebMercatorWorldSize * n);
            double fy0 = Math.Floor((half - maxY) / WebMercatorWorldSize * n); // tile Y grows southwards
            double fy1 = Math.Floor((half - minY) / WebMercatorWorldSize * n);

            int last = (1 << z) - 1;
            return (
                (int)Math.Max(0, fx0), (int)Math.Min(last, fx1),
                (int)Math.Max(0, fy0), (int)Math.Min(last, fy1));
        }

        /// <summary>Attribution in the bottom-left corner (required by the OSM license), styled like the scale bar.</summary>
        private void DrawOsmAttribution(Graphics g)
        {
            if (!_osmEnabled || _buffer == null || string.IsNullOrEmpty(OsmAttribution)) return;

            var size = g.MeasureString(OsmAttribution, ScaleBarFont);
            var rect = new RectangleF(4f, _buffer.Height - size.Height - 6f, size.Width + 6f, size.Height + 2f);

            using (var backBrush = new SolidBrush(Color.FromArgb(200, LabelBackdropColor)))
                g.FillRectangle(backBrush, rect);

            using var textBrush = new SolidBrush(Color.FromArgb(220, LabelTextColor));
            g.DrawString(OsmAttribution, ScaleBarFont, textBrush, rect.X + 3f, rect.Y + 1f);
        }

        #endregion

        #region OpenStreetMap - ground distances (scale bar, measuring)

        /// <summary>
        /// Real ground meters per world unit at the view center. 1 normally; while the OSM layer is on it's
        /// cos(latitude), since 1 Web Mercator "meter" is 1/cos(latitude) real meters on the ground.
        /// </summary>
        private float GroundMetersPerWorldUnit()
        {
            if (!_osmEnabled || _geoAnchorSet || _buffer == null) return 1f; // with an anchor the world is real meters
            if (!TryScreenToGround(new PointF(_buffer.Width / 2f, _buffer.Height / 2f), out var center)) return 1f;

            double latitude = LocalToLatLon(center).Latitude;
            return (float)Math.Max(1e-6, Math.Cos(latitude * Math.PI / 180.0));
        }

        /// <summary>Distance between two local-space points in meters - plain Euclidean normally; while the OSM
        /// layer is on, the great-circle distance on the ground (haversine) combined with the height difference.</summary>
        private float MeasureWorldDistance(Vector3 a, Vector3 b)
        {
            if (!_osmEnabled || _geoAnchorSet) return Vector3.Distance(a, b);

            var (lat1, lon1) = LocalToLatLon(a);
            var (lat2, lon2) = LocalToLatLon(b);

            const double meanEarthRadius = 6371008.8;
            double phi1 = lat1 * Math.PI / 180.0, phi2 = lat2 * Math.PI / 180.0;
            double dPhi = phi2 - phi1;
            double dLambda = (lon2 - lon1) * Math.PI / 180.0;

            double h = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2)
                     + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
            double ground = 2.0 * meanEarthRadius * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
            double dz = b.Z - a.Z;

            return (float)Math.Sqrt(ground * ground + dz * dz);
        }

        #endregion

        #region OpenStreetMap - memory cache (UI thread only)

        private bool TryGetCachedTile(TileKey key, out Bitmap bitmap)
        {
            if (_osmCache.TryGetValue(key, out var node))
            {
                _osmLru.Remove(node);   // touch - move to the front (most recently used)
                _osmLru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
            bitmap = null!;
            return false;
        }

        private void AddToOsmMemoryCache(TileKey key, Bitmap bitmap)
        {
            if (_osmCache.TryGetValue(key, out var existing))
            {
                _osmLru.Remove(existing);
                existing.Value.Bitmap.Dispose();
            }
            _osmCache[key] = _osmLru.AddFirst((key, bitmap));
        }

        private void TrimOsmMemoryCache(int tilesOnScreen)
        {
            int limit = Math.Max(OsmMemoryCacheTiles, tilesOnScreen * 2);
            while (_osmCache.Count > limit && _osmLru.Last != null)
            {
                var oldest = _osmLru.Last.Value;
                _osmLru.RemoveLast();
                _osmCache.Remove(oldest.Key);
                oldest.Bitmap.Dispose();
            }
        }

        #endregion

        #region OpenStreetMap - downloading (background threads)

        /// <summary>Starts a background load of one tile (disk cache first, then the server), unless it's
        /// already loading or recently failed. All settings are captured here, on the UI thread.</summary>
        private void RequestOsmTile(TileKey key)
        {
            if (_osmCts == null) return;
            if (_osmFailedUntil.TryGetValue(key, out var retryAt) && DateTime.UtcNow < retryAt) return;
            if (!_osmPending.TryAdd(key, 0)) return;

            string url = BuildTileUrl(key);
            string userAgent = OsmUserAgent;
            string? diskPath = BuildDiskCachePath(key, url);
            TimeSpan maxAge = OsmDiskCacheMaxAge;
            int generation = _osmGeneration;
            var token = _osmCts.Token;

            _ = Task.Run(() => FetchOsmTileAsync(key, url, userAgent, diskPath, maxAge, generation, token));
        }

        private async Task FetchOsmTileAsync(TileKey key, string url, string userAgent, string? diskPath,
            TimeSpan maxAge, int generation, CancellationToken token)
        {
            bool posted = false;
            try
            {
                if (!_osmWanted.Contains(key)) return; // scrolled/zoomed away before we even started

                byte[]? data = null;
                byte[]? stale = null;

                if (diskPath != null && File.Exists(diskPath))
                {
                    try
                    {
                        var bytes = await File.ReadAllBytesAsync(diskPath, token).ConfigureAwait(false);
                        bool fresh = DateTime.UtcNow - File.GetLastWriteTimeUtc(diskPath) <= maxAge;
                        if (fresh) data = bytes; else stale = bytes;
                    }
                    catch (IOException) { }                  // e.g. being written by another request right now
                    catch (UnauthorizedAccessException) { }
                }

                if (data == null)
                {
                    await OsmDownloadGate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        if (!_osmWanted.Contains(key)) return; // waited in the queue - maybe not needed any more

                        data = await DownloadTileAsync(url, userAgent, token).ConfigureAwait(false);
                        if (data != null && diskPath != null)
                            TryWriteDiskCache(diskPath, data);
                    }
                    finally
                    {
                        OsmDownloadGate.Release();
                    }

                    data ??= stale; // offline / server error -> an expired copy is still better than nothing
                }

                var bitmap = data != null ? DecodeTile(data) : null;
                if (bitmap == null)
                {
                    _osmFailedUntil[key] = DateTime.UtcNow + OsmRetryDelay;
                    return;
                }

                posted = PostTileToUi(key, bitmap, generation);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // layer turned off / source changed - nothing to do
            }
            catch (Exception)
            {
                _osmFailedUntil[key] = DateTime.UtcNow + OsmRetryDelay;
            }
            finally
            {
                if (!posted) _osmPending.TryRemove(key, out _);
            }
        }

        /// <summary>One HTTP GET. Null on any failure (HTTP error, timeout, no network) - never throws,
        /// except for cancellation through the token.</summary>
        private static async Task<byte[]?> DownloadTileAsync(string url, string userAgent, CancellationToken token)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

                using var response = await OsmHttp.SendAsync(request, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException) when (!token.IsCancellationRequested)
            {
                return null; // HttpClient timeout
            }
        }

        /// <summary>PNG/JPEG bytes -> a premultiplied-alpha Bitmap (the format GDI+ draws fastest). Null if
        /// the bytes aren't a valid image (e.g. an HTML error page).</summary>
        private static Bitmap? DecodeTile(byte[] data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                using var image = Image.FromStream(stream);

                var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                }
                return bitmap;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Hands a finished tile over to the UI thread, which owns the cache, and schedules ONE
        /// re-render for however many tiles arrive in a burst.</summary>
        private bool PostTileToUi(TileKey key, Bitmap bitmap, int generation)
        {
            try
            {
                if (_pictureBox.IsDisposed || !_pictureBox.IsHandleCreated)
                {
                    bitmap.Dispose();
                    return false;
                }

                _pictureBox.BeginInvoke(new Action(() =>
                {
                    _osmPending.TryRemove(key, out _);

                    if (generation != _osmGeneration || !_osmEnabled)
                    {
                        bitmap.Dispose(); // a tile for a layer that's been turned off / switched meanwhile
                        return;
                    }

                    AddToOsmMemoryCache(key, bitmap);

                    if (_osmRenderQueued) return;
                    _osmRenderQueued = true;
                    _pictureBox.BeginInvoke(new Action(() =>
                    {
                        _osmRenderQueued = false;
                        Render();
                    }));
                }));
                return true;
            }
            catch (Exception) when (_pictureBox.IsDisposed || !_pictureBox.IsHandleCreated)
            {
                bitmap.Dispose(); // the window closed between the check and BeginInvoke
                return false;
            }
        }

        private string BuildTileUrl(TileKey key)
        {
            return _osmTileUrlTemplate
                .Replace("{z}", key.Z.ToString())
                .Replace("{x}", key.X.ToString())
                .Replace("{y}", key.Y.ToString())
                .Replace("{s}", "abc"[(key.X + key.Y) % 3].ToString());
        }

        /// <summary>{cache}\{host}_{hash of template}\{z}\{x}\{y}.png - one folder per tile source, so two
        /// different map styles never mix.</summary>
        private string? BuildDiskCachePath(TileKey key, string url)
        {
            if (string.IsNullOrEmpty(OsmDiskCacheDirectory)) return null;

            if (_osmCacheFolderName.Length == 0)
            {
                string host = Uri.TryCreate(BuildTileUrl(new TileKey(0, 0, 0)), UriKind.Absolute, out var uri) ? uri.Host : "tiles";
                uint hash = 2166136261; // FNV-1a - stable across runs, unlike string.GetHashCode()
                unchecked
                {
                    foreach (char c in _osmTileUrlTemplate) { hash ^= c; hash *= 16777619; }
                }
                _osmCacheFolderName = $"{host}_{hash:x8}";
            }

            string extension = Uri.TryCreate(url, UriKind.Absolute, out var tileUri)
                ? Path.GetExtension(tileUri.AbsolutePath) : "";
            if (string.IsNullOrEmpty(extension)) extension = ".png";

            return Path.Combine(OsmDiskCacheDirectory, _osmCacheFolderName,
                key.Z.ToString(), key.X.ToString(), key.Y + extension);
        }

        /// <summary>Writes via a temp file + move, so a half-written tile is never read by another request.</summary>
        private static void TryWriteDiskCache(string path, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllBytes(temp, data);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception)
            {
                // disk cache is best-effort - a read-only/full disk just means more downloads
            }
        }

        #endregion
    }
}
