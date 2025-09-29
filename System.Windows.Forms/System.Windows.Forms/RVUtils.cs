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
        public static float cornerRadius = 20;
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
            int radius = cornerRadius ?? (int)RVUtils.cornerRadius;
            // Clamp radius to valid value
            int diameter = Math.Min(Math.Min(rect.Width, rect.Height), radius * 2);
            if (diameter <= 0)
            {
                g.FillRectangle(brush, rect);
                return;
            }

            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                var arcRect = new RectangleF(rect.Location, new SizeF(diameter, diameter));

                // top-left
                path.AddArc(arcRect, 180, 90);

                // top-right
                arcRect.X = rect.Right - diameter;
                path.AddArc(arcRect, 270, 90);

                // bottom-right
                arcRect.Y = rect.Bottom - diameter;
                path.AddArc(arcRect, 0, 90);

                // bottom-left
                arcRect.X = rect.Left;
                path.AddArc(arcRect, 90, 90);

                path.CloseFigure();

                var oldSmoothing = g.SmoothingMode;
                try
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillPath(brush, path);
                }
                finally
                {
                    g.SmoothingMode = oldSmoothing;
                }
            }
        }

    }
}
