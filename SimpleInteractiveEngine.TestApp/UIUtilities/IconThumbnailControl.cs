using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace IconPicker
{
    /// <summary>
    /// Jedna položka v zozname ikon: malý štvorcový náhľad obrázka, ktorý sa dá vizuálne
    /// označiť ako vybraný (Selected) a reaguje na Click / DoubleClick.
    /// </summary>
    public class IconThumbnailControl : Panel
    {
        private readonly PictureBox _pictureBox;
        private bool _selected;

        /// <summary>Celá cesta k súboru, ktorý táto položka reprezentuje.</summary>
        public string FilePath { get; }

        /// <summary>Či je táto položka práve vizuálne označená ako vybraná.</summary>
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                BackColor = _selected ? SystemColors.Highlight : SystemColors.Control;
            }
        }

        public IconThumbnailControl(string filePath, Image thumbnail, int size)
        {
            FilePath = filePath;

            Size = new Size(size + 10, size + 10);
            Margin = new Padding(3);
            Padding = new Padding(3);
            BackColor = SystemColors.Control;
            Cursor = Cursors.Hand;

            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = thumbnail,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            Controls.Add(_pictureBox);

            var tip = new ToolTip();
            tip.SetToolTip(_pictureBox, Path.GetFileName(filePath));
            tip.SetToolTip(this, Path.GetFileName(filePath));

            // Preposlanie udalostí myši z vnútorného PictureBoxu na túto položku,
            // aby klik/dvojklik fungoval kdekoľvek na dlaždici, nielen na okraji Panelu.
            _pictureBox.Click += (s, e) => OnClick(e);
            _pictureBox.DoubleClick += (s, e) => OnDoubleClick(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pictureBox.Image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
