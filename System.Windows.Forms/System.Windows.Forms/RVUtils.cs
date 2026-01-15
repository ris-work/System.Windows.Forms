using ColorUtils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Windows.Forms
{
    
    public static class Logger
    {
        public static string Log = "";
        public static void AddLog(string Entry)
        {
            Log = Log + Environment.NewLine + Entry;
        }
    }
    public static class RVUtils
    {
        public static readonly ConcurrentDictionary<WeakReference, Size> _controlSizes = new ConcurrentDictionary<WeakReference, Size>();
        public static Brush DefaultInnerBrush = ColorUtils.CielRandomGradientGenerator.Generate(3, RandomColorMode.EquiSat, GradientDirection.ForwardDiagonal);
        public static Brush DefaultInnerBrushD = new LinearGradientBrush(new Rectangle(0, 0, 100, 100), CielColorGenerator.RandomColorWithLightnessAndChroma(40, 65), CielColorGenerator.RandomColorWithLightnessAndChroma(40, 65), LinearGradientMode.Vertical) { WrapMode = WrapMode.Tile, GammaCorrection=true };
        public static Brush NewGradientPen() { return new LinearGradientBrush(new Rectangle(0, 0, 100, 100), CielColorGenerator.RandomColorWithLightnessAndChroma(40, 65), CielColorGenerator.RandomColorWithLightnessAndChroma(40, 65), LinearGradientMode.Vertical) { WrapMode = WrapMode.Tile, GammaCorrection = true }; }
        public static SolidBrush DefaultInnerBrushHover = new SolidBrush(Color.LightGoldenrodYellow) { };
        public static int cornerRadius = 6;

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
            control.SetStyle(ControlStyles.Opaque, true);
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
            //path.AddRectangle(rect);
            //return path;

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
        public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int cornerRadius, Brush? innerBrush = null)

        {
            DefaultInnerBrush = ColorUtils.CielRandomGradientGenerator.Generate(3, RandomColorMode.EquiSat, GradientDirection.ForwardDiagonal);

            if (innerBrush == null) { innerBrush = brush; }
            if (g == null) return;

            bool paintedBackground = false;

            // 1) Try screen capture if origin set
            if (_captureOriginX != -1 && _captureOriginY != -1)
            {
                try
                {
                    var srcPoint = new Point(_captureOriginX + rect.Left, _captureOriginY + rect.Top);
                    g.CopyFromScreen(srcPoint, rect.Location, rect.Size);
                    paintedBackground = true;
                }
                catch
                {
                    paintedBackground = false;
                }
            }

            // 2) Use explicit parent background color if provided
            /*if (!paintedBackground)
            {
                var parentColor = GetParentBackgroundColor();
                
                if (parentColor.HasValue)
                {
                    var oldSmoothing = g.SmoothingMode;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    Logger.AddLog($"Painting (1) {rect.Top}-{rect.Left}-{rect.Top+rect.Height}-{rect.Left+rect.Width}");
                    var c = parentColor.Value;
                    using (var bg = new SolidBrush(Color.FromArgb(0, c.R, c.G, c.B)))
                    //using (var bg = new SolidBrush(Color.Transparent))
                    {
                        var p = new System.Drawing.Drawing2D.GraphicsPath();
                        p.AddRectangle(rect);
                        g.FillPath(bg, p);
                        g.FillPath(bg, p);
                    }
                    paintedBackground = true;
                    g.SmoothingMode = oldSmoothing;
                }
            }

            // 3) Fallback opaque fill
            if (!paintedBackground)
            {
                if (brush is SolidBrush sb)
                {
                    Logger.AddLog("Painting (2)");
                    var c = sb.Color;
                    using (var opaque = new SolidBrush(Color.FromArgb(255, c.R, c.G, c.B)))
                        g.FillRectangle(opaque, rect);
                }
                else
                {
                    Logger.AddLog("Painting (3)");
                    using (var opaque = new SolidBrush(Color.FromArgb(255, 255, 255, 255)))
                        g.FillRectangle(opaque, rect);
                }
            }*/

            // 4) Draw anti-aliased rounded path on top
            using (var path = CreateRoundedRectanglePath(rect, cornerRadius))
            {
                var oldSmoothing = g.SmoothingMode;
                try
                {
                    Logger.AddLog($"Painting (4) {brush.GetType().Name}");
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    SolidBrush brush2 = (SolidBrush)brush.Clone();
                    Logger.AddLog($"brush2: {(brush2 is SolidBrush sb ? $"SolidBrush [A:{sb.Color.A} R:{sb.Color.R} G:{sb.Color.G} B:{sb.Color.B}]" : $"Type={brush2?.GetType().Name ?? "NULL"}")}");
                    // 2. Extract the RGB components from the original brush
                    Color c = ((SolidBrush)brush).Color;

                    // 3. Construct brush3: Same Color, but Alpha = 0 (Transparent)
                    // This is effectively "Color to Alpha" like in Photoshop or Krita
                    SolidBrush brush3 = new SolidBrush(Color.FromArgb(255, c.R, c.G, c.B));
                    //brush3.Tra
                    //g.FillRectangle(brush3, new Rectangle(rect.Left - 1, rect.Top - 1, rect.Width + 1, rect.Height + 1));

                    g.SetClip(CreateRoundedRectanglePath(rect, cornerRadius));


                    g.FillPath(innerBrush, path);
                    //g.FillRectangle(brush, rect);
                    g.ResetClip();

                }
                finally
                {
                    Logger.AddLog("Painting (5)");
                    g.SmoothingMode = oldSmoothing;
                }
            }
        }



        public static void FillRoundedRect(this Graphics g, Brush brush, Rectangle rect, int? cornerRadius = null, Brush? innerBrush = null)
        {
            FillRoundedRectangle(g, brush, rect, cornerRadius ?? RVUtils.cornerRadius, innerBrush);
        }
        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, float cornerRadius)
        {
            if (g == null || pen == null) return;

            using (var path = CreateRoundedRectanglePath(rect, cornerRadius))
            {
                var oldSmoothing = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                try
                {
                    g.DrawPath(pen, path);
                }
                finally
                {
                    g.SmoothingMode = oldSmoothing;
                }
            }
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect)
        {
            DrawRoundedRectangle(g, pen, rect, RVUtils.cornerRadius);
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, int x, int y, int width, int height, float cornerRadius)
        {
            DrawRoundedRectangle(g, pen, new Rectangle(x, y, width, height), cornerRadius);
        }

        public static void DrawRoundedRectangle(this Graphics g, Pen pen, int x, int y, int width, int height)
        {
            DrawRoundedRectangle(g, pen, new Rectangle(x, y, width, height), RVUtils.cornerRadius);
        }

        public static void FillRoundedRect(this Graphics g, Brush brush, int x, int y, int width, int height)
        {
            g.FillRoundedRect(brush, new Rectangle(x, y, width, height));
        }

        public static DashStyle ConvertToDashStyle(ButtonBorderStyle style)
        {
            switch (style)
            {
                case ButtonBorderStyle.Dashed: return DashStyle.Dash;
                case ButtonBorderStyle.Dotted: return DashStyle.Dot;
                case ButtonBorderStyle.Solid:
                default: return DashStyle.Solid;
            }
        }

    }
}
