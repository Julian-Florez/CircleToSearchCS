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
    public class CircleToSearch : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        private Form overlayForm = null!;
        private Bitmap originalImage = null!;
        private Bitmap darkImage = null!;
        private PictureBox pictureBox = null!;
        private List<Point> points = new List<Point>();
        private Rectangle selectionRect;
        private int currentModeIndex = 0;
        private System.Windows.Forms.Timer? hideLabelTimer;
        private Label modeLabel = null!;
        private bool isDrawing = false;
        private Point startPoint;
        private string[] modes;
        private string[] urls;

        // Agregar un Pen blanco reutilizable para el trazo
        private readonly Pen tracePen = new Pen(Color.White, 3) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };

        public CircleToSearch()
        {
            SetProcessDPIAware(); // Asegura DPI awareness en tiempo de ejecución
            modes = Config.SEARCH_MODES.Keys.ToArray();
            urls = Config.SEARCH_MODES.Values.ToArray();
            InitOverlay();
        }

        private void InitOverlay()
        {
            overlayForm = new Form();
            overlayForm.FormBorderStyle = FormBorderStyle.None;
            overlayForm.WindowState = FormWindowState.Maximized;
            overlayForm.TopMost = true;
            overlayForm.BackColor = Color.Black;
            overlayForm.Opacity = 1.0;
            overlayForm.KeyPreview = true;

            originalImage = CaptureScreen();
            darkImage = ChangeBrightness(originalImage, 0.4f);

            pictureBox = new PictureBox();
            pictureBox.Dock = DockStyle.Fill;
            pictureBox.Image = darkImage;
            pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            pictureBox.MouseDown += PictureBox_MouseDown;
            pictureBox.MouseMove += PictureBox_MouseMove;
            pictureBox.MouseUp += PictureBox_MouseUp;
            pictureBox.Paint += PictureBox_Paint;
            overlayForm.Controls.Add(pictureBox);

            modeLabel = new Label();
            modeLabel.Text = modes[currentModeIndex];
            modeLabel.Font = new Font("Segoe UI", 24, FontStyle.Bold);
            modeLabel.ForeColor = ColorTranslator.FromHtml(Config.TEXT_COLOR);
            modeLabel.BackColor = Color.Transparent;
            modeLabel.AutoSize = true;
            modeLabel.Top = 50;
            modeLabel.Left = (Screen.PrimaryScreen.Bounds.Width - modeLabel.Width) / 2;
            overlayForm.Controls.Add(modeLabel);

            overlayForm.KeyDown += OverlayForm_KeyDown;
            overlayForm.MouseWheel += OverlayForm_MouseWheel;
        }

        public void Run()
        {
            overlayForm.ShowDialog();
        }

        private void OverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                overlayForm.Close();
            }
        }

        private void OverlayForm_MouseWheel(object? sender, MouseEventArgs e)
        {
            int direction = e.Delta > 0 ? 1 : -1;
            currentModeIndex = (currentModeIndex - direction + modes.Length) % modes.Length;
            modeLabel.Text = modes[currentModeIndex];
            modeLabel.Left = (Screen.PrimaryScreen.Bounds.Width - modeLabel.Width) / 2;
            ResetHideLabelTimer();
        }

        private void ResetHideLabelTimer()
        {
            if (hideLabelTimer != null)
            {
                hideLabelTimer.Stop();
                hideLabelTimer.Dispose();
            }
            hideLabelTimer = new System.Windows.Forms.Timer();
            hideLabelTimer.Interval = 2000;
            hideLabelTimer.Tick += (s, e) => { modeLabel.Visible = false; hideLabelTimer!.Stop(); };
            modeLabel.Visible = true;
            hideLabelTimer.Start();
        }

        private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
        {
            isDrawing = true;
            startPoint = e.Location;
            points.Clear();
            points.Add(e.Location);
        }

        private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!isDrawing) return;
            if (Config.MODE == "BOX")
            {
                selectionRect = new Rectangle(
                    Math.Min(startPoint.X, e.X),
                    Math.Min(startPoint.Y, e.Y),
                    Math.Abs(startPoint.X - e.X),
                    Math.Abs(startPoint.Y - e.Y)
                );
                pictureBox.Invalidate();
            }
            else
            {
                points.Add(e.Location);
                pictureBox.Invalidate();
            }
        }

        // Dibuja el trazo blanco siguiendo los puntos del mouse
        private void PictureBox_Paint(object? sender, PaintEventArgs e)
        {
            if (Config.MODE != "BOX" && points.Count > 1)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(tracePen, points.ToArray());
            }
        }

        private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            isDrawing = false;
            if (Config.MODE == "BOX")
            {
                if (selectionRect.Width > 0 && selectionRect.Height > 0)
                {
                    var cropped = new Bitmap(selectionRect.Width, selectionRect.Height);
                    using (Graphics g = Graphics.FromImage(cropped))
                    {
                        g.DrawImage(originalImage, 0, 0, selectionRect, GraphicsUnit.Pixel);
                    }
                    overlayForm.Close();
                    AutomateGoogleSearch(cropped);
                }
                else
                {
                    overlayForm.Close();
                }
            }
            else
            {
                if (points.Count > 1)
                {
                    int minX = points.Min(p => p.X);
                    int minY = points.Min(p => p.Y);
                    int maxX = points.Max(p => p.X);
                    int maxY = points.Max(p => p.Y);
                    var rect = new Rectangle(minX, minY, maxX - minX, maxY - minY);
                    var cropped = new Bitmap(rect.Width, rect.Height);
                    using (Graphics g = Graphics.FromImage(cropped))
                    {
                        g.DrawImage(originalImage, 0, 0, rect, GraphicsUnit.Pixel);
                    }
                    overlayForm.Close();
                    AutomateGoogleSearch(cropped);
                }
                else
                {
                    overlayForm.Close();
                }
            }
        }

        private Bitmap CaptureScreen()
        {
            // Captura la pantalla en píxeles físicos reales (DPI-aware)
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        private Bitmap ChangeBrightness(Bitmap image, float brightness)
        {
            float b = brightness;
            float[][] ptsArray ={
                new float[] {b, 0, 0, 0, 0},
                new float[] {0, b, 0, 0, 0},
                new float[] {0, 0, b, 0, 0},
                new float[] {0, 0, 0, 1f, 0},
                new float[] {0, 0, 0, 0, 1f}
            };
            var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
            imageAttributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(ptsArray));
            Bitmap result = new Bitmap(image.Width, image.Height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height),
                    0, 0, image.Width, image.Height, GraphicsUnit.Pixel, imageAttributes);
            }
            return result;
        }

        private void AutomateGoogleSearch(Bitmap image)
        {
            string targetUrl = urls[currentModeIndex];
            SendToClipboard(image);
            Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
            Thread.Sleep((int)(Config.BROWSER_LOAD_WAIT_TIME * 500));
            // Pega la imagen dos veces con 0,5s de espera entre cada pegado
            SendKeys.SendWait("^v");
            Thread.Sleep(500);
            SendKeys.SendWait("^v");
            Thread.Sleep(500);
            // Presiona Enter dos veces con 0,5s de espera entre cada uno
            SendKeys.SendWait("{ENTER}");
            Thread.Sleep(500);
            SendKeys.SendWait("{ENTER}");
        }

        private void SendToClipboard(Bitmap image)
        {
            Clipboard.SetImage(image);
        }

        public void Dispose()
        {
            overlayForm?.Dispose();
            originalImage?.Dispose();
            darkImage?.Dispose();
            pictureBox?.Dispose();
        }
    }
}
