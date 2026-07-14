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

using var bmp = new Bitmap(1400, 1600);
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

y += 100;

// TEST 10: Star-shaped complex region + SetClip / ExcludeClip / IntersectClip
DrawHeader("Test 10: Star region — SetClip / ExcludeClip / IntersectClip", 20, y);
y += 25;

// Build a 5-pointed star path
int starCx = 200, starCy = y + 100;
int starOuter = 100, starInner = 40;
PointF[] starPts = new PointF[10];
for (int i = 0; i < 10; i++)
{
    double angle = -Math.PI / 2 + i * Math.PI / 5;
    float r = (i % 2 == 0) ? starOuter : starInner;
    starPts[i] = new PointF(
        (float)(starCx + Math.Cos(angle) * r),
        (float)(starCy + Math.Sin(angle) * r)
    );
}
using var starPath = new GraphicsPath();
starPath.AddPolygon(starPts);
using var starRegion = new Region(starPath);

// Bounding box around the star (so we can see the "outside" zone)
var starBbox = new Rectangle(starCx - starOuter, starCy - starOuter, starOuter * 2, starOuter * 2);
g.DrawRectangle(Pens.Gray, starBbox);
g.DrawPolygon(Pens.Black, starPts);

// 10a: SetClip(starRegion) — fill is clipped to the star shape
g.SetClip(starRegion, CombineMode.Replace);
g.FillRectangle(Brushes.Cyan, starBbox);
g.DrawString("INSIDE star", new Font(FontFamily.GenericSansSerif, 10, FontStyle.Bold), Brushes.Black, starCx - 40, starCy - 6);
g.DrawString("OUTSIDE — should NOT appear", new Font(FontFamily.GenericSansSerif, 9, FontStyle.Italic), Brushes.Red, 110, 1080);
g.ResetClip();

// 10b: ExcludeClip(starRegion) — fill everywhere EXCEPT inside the star
g.ExcludeClip(starRegion);
g.FillRectangle(Brushes.Pink, starBbox);
g.DrawString("anti-clip text — fills everywhere except the star", new Font(FontFamily.GenericSansSerif, 10), Brushes.Magenta, 110, 1045);
g.ResetClip();

// 10c: SetClip(starRegion) + IntersectClip(rightHalf) — star ∩ right half
var rightHalf = new Rectangle(starCx, starCy - starOuter, starOuter, starOuter * 2);
g.DrawRectangle(Pens.Blue, rightHalf);
g.SetClip(starRegion, CombineMode.Replace);
g.IntersectClip(rightHalf);
g.FillRectangle(Brushes.AliceBlue, starBbox);
g.DrawString("star ∩ right", new Font(FontFamily.GenericSansSerif, 9, FontStyle.Bold), Brushes.Orange, starCx + 20, starCy + 25);
g.ResetClip();

y += 230;

// TEST 11: CombineMode matrix — star ⊕ ellipse in all 4 boolean clip modes
DrawHeader("Test 11: CombineMode matrix — star ⊕ ellipse (Intersect / Union / Xor / Exclude)", 20, y);
y += 25;

int cellW = 320;
int cellY = y + 30;                 // top of the cell row
int starR = 65, starIr = 26;        // star outer/inner radii
int elW = 50, elH = 150;            // ellipse size

CombineMode[] modes = {
    CombineMode.Intersect,
    CombineMode.Union,
    CombineMode.Xor,
    CombineMode.Exclude
};
// Reusing the same brushes already in the file: Cyan, Magenta, Lime, Orange
Brush[] fills = { Brushes.Cyan, Brushes.Magenta, Brushes.Pink, Brushes.Orange };
string[] labels = { "Intersect (∩)", "Union (∪)", "Xor (⊕)", "Exclude (\\)" };

for (int col = 0; col < 4; col++)
{
    int cx = 40 + col * cellW + cellW / 2;
    int cy = cellY + 100;

    // --- Build the star region (same recipe as Test 10) ---
    PointF[] pts = new PointF[10];
    for (int i = 0; i < 10; i++)
    {
        double angle = -Math.PI / 2 + i * Math.PI / 5;
        float r = (i % 2 == 0) ? starR : starIr;
        pts[i] = new PointF(
            (float)(cx + Math.Cos(angle) * r),
            (float)(cy + Math.Sin(angle) * r)
        );
    }
    using var starPath2 = new GraphicsPath();
    starPath2.AddPolygon(pts);
    using var starReg = new Region(starPath2);

    // --- Build the ellipse region via a path (works on Mono too) ---
    using var ellipsePath = new GraphicsPath();
    ellipsePath.AddEllipse(cx - elW / 2, cy - elH / 2, elW, elH);
    using var ellipseReg = new Region(ellipsePath);

    // --- Light outlines of both inputs (drawn BEFORE the clip is set) ---
    g.DrawPolygon(Pens.Gray, pts);
    g.DrawEllipse(Pens.Gray, cx - elW / 2, cy - elH / 2, elW, elH);

    // --- Apply the combine mode: clip = star ⊕ ellipse ---
    g.SetClip(starReg, CombineMode.Replace);   // current clip = star
    g.SetClip(ellipseReg, modes[col]);         // combine per mode[col]

    // --- Fill the cell bbox (only the boolean-combined pixels stay) ---
    var bbox = new Rectangle(
        cx - starR - 5, cy - starR - 5,
        (starR + 5) * 2, (starR + 5) * 2);
    g.FillRectangle(fills[col], bbox);

    g.ResetClip();

    // --- Label under the cell ---
    using var lblFont = new Font(FontFamily.GenericSansSerif, 11, FontStyle.Bold);
    g.DrawString(labels[col], lblFont, Brushes.Black, cx - 45, cy + starR + 12);
}

y += 250;

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
/*
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

Application.Run(form);*/

// === Form ===
var form = new Form
{
    AllowTransparency = true,
    //BackColor = Color.Transparent,
    BackgroundImage = Image.FromFile("328551_openclipart_transparent_cube_jarda.png"),
    BackgroundImageLayout = ImageLayout.None,
    ClientSize = new Size(1800, 950),
    BackColor = Color.FromArgb(45, 48, 45),
    Text = "Ultimate Control Test Bench",
    WindowState = FormWindowState.Maximized,
    //DoubleBuffered = true
};

// === Global Strips ===
var menuStrip1 = new MenuStrip();
var statusStrip1 = new StatusStrip();

// === Zone 1: Inputs & Basics ===
var grpInputs = new GroupBox
{
    Text = "Zone 1: Inputs & Basics",
    Location = new Point(20, 40),
    Size = new Size(400, 420),
    BackColor = Color.FromArgb(150, 60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var txtStandard = new TextBox
{
    Location = new Point(20, 30),
    Size = new Size(360, 25),
    Text = "Standard TextBox",
    ForeColor = Color.Black,
    BackColor = Color.Red
};
var txtStandard2 = new TextBox
{
    Location = new Point(200, 30),
    Size = new Size(360, 25),
    Text = "Standard TextBox",
    ForeColor = Color.Black,
    BackColor = Color.Red
};
var txtPassword = new TextBox
{
    Location = new Point(20, 70),
    Size = new Size(360, 25),
    UseSystemPasswordChar = true,
    Text = "password",
    ForeColor = Color.Black,
    //BackColor = Color.FromArgb(200, 125, 75, 175)
};
var txtMulti = new TextBox
{
    Location = new Point(20, 110),
    Size = new Size(360, 100),
    Multiline = true,
    ScrollBars = ScrollBars.Vertical,
    Text = "Multiline text box...",
    BackColor = Color.FromArgb(255, 255, 220)
};
var numericUpDown1 = new NumericUpDown
{
    Location = new Point(20, 230),
    Size = new Size(150, 25),
    Value = 50,
    BorderStyle = BorderStyle.FixedSingle
};
var trackBar1 = new TrackBar
{
    Location = new Point(20, 270),
    Size = new Size(360, 45),
    Maximum = 100,
    Value = 75
};
var comboBox1 = new ComboBox
{
    Location = new Point(20, 330),
    Size = new Size(200, 25),
    FlatStyle = FlatStyle.Flat,
    DropDownStyle = ComboBoxStyle.DropDown
};
comboBox1.Items.AddRange(new object[] { "Option A", "Option B" });
comboBox1.SelectedIndex = 0;

var dateTimePicker1 = new DateTimePicker
{
    Location = new Point(20, 375),
    Size = new Size(200, 25),
    Format = DateTimePickerFormat.Short,
    Value = DateTime.Now,
    BackColor = Color.White,
    ForeColor = Color.Black,
    CalendarTitleBackColor = Color.Green
};

grpInputs.Controls.Add(txtStandard);
grpInputs.Controls.Add(txtPassword);
grpInputs.Controls.Add(txtMulti);
grpInputs.Controls.Add(numericUpDown1);
grpInputs.Controls.Add(trackBar1);
grpInputs.Controls.Add(comboBox1);
grpInputs.Controls.Add(dateTimePicker1);

// === Zone 2: Builtin Transparency Test ===
var grpTransparency = new GroupBox
{
    Text = "Zone 2: Builtin Transparency Test",
    Location = new Point(440, 40),
    Size = new Size(450, 420),
    BackColor = Color.FromArgb(60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var picStar = new PictureBox
{
    Location = new Point(25, 30),
    Size = new Size(400, 350),
    BackColor = Color.LightGray,
    BorderStyle = BorderStyle.Fixed3D,
    SizeMode = PictureBoxSizeMode.CenterImage
};

var btnTran1 = new Button
{
    Text = "Tran Btn 1",
    Location = new Point(30, 30),
    Size = new Size(150, 40),
    BackColor = Color.Transparent,
    FlatStyle = FlatStyle.Flat,
    ForeColor = Color.Black
};
btnTran1.FlatAppearance.BorderSize = 0;

var btnTran2 = new Button
{
    Text = "Tran Btn 2",
    Location = new Point(200, 150),
    Size = new Size(160, 40),
    BackColor = Color.Transparent,
    FlatStyle = FlatStyle.Flat,
    Font = new Font("Arial", 10, FontStyle.Bold),
    ForeColor = Color.Blue
};
btnTran2.FlatAppearance.BorderSize = 0;

var btnTran3 = new Button
{
    Text = "Tran Btn 3",
    Location = new Point(30, 270),
    Size = new Size(120, 40),
    BackColor = Color.Cyan,
    FlatStyle = FlatStyle.Flat,
    ForeColor = Color.Magenta
};
btnTran3.FlatAppearance.BorderSize = 0;

picStar.Controls.Add(btnTran1);
picStar.Controls.Add(btnTran2);
picStar.Controls.Add(btnTran3);
picStar.Controls.Add(txtStandard2);

grpTransparency.Controls.Add(picStar);

// === Zone 3: Rainbow Cell Grid ===
var grpRainbowData = new GroupBox
{
    Text = "Zone 3: Rainbow Cell Grid",
    Location = new Point(910, 40),
    Size = new Size(450, 420),
    BackColor = Color.FromArgb(60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var dataGridViewRainbow = new DataGridView
{
    Location = new Point(10, 30),
    Size = new Size(430, 380),
    AllowUserToAddRows = false,
    ReadOnly = true,
    BorderStyle = BorderStyle.FixedSingle,
    EnableHeadersVisualStyles = false,
    BackgroundColor = Color.FromArgb(255, 60, 65),
    GridColor = Color.Wheat
};
dataGridViewRainbow.DefaultCellStyle.BackColor = Color.White;
dataGridViewRainbow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(100, 0, 0, 0);
dataGridViewRainbow.DefaultCellStyle.SelectionForeColor = Color.White;

DataTable dtRainbow = new DataTable();
dtRainbow.Columns.Add("R"); dtRainbow.Columns.Add("A");
dtRainbow.Columns.Add("I"); dtRainbow.Columns.Add("N");
for (int i = 0; i < 10; i++) dtRainbow.Rows.Add("Cel", "Cel", "Cel", "Cel");
dataGridViewRainbow.DataSource = dtRainbow;

dataGridViewRainbow.CellFormatting += (sender, e) =>
{
    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
    int seed = (e.RowIndex * 100) + e.ColumnIndex;
    Random rnd = new Random(seed);
    e.CellStyle.BackColor = Color.FromArgb(255, rnd.Next(150, 256), rnd.Next(150, 256), rnd.Next(150, 256));
    e.CellStyle.ForeColor = Color.Black;
    e.CellStyle.SelectionBackColor = dataGridViewRainbow.DefaultCellStyle.SelectionBackColor;
};

grpRainbowData.Controls.Add(dataGridViewRainbow);

// === Zone 4: Interactive Buttons ===
var grpButtons = new GroupBox
{
    Text = "Zone 4: Interactive Buttons",
    Location = new Point(20, 480),
    Size = new Size(400, 380),
    BackColor = Color.FromArgb(60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var btnColored1 = new Button
{
    Text = "RED ACTION",
    Location = new Point(20, 30),
    Size = new Size(150, 50),
    BackColor = Color.DarkGray,
    FlatStyle = FlatStyle.Flat,
    ForeColor = Color.White
};
btnColored1.FlatAppearance.BorderSize = 0;

var btnColored2 = new Button
{
    Text = "Blue Action",
    Location = new Point(20, 100),
    Size = new Size(150, 50),
    BackColor = Color.Fuchsia,
    FlatStyle = FlatStyle.Flat,
    ForeColor = Color.White
};
btnColored2.FlatAppearance.BorderSize = 0;

var btnColored3 = new Button
{
    Text = "Green Submit",
    Location = new Point(20, 170),
    Size = new Size(150, 50),
    BackColor = Color.Lime,
    FlatStyle = FlatStyle.Flat,
    ForeColor = Color.White
};
btnColored3.FlatAppearance.BorderSize = 0;

var checkBox1 = new CheckBox
{
    Text = "Enable Options",
    Location = new Point(200, 40),
    AutoSize = true,
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};
var radioButton1 = new RadioButton
{
    Text = "Radio Choice",
    Location = new Point(200, 80),
    AutoSize = true,
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};
var linkLabel1 = new LinkLabel
{
    Text = "Visit Example.com",
    Location = new Point(200, 120),
    AutoSize = true,
    LinkColor = Color.Cyan
};

grpButtons.Controls.Add(btnColored1);
grpButtons.Controls.Add(btnColored2);
grpButtons.Controls.Add(btnColored3);
grpButtons.Controls.Add(checkBox1);
grpButtons.Controls.Add(radioButton1);
grpButtons.Controls.Add(linkLabel1);

// === Zone 5: Hierarchies ===
var grpLists = new GroupBox
{
    Text = "Zone 5: Hierarchies",
    Location = new Point(440, 480),
    Size = new Size(450, 380),
    BackColor = Color.FromArgb(60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var treeView1 = new TreeView
{
    Location = new Point(10, 30),
    Size = new Size(200, 330),
    BackColor = Color.White,
    BorderStyle = BorderStyle.FixedSingle
};
treeView1.Nodes.Add("Root");
treeView1.Nodes[0].Nodes.Add("Child");

var listView1 = new ListView
{
    Location = new Point(220, 30),
    Size = new Size(200, 330),
    View = View.Details,
    BorderStyle = BorderStyle.FixedSingle
};
listView1.Columns.Add("Item");
listView1.Columns.Add("Info");
listView1.Items.Add("Item 1", "Info 1");

grpLists.Controls.Add(treeView1);
grpLists.Controls.Add(listView1);

// === Zone 6: Standard Data ===
var grpStandardData = new GroupBox
{
    Text = "Zone 6: Standard Data",
    Location = new Point(910, 480),
    Size = new Size(450, 380),
    BackColor = Color.FromArgb(60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var dataGridView1 = new DataGridView
{
    Location = new Point(10, 30),
    Size = new Size(410, 150),
    BorderStyle = BorderStyle.FixedSingle,
    EnableHeadersVisualStyles = false
};
dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.Gray;
dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;

DataTable dtStd = new DataTable();
dtStd.Columns.Add("ID"); dtStd.Columns.Add("Data");
dtStd.Rows.Add(1, "Alpha"); dtStd.Rows.Add(2, "Beta");
dataGridView1.DataSource = dtStd;

var richTextBox1 = new RichTextBox
{
    Location = new Point(10, 200),
    Size = new Size(250, 150),
    Text = "Rich Text Area...",
    BackColor = Color.White
};
var progressBar1 = new ProgressBar
{
    Location = new Point(280, 200),
    Size = new Size(140, 23),
    Value = 60
};

grpStandardData.Controls.Add(dataGridView1);
grpStandardData.Controls.Add(richTextBox1);
grpStandardData.Controls.Add(progressBar1);

// === Zone 7: Tab Control & Random Crap ===
var grpTabs = new GroupBox
{
    Text = "Zone 7: Tab Control & Random Crap",
    Location = new Point(1380, 40),
    Size = new Size(380, 820),
    BackColor = Color.FromArgb(60, 60, 65),
    ForeColor = Color.White,
    FlatStyle = FlatStyle.Flat
};

var tabControlRandom = new TabControl
{
    Location = new Point(10, 20),
    Size = new Size(360, 780),
    SelectedIndex = 0,
    Appearance = TabAppearance.FlatButtons
};

// --- Tab 1: Scrolls ---
var tabPageScrolls = new TabPage
{
    Text = "Scrolls",
    BackColor = Color.FromArgb(70, 70, 75)
};
var hScrollBar1 = new HScrollBar
{
    Location = new Point(20, 30),
    Size = new Size(300, 20),
    Maximum = 100,
    Value = 50
};
var vScrollBar1 = new VScrollBar
{
    Location = new Point(20, 70),
    Size = new Size(20, 200),
    Maximum = 100,
    Value = 20
};
var progressBarCrap = new ProgressBar
{
    Location = new Point(60, 70),
    Size = new Size(260, 20),
    Style = ProgressBarStyle.Marquee,
    MarqueeAnimationSpeed = 50
};
tabPageScrolls.Controls.Add(hScrollBar1);
tabPageScrolls.Controls.Add(vScrollBar1);
tabPageScrolls.Controls.Add(progressBarCrap);

// --- Tab 2: More Lists ---
var thisTabPageLists = new TabPage
{
    Text = "More Lists",
    BackColor = Color.FromArgb(70, 70, 75)
};
var listBox1 = new ListBox
{
    Location = new Point(20, 20),
    Size = new Size(150, 200),
    BorderStyle = BorderStyle.FixedSingle
};
listBox1.Items.Add("List Item 1"); listBox1.Items.Add("List Item 2");
listBox1.Items.Add("List Item 3"); listBox1.Items.Add("List Item 4");

var checkedListBox1 = new CheckedListBox
{
    Location = new Point(180, 20),
    Size = new Size(150, 200),
    BorderStyle = BorderStyle.FixedSingle
};
checkedListBox1.Items.Add("Check 1");
checkedListBox1.Items.Add("Check 2");
checkedListBox1.Items.Add("Check 3");

thisTabPageLists.Controls.Add(listBox1);
thisTabPageLists.Controls.Add(checkedListBox1);

// --- Tab 3: Weird Stuff ---
var thisTabPageWeird = new TabPage
{
    Text = "Weird Stuff",
    BackColor = Color.FromArgb(70, 70, 75)
};
var domainUpDown1 = new DomainUpDown
{
    Location = new Point(20, 30),
    Size = new Size(120, 20),
    BorderStyle = BorderStyle.FixedSingle
};
domainUpDown1.Items.Add("Item 1");
domainUpDown1.SelectedIndex = 0;

var maskedTextBox1 = new MaskedTextBox
{
    Location = new Point(20, 60),
    Size = new Size(100, 20),
    Mask = "00/00/0000"
};

var pictureBoxCrap = new PictureBox
{
    Location = new Point(20, 100),
    Size = new Size(300, 100),
    BackColor = Color.White,
    BorderStyle = BorderStyle.FixedSingle
};
var bmpCrap = new Bitmap(300, 100);
using (Graphics g_ = Graphics.FromImage(bmpCrap))
{
    g_.Clear(Color.White);
    g_.FillEllipse(Brushes.Magenta, 10, 10, 80, 80);
    g_.FillRectangle(Brushes.Orange, 100, 20, 180, 60);
}
pictureBoxCrap.Image = bmpCrap;

thisTabPageWeird.Controls.Add(domainUpDown1);
thisTabPageWeird.Controls.Add(maskedTextBox1);
thisTabPageWeird.Controls.Add(pictureBoxCrap);

tabControlRandom.Controls.Add(tabPageScrolls);
tabControlRandom.Controls.Add(thisTabPageLists);
tabControlRandom.Controls.Add(thisTabPageWeird);

grpTabs.Controls.Add(tabControlRandom);

// === Add Zones to Form ===
form.Controls.Add(grpInputs);
form.Controls.Add(grpTransparency);
form.Controls.Add(grpRainbowData);
form.Controls.Add(grpButtons);
form.Controls.Add(grpLists);
form.Controls.Add(grpStandardData);
form.Controls.Add(grpTabs);
form.Controls.Add(statusStrip1);
form.Controls.Add(menuStrip1);

// === Status & Menu Items ===
statusStrip1.Items.Add("Ready");
menuStrip1.Items.Add("File");
menuStrip1.Items.Add("Edit");
menuStrip1.Items.Add("View");

// === Final Star Image (generated) ===
picStar.Image = CreateStarBitmap(400, 350);

// === Event Hookup ===
btnTran1.Click += (_, __) => { txtMulti.Text = Logger.Log; };

// === Run ===
Application.Run(form);

// === Local helper ===
Bitmap CreateStarBitmap(int width, int height)
{
    var bmp = new Bitmap(width, height);
    using (Graphics g = Graphics.FromImage(bmp))
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var center = new PointF(width / 2f, height / 2f);
        float outerRadius = width / 2.5f;
        float innerRadius = width / 6.0f;
        int points = 5;

        var starPoints = new PointF[points * 2];
        float angle = (float)(-Math.PI / 2);
        float step = (float)(Math.PI / points);

        for (int i = 0; i < points * 2; i++)
        {
            float r = (i % 2 == 0) ? outerRadius : innerRadius;
            starPoints[i] = new PointF(
                center.X + (float)Math.Cos(angle) * r,
                center.Y + (float)Math.Sin(angle) * r
            );
            angle += step;
        }

        using (var brush = new LinearGradientBrush(
            new RectangleF(0, 0, width, height), Color.Yellow, Color.Red, LinearGradientMode.ForwardDiagonal))
        {
            g.FillPolygon(brush, starPoints);
        }

        using (var pen = new Pen(Color.White, 4))
        {
            g.DrawPolygon(pen, starPoints);
        }
    }
    return bmp;
}