# ActiveCanvas

**A simple, interactive drawing engine for WinForms (.NET 8)** — built for situations where you need to
draw and interactively edit elements on a canvas: from simple technical sketches, through GIS/map
backgrounds, to interactive diagrams and graphs with connected nodes.

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
- `SetLocalOrigin()` — a coordinate offset for precision when working with large real-world coordinates
  (S-JTSK, UTM, ...)
- A custom render hook (`CustomBackgroundPaint`) for drawing any custom background yourself (e.g. a
  parsed DXF drawing)

**UI integration**
- `CreatePropertyGrid()` — a ready-made `PropertyGrid` bound to the selected item; double-click → a
  properties dialog
- A consistent GUID `Id` on every item, for linking with the host application's own data
- `FindById()`, events for selection/clicks/moves — no need to touch the `PictureBox` directly

## Requirements

- .NET 8.0-windows
- WinForms (`<UseWindowsForms>true</UseWindowsForms>` in your `.csproj`)
- No external NuGet dependencies

## Installation

**Same solution** — add a project reference:

```
Solution Explorer → your project → Add → Project Reference... → check ActiveCanvas
```

**As a standalone DLL** (a different solution) — build it (`dotnet build -c Release`), copy the `.dll`
(plus the `.pdb` for debug symbols) into your project, and reference it via **Add → Project Reference... →
Browse**.

> The target project must also be WinForms (`net8.0-windows`, `UseWindowsForms=true`) — the engine uses
> `System.Drawing`/`System.Windows.Forms`.

## Quick start

```csharp
using SimpleDrawingEngine;

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
| `ActiveCanvas.GeoTiff.cs` | Georeferencing from a world file / GeoTIFF tags |
| `ActiveCanvas.BusyOverlay.cs` | "Please wait" overlay for slower operations |
| `ActiveCanvas.Cursors.cs` | Custom cursor for drawing/placement |

## License

_(fill in as you like - e.g. MIT)_
