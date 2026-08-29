using System;
using System.IO;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    /// <summary>
    /// Example "Open background image" button handler for the host application. Wire it up like:
    ///     btnOpenBackground.Click += (s, e) => BackgroundImageOpener.Show(this, engine);
    /// </summary>
    public static class BackgroundImageOpener
    {
        public static void Show(IWin32Window owner, ActiveCanvas engine)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Open background image",
                Filter = "Image files (*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp)|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp|" +
                         "TIFF files (*.tif;*.tiff)|*.tif;*.tiff|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(owner) != DialogResult.OK)
                return;

            string path = dialog.FileName;

            try
            {
                // SetBackgroundImageFromGeoTiff checks for a sidecar world file (.tfw/.tifw/.wld/...) and,
                // for TIFFs, embedded GeoTIFF tags too - it works the same way regardless of the file's
                // extension, so calling it here is fine even for a plain .png/.jpg (it will just come back
                // with hasGeoreferencing = false unless a matching world file sits next to it).
                //
                // fallbackWorldWidthProvider only runs if no georeferencing was found at all - lazily, so
                // a normal GeoTIFF never bothers the user with this dialog.
                bool wasGeoreferenced = engine.SetBackgroundImageFromGeoTiff(
                    path,
                    fallbackWorldWidthProvider: () => PromptForWidthMeters(owner, path));

                engine.ZoomToFullExtent();

                if (!wasGeoreferenced)
                {
                    MessageBox.Show(owner as Form,
                        "No georeferencing found for this image (no matching world file or embedded GeoTIFF tags) - " +
                        "it was placed using the width you entered.",
                        "Background image", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner as Form, $"Could not open this image:\n{ex.Message}",
                    "Background image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Prompts for the image's real-world width in meters. Returns a sensible default (100)
        /// if the user cancels, so SetBackgroundImageFromGeoTiff always ends up with a usable value.</summary>
        private static float PromptForWidthMeters(IWin32Window owner, string filePath)
        {
            using var dlg = new WidthPromptDialog(Path.GetFileName(filePath));
            return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.WidthMeters : 100f;
        }

        /// <summary>Minimal self-contained "enter a number" dialog - no extra dependency (e.g. on
        /// Microsoft.VisualBasic's InputBox) needed just for this one prompt.</summary>
        private class WidthPromptDialog : Form
        {
            private readonly NumericUpDown _input;
            public float WidthMeters => (float)_input.Value;

            public WidthPromptDialog(string fileName)
            {
                Text = "Image width";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MinimizeBox = false;
                MaximizeBox = false;
                ClientSize = new System.Drawing.Size(340, 130);

                var label = new Label
                {
                    Text = $"No georeferencing found for:\n{fileName}\n\nEnter the image's real-world width (meters):",
                    AutoSize = false,
                    Location = new System.Drawing.Point(12, 12),
                    Size = new System.Drawing.Size(316, 60)
                };

                _input = new NumericUpDown
                {
                    Location = new System.Drawing.Point(12, 76),
                    Size = new System.Drawing.Size(120, 23),
                    Minimum = 0.01m,
                    Maximum = 1_000_000m,
                    DecimalPlaces = 2,
                    Value = 100m
                };

                var okButton = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new System.Drawing.Point(174, 75),
                    Size = new System.Drawing.Size(75, 25)
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new System.Drawing.Point(253, 75),
                    Size = new System.Drawing.Size(75, 25)
                };

                Controls.Add(label);
                Controls.Add(_input);
                Controls.Add(okButton);
                Controls.Add(cancelButton);

                AcceptButton = okButton;
                CancelButton = cancelButton;
            }
        }
    }
}
