using ColorUtils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
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
        public static ConcurrentDictionary<WeakReference, Size> _controlSizes = new ConcurrentDictionary<WeakReference, Size>();
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
        private static bool Initialized = false;

        public static bool IsFrutigerAero = false;
        public static bool IsFrutigerAeroEnableBorder = false;
        public static int FrutigerAeroBorderInset = 2;
        public static int FrutigerAeroBorderThickness = 2;
        public static double Opacity = 1;
        public static double BorderWidth = 1;
        public static bool FakeAA = false;

        public static bool SetRegion = true;
        public static bool UseClipping = false;
        public static int UniversalAlpha = 250;

        internal static SystemResPool ResPool = new SystemResPool();

        /// <summary>
        /// App hook for MWF_ALWAYS_ROUND: decide which controls get rounded.
        /// Null = use the default set (ButtonBase/TextBoxBase/ComboBox/Form).
        /// </summary>
        public static Func<Control, bool>? AutoRoundPredicate = null;

        public static void Initialize() { 
            if (!Initialized)
            {
                if(Environment.GetEnvironmentVariable("RV_CORNER_RADIUS") != null)
                {
                    try
                    {
                        cornerRadius = int.Parse(Environment.GetEnvironmentVariable("RV_CORNER_RADIUS"));
                    }
                    catch(Exception E)
                    {
                        System.Console.WriteLine($"Error setting cornerRadius to {Environment.GetEnvironmentVariable("RV_CORNER_RADIUS")}: {E.StackTrace}");
                    }
                    
                }
                if (Environment.GetEnvironmentVariable("RV_OPACITY") != null)
                {
                    try
                    {
                        Opacity = double.Parse(Environment.GetEnvironmentVariable("RV_OPACITY"));
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting Opacity to {Environment.GetEnvironmentVariable("RV_OPACITY")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDER_INSET") != null)
                {
                    try
                    {
                        FrutigerAeroBorderInset = int.Parse(Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDER_INSET"));
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting FrutigerAeroBorderInset to {Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDER_INSET")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDER_THICKNESS") != null)
                {
                    try
                    {
                        FrutigerAeroBorderThickness = int.Parse(Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDER_THICKNESS"));
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting FrutigerAeroBorderThickness to {Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDER_THICKNESS")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO") != null)
                {
                    try
                    {
                        IsFrutigerAero = Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO").ToLowerInvariant() == "true";
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting IsFrutigerAero to {Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDERS") != null)
                {
                    try
                    {
                        IsFrutigerAeroEnableBorder = Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDERS").ToLowerInvariant() == "true";
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting IsFrutigerAeroEnableBorder to {Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_BORDERS")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_OPACITY") != null)
                {
                    try
                    {
                        Opacity = double.Parse(Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_OPACITY"));
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting Opacity to {Environment.GetEnvironmentVariable("RV_FRUTIGER_AERO_OPACITY")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_BORDER_WIDTH") != null)
                {
                    try
                    {
                        BorderWidth = double.Parse(Environment.GetEnvironmentVariable("RV_BORDER_WIDTH"));
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting BorderWidth to {Environment.GetEnvironmentVariable("RV_BORDER_WIDTH")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_SIM_AA") != null)
                {
                    try
                    {
                        FakeAA = Environment.GetEnvironmentVariable("RV_SIM_AA").ToLowerInvariant() == "true";
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting FakeAA to {Environment.GetEnvironmentVariable("RV_SIM_AA")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_UNIVERSAL_ALPHA") != null)
                {
                    try
                    {
                        UniversalAlpha = int.Parse(Environment.GetEnvironmentVariable("RV_UNIVERSAL_ALPHA"));
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting UniversalAlpha to {Environment.GetEnvironmentVariable("RV_UNIVERSAL_ALPHA")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_SET_REGION") != null)
                {
                    try
                    {
                        SetRegion = Environment.GetEnvironmentVariable("RV_SET_REGION").ToLowerInvariant() == "true";
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting FakeAA to {Environment.GetEnvironmentVariable("RV_SET_REGION")}: {E.StackTrace}");
                    }

                }
                if (Environment.GetEnvironmentVariable("RV_USE_CLIPPING") != null)
                {
                    try
                    {
                        UseClipping = Environment.GetEnvironmentVariable("RV_USE_CLIPPING").ToLowerInvariant() == "true";
                    }
                    catch (Exception E)
                    {
                        System.Console.WriteLine($"Error setting FakeAA to {Environment.GetEnvironmentVariable("RV_USE_CLIPPING")}: {E.StackTrace}");
                    }

                }
                Initialized = true;
            }
        }

        public static Func<Control, Type, bool> IsTypeOrContainedInTypeRecursive = (Control x, Type T) =>
                    {
                        Logger.AddLog($"Encountered Type: {(x== null ? "null" : x.GetType().ToString())}");
                        // 1. Termination condition: If we ran out of parents, return false
                        if (x == null) return false;

                        // 2. Check condition: Is this the type we are looking for?
                        if (T.IsInstanceOfType(x)) return true;

						// 3. Recursive step: Check the parent
						
                        return IsTypeOrContainedInTypeRecursive(x.Parent, T);
        };
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
        /*public static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle rect, float cornerRadius)
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
            RectangleF arcRect = new RectangleF(rect.Location, new SizeF(diameter, diameter));

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
        }*/
        public static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(Rectangle rect, float cornerRadius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            

            // Adjust these values to correct for asymmetry or specific DPI clipping issues.
            // insetTopLeft shifts the Top and Left edges inward (away from 0,0).
            // insetBottomRight shifts the Bottom and Right edges inward (towards 0,0).
            float insetTopLeft = 1.0f;     // Try 0.5f or 0f if left/top is too cut off
            float insetBottomRight = 2.0f;  // Try 1.5f or 2f if right/bottom is still overflowing
            //insetTopLeft = 0; insetBottomRight = 0;

            float left = rect.Left + insetTopLeft;
            float top = rect.Top + insetTopLeft;
            float right = rect.Right - insetBottomRight;
            float bottom = rect.Bottom - insetBottomRight;

            // Calculate effective width/height based on the new insets
            float width = right - left;
            float height = bottom - top;

            float diameter = Math.Min(Math.Min(width, height), cornerRadius * 2);
            if (diameter <= 0)
            {
                // Fallback to a rectangle using the same insets
                // FIX: Prevent ArgumentException from negative width/height
                if (width > 0 && height > 0)
                    path.AddRectangle(new RectangleF(left, top, width, height));
                return path;
            }

            if (diameter <= 0)
            {
                // Fallback to a rectangle using the same insets
                path.AddRectangle(new RectangleF(left, top, width, height));
                return path;
            }

            float radius = diameter / 2f;

            // Top-Left Arc
            path.AddArc(left, top, diameter, diameter, 180, 90);
            path.AddLine(left + radius, top, right - radius, top);

            // Top-Right Arc
            path.AddArc(right - diameter, top, diameter, diameter, 270, 90);
            path.AddLine(right, top + radius, right, bottom - radius);

            // Bottom-Right Arc
            path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
            path.AddLine(right - radius, bottom, left + radius, bottom);

            // Bottom-Left Arc
            path.AddArc(left, bottom - diameter, diameter, diameter, 90, 90);
            path.AddLine(left, bottom - radius, left, top + radius);

            path.CloseFigure();
            return path;
        }

        public static void DrawRoundedRectangleBorder(this Graphics g, Rectangle rect, float cornerRadius, float borderThickness, ColorBlend blend = null, DashStyle dashStyle = DashStyle.Solid)
        {
            // 1. Handle Thickness Inset
            // We shift the path inward by half the thickness so the pen draws strictly inside the rect.
            float borderInset = 2;
            float thicknessInset = borderInset +  borderThickness / 2.0f;

            Rectangle pathRect = new Rectangle(
                (int)(rect.X + thicknessInset),
                (int)(rect.Y + thicknessInset),
                (int)(rect.Width - (thicknessInset * 2)),
                (int)(rect.Height - (thicknessInset * 2))
            );

            if (pathRect.Width < 1 || pathRect.Height < 1) return;

            // 2. Generate the Path (reusing the helper which applies its own internal 1.0/2.0 insets)
            using (var path = CreateRoundedRectanglePath(pathRect, cornerRadius))
            {
                // 3. Handle Color Blend (Default to Aero-like if null)
                // Mimics the Top(White) -> Bottom(Black) border from your original code
                ColorBlend activeBlend = blend ?? new ColorBlend
                {
                    Positions = new float[] { 0.0f, 1.0f },
                    Colors = new Color[] { Color.FromArgb(120, Color.White), Color.FromArgb(80, Color.Black) }
                };

                using (var brush = new LinearGradientBrush(rect, Color.Black, Color.Black, LinearGradientMode.Vertical))
                {
                    brush.InterpolationColors = activeBlend;

                    using (var pen = new Pen(brush, borderThickness))
                    {
                        pen.DashStyle = dashStyle; // Apply Solid, Dash, Dot, etc.

                        // Optional: Align dashes to corners for cleaner look on rounded rects
                        // pen.DashCap = DashCap.Flat; 

                        g.DrawPath(pen, path);
                    }
                }
            }
        }
        public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rect, int cornerRadius, Brush? innerBrush = null)

        {
            Initialize();
            DefaultInnerBrush = ColorUtils.CielRandomGradientGenerator.Generate(3, RandomColorMode.EquiSat, GradientDirection.ForwardDiagonal);

            if (innerBrush == null) { innerBrush = brush; }
            if (g == null) return;

            

            // SAFETY CHECK: Prevent exceptions from negative or zero-size rectangles
            if (rect.Width <= 0 || rect.Height <= 0) return;

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
                    Brush brush2 = (Brush)brush.Clone();
                    Logger.AddLog($"brush2: {(brush2 is SolidBrush sb ? $"SolidBrush [A:{sb.Color.A} R:{sb.Color.R} G:{sb.Color.G} B:{sb.Color.B}]" : $"Type={brush2?.GetType().Name ?? "NULL"}")}");
                    // 2. Extract the RGB components from the original brush
                    //Color c = ((SolidBrush)brush).Color;

                    // 3. Construct brush3: Same Color, but Alpha = 0 (Transparent)
                    // This is effectively "Color to Alpha" like in Photoshop or Krita
                    //SolidBrush brush3 = new SolidBrush(Color.FromArgb(255, c.R, c.G, c.B));
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
            Initialize();
            FillRoundedRectangle(g, brush, rect, cornerRadius ?? RVUtils.cornerRadius, innerBrush);
        }
        public static void DrawRoundedRectangle(this Graphics g, Pen pen, Rectangle rect, float cornerRadius)
        {
            Initialize();
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
            // Wrapper for Interactive Controls
            public static void DrawAeroInteractive(this Graphics g, Rectangle rect)
            {
                // 1. Top Sheen
                using (var glossBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
                {
                    var blend = new ColorBlend();
                    blend.Positions = new float[] { 0.0f, 0.05f, 0.35f, 0.45f, 1.0f };
                    blend.Colors = new Color[] { Color.FromArgb(200, Color.White), Color.FromArgb(150, Color.White), Color.FromArgb(20, Color.White), Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White) };
                    glossBrush.InterpolationColors = blend;
                    g.FillRectangle(glossBrush, rect);
                }

                // 2. Bottom Depth
                using (var depthBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
                {
                    var blend = new ColorBlend();
                    blend.Positions = new float[] { 0.0f, 0.5f, 0.85f, 0.95f, 1.0f };
                    blend.Colors = new Color[] { Color.FromArgb(0, Color.Black), Color.FromArgb(0, Color.Black), Color.FromArgb(40, Color.Black), Color.FromArgb(80, Color.Black), Color.FromArgb(100, Color.Black) };
                    depthBrush.InterpolationColors = blend;
                    g.FillRectangle(depthBrush, rect);
                }

                // 3. Subtle Reflection
                using (var reflectionBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
                {
                    var blend = new ColorBlend();
                    blend.Positions = new float[] { 0.0f, 0.42f, 0.45f, 0.48f, 1.0f };
                    blend.Colors = new Color[] { Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), Color.FromArgb(50, Color.White), Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White) };
                    reflectionBrush.InterpolationColors = blend;
                    g.FillRectangle(reflectionBrush, rect);
                }

                // 4. Borders
                using (var pen = new Pen(Color.FromArgb(120, Color.White))) { g.DrawLine(pen, rect.X + 1, rect.Y + 1, rect.Right - 1, rect.Y + 1); g.DrawLine(pen, rect.X + 1, rect.Y + 1, rect.X + 1, rect.Bottom - 1); }
                using (var pen = new Pen(Color.FromArgb(80, Color.Black))) { g.DrawLine(pen, rect.Right - 1, rect.Y + 1, rect.Right - 1, rect.Bottom - 1); g.DrawLine(pen, rect.X + 1, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1); }

            
            }

        public static void DrawBorderInteractive(this Graphics g, Rectangle rect)
        {
            if (BorderWidth > 0)
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                Rectangle R2 = new Rectangle(rect.Top + 1, rect.Left + 1, rect.Width - 1, rect.Height - 1);
                Rectangle R3 = new Rectangle(rect.Top - 1, rect.Left - 1, rect.Width + 1, rect.Height + 1);
                g.DrawRoundedRectangle(new Pen(Color.FromArgb(255, 40, 40, 40), (int) BorderWidth), rect);
                g.DrawRoundedRectangle(new Pen(Color.FromArgb(255, 40, 40, 40), (int)BorderWidth), R2);
                g.DrawRoundedRectangle(new Pen(Color.FromArgb(255, 40, 40, 40), (int)BorderWidth), R3);
            }
        }

            // Wrapper for Container Controls
            public static void DrawAeroContainer(this Graphics g, Rectangle rect)
            {
                // 1. Subtle Frost
                using (var bgBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
                {
                    var blend = new ColorBlend();
                    blend.Positions = new float[] { 0.0f, 0.4f, 1.0f };
                    blend.Colors = new Color[] { Color.FromArgb(25, Color.White), Color.FromArgb(0, Color.White), Color.FromArgb(15, Color.Black) };
                    bgBrush.InterpolationColors = blend;
                    g.FillRectangle(bgBrush, rect);
                }

                // 2. Clean Border
                using (var topPen = new Pen(Color.FromArgb(60, Color.White))) { g.DrawLine(topPen, rect.X, rect.Y, rect.Right, rect.Y); }
                using (var borderPen = new Pen(Color.FromArgb(100, Color.Black))) { g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1); }
            }

        public static void DrawAeroPressed(this Graphics g, Rectangle rect)
        {
            // 1. Top Shadow (Inverse of Sheen)
            using (var shadowBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
            {
                var blend = new ColorBlend();
                blend.Positions = new float[] { 0.0f, 0.05f, 0.35f, 0.45f, 1.0f };
                blend.Colors = new Color[] { Color.FromArgb(200, Color.Black), Color.FromArgb(150, Color.Black), Color.FromArgb(20, Color.Black), Color.FromArgb(0, Color.Black), Color.FromArgb(0, Color.Black) };
                shadowBrush.InterpolationColors = blend;
                g.FillRectangle(shadowBrush, rect);
            }

            // 2. Bottom Highlight (Inverse of Depth)
            using (var highlightBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Transparent, LinearGradientMode.Vertical))
            {
                var blend = new ColorBlend();
                blend.Positions = new float[] { 0.0f, 0.5f, 0.85f, 0.95f, 1.0f };
                blend.Colors = new Color[] { Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), Color.FromArgb(40, Color.White), Color.FromArgb(80, Color.White), Color.FromArgb(100, Color.White) };
                highlightBrush.InterpolationColors = blend;
                g.FillRectangle(highlightBrush, rect);
            }

            // 3. Borders Swapped (Inverse)
            using (var pen = new Pen(Color.FromArgb(80, Color.Black))) { g.DrawLine(pen, rect.X + 1, rect.Y + 1, rect.Right - 1, rect.Y + 1); g.DrawLine(pen, rect.X + 1, rect.Y + 1, rect.X + 1, rect.Bottom - 1); }
            using (var pen = new Pen(Color.FromArgb(120, Color.White))) { g.DrawLine(pen, rect.Right - 1, rect.Y + 1, rect.Right - 1, rect.Bottom - 1); g.DrawLine(pen, rect.X + 1, rect.Bottom - 1, rect.Right - 1, rect.Bottom - 1); }
        }

    }
}
