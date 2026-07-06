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
/*using (var bitmap = new Bitmap(800, 600))
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
    
    
}*/

using var bmp = new Bitmap(1400, 900);
using var g = Graphics.FromImage(bmp);
g.Clear(Color.White);

void DrawHeader(string text, int x, int y)
{
    using var f = new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold);
    g.DrawString(text, f, Brushes.Blue, x, y);
}

int y = 20;

// TEST 1: Basic SetClip + DrawString
DrawHeader("Test 1: Basic SetClip(rect) + DrawString", 20, y);
y += 25;
var clip1 = new Rectangle(30, y, 200, 50);
g.DrawRectangle(Pens.Black, clip1);
g.SetClip(clip1);
g.FillRectangle(Brushes.Blue, clip1);
g.DrawString("INSIDE clip", new Font(FontFamily.GenericSansSerif, 12), Brushes.Black, clip1);
g.FillRectangle(Brushes.Red, new Rectangle(300, y, 100, 30));
g.DrawString("OUTSIDE — should NOT appear", new Font(FontFamily.GenericSansSerif, 12), Brushes.Red, 300, y);
g.ResetClip();
y += 70;

// TEST 2: g.Clip = region (set style)
DrawHeader("Test 2: g.Clip = region (set-style)", 20, y);
y += 25;
var clip2 = new Rectangle(30, y, 200, 50);
g.DrawRectangle(Pens.Black, clip2);
g.Clip = new Region(clip2);
g.FillRectangle(Brushes.Green, clip2);
g.DrawString("INSIDE clip (set)", new Font(FontFamily.GenericSansSerif, 12), Brushes.Black, clip2);
g.FillRectangle(Brushes.Red, new Rectangle(300, y, 100, 30));
g.DrawString("OUTSIDE — should NOT appear", new Font(FontFamily.GenericSansSerif, 12), Brushes.Red, 300, y);
g.ResetClip();
y += 70;

// TEST 3: Nested SetClip (intersective test)
DrawHeader("Test 3: Nested SetClip (intersective test)", 20, y);
y += 25;
var A = new Rectangle(30, y, 250, 50);
var B = new Rectangle(150, y + 20, 250, 50);
g.DrawRectangle(Pens.Blue, A);
g.DrawRectangle(Pens.Red, B);
g.SetClip(A);
g.SetClip(B);
g.FillRectangle(Brushes.Yellow, new Rectangle(150, y + 20, 200, 30));
g.DrawString("in A∩B (visible)", new Font(FontFamily.GenericSansSerif, 12), Brushes.Black, 155, y + 25);
g.ResetClip();
y += 90;

// TEST 4: After ResetClip, clip should be back to original
DrawHeader("Test 4: ResetClip → original state", 20, y);
y += 25;
var originalTest = new Rectangle(30, y, 600, 60);
g.SetClip(originalTest);
g.FillRectangle(Brushes.Pink, originalTest);
g.ResetClip();
g.FillRectangle(Brushes.Green, new Rectangle(20, y + 30, 1300, 25));
g.DrawString("if this whole line is GREEN, ResetClip worked", new Font(FontFamily.GenericSansSerif, 12), Brushes.Black, 30, y + 33);
y += 90;

// TEST 5: Multiline DrawString
DrawHeader("Test 5: Multiline DrawString (mirrors Document.Draw per-line)", 20, y);
y += 25;
var text = "Line 1 of text\nLine 2 of text\nLine 3 of text\nLine 4";
var multiRect = new Rectangle(30, y, 300, 100);
g.DrawRectangle(Pens.Black, multiRect);
g.SetClip(multiRect);
g.FillRectangle(Brushes.Yellow, multiRect);
var fmt = new StringFormat();
fmt.LineAlignment = StringAlignment.Near;
fmt.Alignment = StringAlignment.Near;
var font = new Font(FontFamily.GenericSansSerif, 11);
var lines = text.Split('\n');
for (int i = 0; i < lines.Length; i++)
{
    int lineY = multiRect.Y + i * 14;
    g.DrawString(lines[i], font, Brushes.Black, multiRect.X + 2, lineY);
}
g.ResetClip();
y += 120;

// TEST 6: Line bg + line text (Document.Draw pattern)
DrawHeader("Test 6: Line bg + line text (Document.Draw pattern)", 20, y);
y += 25;
for (int i = 0; i < 3; i++)
{
    int lineY = y + i * 28;
    var lineRect = new Rectangle(30, lineY, 350, 24);
    g.DrawRectangle(Pens.Gray, lineRect);
    g.FillRectangle(i == 1 ? Brushes.Blue : i == 2 ? Brushes.Green : Brushes.Yellow, lineRect);
    var lineText = $"Line {i}: The quick brown fox jumps over the lazy dog The quick brown fox jumps over the lazy dog";
    g.DrawString(lineText, font, Brushes.Black, lineRect, fmt);
}
y += 100;

// TEST 7: Password text rendering
DrawHeader("Test 7: Password text rendering (*****)", 20, y);
y += 25;
string passwordChar = "*";
string passwordText = new string(passwordChar[0], "SecretPassword123".Length);
var pwdRect = new Rectangle(30, y, 300, 30);
g.DrawRectangle(Pens.Black, pwdRect);
g.FillRectangle(Brushes.White, pwdRect);
g.DrawString(passwordText, font, Brushes.Black, pwdRect, fmt);
y += 50;

// TEST 8: Non-multiline selection highlight
DrawHeader("Test 8: Non-multiline selection highlight + selected text", 20, y);
y += 25;
var lineRect2 = new Rectangle(30, y, 400, 25);
g.DrawRectangle(Pens.Gray, lineRect2);
var selRect = new Rectangle(120, y, 150, 25);
g.SetClip(selRect);
g.FillRectangle(SystemBrushes.Highlight, selRect);
g.DrawString("highlighted", new Font(FontFamily.GenericSansSerif, 12), SystemBrushes.HighlightText, selRect, fmt);
g.ResetClip();
g.DrawString("before ", font, Brushes.Black, 30, y);
g.DrawString("after", font, Brushes.Black, 280, y);
y += 50;

// TEST 9: Transform + SetClip + DrawString
DrawHeader("Test 9: Transform + SetClip + DrawString", 20, y);
y += 25;
g.TranslateTransform(200, y + 30);
g.RotateTransform(20);
var tRect = new Rectangle(-100, -30, 200, 30);
g.DrawRectangle(Pens.Black, tRect);
g.SetClip(tRect);
g.FillRectangle(Brushes.Magenta, tRect);
g.DrawString("rotated & clipped", font, Brushes.Black, tRect, fmt);
g.ResetClip();
g.ResetTransform();

bmp.Save("clipping_test.png", System.Drawing.Imaging.ImageFormat.Png);
Console.WriteLine("Wrote clipping_test.png");
Console.WriteLine("Open it. Where the test rectangles have a green strip but no 'should NOT appear' red, the Graphics class is correct.");
Console.WriteLine("Where the test rectangles are missing the yellow highlight on Test 8, the text is fine and selection works.");
Console.WriteLine("Where the test rectangles on Test 5/6/7 show NO text, Document.Draw's per-line DrawString is the issue.");

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
    ClientSize = new Size(960, 720),
    StartPosition = FormStartPosition.CenterScreen,
    BackColor = Color.FromArgb(240, 240, 245)
};

// ===== Outer toolbar strip =====
var toolStrip = new ToolStrip { Location = new Point(0, 0), Size = new Size(960, 25) };
toolStrip.Items.Add(new ToolStripButton("New"));
toolStrip.Items.Add(new ToolStripButton("Open"));
toolStrip.Items.Add(new ToolStripButton("Save"));
toolStrip.Items.Add(new ToolStripSeparator());
toolStrip.Items.Add(new ToolStripButton("Cut"));
toolStrip.Items.Add(new ToolStripButton("Copy"));
toolStrip.Items.Add(new ToolStripButton("Paste"));
form.Controls.Add(toolStrip);

// ===== TabControl with 4 pages =====
var tabs = new TabControl { Location = new Point(20, 40), Size = new Size(920, 500) };
form.Controls.Add(tabs);

var tabBasic = new TabPage("Basic");
var tabLists = new TabPage("Lists & Trees");
var tabDate = new TabPage("Date & Time");
var tabOrig = new TabPage("Original Test");
tabs.TabPages.AddRange(new[] { tabBasic, tabLists, tabDate, tabOrig });

// --- Tab: Basic controls ---
tabBasic.Controls.Add(new Label { Text = "Name:", Location = new Point(20, 22), AutoSize = true });
tabBasic.Controls.Add(new TextBox { Location = new Point(80, 19), Width = 220, Text = "Sample input" });

tabBasic.Controls.Add(new Label { Text = "Age:", Location = new Point(20, 52), AutoSize = true });
tabBasic.Controls.Add(new NumericUpDown { Location = new Point(80, 49), Width = 80, Minimum = 0, Maximum = 120, Value = 30 });

tabBasic.Controls.Add(new Label { Text = "Password:", Location = new Point(20, 82), AutoSize = true });
tabBasic.Controls.Add(new TextBox { Location = new Point(80, 79), Width = 220, UseSystemPasswordChar = true, Text = "secret" });

tabBasic.Controls.Add(new CheckBox { Text = "Subscribe to newsletter", Location = new Point(20, 115), AutoSize = true, Checked = true });
tabBasic.Controls.Add(new CheckBox { Text = "Enable notifications", Location = new Point(20, 140), AutoSize = true });
tabBasic.Controls.Add(new CheckBox { Text = "Three-state option", Location = new Point(20, 165), AutoSize = true, ThreeState = true, CheckState = CheckState.Indeterminate });

tabBasic.Controls.Add(new RadioButton { Text = "Option A", Location = new Point(320, 115), AutoSize = true, Checked = true });
tabBasic.Controls.Add(new RadioButton { Text = "Option B", Location = new Point(420, 115), AutoSize = true });
tabBasic.Controls.Add(new RadioButton { Text = "Option C", Location = new Point(320, 140), AutoSize = true });

tabBasic.Controls.Add(new Button { Text = "OK", Location = new Point(20, 200), Size = new Size(90, 30) });
tabBasic.Controls.Add(new Button { Text = "Cancel", Location = new Point(120, 200), Size = new Size(90, 30) });
tabBasic.Controls.Add(new Button { Text = "Apply", Location = new Point(220, 200), Size = new Size(90, 30) });

tabBasic.Controls.Add(new ProgressBar { Location = new Point(20, 245), Width = 400, Height = 20, Value = 50 });
tabBasic.Controls.Add(new Label { Text = "Progress (50%)", Location = new Point(425, 248), AutoSize = true });

tabBasic.Controls.Add(new TrackBar { Location = new Point(20, 280), Width = 300, Minimum = 0, Maximum = 100, Value = 30, TickFrequency = 10 });
tabBasic.Controls.Add(new Label { Text = "Volume", Location = new Point(330, 285), AutoSize = true });

tabBasic.Controls.Add(new HScrollBar { Location = new Point(20, 320), Width = 300, Minimum = 0, Maximum = 100, Value = 20 });
tabBasic.Controls.Add(new VScrollBar { Location = new Point(340, 280), Height = 60, Minimum = 0, Maximum = 100, Value = 40 });
tabBasic.Controls.Add(new TextBox
{
    Location = new Point(20, 360),
    Size = new Size(700, 80),
    Font = new Font(SystemFonts.MessageBoxFont.Name, 12),
    Text = "Big textbox — if cursor is full-height here, the bug is size-related",
    AutoSize = false,
    Multiline = true
});

// --- Tab: Lists & Trees ---
var listView = new ListView
{
    Location = new Point(20, 20),
    Size = new Size(300, 230),
    View = View.Details,
    FullRowSelect = true,
    GridLines = true,
    MultiSelect = false
};
listView.Columns.Add("Name", 150);
listView.Columns.Add("Type", 120);
listView.Items.Add(new ListViewItem(new[] { "Apple", "Fruit" }));
listView.Items.Add(new ListViewItem(new[] { "Carrot", "Vegetable" }));
listView.Items.Add(new ListViewItem(new[] { "Salmon", "Fish" }));
listView.Items.Add(new ListViewItem(new[] { "Beef", "Meat" }));
listView.Items[0].Selected = true;
tabLists.Controls.Add(listView);

var treeView = new TreeView
{
    Location = new Point(340, 20),
    Size = new Size(240, 230)
};
var rootNode = treeView.Nodes.Add("Documents");
rootNode.Nodes.Add("Reports").Nodes.Add("2024").Nodes.Add("Q1.pdf");
rootNode.Nodes.Add("Reports").Nodes.Add("2024").Nodes.Add("Q2.pdf");
rootNode.Nodes.Add("Photos");
rootNode.Nodes.Add("Music");
rootNode.Expand();
tabLists.Controls.Add(treeView);

var checkedListBox = new CheckedListBox
{
    Location = new Point(600, 20),
    Size = new Size(180, 230),
    CheckOnClick = true
};
checkedListBox.Items.AddRange(new object[] { "Red", "Green", "Blue", "Yellow", "Cyan", "Magenta" });
checkedListBox.SetItemChecked(0, true);
checkedListBox.SetItemChecked(2, true);
tabLists.Controls.Add(checkedListBox);

var listBox = new ListBox
{
    Location = new Point(20, 270),
    Size = new Size(300, 150),
    SelectionMode = SelectionMode.MultiExtended
};
listBox.Items.AddRange(new object[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" });
listBox.SetSelected(0, true);
listBox.SetSelected(2, true);
tabLists.Controls.Add(listBox);

// --- Tab: Date & Time ---
tabDate.Controls.Add(new Label { Text = "Date of birth:", Location = new Point(20, 22), AutoSize = true });
tabDate.Controls.Add(new DateTimePicker { Location = new Point(130, 19), Width = 220 });

tabDate.Controls.Add(new Label { Text = "Time:", Location = new Point(20, 52), AutoSize = true });
tabDate.Controls.Add(new DateTimePicker { Location = new Point(130, 49), Width = 220, Format = DateTimePickerFormat.Time, ShowUpDown = true });

tabDate.Controls.Add(new Label { Text = "Custom fmt:", Location = new Point(20, 82), AutoSize = true });
tabDate.Controls.Add(new DateTimePicker { Location = new Point(130, 79), Width = 220, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss" });

tabDate.Controls.Add(new MonthCalendar { Location = new Point(20, 120) });

tabDate.Controls.Add(new DateTimePicker { Location = new Point(280, 120), Width = 220, ShowCheckBox = true, Checked = false });

// --- Tab: Original test (your exact setup, kept verbatim) ---
var mainPanel = new Panel
{
    Location = new Point(20, 20),
    Size = new Size(560, 400),
    BackColor = Color.White,
    BorderStyle = BorderStyle.FixedSingle
};
tabOrig.Controls.Add(mainPanel);

var nestedPanel = new Panel
{
    Location = new Point(20, 20),
    Size = new Size(200, 200),
    BackColor = Color.Blue,
    BorderStyle = BorderStyle.FixedSingle
};
mainPanel.Controls.Add(nestedPanel);

var comboBox = new ComboBox
{
    Location = new Point(20, 20),
    Size = new Size(150, 25),
    DropDownStyle = ComboBoxStyle.DropDownList
};
comboBox.Items.AddRange(new object[] {
    "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3",
    "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3",
    "Item 1", "Item 2", "Item 3", "Item 1", "Item 2", "Item 3",
    "Item 1", "Item 2", "Item 3"
});
comboBox.SelectedIndex = 0;
nestedPanel.Controls.Add(comboBox);

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

// ===== Status bar (bottom) =====
var statusBar = new StatusStrip { Location = new Point(0, 698), Size = new Size(960, 22) };
statusBar.Items.Add(new ToolStripStatusLabel("Ready"));
statusBar.Items.Add(new ToolStripStatusLabel("   |   ") { Spring = true });
statusBar.Items.Add(new ToolStripStatusLabel("Items: 0"));
form.Controls.Add(statusBar);

// ===== Click Me button outside the tab (matches your original) =====
var button = new Button { Text = "Click Me", Location = new Point(20, 555), Size = new Size(100, 30) };
form.Controls.Add(button);

var comboBox2 = new ComboBox
{
    Location = new Point(140, 555),
    Size = new Size(150, 25),
    DropDownStyle = ComboBoxStyle.DropDownList
};
comboBox2.Items.AddRange(new object[] { "Apple", "Banana", "Cherry", "Date", "Elderberry" });
comboBox2.SelectedIndex = 0;
form.Controls.Add(comboBox2);

var datePicker = new DateTimePicker { Location = new Point(310, 555), Width = 200 };
form.Controls.Add(datePicker);

Application.Run(form);