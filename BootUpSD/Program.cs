using System;
using System.Data;
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

/*

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
txtName.TextChanged+= (_, __) => { txtName.Invalidate(true);  };
txtName.MouseMove += (_, __) => { txtName.Invalidate(true); };
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
lstItems.TextChanged += (_, __) => { lstItems.Invalidate(true); };
lstItems.MouseMove += (_, __) => { lstItems.Invalidate(true); };

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
*/
/*
var form = new Form
{
    Text = "Rainbow DataGridView Test",
    ClientSize = new Size(800, 600),
    StartPosition = FormStartPosition.CenterScreen
};

// --- Rainbow Grid ---
var gridRainbow = new DataGridView
{
    Location = new Point(20, 20),
    Size = new Size(360, 540),
    AllowUserToAddRows = false,
    ReadOnly = true,
    BorderStyle = BorderStyle.FixedSingle,
    EnableHeadersVisualStyles = false,
    BackgroundColor = Color.FromArgb(255, 60, 65),
    GridColor = Color.Wheat
};

// Default Styles
gridRainbow.DefaultCellStyle.BackColor = Color.White;
gridRainbow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(100, 0, 0, 0); // Semi-transparent selection
gridRainbow.DefaultCellStyle.SelectionForeColor = Color.White;

// Data
DataTable dtRainbow = new DataTable();
dtRainbow.Columns.Add("R"); dtRainbow.Columns.Add("A"); dtRainbow.Columns.Add("I"); dtRainbow.Columns.Add("N");
for (int i = 0; i < 15; i++) dtRainbow.Rows.Add("Cell", "Cell", "Cell", "Cell");
gridRainbow.DataSource = dtRainbow;

// CellFormatting Event for Rainbow Colors
gridRainbow.CellFormatting += (sender, e) =>
{
    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
    int seed = (e.RowIndex * 100) + e.ColumnIndex;
    Random rnd = new Random(seed);
    e.CellStyle.BackColor = Color.FromArgb(255, rnd.Next(150, 256), rnd.Next(150, 256), rnd.Next(150, 256));
    e.CellStyle.ForeColor = Color.Black;
    e.CellStyle.SelectionBackColor = gridRainbow.DefaultCellStyle.SelectionBackColor;
};

// --- Standard Grid ---
var gridStandard = new DataGridView
{
    Location = new Point(400, 20),
    Size = new Size(380, 540),
    AllowUserToAddRows = false,
    BorderStyle = BorderStyle.FixedSingle,
    EnableHeadersVisualStyles = false,
};
gridStandard.ColumnHeadersDefaultCellStyle.BackColor = Color.Gray;
gridStandard.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;

DataTable dtStd = new DataTable();
dtStd.Columns.Add("ID"); dtStd.Columns.Add("Data");
dtStd.Rows.Add(1, "Alpha"); dtStd.Rows.Add(2, "Beta");
gridStandard.DataSource = dtStd;

// Add to form
form.Controls.Add(gridRainbow);
form.Controls.Add(gridStandard);

Application.Run(form);
*/

// ==========================================
// 2. WinForms Application with Nested Controls
// ==========================================
var form = new Form
{
    Text = "Nested Controls Test",
    ClientSize = new Size(600, 450),
    StartPosition = FormStartPosition.CenterScreen,
    BackColor = Color.FromArgb(240, 240, 245)
};

// Create a main container panel
var mainPanel = new Panel
{
    Location = new Point(20, 20),
    Size = new Size(560, 400),
    BackColor = Color.White,
    BorderStyle = BorderStyle.FixedSingle
};

// Add a nested panel inside the main panel
var nestedPanel = new Panel
{
    Location = new Point(20, 20),
    Size = new Size(200, 200),
    BackColor = Color.Blue,
    BorderStyle = BorderStyle.FixedSingle
};
mainPanel.Controls.Add(nestedPanel);

// Add a ComboBox to the nested panel
var comboBox = new ComboBox
{
    Location = new Point(20, 20),
    Size = new Size(150, 25),
    DropDownStyle = ComboBoxStyle.DropDownList
};
comboBox.Items.AddRange(new object[] { "Item 1", "Item 2", "Item 3" });
comboBox.SelectedIndex = 0;
nestedPanel.Controls.Add(comboBox);

// Add a DataGridView to the main panel
var grid = new DataGridView
{
    Location = new Point(250, 20),
    Size = new Size(290, 360),
    AllowUserToAddRows = false,
    ReadOnly = true,
    BorderStyle = BorderStyle.FixedSingle,
    EnableHeadersVisualStyles = false,
    BackgroundColor = Color.White
};

DataTable dt = new DataTable();
dt.Columns.Add("ID");
dt.Columns.Add("Name");
dt.Rows.Add(1, "Alice");
dt.Rows.Add(2, "Bob");
dt.Rows.Add(3, "Charlie");
grid.DataSource = dt;

mainPanel.Controls.Add(grid);

// Add a button to the form (outside the panel)
var button = new Button
{
    Text = "Click Me",
    Location = new Point(20, 430),
    Size = new Size(100, 30)
};
button.Click += (s, e) => MessageBox.Show("Hello!", "Test");

form.Controls.Add(mainPanel);
form.Controls.Add(button);

Application.Run(form);