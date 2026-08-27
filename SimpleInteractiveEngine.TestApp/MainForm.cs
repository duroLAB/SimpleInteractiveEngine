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
            
            if(pd>= 89f && pd < 91)
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
    }
}
