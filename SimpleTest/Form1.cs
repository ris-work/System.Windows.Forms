using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SimpleTest
{
    public partial class Form1 : Form
    {
        int i = 0;
        public Form1()
        {
            InitializeComponent();
            Resize += (_, __) => { 
                //this.Invalidate(); 
            };
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //label1.Text = i++.ToString();
            //this.webBrowser1.Navigate("https://google.com");
            MessageBox.Show(Logger.Log);
        }
    }
}
