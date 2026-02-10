using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;

namespace CircleToSearchCS
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            if (Config.MODE != "BOX" && Config.MODE != "CIRCLE")
            {
                MessageBox.Show($"Error: Invalid MODE '{Config.MODE}' in Config.cs. Must be 'BOX' or 'CIRCLE'.", "Circle to Search - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var app = new CircleToSearch())
            {
                app.Run();
            }
        }
    }
}
