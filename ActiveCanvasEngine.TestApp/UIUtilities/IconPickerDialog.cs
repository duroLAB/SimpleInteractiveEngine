using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace IconPicker
{
    /// <summary>
    /// Jednoduchý dialóg na výber obrázka/ikony (PNG, BMP, ICO a pod.) z preddefinovaného
    /// adresára - ikony sa zobrazia ako malé náhľady v mriežke (predvolene 10 na riadok, 32x32 px).
    /// Používateľ môže adresár aj zmeniť, alebo namiesto výberu z adresára zvoliť ľubovoľný
    /// vlastný súbor cez štandardný OpenFileDialog.
    ///
    /// Príklad použitia:
    ///
    ///     using (var dlg = new IconPickerDialog(@"C:\MojaApp\Resources\Icons"))
    ///     {
    ///         dlg.ThumbnailSize = 32;
    ///         dlg.IconsPerRow = 10;
    ///         dlg.AllowedExtensions = new[] { ".png", ".bmp", ".ico" };
    ///
    ///         if (dlg.ShowDialog(this) == DialogResult.OK)
    ///         {
    ///             string vybranaCesta = dlg.SelectedFilePath;
    ///             pictureBoxNahlad.Image = Image.FromFile(vybranaCesta);
    ///         }
    ///     }
    ///
    /// POZNÁMKA k [Browsable(false)]/[DesignerSerializationVisibility(Hidden)] nižšie: tento dialóg sa
    /// vytvára výhradne v kóde (new IconPickerDialog(...)) a nikdy by nemal skončiť ako komponenta
    /// položená na plátne iného Formu vo VS Designeri. Keďže ale dedí z Form, Designer by sa - ak by sa
    /// tam predsa len ocitol (napr. omylom pretiahnutý, alebo pri reloade projektu) - pokúsil
    /// automaticky vygenerovať serializačný kód pre všetky jeho verejné vlastnosti do
    /// InitializeComponent(). Explicitné skrytie týchto vlastností pred Designerom tomu predchádza.
    ///
    /// [DesignerCategory("")] na triede navyše rieši aj prípad, keď sa žiadne chyby netýkajú priamo
    /// pretiahnutia na plátno - VS na pozadí skenuje všetky verejné triedy dediace z Form/Control/Component
    /// pre Toolbox/IntelliSense, nezávisle od toho, či ich niekto reálne použije v Designeri. Prázdna
    /// kategória hovorí tomuto skenovaniu "túto triedu vôbec neanalyzuj ako dizajnovateľnú".
    /// </summary>
    [DesignerCategory("")]
    public class IconPickerDialog : Form
    {
        private readonly TextBox _txtDirectory;
        private readonly Button _btnBrowseDir;
        private readonly FlowLayoutPanel _flpIcons;
        private readonly Button _btnCustomFile;
        private readonly Label _lblStatus;
        private readonly Button _btnOK;
        private readonly Button _btnCancel;

        private IconThumbnailControl _selectedThumb;

        /// <summary>Adresár, z ktorého sa načítavajú náhľady ikon/obrázkov.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IconsDirectory
        {
            get => _txtDirectory.Text;
            set => _txtDirectory.Text = value ?? string.Empty;
        }

        /// <summary>Povolené prípony súborov (s bodkou), napr. ".png", ".bmp", ".ico".</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string[] AllowedExtensions { get; set; } = new string[] { ".png", ".bmp", ".ico" };

        /// <summary>Veľkosť náhľadu ikony v pixeloch (štvorec). Predvolené 32.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int ThumbnailSize { get; set; } = 32;

        /// <summary>Počet ikon na riadok pri predvolenej šírke okna (ovplyvňuje len počiatočnú šírku - pri zmene veľkosti okna sa mriežka prirodzene prelamuje).</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int IconsPerRow { get; set; } = 10;

        /// <summary>
        /// Cesta k súboru, ktorý používateľ vybral - buď dvojklikom / OK v zozname ikon,
        /// alebo cez tlačidlo "Vlastný súbor...". Platné len ak ShowDialog() vráti DialogResult.OK.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string SelectedFilePath { get; private set; }

        public IconPickerDialog() : this(null)
        {
        }

        public IconPickerDialog(string initialDirectory)
        {
            Text = "Výber ikony";
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = SystemFonts.MessageBoxFont;

            // --- horný panel: adresár + tlačidlo na jeho zmenu ---
            var pnlTop = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8, 6, 8, 4)
            };
            pnlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pnlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34f));

            _txtDirectory = new TextBox { Dock = DockStyle.Fill };
            _btnBrowseDir = new Button { Text = "...", Dock = DockStyle.Fill };
            _btnBrowseDir.Click += BtnBrowseDir_Click;

            pnlTop.Controls.Add(_txtDirectory, 0, 0);
            pnlTop.Controls.Add(_btnBrowseDir, 1, 0);

            // --- spodný panel: vlastný súbor / stav / OK / Zrušiť ---
            var pnlBottom = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(8, 6, 8, 6)
            };
            pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));
            pnlBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84f));

            _btnCustomFile = new Button { Text = "Vlastný súbor...", AutoSize = true, Dock = DockStyle.Fill };
            _btnCustomFile.Click += BtnCustomFile_Click;

            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray,
                Padding = new Padding(10, 0, 0, 0),
                AutoEllipsis = true
            };

            _btnOK = new Button { Text = "OK", Dock = DockStyle.Fill, DialogResult = DialogResult.OK, Enabled = false };
            _btnOK.Click += (s, e) => ConfirmSelection(_selectedThumb);

            _btnCancel = new Button { Text = "Zrušiť", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };

            pnlBottom.Controls.Add(_btnCustomFile, 0, 0);
            pnlBottom.Controls.Add(_lblStatus, 1, 0);
            pnlBottom.Controls.Add(_btnOK, 2, 0);
            pnlBottom.Controls.Add(_btnCancel, 3, 0);

            // --- stredná časť: mriežka náhľadov ikon ---
            _flpIcons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(6),
                BackColor = SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D
            };

            // Poradie pridávania je dôležité: Fill najprv, potom Top/Bottom (viď dokovanie vo WinForms).
            Controls.Add(_flpIcons);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);

            AcceptButton = _btnOK;
            CancelButton = _btnCancel;

            // Šírka okna nastavená tak, aby sa pri predvolenej veľkosti zmestilo IconsPerRow ikon v jednom riadku.
            int itemOuterSize = ThumbnailSize + 10 /* vnútorný padding položky */ + 6 /* margin medzi položkami */;
            int desiredWidth = itemOuterSize * IconsPerRow + 40 /* okraje + posuvník */;
            ClientSize = new Size(Math.Max(360, desiredWidth), 420);
            MinimumSize = new Size(320, 260);

            Load += IconPickerDialog_Load;

            if (!string.IsNullOrWhiteSpace(initialDirectory))
                IconsDirectory = initialDirectory;
        }

        private void IconPickerDialog_Load(object sender, EventArgs e)
        {
            LoadIcons();
        }

        private void BtnBrowseDir_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                if (!string.IsNullOrWhiteSpace(IconsDirectory) && Directory.Exists(IconsDirectory))
                    fbd.SelectedPath = IconsDirectory;

                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    IconsDirectory = fbd.SelectedPath;
                    LoadIcons();
                }
            }
        }

        private void BtnCustomFile_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Title = "Vyberte súbor s obrázkom",
                Filter = BuildFileFilter(),
                CheckFileExists = true
            })
            {
                if (!string.IsNullOrWhiteSpace(IconsDirectory) && Directory.Exists(IconsDirectory))
                    ofd.InitialDirectory = IconsDirectory;

                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    SelectedFilePath = ofd.FileName;
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
        }

        private string BuildFileFilter()
        {
            string extsPattern = string.Join(";", AllowedExtensions.Select(x => "*" + x));
            return $"Obrázky ({extsPattern})|{extsPattern}|Všetky súbory (*.*)|*.*";
        }

        /// <summary>Znovu načíta náhľady zo súčasne nastaveného adresára (IconsDirectory).</summary>
        public void LoadIcons()
        {
            ClearThumbnails();

            string dir = IconsDirectory;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                _lblStatus.Text = "Adresár neexistuje.";
                return;
            }

            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir)
                    .Where(f => AllowedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Chyba pri čítaní adresára: " + ex.Message;
                return;
            }

            _flpIcons.SuspendLayout();

            int loaded = 0;
            int failed = 0;

            foreach (var file in files)
            {
                Image thumb = IconThumbnailLoader.LoadThumbnail(file, ThumbnailSize);
                if (thumb == null)
                {
                    failed++;
                    continue;
                }

                var item = new IconThumbnailControl(file, thumb, ThumbnailSize);
                item.Click += Thumbnail_Click;
                item.DoubleClick += Thumbnail_DoubleClick;
                _flpIcons.Controls.Add(item);
                loaded++;
            }

            _flpIcons.ResumeLayout();

            _lblStatus.Text = failed > 0
                ? $"Načítaných {loaded} položiek ({failed} sa nepodarilo načítať)."
                : $"Načítaných {loaded} položiek.";

            _selectedThumb = null;
            _btnOK.Enabled = false;
        }

        private void ClearThumbnails()
        {
            foreach (Control c in _flpIcons.Controls)
                c.Dispose();
            _flpIcons.Controls.Clear();
        }

        private void Thumbnail_Click(object sender, EventArgs e)
        {
            SelectThumbnail((IconThumbnailControl)sender);
        }

        private void Thumbnail_DoubleClick(object sender, EventArgs e)
        {
            var thumb = (IconThumbnailControl)sender;
            SelectThumbnail(thumb);
            ConfirmSelection(thumb);
        }

        private void SelectThumbnail(IconThumbnailControl thumb)
        {
            if (_selectedThumb != null)
                _selectedThumb.Selected = false;

            _selectedThumb = thumb;
            _selectedThumb.Selected = true;
            _btnOK.Enabled = true;
        }

        private void ConfirmSelection(IconThumbnailControl thumb)
        {
            if (thumb == null)
                return;

            SelectedFilePath = thumb.FilePath;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}