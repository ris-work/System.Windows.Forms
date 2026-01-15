using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Data;


namespace SimpleTest
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            //Application.EnableVisualStyles();
            this.components = new System.ComponentModel.Container();

            // Form Setup
            this.AutoScaleMode = AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1800, 950);
            this.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.Text = "Ultimate Control Test Bench";
            this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;

            // --- Global Controls ---
            this.menuStrip1 = new MenuStrip();
            this.statusStrip1 = new StatusStrip();
            this.toolTip1 = new ToolTip(this.components);
            this.contextMenuStrip1 = new ContextMenuStrip(this.components);

            // --- Main Containers (Layout Zones) ---
            // Y positions shifted to 40 to account for MenuStrip

            // Zone 1: Top Left - Inputs
            this.grpInputs = new GroupBox();
            this.grpInputs.Text = "Zone 1: Inputs & Basics";
            this.grpInputs.Location = new Point(20, 40);
            this.grpInputs.Size = new Size(400, 420);
            this.grpInputs.BackColor = Color.FromArgb(60, 60, 65);
            this.grpInputs.ForeColor = Color.White;
            this.grpInputs.FlatStyle = FlatStyle.Flat; // GroupBox FlatStyle (WinForms 2.0+)

            // Zone 2: Top Center - Transparency
            this.grpTransparency = new GroupBox();
            this.grpTransparency.Text = "Zone 2: Builtin Transparency Test";
            this.grpTransparency.Location = new Point(440, 40);
            this.grpTransparency.Size = new Size(450, 420);
            this.grpTransparency.BackColor = Color.FromArgb(60, 60, 65);
            this.grpTransparency.ForeColor = Color.White;
            this.grpTransparency.FlatStyle = FlatStyle.Flat;

            // Zone 3: Top Right - Rainbow DataGridView
            this.grpRainbowData = new GroupBox();
            this.grpRainbowData.Text = "Zone 3: Rainbow Cell Grid";
            this.grpRainbowData.Location = new Point(910, 40);
            this.grpRainbowData.Size = new Size(450, 420);
            this.grpRainbowData.BackColor = Color.FromArgb(60, 60, 65);
            this.grpRainbowData.ForeColor = Color.White;
            this.grpRainbowData.FlatStyle = FlatStyle.Flat;

            // Zone 4: Bottom Left - Buttons
            this.grpButtons = new GroupBox();
            this.grpButtons.Text = "Zone 4: Interactive Buttons";
            this.grpButtons.Location = new Point(20, 480);
            this.grpButtons.Size = new Size(400, 380);
            this.grpButtons.BackColor = Color.FromArgb(60, 60, 65);
            this.grpButtons.ForeColor = Color.White;
            this.grpButtons.FlatStyle = FlatStyle.Flat;

            // Zone 5: Bottom Center - Lists
            this.grpLists = new GroupBox();
            this.grpLists.Text = "Zone 5: Hierarchies";
            this.grpLists.Location = new Point(440, 480);
            this.grpLists.Size = new Size(450, 380);
            this.grpLists.BackColor = Color.FromArgb(60, 60, 65);
            this.grpLists.ForeColor = Color.White;
            this.grpLists.FlatStyle = FlatStyle.Flat;

            // Zone 6: Bottom Right - Standard Data
            this.grpStandardData = new GroupBox();
            this.grpStandardData.Text = "Zone 6: Standard Data";
            this.grpStandardData.Location = new Point(910, 480);
            this.grpStandardData.Size = new Size(450, 380);
            this.grpStandardData.BackColor = Color.FromArgb(60, 60, 65);
            this.grpStandardData.ForeColor = Color.White;
            this.grpStandardData.FlatStyle = FlatStyle.Flat;

            // Zone 7: Far Right - Tab Control
            this.grpTabs = new GroupBox();
            this.grpTabs.Text = "Zone 7: Tab Control & Random Crap";
            this.grpTabs.Location = new Point(1380, 40);
            this.grpTabs.Size = new Size(380, 820);
            this.grpTabs.BackColor = Color.FromArgb(60, 60, 65);
            this.grpTabs.ForeColor = Color.White;
            this.grpTabs.FlatStyle = FlatStyle.Flat;

            // --- Controls Initialization ---

            // Zone 1: Inputs
            this.txtStandard = new TextBox();
            this.txtStandard2 = new TextBox();
            this.txtPassword = new TextBox();
            this.txtMulti = new TextBox();
            this.numericUpDown1 = new NumericUpDown();
            this.trackBar1 = new TrackBar();
            this.comboBox1 = new ComboBox();
            this.dateTimePicker1 = new DateTimePicker(); // Added

            // Zone 2: Transparency (Builtin Buttons)
            this.picStar = new PictureBox();
            this.btnTran1 = new System.Windows.Forms.Button();
            this.btnTran2 = new System.Windows.Forms.Button();
            this.btnTran3 = new System.Windows.Forms.Button();

            // Zone 3: Rainbow Grid
            this.dataGridViewRainbow = new DataGridView();

            // Zone 4: Buttons
            this.btnColored1 = new Button();
            this.btnColored2 = new Button();
            this.btnColored3 = new Button();
            this.checkBox1 = new CheckBox();
            this.radioButton1 = new RadioButton();
            this.linkLabel1 = new LinkLabel();

            // Zone 5: Lists
            this.treeView1 = new TreeView();
            this.listView1 = new ListView();

            // Zone 6: Standard Data
            this.dataGridView1 = new DataGridView();
            this.richTextBox1 = new RichTextBox();
            this.progressBar1 = new ProgressBar();

            // Zone 7: TabPages
            this.tabControlRandom = new TabControl();
            this.tabPageScrolls = new TabPage();
            thisTabPageLists = new TabPage();
            thisTabPageWeird = new TabPage();

            // Crap for Tab 1
            this.hScrollBar1 = new HScrollBar();
            this.vScrollBar1 = new VScrollBar();
            this.progressBarCrap = new ProgressBar();

            // Crap for Tab 2
            this.listBox1 = new ListBox();
            this.checkedListBox1 = new CheckedListBox();

            // Crap for Tab 3
            this.domainUpDown1 = new DomainUpDown();
            this.maskedTextBox1 = new MaskedTextBox();
            this.pictureBoxCrap = new PictureBox();

            // Begin Suspend Layout
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRainbow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCrap)).BeginInit();
            this.grpInputs.SuspendLayout();
            this.grpTransparency.SuspendLayout();
            this.grpRainbowData.SuspendLayout();
            this.grpButtons.SuspendLayout();
            this.grpLists.SuspendLayout();
            this.grpStandardData.SuspendLayout();
            this.grpTabs.SuspendLayout();
            this.tabControlRandom.SuspendLayout();
            this.tabPageScrolls.SuspendLayout();
            thisTabPageLists.SuspendLayout();
            thisTabPageWeird.SuspendLayout();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();

            // ==========================================
            // CONFIGURING CONTROLS
            // ==========================================

            // --- Zone 1: Inputs ---
            txtStandard.Location = new Point(20, 30); txtStandard.Size = new Size(360, 25);
            txtStandard.Text = "Standard TextBox";
            txtStandard.ForeColor = Color.Black; txtStandard.BackColor = Color.Red;
            //txtStandard.BorderStyle = BorderStyle.FixedSingle;
            txtStandard2.Location = new Point(200, 30); txtStandard.Size = new Size(360, 25);
            txtStandard2.Text = "Standard TextBox";
            txtStandard2.ForeColor = Color.Black; txtStandard2.BackColor = Color.Red;

            txtPassword.Location = new Point(20, 70); txtPassword.Size = new Size(360, 25);
            txtPassword.UseSystemPasswordChar = true; txtPassword.Text = "password";
            txtPassword.ForeColor = Color.Black; txtPassword.BackColor = Color.Violet;
            //txtPassword.BorderStyle = BorderStyle.FixedSingle;

            txtMulti.Location = new Point(20, 110); txtMulti.Size = new Size(360, 100);
            txtMulti.Multiline = true; txtMulti.ScrollBars = ScrollBars.Vertical;
            txtMulti.Text = "Multiline text box...";
            txtMulti.BackColor = Color.FromArgb(255, 255, 220);
            //txtMulti.BorderStyle = BorderStyle.FixedSingle;

            numericUpDown1.Location = new Point(20, 230); numericUpDown1.Size = new Size(150, 25);
            numericUpDown1.Value = 50;
            numericUpDown1.BorderStyle = BorderStyle.FixedSingle;

            trackBar1.Location = new Point(20, 270); trackBar1.Size = new Size(360, 45);
            trackBar1.Maximum = 100; trackBar1.Value = 75;

            comboBox1.Location = new Point(20, 330); comboBox1.Size = new Size(200, 25);
            comboBox1.Items.AddRange(new object[] { "Option A", "Option B" });
            comboBox1.SelectedIndex = 0;
            comboBox1.FlatStyle = FlatStyle.Flat;
            comboBox1.DropDownStyle = ComboBoxStyle.DropDown;

            // New DateTimePicker Config
            dateTimePicker1.Location = new Point(20, 375); dateTimePicker1.Size = new Size(200, 25);
            dateTimePicker1.Format = DateTimePickerFormat.Short;
            dateTimePicker1.Value = DateTime.Now;
            dateTimePicker1.BackColor = Color.White;
            dateTimePicker1.ForeColor = Color.Black;
            //dateTimePicker1.CalendarForeColor = Color.Black;
            dateTimePicker1.CalendarTitleBackColor = Color.PaleGoldenrod;
            
            //dateTimePicker1.BorderStyle = BorderStyle.FixedSingle;

            grpInputs.Controls.Add(txtStandard);
            grpInputs.Controls.Add(txtPassword);
            grpInputs.Controls.Add(txtMulti);
            grpInputs.Controls.Add(numericUpDown1);
            grpInputs.Controls.Add(trackBar1);
            grpInputs.Controls.Add(comboBox1);
            grpInputs.Controls.Add(dateTimePicker1); // Added to group

            // --- Zone 2: Transparency (Builtin Buttons - FLAT) ---
            picStar.Location = new Point(25, 30); picStar.Size = new Size(400, 350);
            picStar.BackColor = Color.LightGray;
            picStar.BorderStyle = BorderStyle.Fixed3D;
            picStar.SizeMode = PictureBoxSizeMode.CenterImage;

            // FlatStyle = Flat enables the OS Rounded Corners if the theme supports it.
            btnTran1.Text = "Tran Btn 1"; btnTran1.Location = new Point(30, 30);
            btnTran1.Size = new Size(150, 40);
            btnTran1.BackColor = Color.Transparent;
            btnTran1.FlatStyle = FlatStyle.Flat; // Requested
            btnTran1.ForeColor = Color.Black;
            btnTran1.FlatAppearance.BorderSize = 0; // Clean look
            

            btnTran2.Text = "Tran Btn 2"; btnTran2.Location = new Point(200, 150);
            btnTran2.Size = new Size(160, 40);
            btnTran2.BackColor = Color.Transparent;
            btnTran2.FlatStyle = FlatStyle.Flat; // Requested
            btnTran2.Font = new Font("Arial", 10, FontStyle.Bold);
            btnTran2.ForeColor = Color.Blue;
            btnTran2.FlatAppearance.BorderSize = 0;

            btnTran3.Text = "Tran Btn 3"; btnTran3.Location = new Point(30, 270);
            btnTran3.Size = new Size(120, 40);
            btnTran3.BackColor = Color.DarkCyan;
            btnTran3.FlatStyle = FlatStyle.Flat; // Requested
            btnTran3.ForeColor = Color.DarkGreen;
            btnTran3.FlatAppearance.BorderSize = 0;

            picStar.Controls.Add(btnTran1);
            picStar.Controls.Add(btnTran2);
            picStar.Controls.Add(btnTran3);
            picStar.Controls.Add(txtStandard2);

            grpTransparency.Controls.Add(picStar);


            // --- Zone 3: Rainbow DataGrid ---
            dataGridViewRainbow.Location = new Point(10, 30);
            dataGridViewRainbow.Size = new Size(430, 380);
            dataGridViewRainbow.AllowUserToAddRows = false;
            dataGridViewRainbow.ReadOnly = true;
            dataGridViewRainbow.BorderStyle = BorderStyle.FixedSingle;

            // --- VISUAL STYLES ---
            dataGridViewRainbow.EnableHeadersVisualStyles = false;
            dataGridViewRainbow.BackgroundColor = Color.FromArgb(255, 60, 65);
            dataGridViewRainbow.GridColor = Color.WhiteSmoke;

            // DEFAULT STYLES (Fallback)
            // Set a default color so Mono doesn't render null/empty
            dataGridViewRainbow.DefaultCellStyle.BackColor = Color.White;
            dataGridViewRainbow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(100, 0, 0, 0);
            dataGridViewRainbow.DefaultCellStyle.SelectionForeColor = Color.White;

            // --- DATA ---
            DataTable dtRainbow = new DataTable();
            dtRainbow.Columns.Add("R"); dtRainbow.Columns.Add("A"); dtRainbow.Columns.Add("I"); dtRainbow.Columns.Add("N");
            for (int i = 0; i < 10; i++) dtRainbow.Rows.Add("Cel", "Cel", "Cel", "Cel");
            dataGridViewRainbow.DataSource = dtRainbow;

            // --- THE FIX: CellFormatting Event ---
            // This is the equivalent to Eto.Forms gridView.CellStyle += ...
            dataGridViewRainbow.CellFormatting += (sender, e) =>
            {
                // 1. Safety check: Ignore headers and the placeholder row
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                // 2. Generate a Deterministic Random Color
                // We use Row/Col index as a seed so the color is stable.
                // If we just used 'new Random()', the colors would flicker 
                // every time you move the mouse over the grid.
                int seed = (e.RowIndex * 100) + e.ColumnIndex;
                Random rnd = new Random(seed);

                // 3. Apply the style directly to the event arguments
                e.CellStyle.BackColor = Color.FromArgb(255, rnd.Next(150, 256), rnd.Next(150, 256), rnd.Next(150, 256));
                e.CellStyle.ForeColor = Color.Black;

                // 4. Ensure selection transparency is respected
                e.CellStyle.SelectionBackColor = dataGridViewRainbow.DefaultCellStyle.SelectionBackColor;
            };

            grpRainbowData.Controls.Add(dataGridViewRainbow);

            // --- Zone 4: Buttons (FlatStyle.Flat) ---
            btnColored1.Text = "RED ACTION"; btnColored1.Location = new Point(20, 30);
            btnColored1.Size = new Size(150, 50); btnColored1.BackColor = Color.Tomato;
            btnColored1.FlatStyle = FlatStyle.Flat; // Requested
            btnColored1.ForeColor = Color.White;
            btnColored1.FlatAppearance.BorderSize = 0;

            btnColored2.Text = "Blue Action"; btnColored2.Location = new Point(20, 100);
            btnColored2.Size = new Size(150, 50); btnColored2.BackColor = Color.CornflowerBlue;
            btnColored2.FlatStyle = FlatStyle.Flat; // Requested
            btnColored2.ForeColor = Color.White;
            btnColored2.FlatAppearance.BorderSize = 0;

            btnColored3.Text = "Green Submit"; btnColored3.Location = new Point(20, 170);
            btnColored3.Size = new Size(150, 50); btnColored3.BackColor = Color.MediumSeaGreen;
            btnColored3.FlatStyle = FlatStyle.Flat; // Requested
            btnColored3.ForeColor = Color.White;
            btnColored3.FlatAppearance.BorderSize = 0;

            checkBox1.Text = "Enable Options"; checkBox1.Location = new Point(200, 40);
            checkBox1.AutoSize = true; checkBox1.ForeColor = Color.White;
            checkBox1.FlatStyle = FlatStyle.Flat; // Requested

            radioButton1.Text = "Radio Choice"; radioButton1.Location = new Point(200, 80);
            radioButton1.AutoSize = true; radioButton1.ForeColor = Color.White;
            radioButton1.FlatStyle = FlatStyle.Flat; // Requested

            linkLabel1.Text = "Visit Example.com"; linkLabel1.Location = new Point(200, 120);
            linkLabel1.AutoSize = true; linkLabel1.LinkColor = Color.Cyan;

            grpButtons.Controls.Add(btnColored1);
            grpButtons.Controls.Add(btnColored2);
            grpButtons.Controls.Add(btnColored3);
            grpButtons.Controls.Add(checkBox1);
            grpButtons.Controls.Add(radioButton1);
            grpButtons.Controls.Add(linkLabel1);

            // --- Zone 5: Lists ---
            treeView1.Location = new Point(10, 30); treeView1.Size = new Size(200, 330);
            treeView1.BackColor = Color.White;
            treeView1.BorderStyle = BorderStyle.FixedSingle;
            treeView1.Nodes.Add("Root"); treeView1.Nodes[0].Nodes.Add("Child");

            listView1.Location = new Point(220, 30); listView1.Size = new Size(200, 330);
            listView1.View = View.Details;
            listView1.BorderStyle = BorderStyle.FixedSingle;
            listView1.Columns.Add("Item"); listView1.Columns.Add("Info");
            listView1.Items.Add("Item 1", "Info 1");

            grpLists.Controls.Add(treeView1);
            grpLists.Controls.Add(listView1);

            // --- Zone 6: Standard Data ---
            dataGridView1.Location = new Point(10, 30); dataGridView1.Size = new Size(410, 150);
            dataGridView1.BorderStyle = BorderStyle.FixedSingle;
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.Gray;
            dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            DataTable dtStd = new DataTable();
            dtStd.Columns.Add("ID"); dtStd.Columns.Add("Data");
            dtStd.Rows.Add(1, "Alpha"); dtStd.Rows.Add(2, "Beta");
            dataGridView1.DataSource = dtStd;
            

            richTextBox1.Location = new Point(10, 200); richTextBox1.Size = new Size(250, 150);
            richTextBox1.Text = "Rich Text Area..."; richTextBox1.BackColor = Color.White;
            //richTextBox1.BorderStyle = BorderStyle.FixedSingle;

            progressBar1.Location = new Point(280, 200); progressBar1.Size = new Size(140, 23);
            progressBar1.Value = 60;

            grpStandardData.Controls.Add(dataGridView1);
            grpStandardData.Controls.Add(richTextBox1);
            grpStandardData.Controls.Add(progressBar1);

            // --- Zone 7: Tab Control (Random Crap) ---

            tabControlRandom.Location = new Point(10, 20);
            tabControlRandom.Size = new Size(360, 780);
            tabControlRandom.SelectedIndex = 0;
            tabControlRandom.Appearance = TabAppearance.FlatButtons; // Flat appearance

            // Tab 1: Scrolls
            tabPageScrolls.Text = "Scrolls";
            tabPageScrolls.BackColor = Color.FromArgb(70, 70, 75);

            hScrollBar1.Location = new Point(20, 30); hScrollBar1.Size = new Size(300, 20);
            hScrollBar1.Maximum = 100; hScrollBar1.Value = 50;

            vScrollBar1.Location = new Point(20, 70); vScrollBar1.Size = new Size(20, 200);
            vScrollBar1.Maximum = 100; vScrollBar1.Value = 20;

            progressBarCrap.Location = new Point(60, 70); progressBarCrap.Size = new Size(260, 20);
            progressBarCrap.Style = ProgressBarStyle.Marquee;
            progressBarCrap.MarqueeAnimationSpeed = 50;

            tabPageScrolls.Controls.Add(hScrollBar1);
            tabPageScrolls.Controls.Add(vScrollBar1);
            tabPageScrolls.Controls.Add(progressBarCrap);

            // Tab 2: Lists
            thisTabPageLists.Text = "More Lists";
            thisTabPageLists.BackColor = Color.FromArgb(70, 70, 75);

            listBox1.Location = new Point(20, 20); listBox1.Size = new Size(150, 200);
            listBox1.BorderStyle = BorderStyle.FixedSingle;
            listBox1.Items.Add("List Item 1"); listBox1.Items.Add("List Item 2");
            listBox1.Items.Add("List Item 3"); listBox1.Items.Add("List Item 4");

            checkedListBox1.Location = new Point(180, 20); checkedListBox1.Size = new Size(150, 200);
            checkedListBox1.BorderStyle = BorderStyle.FixedSingle;
            checkedListBox1.Items.Add("Check 1"); checkedListBox1.Items.Add("Check 2");
            checkedListBox1.Items.Add("Check 3");

            thisTabPageLists.Controls.Add(listBox1);
            thisTabPageLists.Controls.Add(checkedListBox1);

            // Tab 3: Weird Stuff
            thisTabPageWeird.Text = "Weird Stuff";
            thisTabPageWeird.BackColor = Color.FromArgb(70, 70, 75);

            domainUpDown1.Location = new Point(20, 30); domainUpDown1.Size = new Size(120, 20);
            domainUpDown1.Items.Add("Item 1"); domainUpDown1.SelectedIndex = 0;
            domainUpDown1.BorderStyle = BorderStyle.FixedSingle;

            maskedTextBox1.Location = new Point(20, 60); maskedTextBox1.Size = new Size(100, 20);
            maskedTextBox1.Mask = "00/00/0000";
            //maskedTextBox1.BorderStyle = BorderStyle.FixedSingle;

            pictureBoxCrap.Location = new Point(20, 100); pictureBoxCrap.Size = new Size(300, 100);
            pictureBoxCrap.BackColor = Color.White;
            pictureBoxCrap.BorderStyle = BorderStyle.FixedSingle;
            Bitmap bmpCrap = new Bitmap(300, 100);
            using (Graphics g = Graphics.FromImage(bmpCrap))
            {
                g.Clear(Color.White);
                g.FillEllipse(Brushes.Purple, 10, 10, 80, 80);
                g.FillRectangle(Brushes.Orange, 100, 20, 180, 60);
            }
            pictureBoxCrap.Image = bmpCrap;

            thisTabPageWeird.Controls.Add(domainUpDown1);
            thisTabPageWeird.Controls.Add(maskedTextBox1);
            thisTabPageWeird.Controls.Add(pictureBoxCrap);

            tabControlRandom.Controls.Add(tabPageScrolls);
            tabControlRandom.Controls.Add(thisTabPageLists);
            tabControlRandom.Controls.Add(thisTabPageWeird);

            grpTabs.Controls.Add(tabControlRandom);


            // --- Global Form Settings ---
            this.Controls.Add(this.grpInputs);
            this.Controls.Add(this.grpTransparency);
            this.Controls.Add(this.grpRainbowData);
            this.Controls.Add(this.grpButtons);
            this.Controls.Add(this.grpLists);
            this.Controls.Add(this.grpStandardData);
            this.Controls.Add(this.grpTabs);

            this.Controls.Add(this.statusStrip1);
            this.Controls.Add(this.menuStrip1);

            statusStrip1.Items.Add("Ready");

            // Menu Strip
            menuStrip1.Items.Add("File");
            menuStrip1.Items.Add("Edit");
            menuStrip1.Items.Add("View");

            // Final Image Generation
            this.picStar.Image = CreateStarBitmap(400, 350);

            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.trackBar1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewRainbow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBoxCrap)).EndInit();
            this.grpInputs.ResumeLayout(false);
            this.grpInputs.PerformLayout();
            this.grpTransparency.ResumeLayout(false);
            this.grpRainbowData.ResumeLayout(false);
            this.grpButtons.ResumeLayout(false);
            this.grpButtons.PerformLayout();
            this.grpLists.ResumeLayout(false);
            this.grpStandardData.ResumeLayout(false);
            this.grpTabs.ResumeLayout(false);
            this.tabControlRandom.ResumeLayout(false);
            this.tabPageScrolls.ResumeLayout(false);
            thisTabPageLists.ResumeLayout(false);
            thisTabPageWeird.ResumeLayout(false);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
            btnTran1.Click += (_, __) => { txtMulti.Text = Logger.Log; };
        }

        #endregion

        // --- Control Declarations ---

        // Zones
        private GroupBox grpInputs;
        private GroupBox grpTransparency;
        private GroupBox grpRainbowData;
        private GroupBox grpButtons;
        private GroupBox grpLists;
        private GroupBox grpStandardData;
        private GroupBox grpTabs;

        // Zone 1
        private TextBox txtStandard;
        private TextBox txtPassword;
        private TextBox txtMulti;
        private TextBox txtStandard2;
        private NumericUpDown numericUpDown1;
        private TrackBar trackBar1;
        private ComboBox comboBox1;
        private DateTimePicker dateTimePicker1; // Added

        // Zone 2
        private PictureBox picStar;
        private System.Windows.Forms.Button btnTran1;
        private System.Windows.Forms.Button btnTran2;
        private System.Windows.Forms.Button btnTran3;

        // Zone 3
        private DataGridView dataGridViewRainbow;

        // Zone 4
        private Button btnColored1;
        private Button btnColored2;
        private Button btnColored3;
        private CheckBox checkBox1;
        private RadioButton radioButton1;
        private LinkLabel linkLabel1;

        // Zone 5
        private TreeView treeView1;
        private ListView listView1;

        // Zone 6
        private DataGridView dataGridView1;
        private RichTextBox richTextBox1;
        private ProgressBar progressBar1;

        // Zone 7
        private TabControl tabControlRandom;
        private TabPage tabPageScrolls;
        private TabPage thisTabPageLists;
        private TabPage thisTabPageWeird;

        private HScrollBar hScrollBar1;
        private VScrollBar vScrollBar1;
        private ProgressBar progressBarCrap;

        private ListBox listBox1;
        private CheckedListBox checkedListBox1;

        private DomainUpDown domainUpDown1;
        private MaskedTextBox maskedTextBox1;
        private PictureBox pictureBoxCrap;

        // Global
        private MenuStrip menuStrip1;
        private StatusStrip statusStrip1;
        private ToolTip toolTip1;
        private ContextMenuStrip contextMenuStrip1;

        // --- Helper Methods ---

        private Bitmap CreateStarBitmap(int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                PointF center = new PointF(width / 2, height / 2);
                float outerRadius = width / 2.5f;
                float innerRadius = width / 6.0f;
                int points = 5;

                PointF[] starPoints = new PointF[points * 2];
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

                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new RectangleF(0, 0, width, height), Color.Gold, Color.OrangeRed, LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPolygon(brush, starPoints);
                }

                using (Pen pen = new Pen(Color.White, 4))
                {
                    g.DrawPolygon(pen, starPoints);
                }
            }
            return bmp;
        }
    }
}