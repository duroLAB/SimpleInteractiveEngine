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

            
            
            this.KeyPreview = true;
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) engine.CancelDrawing(); };
            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) engine.FinishDrawing();};
        }

        private void toolStripButtonAddPoint_Click(object sender, EventArgs e)
        {
            engine.StartPlacingPoints(label: "New Point", brush: new SolidBrush(Color.Crimson), shape: ActiveCanvas.PointShape.Diamond, size: 8f, continuous: false);
        }

        private void toolStripButtonAddPolyLine_Click(object sender, EventArgs e)
        {
            engine.StartDrawingPolyline("Nová trasa", new Pen(Color.ForestGreen, 3f));
             
        }
    }
}
