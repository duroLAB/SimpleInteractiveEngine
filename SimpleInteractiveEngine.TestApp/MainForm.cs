using SimpleDrawingEngine;
using SimpleInteractiveEngine;
using System.Numerics;
using System.Windows.Forms;
using System.Xml.Linq;

namespace SimpleInteractiveEngine.TestApp
{
    public partial class MainForm : Form
    {
        ActiveCanvas engine;

        public MainForm()
        {
            InitializeComponent();
            engine = new ActiveCanvas(pictureBox1);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //DemoScene.Build(engine);

            toolStripButton3D2Dview.Image = imageList1.Images[1];

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) engine.CancelDrawing(); };
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) engine.FinishDrawing(); };


            engine.PointDoubleClicked += (s, p) =>
            {
                var grid = engine.CreatePropertyGrid(p, hiddenProperties: new[] { "Id", "HoverEnabled", "Draggable","X" ,"Label"});
                // zostane len: X, Y, Z, Label, ShowLabel, Shape, Size, Color, Selectable

                using var dlg = new Form { Text = "Point properties", Width = 320, Height = 380 };
                dlg.Controls.Add(grid);
                dlg.ShowDialog(this);
            };


            engine.PolylineDoubleClicked += (s, pl) => ShowPropertyDialog(pl);
            engine.PolygonDoubleClicked += (s, pg) => ShowPropertyDialog(pg);
            engine.ImageDoubleClicked += (s, im) => ShowPropertyDialog(im);


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
            // pictureBox1.Cursor = CreatePointCursor();
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
            }
            else
            {
                toolStripButton3D2Dview.Image = imageList1.Images[1];
                engine.ShowTopView();
                engine.AllowFreeRotate = false;
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
         //   engine.ZoomToFullExtent(0.25f);      // väčší okraj
        }
    }
}
