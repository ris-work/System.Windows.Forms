//
// TextBoxRenderer.cs
//
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
// Copyright (c) 2006 Novell, Inc.
//
// Authors:
//	Jonathan Pobst (monkey@jpobst.com)
//

using Mono.Unix.Native;
using System;
using System.Drawing;

using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms
{
	public sealed class TextBoxRenderer
	{
		#region Private Constructor
		private TextBoxRenderer () { }
		#endregion

		#region Public Static Methods
		public static void DrawTextBox (Graphics g, Rectangle bounds, TextBoxState state)
		{
			DrawTextBox (g, bounds, String.Empty, null, Rectangle.Empty, TextFormatFlags.Default, state);
		}

		public static void DrawTextBox (Graphics g, Rectangle bounds, string textBoxText, Font font, TextBoxState state)
		{
			DrawTextBox (g, bounds, textBoxText, font, Rectangle.Empty, TextFormatFlags.Default, state);
		}

		public static void DrawTextBox (Graphics g, Rectangle bounds, string textBoxText, Font font, Rectangle textBounds, TextBoxState state)
		{
			DrawTextBox (g, bounds, textBoxText, font, textBounds, TextFormatFlags.Default, state);
		}

		public static void DrawTextBox (Graphics g, Rectangle bounds, string textBoxText, Font font, TextFormatFlags flags, TextBoxState state)
		{
			DrawTextBox (g, bounds, textBoxText, font, Rectangle.Empty, flags, state);
		}

		public static void DrawTextBox (Graphics g, Rectangle bounds, string textBoxText, Font font, Rectangle textBounds, TextFormatFlags flags, TextBoxState state) { 
            if (!IsSupported)
                throw new InvalidOperationException();

        // Set smoothing mode for high-quality drawing.
        var originalSmoothingMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Define the corner radius for the rounded rectangle.
            int cornerRadius = 8;
        Rectangle borderRect = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        // Draw the background fill based on the state.
        Color backColor;
            switch (state)
            {
                case TextBoxState.Disabled:
                    backColor = SystemColors.Control;
                    break;
                default:
                    backColor = SystemColors.Window;
                    break;
            }

            using (Brush backgroundBrush = new SolidBrush(backColor))
            {
                RVUtils.FillRoundedRectangle(g, backgroundBrush, bounds, cornerRadius);
            }

// Draw the rounded border based on the state.
Pen borderPen;
switch (state)
{

    case TextBoxState.Selected:
    case TextBoxState.Hot:
        borderPen = new Pen(Color.DodgerBlue, 2); // Thicker, distinct color for focus
        break;
    case TextBoxState.Disabled:
        borderPen = SystemPens.ControlDark;
        break;
    default:
        borderPen = SystemPens.ControlDark;
        break;
}

using (var path = RVUtils.CreateRoundedRectanglePath(borderRect, cornerRadius))
{
    g.DrawPath(borderPen, path);
}

// The original text drawing logic remains unchanged.
if (textBounds == Rectangle.Empty)
    textBounds = new Rectangle(bounds.Left + 3, bounds.Top + 3, bounds.Width - 6, bounds.Height - 6);

if (textBoxText != String.Empty)
    if (state == TextBoxState.Disabled)
        TextRenderer.DrawText(g, textBoxText, font, textBounds, SystemColors.GrayText, flags);
    else
        TextRenderer.DrawText(g, textBoxText, font, textBounds, SystemColors.ControlText, flags);

// Restore the original SmoothingMode.
g.SmoothingMode = originalSmoothingMode;
		}
		#endregion

		#region Public Static Properties
		public static bool IsSupported {
			get { return VisualStyleInformation.IsEnabledByUser && (Application.VisualStyleState == VisualStyleState.ClientAndNonClientAreasEnabled || Application.VisualStyleState == VisualStyleState.ClientAreaEnabled); }
		}
		#endregion
	}
}
