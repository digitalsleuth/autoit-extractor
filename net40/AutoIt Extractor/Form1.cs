using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace AutoIt_Extractor
{
    public partial class MainForm : Form
    {
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            threads.ForEach(x => x.Abort());
            Quit?.Invoke(sender, null);
        }
    }
}
