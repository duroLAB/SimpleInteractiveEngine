using ActiveCanvasEngine;
using System.Globalization;
using System.Numerics;
using System.Windows.Forms;
using System.Xml.Linq;

namespace ActiveCanvasEngine.TestApp
{
    public partial class MainForm : Form
    {
        ActiveCanvas engine;
        private bool _isDark;

        // OSM demo - Bratislava, Hlavne namestie
        private const double DemoLatitude = 48.1437;
        private const double DemoLongitude = 17.1078;
        private bool _geoDemoAdded;
        public MainForm()
        {
            InitializeComponent();
            engine = new ActiveCanvas(pictureBox1);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            DemoScene.Build(engine);


            /*   // odteraz zadávaš VŠETKO v surových S-JTSK súradniciach - engine si sám odpočíta offset
               engine.SetBackgroundImageFromGeoTiff(@"D:\TestGisData\slovnaftOrto\BRATISLAVA_7-2_BL.tif");
               engine.AddPoint(-569500f, -1283200f, 0f, "Bod A"); // reálna S-JTSK súradnica, žiadne prepočítavanie naspamäť

               engine.ZoomToFullExtent();*/

            toolStripButton3D2Dview.Image = imageList1.Images[1];

            // Status bar: GPS position under the cursor while the OSM layer is on.
            pictureBox1.MouseMove += (s, e) =>
            {
                var geo = engine.IsOpenStreetMapEnabled ? engine.ScreenToLatLon(e.Location) : null;
                toolStripStatusLabelCoords.Text = geo == null ? "" : string.Format(CultureInfo.InvariantCulture,
                    "Lat {0:F6}   Lon {1:F6}   |   OSM zoom {2}", geo.Value.Latitude, geo.Value.Longitude, engine.OsmCurrentZoom);
            };

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) engine.CancelDrawing(); };
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) engine.FinishDrawing(); };


            engine.PointDoubleClicked += (s, p) =>
            {
                var grid = engine.CreatePropertyGrid(p, hiddenProperties: new[] { "Id", "HoverEnabled", "Draggable" });
                // zostane len: X, Y, Z, Label, ShowLabel, Shape, Size, Color, Selectable

                using var dlg = new Form { Text = "Point properties", Width = 320, Height = 380 };
                dlg.Controls.Add(grid);
                dlg.ShowDialog(this);
            };
            ///


            engine.PolylineDoubleClicked += (s, pl) => ShowPropertyDialog(pl);
            engine.PolygonDoubleClicked += (s, pg) => ShowPropertyDialog(pg);
            engine.ImageDoubleClicked += (s, im) => ShowPropertyDialog(im);


            engine.PointPlaced += (s, p) => { MessageBox.Show("Point placed! x:"+ p.World.X);};
            engine.PolygonDrawingFinished += (s, pg) => { MessageBox.Show("Polygon finished!"); };


            engine.CustomBackgroundPaint += (s, e) =>
            {
                var visible = engine.GetVisibleWorldBounds(); // orezanie – kresli len to, čo je vidno

                using var wallPen = new Pen(Color.Black, 1.5f);

                //  if (!visible.IntersectsWith(line.Bounds)) continue; // preskoč, čo je mimo obrazovky

                var p1 = engine.Project(new Vector3(5, 20, 0));
                var p2 = engine.Project(new Vector3(5, 30, 0));
                e.Graphics.DrawLine(wallPen, p1, p2);

            };
        }


        private void ShowPropertyDialog(object item)
        {
            var grid = engine.CreatePropertyGrid(item);
            if (grid == null) return; // neznámy typ



            using var dlg = new Form { Text = "Properties", Width = 320, Height = 420, StartPosition = FormStartPosition.CenterParent };
            dlg.Controls.Add(grid); // grid má Dock = Fill, netreba nič ďalšie
            dlg.ShowDialog(this);
        }

        private void toolStripButtonAddPoint_Click(object sender, EventArgs e)
        {
            engine.StartPlacingPoints(label: "New Point", brush: new SolidBrush(Color.Crimson), shape: ActiveCanvas.PointShape.Diamond, size: 8f, continuous: false);
        }

        private void toolStripButtonAddPolyLine_Click(object sender, EventArgs e)
        {
            engine.StartDrawingPolyline("Nová trasa", new Pen(Color.ForestGreen, 3f));

        }

        private void toolStripButtonAddPolygon_Click(object sender, EventArgs e)
        {
            engine.StartDrawingPolygon("Nový polygon", new SolidBrush(Color.FromArgb(128, Color.LightBlue)), new Pen(Color.DarkBlue, 2f));
        }

        private void toolStripButton3D2Dview_Click(object sender, EventArgs e)
        {
            float pd = engine.PitchDegrees;
            if (pd >= 89f && pd < 91)
            {
                toolStripButton3D2Dview.Image = imageList1.Images[0];
                engine.ShowIsometricView();
                engine.AllowFreeRotate = true;
                engine.ZoomToFullExtent();
            }
            else
            {
                toolStripButton3D2Dview.Image = imageList1.Images[1];
                engine.ShowTopView();
                engine.AllowFreeRotate = false;
                engine.ZoomToFullExtent();
            }
        }

        private void toolStripButtonAddIcon_Click(object sender, EventArgs e)
        {
            string iconsPath = Path.Combine(AppContext.BaseDirectory, "testicons");
            using (var dlg = new IconPicker.IconPickerDialog(iconsPath))
            {
                dlg.ThumbnailSize = 32;   // predvolené
                dlg.IconsPerRow = 60;     // predvolené, ovplyvňuje počiatočnú šírku okna
                dlg.AllowedExtensions = new[] { ".png", ".bmp", ".ico" };

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string cesta = dlg.SelectedFilePath;
                    // pictureBox1.Image = Image.FromFile(cesta);
                    Image icon = Image.FromFile(cesta);
                    engine.StartPlacingImages(icon, label: "Kamera", width: 32, continuous: false);
                }
            }
        }

        private void toolStripButtonFullZoom_Click(object sender, EventArgs e)
        {
            engine.ZoomToFullExtent();           // 10% okraj (default)
            engine.ZoomToFullExtent(0f);                                      //   engine.ZoomToFullExtent(0f);         // presne na okraj, bez rezervy

        }

        private readonly ContextMenuStrip _backgroundMenu = new ContextMenuStrip();

        /// <summary>Background images: open one per plane (plan / front / side), show/hide, opacity.</summary>
        private void toolStripButtonOpenBackGroundImage_Click(object sender, EventArgs e)
        {
            _backgroundMenu.Items.Clear();
            _backgroundMenu.Items.Add("Otvoriť pôdorys (X–Y)...", null, async (s, a) => await OpenBackgroundAsync(ActiveCanvas.BackgroundPlane.Ground));
            _backgroundMenu.Items.Add("Otvoriť čelný pohľad (X–Z)...", null, async (s, a) => await OpenBackgroundAsync(ActiveCanvas.BackgroundPlane.Front));
            _backgroundMenu.Items.Add("Otvoriť bočný pohľad (Y–Z)...", null, async (s, a) => await OpenBackgroundAsync(ActiveCanvas.BackgroundPlane.Side));

            var planes = new[]
            {
                (Plane: ActiveCanvas.BackgroundPlane.Ground, Name: "pôdorys"),
                (Plane: ActiveCanvas.BackgroundPlane.Front, Name: "čelný pohľad"),
                (Plane: ActiveCanvas.BackgroundPlane.Side, Name: "bočný pohľad")
            };

            foreach (var (plane, name) in planes)
            {
                bool hasImage = engine.HasBackgroundImage(plane);
                _backgroundMenu.Items.Add(new ToolStripSeparator());

                var visible = new ToolStripMenuItem($"Zobraziť {name}")
                {
                    Enabled = hasImage,
                    Checked = engine.IsBackgroundVisible(plane)
                };
                visible.Click += (s, a) => engine.SetBackgroundVisible(plane, !engine.IsBackgroundVisible(plane));
                _backgroundMenu.Items.Add(visible);

                var opacity = new ToolStripMenuItem($"Krytie – {name}") { Enabled = hasImage };
                foreach (int percent in new[] { 100, 75, 50, 25 })
                {
                    var option = new ToolStripMenuItem($"{percent} %")
                    {
                        Checked = Math.Abs(engine.GetBackgroundOpacity(plane) * 100f - percent) < 1f
                    };
                    option.Click += (s, a) => engine.SetBackgroundOpacity(plane, percent / 100f);
                    opacity.DropDownItems.Add(option);
                }
                _backgroundMenu.Items.Add(opacity);

                var remove = new ToolStripMenuItem($"Odstrániť {name}") { Enabled = hasImage };
                remove.Click += (s, a) => engine.ClearBackgroundImage(plane);
                _backgroundMenu.Items.Add(remove);
            }

            var button = toolStripButtonOpenBackGroundImage;
            _backgroundMenu.Show(toolStrip1, new Point(button.Bounds.Left, button.Bounds.Bottom));
        }

        private async Task OpenBackgroundAsync(ActiveCanvas.BackgroundPlane plane)
        {
            // A background image is plain local meters / S-JTSK here, not Web Mercator - and it would be
            // hidden under the map anyway (OsmHidesBackgroundImage) - so opening one switches the map off.
            if (toolStripButtonOsm.Checked)
            {
                toolStripButtonOsm.Checked = false;
                SetMapEnabled(false);
            }

            await BackgroundImageOpener.ShowAsync(this, engine, plane);
            if (plane == ActiveCanvas.BackgroundPlane.Ground || !engine.HasBackgroundImage(plane)) return;

            // An elevation is only seen face-on in its own view - switch there so it's visible right away.
            if (plane == ActiveCanvas.BackgroundPlane.Front) engine.ShowFrontView();
            else engine.ShowSideView();

            toolStripButton3D2Dview.Image = imageList1.Images[0]; // we're out of the top view now
            engine.ZoomToFullExtent();
        }

        private void toolStripButtonClearAll_Click(object sender, EventArgs e)
        {
            
             engine.Clear();
             engine.SetLocalOrigin(0, 0);
        }

        private void toolStripButtonLengthMeasure_Click(object sender, EventArgs e)
        {
            engine.StartMeasuring();
        }

        private void toolStripButtonOsm_Click(object sender, EventArgs e) => SetMapEnabled(toolStripButtonOsm.Checked);

        /// <summary>
        /// Everything in this test app is drawn in local "drawing" meters (like a CAD drawing). The geo
        /// anchor places that drawing on the map: drawing 0,0 = Hlavne namestie, rotated by the bearing
        /// set with the N° button. The world coordinates never change - the map is fitted under them.
        /// </summary>
        private void SetMapEnabled(bool enabled)
        {
            if (enabled)
            {
                if (!engine.HasGeoAnchor)
                    engine.SetGeoAnchor(DemoLatitude, DemoLongitude); // drawing origin (0,0) lies here

                if (!_geoDemoAdded)
                {
                    AddGeoDemo();
                    _geoDemoAdded = true;
                }

                engine.OsmUserAgent = "ActiveCanvas.TestApp/1.0.1 (+https://github.com/duroLAB/SimpleInteractiveEngine)";
                engine.EnableOpenStreetMap();   // switches to top view, north up (OsmLockNorthUp = true by default)
                engine.ZoomToLatLon(DemoLatitude, DemoLongitude, zoomLevel: 16);

                // The map keeps the view locked north-up, so 3D/rotation makes no sense meanwhile.
                engine.AllowFreeRotate = false;
                toolStripButton3D2Dview.Image = imageList1.Images[1];
                toolStripButton3D2Dview.Enabled = !engine.IsNorthUpLocked;
            }
            else
            {
                engine.DisableOpenStreetMap();   // the background image (if any) shows again, the view turns back
                toolStripButton3D2Dview.Enabled = true;
                toolStripStatusLabelCoords.Text = "";
                engine.ZoomToFullExtent();
            }
        }

        /// <summary>A few items placed by GPS coordinates, to check they sit on the right spot of the map.</summary>
        private void AddGeoDemo()
        {
            // Converted to drawing coordinates through the geo anchor (and the bearing valid right now) -
            // they stay where they are in the drawing if the bearing is changed later.
            var castle = engine.CreatePointLatLon(48.14216, 17.10004, label: "Bratislavsky hrad")
                .WithBrush(new SolidBrush(Color.Crimson)).WithShape(ActiveCanvas.PointShape.Star).WithSize(10f);
            var gate = engine.CreatePointLatLon(48.14513, 17.10668, label: "Michalska brana")
                .WithBrush(new SolidBrush(Color.Crimson)).WithShape(ActiveCanvas.PointShape.Star).WithSize(10f);

            engine.AddPolyline(new[] { castle, gate }, "Hrad - Michalska brana", new Pen(Color.Crimson, 2.5f));
        }

        /// <summary>Asks where the drawing's "up" really points (e.g. an AutoCAD background in local
        /// coordinates) - the engine then shows a north arrow in the 2D view.</summary>
        private void toolStripButtonNorth_Click(object sender, EventArgs e)
        {
            using var dlg = new Form
            {
                Text = "Orientácia výkresu",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(340, 120)
            };

            var label = new Label
            {
                Text = "Kam reálne smeruje vrch výkresu (azimut v stupňoch):0 = sever, 90 = východ, 135 = juhovýchod, 270 = západ",
                Location = new Point(12, 12),
                AutoSize = true
            };
            var input = new NumericUpDown
            {
                Location = new Point(12, 60),
                Width = 100,
                Minimum = -360,
                Maximum = 360,
                DecimalPlaces = 1,
                Value = (decimal)(engine.WorldUpBearingDegrees % 360f)
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(174, 59), Size = new Size(75, 25) };
            var cancel = new Button { Text = "Zrušiť", DialogResult = DialogResult.Cancel, Location = new Point(253, 59), Size = new Size(75, 25) };

            dlg.Controls.AddRange(new Control[] { label, input, ok, cancel });
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

            if (dlg.ShowDialog(this) == DialogResult.OK)
                engine.WorldUpBearingDegrees = (float)input.Value;
        }

        private void toolStripButtonDarkMode_Click(object sender, EventArgs e)
        {
            _isDark = !_isDark;

            if (_isDark)
            {
                AppTheme.ApplyDark(this);
                engine.ApplyDarkTheme();
                toolStripButtonDarkMode.Text = "☀ Svetlý režim";
            }
            else
            {
                AppTheme.ApplyLight(this);
                engine.ApplyLightTheme();
                toolStripButtonDarkMode.Text = "🌙 Tmavý režim";
            }
        }
    }
}
