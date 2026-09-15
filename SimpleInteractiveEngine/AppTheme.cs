using System.Drawing;
using System.Windows.Forms;

namespace SimpleDrawingEngine
{
    /// <summary>
    /// Simple, dependency-free dark/light theming for a whole WinForms app (Form + all its child
    /// controls, recursively). .NET 8 WinForms has no built-in dark mode, so this just walks the control
    /// tree and sets BackColor/ForeColor per control type - ToolStrip needs a custom Renderer instead of
    /// plain colors to actually look right, which is why it gets special-cased below.
    ///
    /// Usage:
    ///     private bool _isDark;
    ///
    ///     private void toolStripButtonTheme_Click(object sender, EventArgs e)
    ///     {
    ///         _isDark = !_isDark;
    ///         if (_isDark) { AppTheme.ApplyDark(this); engine.ApplyDarkTheme(); }
    ///         else         { AppTheme.ApplyLight(this); engine.ApplyLightTheme(); }
    ///     }
    ///
    /// Call AppTheme.Apply...() on the Form itself (or any container) - it recurses into every child
    /// automatically. Pair it with the engine's own ApplyDarkTheme()/ApplyLightTheme() (see
    /// ActiveCanvas.Theme.cs) so the canvas and the surrounding UI switch together.
    /// </summary>
    public static class AppTheme
    {
        private static readonly Color DarkBackground = Color.FromArgb(30, 30, 30);
        private static readonly Color DarkControlBackground = Color.FromArgb(45, 45, 45);
        private static readonly Color DarkForeground = Color.WhiteSmoke;
        private static readonly Color DarkBorder = Color.FromArgb(90, 90, 90);

        public static void ApplyDark(Control root) => Apply(root, isDark: true);
        public static void ApplyLight(Control root) => Apply(root, isDark: false);

        private static void Apply(Control root, bool isDark)
        {
            root.BackColor = isDark ? DarkBackground : SystemColors.Control;
            root.ForeColor = isDark ? DarkForeground : SystemColors.ControlText;

            foreach (Control child in root.Controls)
            {
                switch (child)
                {
                    case ToolStrip toolStrip:
                        ThemeToolStrip(toolStrip, isDark);
                        break;

                    case TextBoxBase textBox:
                        textBox.BackColor = isDark ? DarkControlBackground : SystemColors.Window;
                        textBox.ForeColor = isDark ? DarkForeground : SystemColors.WindowText;
                        textBox.BorderStyle = isDark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                        break;

                    case Button button:
                        button.BackColor = isDark ? DarkControlBackground : SystemColors.Control;
                        button.ForeColor = isDark ? DarkForeground : SystemColors.ControlText;
                        button.FlatStyle = isDark ? FlatStyle.Flat : FlatStyle.Standard;
                        if (isDark) button.FlatAppearance.BorderColor = DarkBorder;
                        break;

                    case ListControl or DataGridView or ListView:
                        child.BackColor = isDark ? DarkControlBackground : SystemColors.Window;
                        child.ForeColor = isDark ? DarkForeground : SystemColors.WindowText;
                        break;

                    case PictureBox:
                        // Left alone deliberately - the ActiveCanvas engine manages its own PictureBox
                        // background via ApplyDarkTheme()/ApplyLightTheme() (CanvasBackgroundColor), so
                        // touching BackColor here would just fight with that.
                        break;

                    default:
                        child.BackColor = isDark ? DarkBackground : SystemColors.Control;
                        child.ForeColor = isDark ? DarkForeground : SystemColors.ControlText;
                        break;
                }

                // Recurse into containers (Panel, GroupBox, SplitContainer, TableLayoutPanel, ...).
                if (child.HasChildren)
                    Apply(child, isDark);
            }
        }

        private static void ThemeToolStrip(ToolStrip toolStrip, bool isDark)
        {
            toolStrip.Renderer = isDark
                ? new ToolStripProfessionalRenderer(new DarkColorTable())
                : new ToolStripProfessionalRenderer();

            toolStrip.BackColor = isDark ? DarkControlBackground : SystemColors.Control;
            toolStrip.ForeColor = isDark ? DarkForeground : SystemColors.ControlText;

            foreach (ToolStripItem item in toolStrip.Items)
                item.ForeColor = isDark ? DarkForeground : SystemColors.ControlText;
        }

        /// <summary>Dark color scheme for ToolStrip/MenuStrip/StatusStrip - plain BackColor alone doesn't
        /// theme these controls properly, they need a custom ProfessionalColorTable via the Renderer.</summary>
        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => DarkControlBackground;
            public override Color ToolStripGradientMiddle => DarkControlBackground;
            public override Color ToolStripGradientEnd => DarkControlBackground;
            public override Color ImageMarginGradientBegin => DarkControlBackground;
            public override Color ImageMarginGradientMiddle => DarkControlBackground;
            public override Color ImageMarginGradientEnd => DarkControlBackground;
            public override Color MenuStripGradientBegin => DarkControlBackground;
            public override Color MenuStripGradientEnd => DarkControlBackground;
            public override Color MenuItemSelected => DarkBorder;
            public override Color MenuItemSelectedGradientBegin => DarkBorder;
            public override Color MenuItemSelectedGradientEnd => DarkBorder;
            public override Color MenuItemBorder => DarkBorder;
            public override Color MenuBorder => DarkBorder;
            public override Color ButtonSelectedHighlight => DarkBorder;
            public override Color ButtonSelectedBorder => DarkBorder;
            public override Color ButtonPressedHighlight => DarkBorder;
            public override Color SeparatorDark => DarkBorder;
            public override Color SeparatorLight => DarkBackground;
        }
    }
}
