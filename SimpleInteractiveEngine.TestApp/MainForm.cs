using SimpleDrawingEngine;
using SimpleInteractiveEngine;
using System.Globalization;
using System.Numerics;
using System.Windows.Forms;
using System.Xml.Linq;

namespace SimpleInteractiveEngine.TestApp
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


            // engine.PointPlaced += (s, p) => { engine.ShowBusyOverlay("Saving point...");p.WithBrush(b) };


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
                                                 //   engine.ZoomToFullExtent(0f);         // presne na okraj, bez rezervy
            engine.ApplyDarkTheme();                                     //   engine.ZoomToFullExtent(0.25f);      // väčší okraj
        }

        private async void toolStripButtonOpenBackGroundImage_Click(object sender, EventArgs e)
        {
            // BackgroundImageOpener.Show(this, engine);
            await BackgroundImageOpener.ShowAsync(this, engine);
        }

        private void toolStripButtonClearAll_Click(object sender, EventArgs e)
        {
            engine.ShowBusyOverlay("Clearing all items...");
            /* engine.Clear();
             engine.SetLocalOrigin(0, 0);*/
        }

        private void toolStripButtonLengthMeasure_Click(object sender, EventArgs e)
        {
            engine.StartMeasuring();
        }

        private void toolStripButtonOsm_Click(object sender, EventArgs e)
        {
            if (toolStripButtonOsm.Checked)
            {
                if (!_geoDemoAdded)
                {
                    AddGeoDemo();
                    _geoDemoAdded = true;
                }

                engine.OsmUserAgent = "ActiveCanvas.TestApp/1.0.1 (+https://github.com/duroLAB/SimpleInteractiveEngine)";
                engine.EnableOpenStreetMap();
                engine.ZoomToLatLon(DemoLatitude, DemoLongitude, zoomLevel: 16);
            }
            else
            {
                engine.DisableOpenStreetMap();
                toolStripStatusLabelCoords.Text = "";
                engine.ZoomToFullExtent();
            }
        }

        /// <summary>A few items placed by GPS coordinates, to check they sit on the right spot of the map.</summary>
        private void AddGeoDemo()
        {
            // Local origin in the middle of the area of interest - keeps float coordinates precise.
            // NOTE: in a real app call this BEFORE adding any content. Here the demo scene (built in local
            // meters around 0,0) already exists, so it simply ends up sitting on Hlavne namestie.
            engine.SetLocalOriginLatLon(DemoLatitude, DemoLongitude);

            var castle = engine.CreatePointLatLon(48.14216, 17.10004, label: "Bratislavsky hrad")
                .WithBrush(new SolidBrush(Color.Crimson)).WithShape(ActiveCanvas.PointShape.Star).WithSize(10f);
            var gate = engine.CreatePointLatLon(48.14513, 17.10668, label: "Michalska brana")
                .WithBrush(new SolidBrush(Color.Crimson)).WithShape(ActiveCanvas.PointShape.Star).WithSize(10f);

            engine.AddPolyline(new[] { castle, gate }, "Hrad - Michalska brana", new Pen(Color.Crimson, 2.5f));
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
