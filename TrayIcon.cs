using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace CircleToSearchCS
{
    public class TrayIcon
    {
        public static bool AppRunning = true;
        private static NotifyIcon notifyIcon;

        public static void RunTrayIcon()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Information;
            notifyIcon.Text = "Circle to Search";
            notifyIcon.Visible = true;
            var menu = new ContextMenuStrip();
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => QuitProgram();
            menu.Items.Add(exitItem);
            notifyIcon.ContextMenuStrip = menu;
            Application.Run();
        }

        private static void QuitProgram()
        {
            AppRunning = false;
            notifyIcon.Visible = false;
            Application.ExitThread();
            Environment.Exit(0);
        }
    }
}
