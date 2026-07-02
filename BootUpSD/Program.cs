using System;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

// ==========================================
// 1. System.Drawing Test: Output drawing.png
// ==========================================
using (var bitmap = new Bitmap(800, 600))
using (var g = Graphics.FromImage(bitmap))
{
    // Clear background
    g.Clear(Color.White);

    // --- Test 1: Simple DrawImage at point ---
    using (var src1 = new Bitmap(100, 100))
    using (var gSrc1 = Graphics.FromImage(src1))
    {
        gSrc1.Clear(Color.Red);
        gSrc1.FillRectangle(Brushes.Blue, 10, 10, 80, 80);
        gSrc1.DrawString("SRC1", new Font("Arial", 12), Brushes.White, 10, 40);
        g.DrawImage(src1, 10, 10); // Top-left

        // --- Test 2: Stretched DrawImage ---
        g.DrawImage(src1, new Rectangle(120, 10, 200, 100)); // Stretched

        // --- Test 3: DrawImage with Source/Dest Rects (Sub-rectangles) ---
        // Draw only the 80x80 blue rect from src1 to a 150x150 area
        g.DrawImage(src1, new Rectangle(330, 10, 150, 150), 10, 10, 80, 80, GraphicsUnit.Pixel);

        // --- Test 4: Nested Contexts (Draw a bitmap onto another, then draw that result) ---
        using (var nested = new Bitmap(200, 200))
        using (var gNested = Graphics.FromImage(nested))
        {
            gNested.Clear(Color.Yellow);
            gNested.DrawImage(src1, 0, 0); // Draw src1 onto nested
            gNested.DrawImage(src1, 100, 100); // Draw src1 again offset
            g.DrawImage(nested, 10, 120); // Draw nested result to main

            // --- Test 5: Transforms with DrawImage ---
            g.TranslateTransform(400, 300);
            g.RotateTransform(15);
            g.ScaleTransform(1.5f, 1.5f);

            // Draw src1 transformed
            g.DrawImage(src1, 0, 0);
            g.DrawRectangle(Pens.Black, 0, 0, 100, 100); // Outline to verify bounds

            g.ResetTransform();

            // Save the output as a PNG
            bitmap.Save("drawing.png", System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("Successfully wrote drawing.png");
        }
    }
    
    
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
comboBox.Items.AddRange(new object[] { "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3" });
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
    BackgroundColor = Color.White,
    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
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
//button.Click += (s, e) => { MessageBox.Show("Hello!", "Test"); form.Invalidate(true); };

form.Controls.Add(mainPanel);
form.Controls.Add(button);
//form.MouseMove += (_, __) => { form.Invalidate(true); };
//comboBox.MouseMove += (_, __) => { form.Invalidate(true); };

Application.Run(form);