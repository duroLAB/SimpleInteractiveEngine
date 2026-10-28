using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ActiveCanvasEngine
{
    /// <summary>
    /// Example "Open background image" button handler for the host application. Wire it up like:
    ///     btnOpenBackground.Click += async (s, e) => await BackgroundImageOpener.ShowAsync(this, engine);
    /// Pass a plane to open a front (X-Z) or side (Y-Z) elevation instead of the plan view:
    ///     await BackgroundImageOpener.ShowAsync(this, engine, ActiveCanvas.BackgroundPlane.Front);
    /// </summary>
    public static class BackgroundImageOpener
    {
        public static async Task ShowAsync(IWin32Window owner, ActiveCanvas engine,
            ActiveCanvas.BackgroundPlane plane = ActiveCanvas.BackgroundPlane.Ground)
        {
            using var dialog = new OpenFileDialog
            {
                Title = plane switch
                {
                    ActiveCanvas.BackgroundPlane.Front => "Open front view image (X-Z plane)",
                    ActiveCanvas.BackgroundPlane.Side => "Open side view image (Y-Z plane)",
                    _ => "Open background image"
                },
                Filter = "Image files (*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp)|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp|" +
                         "TIFF files (*.tif;*.tiff)|*.tif;*.tiff|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(owner) != DialogResult.OK)
                return;

            string path = dialog.FileName;

            if (plane != ActiveCanvas.BackgroundPlane.Ground)
            {
                await OpenElevationAsync(owner, engine, plane, path);
                return;
            }

            try
            {
                // The Async variant decodes the file on a background thread and shows engine's built-in
                // busy overlay for the duration, so the UI (and the canvas itself) stays responsive instead
                // of freezing for the few seconds a large TIFF can take to decode.
                //
                // fallbackWorldWidthProvider only runs if no georeferencing was found at all (after the
                // background decode completes, back on this UI thread) - lazily, so a normal GeoTIFF never
                // bothers the user with this dialog.
                bool wasGeoreferenced = await engine.SetBackgroundImageFromGeoTiffAsync(
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

        /// <summary>Front/side image: decode on a background thread, then ask where it sits (no world file
        /// or GeoTIFF tags exist for elevations).</summary>
        private static async Task OpenElevationAsync(IWin32Window owner, ActiveCanvas engine,
            ActiveCanvas.BackgroundPlane plane, string path)
        {
            try
            {
                System.Drawing.Image image;
                engine.ShowBusyOverlay("Loading image...");
                try
                {
                    image = await Task.Run(() => ActiveCanvas.LoadImageFile(path));
                }
                finally
                {
                    engine.HideBusyOverlay();
                }

                using var placement = new ElevationPlacementDialog(plane, Path.GetFileName(path));
                if (placement.ShowDialog(owner) != DialogResult.OK)
                {
                    image.Dispose();
                    return;
                }

                if (plane == ActiveCanvas.BackgroundPlane.Front)
                    engine.SetFrontBackgroundImage(image, placement.Horizontal, placement.Bottom, placement.WidthMeters, atY: placement.PlaneOffset);
                else
                    engine.SetSideBackgroundImage(image, placement.Horizontal, placement.Bottom, placement.WidthMeters, atX: placement.PlaneOffset);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner as Form, $"Could not open this image:\n{ex.Message}",
                    "Background image", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Asks where a front/side image sits: horizontal start, bottom Z, width and the plane's position.</summary>
        private class ElevationPlacementDialog : Form
        {
            private readonly NumericUpDown _horizontal, _bottom, _width, _offset;

            public float Horizontal => (float)_horizontal.Value;
            public float Bottom => (float)_bottom.Value;
            public float WidthMeters => (float)_width.Value;
            public float PlaneOffset => (float)_offset.Value;

            public ElevationPlacementDialog(ActiveCanvas.BackgroundPlane plane, string fileName)
            {
                bool front = plane == ActiveCanvas.BackgroundPlane.Front;

                Text = front ? "Front view image (X-Z)" : "Side view image (Y-Z)";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MinimizeBox = false;
                MaximizeBox = false;
                ClientSize = new System.Drawing.Size(360, 210);

                Controls.Add(new Label
                {
                    Text = fileName,
                    AutoSize = false,
                    Location = new System.Drawing.Point(12, 10),
                    Size = new System.Drawing.Size(336, 18)
                });

                _horizontal = AddRow(front ? "Left edge X (m):" : "Minimum Y (m):", 36, 0m);
                _bottom = AddRow("Bottom edge Z (m):", 66, 0m);
                _width = AddRow("Width (m):", 96, 100m, minimum: 0.01m);
                _offset = AddRow(front ? "Plane position Y (m):" : "Plane position X (m):", 126, 0m);

                var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(192, 170), Size = new System.Drawing.Size(75, 25) };
                var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(273, 170), Size = new System.Drawing.Size(75, 25) };
                Controls.Add(okButton);
                Controls.Add(cancelButton);
                AcceptButton = okButton;
                CancelButton = cancelButton;
            }

            private NumericUpDown AddRow(string caption, int top, decimal value, decimal minimum = -10_000_000m)
            {
                Controls.Add(new Label
                {
                    Text = caption,
                    AutoSize = false,
                    Location = new System.Drawing.Point(12, top + 3),
                    Size = new System.Drawing.Size(180, 20)
                });

                var input = new NumericUpDown
                {
                    Location = new System.Drawing.Point(198, top),
                    Size = new System.Drawing.Size(150, 23),
                    Minimum = minimum,
                    Maximum = 10_000_000m,
                    DecimalPlaces = 2,
                    Value = value
                };
                Controls.Add(input);
                return input;
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
