# ActiveCanvas

**A simple, interactive drawing engine for WinForms (.NET 9)** — built for situations where you need to
draw and interactively edit elements on a canvas: from simple technical sketches, through GIS/map
backgrounds, to interactive diagrams and graphs with connected nodes.

**Current version: 1.0.1** — see the [Changelog](#changelog).

It's not a CAD or GIS library — it's a small, readable engine (a single `ActiveCanvas` class, split across
several files by responsibility via `partial class`) that handles the stuff you end up needing again and
again when drawing on top of a `PictureBox`: coordinate conversion, pan/zoom, selection, drag-and-drop,
hit-testing — done properly once, instead of rebuilt from scratch for every new project.

## Why you might care

- **2D and 3D with the same code.** Top view is mathematically just a special case of free 3D rotation
  with a fixed angle — no duplicated logic between a "2D mode" and a "3D mode".
- **Fits a wide range of engineering tasks** — from floor plans and technical drawings, through GIS
  backgrounds (including GeoTIFF with automatic georeferencing), to interactive flowcharts/diagrams with
  connected nodes and directional connectors.
- **Small surface to learn** — one class, a fluent API (`.With...()` methods), no external dependencies
  beyond the .NET BCL.

## What it can do

**Primitives**
- `Point3D` — a point with an optional shape (circle, square, triangle, diamond, star, cross)
- `Polyline` / `Polygon` — a line / a closed area, whose vertices are shared `Point3D` objects (each
  individually draggable)
- `ImageMarker` — an icon/image at a point, either a fixed pixel size or a real-world size
- `ShapeMarker` — a standalone vector shape (circle/ellipse/rectangle/rounded rectangle/custom polygon),
  dragged as a single whole, with optional multi-line text drawn inside it (auto-scaling font)
- `ShapeConnector` — a line connecting two `ShapeMarker`s, straight or elbow-routed (Orthogonal), an
  optional arrowhead, and automatic attachment-side selection based on relative position (updated live
  during drag-and-drop)

**Interaction**
- Pan (right/middle mouse button), zoom (mouse wheel, works independently of focus), free 3D rotation
- Drag-and-drop, hover effects, selection (unified across all element types), click and double-click as
  separate events
- Interactive drawing by clicking (lines/areas) and placement (points/icons) directly from the GUI
- Distance measuring tool (click-click, with a live preview)
- `ZoomToFullExtent()` — automatically frames all current content

**Backgrounds and data**
- A raster background image with automatic georeferencing from GeoTIFF (world files like `.tfw`, as well
  as embedded GeoTIFF tags), loaded asynchronously with a "please wait" overlay for large files
- Elevation backgrounds: one image each for the front (X-Z) and side (Y-Z) plane, next to the plan view -
  they stand in the 3D view like walls and can be hidden or made semi-transparent
- `SetLocalOrigin()` — a coordinate offset for precision when working with large real-world coordinates
  (S-JTSK, UTM, ...)
- A custom render hook (`CustomBackgroundPaint`) for drawing any custom background yourself (e.g. a
  parsed DXF drawing)
- An optional OpenStreetMap tile layer (or any other XYZ tile source) with GPS (WGS84) helpers -
  background download, memory + disk cache, real ground distances in the scale bar and measuring tool
- `SetGeoAnchor()` — places a drawing in its own coordinates (e.g. from AutoCAD) on the map at a GPS
  position; the map is fitted under the drawing, the drawing's coordinates never change
- A north arrow in 2D views, and `WorldUpBearingDegrees` for drawings whose "up" isn't north (e.g. a
  building that in reality faces south-east); `ToTrueNorth()` / `FromTrueNorth()` convert drawing
  coordinates to north-aligned East/North ones, and the property grid shows them next to X/Y

**UI integration**
- `CreatePropertyGrid()` — a ready-made `PropertyGrid` bound to the selected item; double-click → a
  properties dialog
- A consistent GUID `Id` on every item, for linking with the host application's own data
- `FindById()`, events for selection/clicks/moves — no need to touch the `PictureBox` directly
- Dark/light theme for the canvas (`ApplyDarkTheme()` / `ApplyLightTheme()`) and for the whole host form
  (`AppTheme`), plus hatch/dot pattern fills for areas and shapes (`HatchBrushes`)

## Requirements

- .NET 9.0-windows (`net9.0-windows`)
- WinForms (`<UseWindowsForms>true</UseWindowsForms>` in your `.csproj`)
- No external NuGet dependencies
- Internet access only for the optional OpenStreetMap layer

## Installation

**Same solution** — add a project reference:

```
Solution Explorer → your project → Add → Project Reference... → check ActiveCanvasEngine
```

**As a standalone DLL** (a different solution) — build it (`dotnet build -c Release`), copy
`ActiveCanvasEngine.dll`
(plus the `.pdb` for debug symbols) into your project, and reference it via **Add → Project Reference... →
Browse**.

> The target project must also be WinForms (`net9.0-windows`, `UseWindowsForms=true`) — the engine uses
> `System.Drawing`/`System.Windows.Forms`.

## Quick start

```csharp
using ActiveCanvasEngine;

var engine = new ActiveCanvas(pictureBox1);   // default = top view, pure 2D
engine.AddPoint(5, 3, 0, "Point A");
```

That's it to get started — the engine handles the mouse, canvas resizing, and rendering by itself.

---

## Examples by element type

### Point3D

```csharp
// a basic point
engine.AddPoint(0, 0, 0, "Point");

// custom look
engine.AddPoint(4, 2, 0, "Sensor",
    brush: new SolidBrush(Color.Crimson),
    shape: ActiveCanvas.PointShape.Diamond,
    size: 10f);

// locked (selectable, but not draggable)
engine.AddPoint(4, -2, 0, "Reference point", draggable: false);
```

### Polyline

```csharp
var route = engine.AddPolyline(
    new[] { (0f, 0f, 0f), (3f, 4f, 0f), (7f, 3f, 0f) },
    label: "Route A",
    linePen: new Pen(Color.ForestGreen, 3f));
```

### Polygon

```csharp
var zone = engine.AddPolygon(
    new[] { (10f, 0f, 0f), (15f, 0f, 0f), (15f, 4f, 0f), (10f, 4f, 0f) },
    label: "Zone A",
    fillBrush: new SolidBrush(Color.FromArgb(80, Color.Orange)),
    outlinePen: new Pen(Color.OrangeRed, 2f));
```

### ImageMarker

```csharp
// fixed pixel size, like a map pin
engine.AddImage(pinBitmap, 6, 8, 0, "Warehouse", width: 28);

// real-world size, scales with zoom
var building = engine.AddImage(floorplanBitmap, -6, 6, 0, "Hall 1", width: 4f);
building.WithScaleWithZoom(true);
```

### ShapeMarker

```csharp
var box = engine.AddShape(5, 3, 0, ActiveCanvas.MarkerShapeType.RoundedRectangle,
    brush: new SolidBrush(Color.LightSteelBlue), width: 3f, height: 2f);

box.WithLabel("Hall 7").WithLabelAnchor(ActiveCanvas.LabelAnchor.Bottom);
box.WithInnerText("Capacity: 500 units\nLast inventory: 03/2025")
   .WithAutoScaleInnerText(true);

// a custom outline from a set of points (normalized to -0.5..0.5)
engine.AddCustomShape(2, 5, 0,
    new[] { (0f, -0.5f), (0.5f, 0.1f), (0.15f, 0.1f), (0.15f, 0.5f),
            (-0.15f, 0.5f), (-0.15f, 0.1f), (-0.5f, 0.1f) },
    label: "Direction", brush: new SolidBrush(Color.DodgerBlue));
```

### ShapeConnector

```csharp
var a = engine.AddShape(0, 0, 0, ActiveCanvas.MarkerShapeType.Rectangle, label: "Input");
var b = engine.AddShape(6, 3, 0, ActiveCanvas.MarkerShapeType.Rectangle, label: "Output");

engine.AddConnector(a, b, label: "processing",
        routing: ActiveCanvas.ConnectorRouting.Orthogonal)
    .WithAutoAnchors();   // picks the right side automatically, even while dragging the shapes
```

### Background image / GeoTIFF

```csharp
// with automatic georeferencing (world file or embedded GeoTIFF tags)
await engine.SetBackgroundImageFromGeoTiffAsync(@"C:\data\ortho.tif");
engine.ZoomToFullExtent();

// without georeferencing - set the width by hand
engine.SetBackgroundImage(bitmap, worldX: 0, worldY: 0, worldWidthMeters: 250f);
```

### Front / side view background (elevations)

```csharp
// an elevation drawing in the X-Z plane at Y = 0: X from 0 to 60 m, bottom edge at Z = 0
engine.SetFrontBackgroundImage(ActiveCanvas.LoadImageFile(@"C:\data\elevation.png"), minX: 0, minZ: 0, widthMeters: 60);
engine.ShowFrontView();

// side view (Y-Z plane at X = 0), shown the right way round in ShowSideView()
engine.SetSideBackgroundImage(sideBitmap, minY: 0, minZ: 0, widthMeters: 40, atX: 0);

// in the 3D view both stand in the scene - hide one or make it semi-transparent
engine.SetBackgroundOpacity(ActiveCanvas.BackgroundPlane.Front, 0.5f);
engine.SetBackgroundVisible(ActiveCanvas.BackgroundPlane.Side, false);
```

The plan-view image (`SetBackgroundImage`, GeoTIFF) is the `Ground` plane; visibility, opacity and
`ClearBackgroundImage(plane)` work the same way for all three.

### OpenStreetMap tile layer

```csharp
// While the layer is on, RAW world coordinates are Web Mercator meters (EPSG:3857).
engine.SetLocalOriginLatLon(48.1437, 17.1078);              // once, before adding content (float precision)
engine.OsmUserAgent = "MyApp/1.0 (+https://example.com)";   // required by the OSM tile usage policy
engine.EnableOpenStreetMap();
engine.ZoomToLatLon(48.1437, 17.1078, zoomLevel: 16);

// place content by GPS coordinates
var castle = engine.CreatePointLatLon(48.14216, 17.10004, label: "Castle");
var gate = engine.CreatePointLatLon(48.14513, 17.10668, label: "Gate");
engine.AddPolyline(new[] { castle, gate }, "Route");

// GPS position under the cursor
var geo = engine.ScreenToLatLon(mousePosition);   // (Latitude, Longitude)? - null in a front/side view
```

**A drawing in its own coordinates on the map** (e.g. from AutoCAD, in meters, rotated against north):

```csharp
engine.WorldUpBearingDegrees = 60f;           // the drawing's "up" really faces 60 deg (ENE)
engine.SetGeoAnchor(48.1437, 17.1078);        // drawing 0,0 lies at this GPS position
engine.EnableOpenStreetMap();                 // the map is fitted under the drawing, view turns north-up
```

The world coordinates stay the drawing's own (nothing moves, distances stay real meters); only the tiles
are rotated and scaled into the drawing. `SetGeoAnchor(lat, lon, drawingX, drawingY)` anchors any drawing point.

Tiles load in the background (max. 2 parallel requests), are cached in memory and on disk
(`%LOCALAPPDATA%\ActiveCanvas\TileCache`, 7 days by default), and a cached lower-zoom tile is stretched
in place while a tile is still loading. Another tile source: set `OsmTileUrlTemplate` and `OsmAttribution`.

While the layer is on, the view is locked to the top view with north up (`OsmLockNorthUp`, check
`IsNorthUpLocked` to disable rotation buttons in your UI), and the background image is hidden - not removed -
until the layer is turned off again (`OsmHidesBackgroundImage`). Set either property to `false` to opt out.
Please respect the [OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/) - heavy
use needs your own tile server or a commercial provider.

### Interactive drawing and measuring

```csharp
engine.StartDrawingPolygon("New zone");
engine.PolygonDrawingFinished += (s, polygon) => Console.WriteLine("Done");

engine.StartMeasuring();
engine.MeasurementCompleted += (s, meters) => statusLabel.Text = $"{meters:F2} m";
```

### PropertyGrid dialog on double-click

```csharp
engine.ShapeDoubleClicked += (s, shape) =>
{
    using var dlg = new Form { Text = "Properties", Width = 320, Height = 400 };
    dlg.Controls.Add(engine.CreatePropertyGrid(shape));
    dlg.ShowDialog(this);
};
```

---

## Architecture (for orientation in the files)

| File | Responsibility |
|---|---|
| `ActiveCanvas.cs` | Data, configuration, state, constructor, events |
| `ActiveCanvas.Types.cs` | Nested types (`Point3D`, `Polyline`, `Polygon`, `ImageMarker`, `ShapeMarker`, `ShapeConnector`) |
| `ActiveCanvas.PublicApi.cs` | `Add...()` methods, `Clear()`, `FindById()`, drawing/placing API |
| `ActiveCanvas.Camera.cs` | View presets, coordinate projection, hit-testing, selection |
| `ActiveCanvas.Input.cs` | Mouse handling, wheel zoom via `IMessageFilter` |
| `ActiveCanvas.Rendering.cs` | All drawing |
| `ActiveCanvas.PropertyGrid.cs` | `PropertyGrid` integration |
| `ActiveCanvas.Backgrounds.cs` | Background images in the plan / front / side plane, visibility, opacity |
| `ActiveCanvas.GeoTiff.cs` | Georeferencing from a world file / GeoTIFF tags |
| `ActiveCanvas.OpenStreetMap.cs` | OSM/XYZ tile layer, Web Mercator and GPS helpers, tile cache |
| `ActiveCanvas.NorthArrow.cs` | North arrow for rotated drawings / rotated 2D views |
| `ActiveCanvas.BusyOverlay.cs` | "Please wait" overlay for slower operations |
| `ActiveCanvas.Cursors.cs` | Custom cursor for drawing/placement |
| `ActiveCanvas.Theme.cs` | Dark/light canvas theme |
| `AppTheme.cs` | Dark/light theme for a whole host form |
| `HatchBrushes.cs` | Hatch/dot pattern brushes |
| `BackgroundImageOpener.cs` | Ready-made "open background image" dialog for a host app |

## Changelog

### Unreleased

- **Renamed (breaking):** namespace `SimpleDrawingEngine` → `ActiveCanvasEngine`, library project/DLL
  `SimpleInteractiveEngine.Core` → `ActiveCanvasEngine` (`ActiveCanvasEngine.dll`), solution
  `ActiveCanvasEngine.sln`. The class is still `ActiveCanvas` - just change `using SimpleDrawingEngine;`
  to `using ActiveCanvasEngine;` and re-add the project/DLL reference.

- Background images for the front (X-Z) and side (Y-Z) plane in addition to the plan view
  (`SetFrontBackgroundImage()`, `SetSideBackgroundImage()`), per-plane visibility and opacity
  (`SetBackgroundVisible()`, `SetBackgroundOpacity()`), `ClearBackgroundImage(plane)`,
  `ClearAllBackgroundImages()`, `LoadImageFile()`
- `BackgroundImageOpener.ShowAsync()` can open a front/side image too

### 1.0.1

**OpenStreetMap / map backgrounds**
- Optional OSM (or any XYZ) tile layer: `EnableOpenStreetMap()` / `DisableOpenStreetMap()`, background
  download (max. 2 parallel requests), memory + disk cache, lower-zoom stand-in tiles while loading,
  attribution, configurable tile source (`OsmTileUrlTemplate`, `OsmAttribution`, `OsmUserAgent`, ...)
- GPS helpers: `LatLonToWebMercator()`, `WebMercatorToLatLon()`, `SetLocalOriginLatLon()`,
  `LatLonToLocal()`, `LocalToLatLon()`, `CreatePointLatLon()`, `ScreenToLatLon()`, `ZoomToLatLon()`
- `SetGeoAnchor()` — a drawing in its own coordinates placed on the map
- While the map is shown the view is locked north-up (`OsmLockNorthUp`, `IsNorthUpLocked`) and the
  background image is hidden (`OsmHidesBackgroundImage`)
- Scale bar and measuring tool show real ground distances on a Web Mercator map
- Zoom limits are public (`MinPixelsPerMeter`, `MaxPixelsPerMeter`); with the map on you can zoom out to
  the whole world

**Orientation**
- North arrow in 2D views (`NorthArrow`: `Always` / `WhenRotated` / `Never`)
- `WorldUpBearingDegrees`, `ToTrueNorth()` / `FromTrueNorth()`, East/North in the property grid

**Other**
- Dark/light theme (`ApplyDarkTheme()`, `ApplyLightTheme()`, `AppTheme`) and `HatchBrushes`
- Fix: the background image keeps its position when `SetLocalOrigin()` is called after loading it

### 1.0.0

- First public release

## License

MIT — see [LICENSE](LICENSE) for the full text.
