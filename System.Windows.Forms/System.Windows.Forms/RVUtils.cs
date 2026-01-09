using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Windows.Forms
{
    public static class RVUtils
    {
        public static int cornerRadius = 5;

        // near the top of RVUtils
        private static volatile int _captureOriginX = -1;
        private static volatile int _captureOriginY = -1;

        // ParentBackgroundColor: plain static nullable Color plus a simple lock for thread-safety
        private static readonly object _parentBgLock = new object();
        private static Color? _parentBackgroundColor = null;

        public static void SetCaptureOrigin(int screenX, int screenY)
        {
            _captureOriginX = screenX;
            _captureOriginY = screenY;
        }

        public static void ClearCaptureOrigin()
        {
            _captureOriginX = -1;
            _captureOriginY = -1;
        }

        public static void SetParentBackgroundColor(Color? color)
        {
            lock (_parentBgLock) { _parentBackgroundColor = color; }
        }

        public static void ClearParentBackgroundColor()
        {
            lock (_parentBgLock) { _parentBackgroundColor = null; }
        }

        // internal helper to read the value in a thread-safe way
        private static Color? GetParentBackgroundColor()
        {
            lock (_parentBgLock) { return _parentBackgroundColor; }
        }

        // Place these in RVUtils (alongside SetCaptureOrigin, ClearCaptureOrigin, etc.)
        public static void SetCaptureLocalOrigin(Form form, Point localPoint)
        {
            if (form == null) { ClearCaptureOrigin(); return; }
            // localPoint is in form client coordinates (0,0 is top-left of form client area)
            var screen = form.PointToScreen(localPoint);
            SetCaptureOrigin(screen.X, screen.Y);
        }

        public static void SetCaptureLocalOrigin(Control control, Point localPoint)
        {
            if (control == null) { ClearCaptureOrigin(); return; }
            // Convert localPoint (relative to control) to form client coords then to screen
            var form = control.FindForm();
            if (form == null) { ClearCaptureOrigin(); return; }

            // Convert point from control-client to screen:
            // 1) control.PointToScreen(localPoint) gives screen directly
            var screen = control.PointToScreen(localPoint);
            SetCaptureOrigin(screen.X, screen.Y);
        }

        // Convenience overloads for the common case of using the control's top-left
        public static void SetCaptureLocalOrigin(Control control)
        {
            if (control == null) { ClearCaptureOrigin(); return; }
            SetCaptureLocalOrigin(control, Point.Empty);
        }

        public static void SetCaptureLocalOrigin(Form form)
        {
            if (form == null) { ClearCaptureOrigin(); return; }
            SetCaptureLocalOrigin(form, Point.Empty);
        }



        // Draw the parent's pixels into this control's graphics so no real transparency is left.
        public static void DrawParentBackgroundToGraphics(Control ctrl, Graphics g)
        {
            if (ctrl == null || g == null) return;

            var parent = ctrl.Parent;
            if (parent == null)
            {
                // No parent: just fill with the control BackColor
                /*using (var b = new SolidBrush(ctrl.BackColor))
                    g.FillRectangle(b, ctrl.ClientRectangle);*/
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
                    //g.DrawImage(parentBmp, ctrl.ClientRectangle, srcRect, GraphicsUnit.Pixel);
                }
            }
            catch
            {
                // If DrawToBitmap fails on some controls/platforms, fallback to BackColor fill
                using (var b = new SolidBrush(ctrl.BackColor))
                    g.FillRoundedRect(b, ctrl.ClientRectangle);
            }
        }


        /// <summary>
        /// Applies a standard set of ControlStyles for custom-painted, opaque controls to ensure smooth rendering and prevent transparency artifacts.
        /// </summary>
        public static void EnableOptimizedCustomPainting(this Control control)
        {
            control.SetStyle(ControlStyles.Opaque, false);
            control.SetStyle(ControlStyles.UserPaint, false);
            control.SetStyle(ControlStyles.AllPaintingInWmPaint, true); // Reduces flicker
            control.SetStyle(ControlStyles.ResizeRedraw, true);
            control.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            control.SetStyle(ControlStyles.UserPaint |
              ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw |
              ControlStyles.OptimizedDoubleBuffer, true);
        }
        public static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle rect, float cornerRadius)
        {
            // Create a new path
            var path = new System.Drawing.Drawing2D.GraphicsPath();

            // To prevent an exception, the corner radius can't be larger than half the rectangle's smallest side.
            float diameter = Math.Min(Math.Min(rect.Width, rect.Height), cornerRadius * 2);

            // If the radius is 0, just return a standard rectangle path
            if (diameter <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            // Define the rectangle for the arcs
            RectangleF arcRect = new RectangleF(rect.Location, new Size((int)diameter, (int)diameter));

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
            if (g == null) return;

            if (rect.Width <= 0 || rect.Height <= 0) return;

            bool paintedBackground = false;

            if (_captureOriginX != -1 && _captureOriginY != -1)
            {
                try
                {
                    var srcPoint = new Point(_captureOriginX + rect.Left - 1, _captureOriginY + rect.Top - 1);
                    g.CopyFromScreen(srcPoint, rect.Location, rect.Size);
                    paintedBackground = true;
                }
                catch
                {
                    paintedBackground = false;
                }
            }

            if (!paintedBackground)
            {
                var parentColor = GetParentBackgroundColor();
                if (parentColor.HasValue)
                {
                    var c = parentColor.Value;
                    using (var bg = new SolidBrush(Color.FromArgb(250, c.R, c.G, c.B)))
                    {
                        g.FillRectangle(bg, rect);
                    }
                    paintedBackground = true;
                }
            }

            if (!paintedBackground)
            {
                if (brush is SolidBrush sb)
                {
                    var c = sb.Color;
                    using (var opaque = new SolidBrush(Color.FromArgb(250, c.R, c.G, c.B)))
                        g.FillRectangle(opaque, rect);
                }
                else if (brush != null)
                {
                    using (var opaque = new SolidBrush(Color.FromArgb(250, 255, 255, 255)))
                        g.FillRectangle(opaque, rect);
                }
            }

            using (var path = CreateRoundedRectanglePath(rect, cornerRadius))
            {
                var oldSmoothing = g.SmoothingMode;
                try
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    var plusminus = 5;

                    if (brush is SolidBrush solidBrush)
                    {
                        /*var c = solidBrush.Color;
                        using (var gradientBrush = new LinearGradientBrush(
                            new Point(rect.Left, rect.Top),
                            new Point(rect.Right, rect.Bottom),
                            Color.FromArgb(150, Math.Min(255, c.R + plusminus), Math.Min(255, c.G + plusminus), Math.Min(255, c.B + plusminus)),
                            Color.FromArgb(150, Math.Max(0, c.R -plusminus), Math.Max(0, c.G - plusminus), Math.Max(0, c.B - plusminus))))
                        {
                            g.FillPath(gradientBrush, path);
                        }
                        using (var gradientBrush = new LinearGradientBrush(
                            new Point(rect.Right, rect.Bottom),
                            new Point(rect.Left, rect.Top),
                            
                            Color.FromArgb(50, Math.Min(255, c.R + plusminus), Math.Min(255, c.G + plusminus), Math.Min(255, c.B + plusminus)),
                            Color.FromArgb(50, Math.Max(0, c.R - plusminus), Math.Max(0, c.G - plusminus), Math.Max(0, c.B - plusminus))))
                        {
                            g.FillPath(gradientBrush, path);
                        }*/
                        using(var gradientBrush = brush)
                        {
                            g.FillPath(gradientBrush, path);
                        }
                    }
                    else if (brush != null)
                    {
                        /*using (var gradientBrush = new LinearGradientBrush(
                            new Point(rect.Left, rect.Top),
                            new Point(rect.Left, rect.Bottom),
                            Color.FromArgb(255, 220, 220, 220),
                            Color.FromArgb(255, 180, 180, 180)))
                        {
                            g.FillPath(gradientBrush, path);
                        }*/
                        using (var gradientBrush = brush)
                        {
                            g.FillPath(gradientBrush, path);
                        }
                    }
                }
                finally
                {
                    g.SmoothingMode = oldSmoothing;
                }
            }
        }



        public static void FillRoundedRect(this Graphics g, Brush brush, Rectangle rect, int? cornerRadius = null)
        {
            FillRoundedRectangle(g, brush, rect, cornerRadius ?? RVUtils.cornerRadius);
        }
        /// <summary>
        /// Creates a GraphicsPath representing a rounded rectangle.
        /// </summary>
        public static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(rect.Location, size);

            

            // Top left
            path.AddArc(arc, 180, 90);

            // Top right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        #region DrawRoundedRect (Outline Only) - Default Radius 5

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, int radius = 5)
        {
            if (g == null) return;
            using (GraphicsPath path = CreateRoundedRectanglePath(rect, radius))
            {
                g.DrawPath(pen, path);
            }
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, int x, int y, int width, int height, int radius = 5)
        {
            DrawRoundedRectangle(g, pen, new Rectangle(x, y, width, height), radius);
        }

        public static void DrawRoundedRect(Graphics g, Pen pen, Rectangle rect, int radius = 5)
        {
            DrawRoundedRectangle(g, pen, rect, radius);
        }

        public static void DrawRoundedRect(Graphics g, Pen pen, int x, int y, int width, int height, int radius = 5)
        {
            DrawRoundedRectangle(g, pen, new Rectangle(x, y, width, height), radius);
        }

        #endregion

        

    }
}

