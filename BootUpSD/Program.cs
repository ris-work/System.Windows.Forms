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
// 2. WinForms Application with Multiple Controls
// ==========================================
var form = new Form
{
    Text = "SkiaSharp WinForms Test",
    ClientSize = new Size(600, 450),
    StartPosition = FormStartPosition.CenterScreen,
    BackColor = Color.FromArgb(240, 240, 245)
};

// Create a DataGridView with data FIRST, then assign name
var gridData = new List<Person>
{
    new Person { Id = 1, Name = "Alice", Age = 30, Email = "alice@test.com" },
    new Person { Id = 2, Name = "Bob", Age = 25, Email = "bob@test.com" },
    new Person { Id = 3, Name = "Charlie", Age = 35, Email = "charlie@test.com" }
};

var grid = new DataGridView
{
    Location = new Point(20, 180),
    Size = new Size(560, 200),
    AllowUserToAddRows = false,
    ReadOnly = true,
    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    AutoGenerateColumns = true,
    DataSource = gridData
};
grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", DataPropertyName = "Id", Width = 50 });
grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", DataPropertyName = "Name", Width = 120 });
grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Age", DataPropertyName = "Age", Width = 50 });
grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Email", DataPropertyName = "Email", Width = 180 });
// Name assigned AFTER data binding to avoid Mono bug
grid.Name = "dataGridView1";

// Add a variety of controls
var lblTitle = new Label { Text = "SkiaSharp WinForms Demo", Location = new Point(20, 15), Size = new Size(300, 25), Font = new Font("Segoe UI", 14, FontStyle.Bold) };
var txtName = new TextBox { Location = new Point(120, 55), Size = new Size(200, 25), Text = "Type here..." };
var lblName = new Label { Text = "Name:", Location = new Point(20, 58), Size = new Size(80, 20) };
var btnClick = new Button { Text = "Click Me", Location = new Point(340, 53), Size = new Size(100, 30) };
var cboItems = new ComboBox { Location = new Point(120, 95), Size = new Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
cboItems.Items.AddRange(new object[] { "Item 1", "Item 2", "Item 3" });
cboItems.SelectedIndex = 0;
var lblCombo = new Label { Text = "Select:", Location = new Point(20, 98), Size = new Size(80, 20) };
var chkOption = new CheckBox { Text = "Enable option", Location = new Point(340, 95), Size = new Size(150, 25), Checked = true };
var rdo1 = new RadioButton { Text = "Option A", Location = new Point(20, 130), Size = new Size(100, 25), Checked = true };
var rdo2 = new RadioButton { Text = "Option B", Location = new Point(130, 130), Size = new Size(100, 25) };
var progressBar = new ProgressBar { Location = new Point(260, 130), Size = new Size(180, 20), Value = 65 };
var lstItems = new ListBox { Location = new Point(460, 53), Size = new Size(120, 70) };
lstItems.Items.AddRange(new object[] { "Red", "Green", "Blue", "Yellow" });
lstItems.SelectedIndex = 0;

btnClick.Click += (s, e) => MessageBox.Show($"Hello {txtName.Text}!\nSelected: {cboItems.SelectedItem}\nOption enabled: {chkOption.Checked}", "Test");

form.Controls.AddRange(new Control[] { lblTitle, lblName, txtName, btnClick, lblCombo, cboItems, chkOption, rdo1, rdo2, progressBar, lstItems, grid });

Application.Run(form);

public class Person
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    public string Email { get; set; }
}