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

            var trayThread = new Thread(() => TrayIcon.RunTrayIcon());
            trayThread.IsBackground = true;
            trayThread.Start();

            Console.WriteLine("Circle to Search is running.");
            Console.WriteLine("Check your system tray to exit.");

            // Hotkey loop mejorado: permite múltiples ejecuciones seguidas
            bool hotkeyPressed = false;
            while (TrayIcon.AppRunning)
            {
                bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                bool alt = (Control.ModifierKeys & Keys.Alt) == Keys.Alt;
                bool prtsc = Keyboard.IsKeyDown(Keys.PrintScreen);

                if (ctrl && alt && prtsc)
                {
                    if (!hotkeyPressed)
                    {
                        hotkeyPressed = true;
                        Thread thread = new Thread(() =>
                        {
                            using (var app = new CircleToSearch())
                            {
                                app.Run();
                            }
                        });
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                    }
                }
                else
                {
                    hotkeyPressed = false;
                }
                Thread.Sleep(50);
            }
        }
    }
}
