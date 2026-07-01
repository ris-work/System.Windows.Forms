using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

// ==========================================
// 1. System.Drawing Test: Output drawing.png
// ==========================================
using (var bitmap = new Bitmap(400, 200))
using (var g = Graphics.FromImage(bitmap))
{
    // Clear background
    g.Clear(Color.White);

    // Draw a red rectangle border
    using var pen = new Pen(Color.Red, 3);
    g.DrawRectangle(pen, 10, 10, 380, 180);

    // Fill a semi-transparent blue ellipse (Alpha Blending)
    using var blueBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 255));
    g.FillEllipse(blueBrush, 50, 40, 120, 120);

    // Apply Transforms: Translate, Rotate, and Scale
    g.TranslateTransform(200, 100);
    g.RotateTransform(25);
    g.ScaleTransform(1.2f, 1.2f);

    // Draw a green dashed diagonal line
    using var greenPen = new Pen(Color.Green, 2) { DashStyle = DashStyle.Dash };
    g.DrawLine(greenPen, 0, 0, 120, 60);

    // Draw some text
    using var font = new Font("Arial", 14, FontStyle.Bold);
    using var textBrush = new SolidBrush(Color.Purple);
    g.DrawString("SkiaSharp Drawing!", font, textBrush, 0, 0);

    // Reset transform for any further drawing
    g.ResetTransform();

    // Save the output as a PNG
    bitmap.Save("drawing.png", ImageFormat.Png);
    Console.WriteLine("Successfully wrote drawing.png");
}

// ==========================================
// 2. WinForms Application Initialization
// ==========================================
var form = new Form
{
    Text = "SkiaSharp WinForms Test",
    ClientSize = new Size(400, 300),
    StartPosition = FormStartPosition.CenterScreen,
    //AutoScaleBaseSize = new System.Drawing.Size(5, 13)
};

var button = new Button
{
    Text = "Click Me",
    Location = new Point(150, 130),
    Size = new Size(100, 40)
};

button.Click += (sender, e) =>
{
    MessageBox.Show("Hello from SkiaSharp WinForms!", "Greeting");
};

form.Controls.Add(button);
//Application.EnableVisualStyles();
Application.Run(form);