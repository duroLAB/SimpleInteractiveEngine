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
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            pictureBox1 = new PictureBox();
            toolStrip1 = new ToolStrip();
            toolStripButtonAddPoint = new ToolStripButton();
            toolStripButtonAddPolyLine = new ToolStripButton();
            toolStripButtonAddPolygon = new ToolStripButton();
            toolStripButtonAddIcon = new ToolStripButton();
            toolStripSeparator1 = new ToolStripSeparator();
            toolStripButton3D2Dview = new ToolStripButton();
            toolStripSeparator2 = new ToolStripSeparator();
            toolStripButtonFullZoom = new ToolStripButton();
            toolStripButtonOpenBackGroundImage = new ToolStripButton();
            imageList1 = new ImageList(components);
            statusStrip1 = new StatusStrip();
            toolStripButtonClearAll = new ToolStripButton();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            toolStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 39);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(800, 389);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            // 
            // toolStrip1
            // 
            toolStrip1.AutoSize = false;
            toolStrip1.ImageScalingSize = new Size(32, 32);
            toolStrip1.Items.AddRange(new ToolStripItem[] { toolStripButtonAddPoint, toolStripButtonAddPolyLine, toolStripButtonAddPolygon, toolStripButtonAddIcon, toolStripButtonOpenBackGroundImage, toolStripButtonClearAll, toolStripSeparator1, toolStripButton3D2Dview, toolStripSeparator2, toolStripButtonFullZoom });
            toolStrip1.Location = new Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(800, 39);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // toolStripButtonAddPoint
            // 
            toolStripButtonAddPoint.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonAddPoint.Image = (Image)resources.GetObject("toolStripButtonAddPoint.Image");
            toolStripButtonAddPoint.ImageScaling = ToolStripItemImageScaling.None;
            toolStripButtonAddPoint.ImageTransparentColor = Color.Magenta;
            toolStripButtonAddPoint.Name = "toolStripButtonAddPoint";
            toolStripButtonAddPoint.Size = new Size(36, 36);
            toolStripButtonAddPoint.Text = "Add Point";
            toolStripButtonAddPoint.Click += toolStripButtonAddPoint_Click;
            // 
            // toolStripButtonAddPolyLine
            // 
            toolStripButtonAddPolyLine.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonAddPolyLine.Image = (Image)resources.GetObject("toolStripButtonAddPolyLine.Image");
            toolStripButtonAddPolyLine.ImageScaling = ToolStripItemImageScaling.None;
            toolStripButtonAddPolyLine.ImageTransparentColor = Color.Magenta;
            toolStripButtonAddPolyLine.Name = "toolStripButtonAddPolyLine";
            toolStripButtonAddPolyLine.Size = new Size(36, 36);
            toolStripButtonAddPolyLine.Text = "Add PolyLine";
            toolStripButtonAddPolyLine.Click += toolStripButtonAddPolyLine_Click;
            // 
            // toolStripButtonAddPolygon
            // 
            toolStripButtonAddPolygon.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonAddPolygon.Image = (Image)resources.GetObject("toolStripButtonAddPolygon.Image");
            toolStripButtonAddPolygon.ImageScaling = ToolStripItemImageScaling.None;
            toolStripButtonAddPolygon.ImageTransparentColor = Color.Magenta;
            toolStripButtonAddPolygon.Name = "toolStripButtonAddPolygon";
            toolStripButtonAddPolygon.Size = new Size(36, 36);
            toolStripButtonAddPolygon.Text = "Add polygon";
            toolStripButtonAddPolygon.Click += toolStripButtonAddPolygon_Click;
            // 
            // toolStripButtonAddIcon
            // 
            toolStripButtonAddIcon.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonAddIcon.Image = (Image)resources.GetObject("toolStripButtonAddIcon.Image");
            toolStripButtonAddIcon.ImageTransparentColor = Color.Magenta;
            toolStripButtonAddIcon.Name = "toolStripButtonAddIcon";
            toolStripButtonAddIcon.Size = new Size(36, 36);
            toolStripButtonAddIcon.Text = "toolStripButton1";
            toolStripButtonAddIcon.Click += toolStripButtonAddIcon_Click;
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 39);
            // 
            // toolStripButton3D2Dview
            // 
            toolStripButton3D2Dview.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButton3D2Dview.Image = (Image)resources.GetObject("toolStripButton3D2Dview.Image");
            toolStripButton3D2Dview.ImageTransparentColor = Color.Magenta;
            toolStripButton3D2Dview.Name = "toolStripButton3D2Dview";
            toolStripButton3D2Dview.Size = new Size(36, 36);
            toolStripButton3D2Dview.Text = "toolStripButton2";
            toolStripButton3D2Dview.Click += toolStripButton3D2Dview_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(6, 39);
            // 
            // toolStripButtonFullZoom
            // 
            toolStripButtonFullZoom.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonFullZoom.Image = (Image)resources.GetObject("toolStripButtonFullZoom.Image");
            toolStripButtonFullZoom.ImageTransparentColor = Color.Magenta;
            toolStripButtonFullZoom.Name = "toolStripButtonFullZoom";
            toolStripButtonFullZoom.Size = new Size(36, 36);
            toolStripButtonFullZoom.Text = "toolStripButton1";
            toolStripButtonFullZoom.Click += toolStripButtonFullZoom_Click;
            // 
            // toolStripButtonOpenBackGroundImage
            // 
            toolStripButtonOpenBackGroundImage.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonOpenBackGroundImage.Image = (Image)resources.GetObject("toolStripButtonOpenBackGroundImage.Image");
            toolStripButtonOpenBackGroundImage.ImageTransparentColor = Color.Magenta;
            toolStripButtonOpenBackGroundImage.Name = "toolStripButtonOpenBackGroundImage";
            toolStripButtonOpenBackGroundImage.Size = new Size(36, 36);
            toolStripButtonOpenBackGroundImage.Text = "toolStripButton1";
            toolStripButtonOpenBackGroundImage.Click += toolStripButtonOpenBackGroundImage_Click;
            // 
            // imageList1
            // 
            imageList1.ColorDepth = ColorDepth.Depth32Bit;
            imageList1.ImageStream = (ImageListStreamer)resources.GetObject("imageList1.ImageStream");
            imageList1.TransparentColor = Color.Transparent;
            imageList1.Images.SetKeyName(0, "2D");
            imageList1.Images.SetKeyName(1, "3D");
            // 
            // statusStrip1
            // 
            statusStrip1.Location = new Point(0, 428);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(800, 22);
            statusStrip1.TabIndex = 2;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripButtonClearAll
            // 
            toolStripButtonClearAll.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButtonClearAll.Image = (Image)resources.GetObject("toolStripButtonClearAll.Image");
            toolStripButtonClearAll.ImageTransparentColor = Color.Magenta;
            toolStripButtonClearAll.Name = "toolStripButtonClearAll";
            toolStripButtonClearAll.Size = new Size(36, 36);
            toolStripButtonClearAll.Text = "toolStripButton1";
            toolStripButtonClearAll.Click += toolStripButtonClearAll_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(pictureBox1);
            Controls.Add(statusStrip1);
            Controls.Add(toolStrip1);
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
        private ToolStripButton toolStripButtonAddPolygon;
        private ToolStripButton toolStripButtonAddIcon;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripButton toolStripButton3D2Dview;
        private ImageList imageList1;
        private StatusStrip statusStrip1;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripButton toolStripButtonFullZoom;
        private ToolStripButton toolStripButtonOpenBackGroundImage;
        private ToolStripButton toolStripButtonClearAll;
    }
}
