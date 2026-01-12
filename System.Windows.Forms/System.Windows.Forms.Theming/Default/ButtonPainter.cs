// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (c) 2007 Novell, Inc.
//
// Authors:
//	Andreia Gaita (avidigal@novell.com)

using System;
using System.Drawing;
using static System.Windows.Forms.RVUtils;

namespace System.Windows.Forms.Theming.Default
{
	/// <summary>
	/// Summary description for Button.
	/// </summary>
	internal class ButtonPainter
	{
		public ButtonPainter ()
		{

		}

		protected SystemResPool ResPool { get { return ThemeEngine.Current.ResPool; } }
		
		#region Buttons
		#region Standard Button
		public virtual void Draw (Graphics g, Rectangle bounds, ButtonThemeState state, Color backColor, Color foreColor) {
			bool is_themecolor = backColor.ToArgb () == ThemeEngine.Current.ColorControl.ToArgb () || backColor == Color.Empty ? true : false;
			CPColor cpcolor = is_themecolor ? CPColor.Empty : ResPool.GetCPColor (backColor);
			//Pen pen;

            int cornerRadius = RVUtils.cornerRadius;
            var originalSmoothingMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            Rectangle borderRect = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

            switch (state)
            {
                case ButtonThemeState.Normal:
                case ButtonThemeState.Entered:
                case ButtonThemeState.Disabled:
                    // Draw a single, smooth, rounded border.
                    Pen pen = is_themecolor ? SystemPens.ControlDark : ResPool.GetPen(cpcolor.Dark);
                    pen = new Pen(RVUtils.NewGradientPen());
                    using (var path = RVUtils.CreateRoundedRectanglePath(borderRect, cornerRadius))
                    {
                        g.DrawPath(pen, path);
                    }
                    break;
                case ButtonThemeState.Pressed:
                case ButtonThemeState.Default:
                    // Draw the outer rounded border.
                    Pen outerPen = is_themecolor ? SystemPens.ControlDarkDark : ResPool.GetPen(cpcolor.DarkDark);
                    outerPen = new Pen(RVUtils.NewGradientPen());
                    using (var path = RVUtils.CreateRoundedRectanglePath(borderRect, cornerRadius))
                    {
                        g.DrawPath(outerPen, path);
                    }

                    // Inflate the bounds to get the inner rectangle for the inset look.
                    Rectangle innerRect = new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 5, bounds.Height - 5);
                    Pen innerPen = is_themecolor ? SystemPens.ControlDark : ResPool.GetPen(cpcolor.Dark);
                    innerPen = new Pen(RVUtils.NewGradientPen());
                    using (var path = RVUtils.CreateRoundedRectanglePath(innerRect, cornerRadius - 2))
                    {
                        g.DrawPath(innerPen, path);
                    }
                    break;
            }

            g.SmoothingMode = originalSmoothingMode;
        }
		#endregion

		#region FlatStyle Button
		public virtual void DrawFlat (Graphics g, Rectangle bounds, ButtonThemeState state, Color backColor, Color foreColor, FlatButtonAppearance appearance) {
			bool is_themecolor = backColor.ToArgb () == ThemeEngine.Current.ColorControl.ToArgb () || backColor == Color.Empty ? true : false;
			CPColor cpcolor = is_themecolor ? CPColor.Empty : ResPool.GetCPColor (backColor);
			Pen pen;
            var originalSmoothingMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;


            int cornerRadius = RVUtils.cornerRadius;
            switch (state)
            {
                case ButtonThemeState.Normal:
                case ButtonThemeState.Disabled:
                    // This will just use the BackColor
                    break;
                case ButtonThemeState.Entered:
                case ButtonThemeState.Default | ButtonThemeState.Entered:
                    if (appearance.MouseOverBackColor != Color.Empty)
                        RVUtils.FillRoundedRectangle(g, ResPool.GetSolidBrush(appearance.MouseOverBackColor), bounds, cornerRadius, RVUtils.DefaultInnerBrush);
                    else
                        RVUtils.FillRoundedRectangle(g, ResPool.GetSolidBrush(ChangeIntensity(backColor, .9F)), bounds, cornerRadius, RVUtils.DefaultInnerBrush);
                    break;
                case ButtonThemeState.Pressed:
                    if (appearance.MouseDownBackColor != Color.Empty)
                        RVUtils.FillRoundedRectangle(g, ResPool.GetSolidBrush(appearance.MouseDownBackColor), bounds, cornerRadius, RVUtils.DefaultInnerBrush);
                    else
                        RVUtils.FillRoundedRectangle(g, ResPool.GetSolidBrush(ChangeIntensity(backColor, .95F)), bounds, cornerRadius, RVUtils.DefaultInnerBrush);
                    break;
                case ButtonThemeState.Default:
                    if (appearance.CheckedBackColor != Color.Empty)
                        RVUtils.FillRoundedRectangle(g, ResPool.GetSolidBrush(appearance.CheckedBackColor), bounds, cornerRadius, RVUtils.DefaultInnerBrush);
                    break;
            }

            if (appearance.BorderColor == Color.Empty)
				pen = is_themecolor ? SystemPens.ControlDarkDark : ResPool.GetSizedPen (cpcolor.DarkDark, appearance.BorderSize);
			else
				pen = ResPool.GetSizedPen (appearance.BorderColor, appearance.BorderSize);
            pen = new Pen(RVUtils.NewGradientPen());

            bounds.Width -= 1;
			bounds.Height -= 1;
				
			if (appearance.BorderSize > 0)
				g.DrawRectangle (pen, bounds);
            g.SmoothingMode = originalSmoothingMode;
        }
        #endregion

        #region Popup Button
        public virtual void DrawPopup(Graphics g, Rectangle bounds, ButtonThemeState state, Color backColor, Color foreColor)
        {
            bool is_themecolor = backColor.ToArgb() == ThemeEngine.Current.ColorControl.ToArgb() || backColor == Color.Empty;
            CPColor cpcolor = is_themecolor ? CPColor.Empty : ResPool.GetCPColor(backColor);
            Pen pen;

            // --- START: New Rounded Drawing Logic ---

            // Define the radius for the corners.
            int cornerRadius = RVUtils.cornerRadius;

            // Set high-quality rendering for smooth curves.
            var originalSmoothingMode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            switch (state)
            {
                case ButtonThemeState.Normal:
                case ButtonThemeState.Disabled:
                case ButtonThemeState.Pressed:
                case ButtonThemeState.Default:
                    pen = is_themecolor ? SystemPens.ControlDarkDark : ResPool.GetPen(cpcolor.DarkDark);
                    pen = new Pen(RVUtils.NewGradientPen());

                    Rectangle outerBounds = new Rectangle(bounds.Location, new Size(bounds.Width - 1, bounds.Height - 1));

                    // Draw the outer rounded rectangle.
                    using (var path = CreateRoundedRectanglePath(outerBounds, cornerRadius))
                    {
                        g.DrawPath(pen, path);
                    }

                    // For Default or Pressed states, draw a second, inner border.
                    if (state == ButtonThemeState.Default || state == ButtonThemeState.Pressed)
                    {
                        Rectangle innerBounds = outerBounds;
                        innerBounds.Inflate(-1, -1);
                        using (var innerPath = CreateRoundedRectanglePath(innerBounds, cornerRadius > 1 ? cornerRadius - 1 : 1))
                        {
                            g.DrawPath(pen, innerPath);
                        }
                    }
                    break;

                case ButtonThemeState.Entered:
                    // For the 3D effect, we must use clipping to draw the path in two different colors.
                    Rectangle borderBounds = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                    using (var path = CreateRoundedRectanglePath(borderBounds, cornerRadius))
                    {
                        // Draw the top-left highlight using a diagonal clip.
                        pen = is_themecolor ? SystemPens.ControlLightLight : ResPool.GetPen(cpcolor.LightLight);
                        pen = new Pen(RVUtils.NewGradientPen());
                        using (var clipPath = new System.Drawing.Drawing2D.GraphicsPath())
                        {
                            clipPath.AddPolygon(new Point[] {
                        borderBounds.Location,
                        new Point(borderBounds.Right, borderBounds.Top),
                        new Point(borderBounds.Left, borderBounds.Bottom)
                    });
                            using (var clipRegion = new Region(clipPath))
                            {
                                g.SetClip(clipRegion, System.Drawing.Drawing2D.CombineMode.Intersect);
                                g.DrawPath(pen, path);
                                g.ResetClip();
                            }
                        }

                        // Draw the bottom-right shadow using an inverted diagonal clip.
                        pen = is_themecolor ? SystemPens.ControlDark : ResPool.GetPen(cpcolor.Dark);
                        pen = new Pen(RVUtils.NewGradientPen());
                        using (var clipPath = new System.Drawing.Drawing2D.GraphicsPath())
                        {
                            clipPath.AddPolygon(new Point[] {
                        new Point(borderBounds.Right, borderBounds.Top),
                        new Point(borderBounds.Right, borderBounds.Bottom),
                        new Point(borderBounds.Left, borderBounds.Bottom)
                    });
                            using (var clipRegion = new Region(clipPath))
                            {
                                g.SetClip(clipRegion, System.Drawing.Drawing2D.CombineMode.Intersect);
                                g.DrawPath(pen, path);
                                g.ResetClip();
                            }
                        }
                    }
                    break;
            }

            // Restore the original graphics state.
            g.SmoothingMode = originalSmoothingMode;
        }
        #endregion
        #endregion

        private static Color ChangeIntensity (Color baseColor, float percent)
		{
			int H, I, S;

			ControlPaint.Color2HBS (baseColor, out H, out I, out S);
			int NewIntensity = Math.Min (255, (int)(I * percent));

			return ControlPaint.HBS2Color (H, NewIntensity, S);			
		}
	}
}
