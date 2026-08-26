namespace SimpleInteractiveEngine.TestApp
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            pictureBox1 = new PictureBox();
            toolStrip1 = new ToolStrip();
            toolStripButtonAddPoint = new ToolStripButton();
            toolStripButtonAddPolyLine = new ToolStripButton();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            toolStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Location = new Point(136, 55);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(537, 316);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // toolStrip1
            // 
            toolStrip1.Items.AddRange(new ToolStripItem[] { toolStripButtonAddPoint, toolStripButtonAddPolyLine });
            toolStrip1.Location = new Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(800, 25);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // toolStripButtonAddPoint
            // 
            toolStripButtonAddPoint.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonAddPoint.Image = (Image)resources.GetObject("toolStripButtonAddPoint.Image");
            toolStripButtonAddPoint.ImageTransparentColor = Color.Magenta;
            toolStripButtonAddPoint.Name = "toolStripButtonAddPoint";
            toolStripButtonAddPoint.Size = new Size(23, 22);
            toolStripButtonAddPoint.Text = "Add Point";
            toolStripButtonAddPoint.Click += toolStripButtonAddPoint_Click;
            // 
            // toolStripButtonAddPolyLine
            // 
            toolStripButtonAddPolyLine.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonAddPolyLine.Image = (Image)resources.GetObject("toolStripButtonAddPolyLine.Image");
            toolStripButtonAddPolyLine.ImageTransparentColor = Color.Magenta;
            toolStripButtonAddPolyLine.Name = "toolStripButtonAddPolyLine";
            toolStripButtonAddPolyLine.Size = new Size(23, 22);
            toolStripButtonAddPolyLine.Text = "Add PolyLine";
            toolStripButtonAddPolyLine.Click += toolStripButtonAddPolyLine_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(toolStrip1);
            Controls.Add(pictureBox1);
            Name = "MainForm";
            Text = "Form1";
            Load += Form1_Load;
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private PictureBox pictureBox1;
        private ToolStrip toolStrip1;
        private ToolStripButton toolStripButtonAddPoint;
        private ToolStripButton toolStripButtonAddPolyLine;
    }
}
