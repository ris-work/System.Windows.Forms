using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Windows.Forms
{
    public static class RVUtils
    {
        public static int cornerRadius = 100;

        // Draw the parent's pixels into this control's graphics so no real transparency is left.
        public static void DrawParentBackgroundToGraphics(Control ctrl, Graphics g)
        {
            if (ctrl == null || g == null) return;

            var parent = ctrl.Parent;
            if (parent == null)
            {
                // No parent: just fill with the control BackColor
                using (var b = new SolidBrush(ctrl.BackColor))
                    g.FillRectangle(b, ctrl.ClientRectangle);
                return;
            }

            try
            {
                // Capture parent into bitmap (size = parent client size)
                using (var parentBmp = new Bitmap(parent.ClientSize.Width, parent.ClientSize.Height))
                {
                    parent.DrawToBitmap(parentBmp, new Rectangle(Point.Empty, parent.ClientSize));

                    // Source rectangle inside parent bitmap that corresponds to this control's bounds
                    var srcRect = new Rectangle(ctrl.Left, ctrl.Top, ctrl.Width, ctrl.Height);

                    // Draw that portion into the control's client rectangle
                    g.DrawImage(parentBmp, ctrl.ClientRectangle, srcRect, GraphicsUnit.Pixel);
                }
            }
            catch
            {
                // If DrawToBitmap fails on some controls/platforms, fallback to BackColor fill
                using (var b = new SolidBrush(ctrl.BackColor))
                    g.FillRectangle(b, ctrl.ClientRectangle);
            }
        }


        /// <summary>
        /// Applies a standard set of ControlStyles for custom-painted, opaque controls to ensure smooth rendering and prevent transparency artifacts.
        /// </summary>
        public static void EnableOptimizedCustomPainting(this Control control)
        {
            control.SetStyle(ControlStyles.Opaque, true);
            control.SetStyle(ControlStyles.UserPaint, true);
            control.SetStyle(ControlStyles.AllPaintingInWmPaint, true); // Reduces flicker
            control.SetStyle(ControlStyles.ResizeRedraw, true);
            control.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            control.SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint |
              ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
              ControlStyles.OptimizedDoubleBuffer, true);
        }
        public static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int cornerRadius)
        {
            // Create a new path
            var path = new System.Drawing.Drawing2D.GraphicsPath();

            // To prevent an exception, the corner radius can't be larger than half the rectangle's smallest side.
            int diameter = Math.Min(Math.Min(rect.Width, rect.Height), cornerRadius * 2);

            // If the radius is 0, just return a standard rectangle path
            if (diameter <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            // Define the rectangle for the arcs
            RectangleF arcRect = new RectangleF(rect.Location, new Size(diameter, diameter));

            // Add the arcs for each corner
            path.AddArc(arcRect, 180, 90); // Top-left
            arcRect.X = rect.Right - diameter;
            path.AddArc(arcRect, 270, 90); // Top-right
            arcRect.Y = rect.Bottom - diameter;
            path.AddArc(arcRect, 0, 90);   // Bottom-right
            arcRect.X = rect.Left;
            path.AddArc(arcRect, 90, 90);  // Bottom-left

            path.CloseFigure();
            return path;
        }
        public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int cornerRadius)
        {
            using (var path = CreateRoundedRectanglePath(rect, cornerRadius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void FillRoundedRect(this Graphics g, Brush brush, Rectangle rect, int? cornerRadius = null)
        {
            using (var path = CreateRoundedRectanglePath(rect, cornerRadius ?? RVUtils.cornerRadius))
            {
                g.FillPath(brush, path);
            }
        }

    }
}
