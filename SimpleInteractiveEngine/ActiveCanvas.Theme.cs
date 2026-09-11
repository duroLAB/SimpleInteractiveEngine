using System.Drawing;

namespace SimpleDrawingEngine
{
    public partial class ActiveCanvas
    {
        // Convenience theme switches. These only touch the canvas "chrome" (background, grid, label
        // text/backdrop) - not DefaultPointColor/DefaultLineColor/DefaultPolygonFillColor/etc., since
        // those are YOUR content's colors (e.g. a point you deliberately made blue should probably stay
        // blue in both themes). Call either method any time - it just re-renders with the new colors,
        // nothing else about the scene changes.

        #region Theming

        /// <summary>Switches the canvas to a dark theme (dark background, light grid/text).</summary>
        public void ApplyDarkTheme()
        {
            CanvasBackgroundColor = Color.FromArgb(30, 30, 30);
            GridColor = Color.FromArgb(55, 55, 55);
            LabelTextColor = Color.WhiteSmoke;
            LabelBackdropColor = Color.FromArgb(45, 45, 45);
            Render();
        }

        /// <summary>Switches the canvas back to the default light theme (white background, dark grid/text).</summary>
        public void ApplyLightTheme()
        {
            CanvasBackgroundColor = Color.White;
            GridColor = Color.Gainsboro;
            LabelTextColor = Color.Black;
            LabelBackdropColor = Color.White;
            Render();
        }

        #endregion
    }
}
