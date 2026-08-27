using SimpleDrawingEngine;
using SimpleInteractiveEngine;
using System.Windows.Forms;

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
            DemoScene.Build(engine);

            toolStripButton3D2Dview.Image = imageList1.Images[1];

            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) engine.CancelDrawing(); };
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) engine.FinishDrawing(); };
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
