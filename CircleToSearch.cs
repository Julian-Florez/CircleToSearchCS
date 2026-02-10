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
        private System.Windows.Forms.Timer? brightnessTimer;
        private float currentBrightness = 1f;
        private float targetBrightness = 0.5f;
        private float brightnessStep = 0.01f;
        private bool animatingToDark = true;
        private List<Point> points = new List<Point>();
        private Rectangle selectionRect;
        private int currentModeIndex = 0;
        private System.Windows.Forms.Timer? hideLabelTimer;
        private Label modeLabel = null!;
        private bool isDrawing = false;
        private Point startPoint;
        private string[] modes;
        private string[] urls;

        // Pen blanco más grueso y suave para el trazo
        private readonly Pen tracePen = new Pen(Color.White, 8)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };

        public CircleToSearch()
        {
            SetProcessDPIAware(); // Asegura DPI awareness en tiempo de ejecución
            modes = Config.SEARCH_MODES.Keys.ToArray();
            urls = Config.SEARCH_MODES.Values.ToArray();
            InitOverlay();
        }

        private void InitOverlay()
        {
            overlayForm = new OverlayForm();
            overlayForm.FormBorderStyle = FormBorderStyle.None;
            overlayForm.WindowState = FormWindowState.Maximized;
            overlayForm.TopMost = true;
            overlayForm.Opacity = 0.0; // Comienza invisible
            overlayForm.BackColor = Color.Black;
            overlayForm.KeyPreview = true;
            overlayForm.ShowInTaskbar = false; // Oculta de Alt-Tab
            overlayForm.Activated += (s, e) => overlayForm.Opacity = 1.0; // Forzar opacidad al activarse
            overlayForm.Load += (s, e) => {
                overlayForm.Opacity = 1.0;
                StartBrightnessAnimation(1f, 0.5f, false);
            };
            overlayForm.KeyDown += OverlayForm_KeyDown;

            // Captura pantalla antes de mostrar
            originalImage = CaptureScreen();
            currentBrightness = 1f;
            darkImage = ChangeBrightness(originalImage, currentBrightness);

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

            // Mostrar ventana y luego animar (más rápido y sin frame oscuro)
            overlayForm.Load += (s, e) => {
                overlayForm.Opacity = 1.0;
                StartBrightnessAnimation(1f, 0.5f, false);
            };

        }

        private void StartBrightnessAnimation(float from, float to, bool closeAfter = false)
        {
            if (brightnessTimer != null)
            {
                brightnessTimer.Stop();
                brightnessTimer.Dispose();
            }
            currentBrightness = from;
            targetBrightness = to;
            animatingToDark = !closeAfter;
            brightnessStep = (to > from ? 1 : -1) * 0.045f; // Aún más rápido
            brightnessTimer = new System.Windows.Forms.Timer();
            brightnessTimer.Interval = 4; // Aún más rápido
            brightnessTimer.Tick += (s, e) =>
            {
                if ((brightnessStep < 0 && currentBrightness > targetBrightness) || (brightnessStep > 0 && currentBrightness < targetBrightness))
                {
                    currentBrightness += brightnessStep;
                    if ((brightnessStep < 0 && currentBrightness < targetBrightness) || (brightnessStep > 0 && currentBrightness > targetBrightness))
                        currentBrightness = targetBrightness;
                    pictureBox.Image = ChangeBrightness(originalImage, currentBrightness);
                }
                else
                {
                    brightnessTimer!.Stop();
                    pictureBox.Image = ChangeBrightness(originalImage, targetBrightness);
                    if (closeAfter)
                    {
                        overlayForm.Close();
                    }
                }
            };
            brightnessTimer.Start();
        
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
            overlayForm.Show();
            overlayForm.Focus();
            overlayForm.Activate();
            Application.Run(overlayForm);
        }

        private void OverlayForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                StartBrightnessAnimation(currentBrightness, 1f, true);
                e.Handled = true;
            }
        }

        // Form especial para ocultar de Alt-Tab
        private class OverlayForm : Form
        {
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                    cp.ExStyle &= ~0x40000; // Quita WS_EX_APPWINDOW
                    return cp;
                }
            }
            // Evita que la ventana reciba foco y aparezca en-Tab
            protected override bool ShowWithoutActivation => true;
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
                // Solo agregar el punto si la distancia al anterior es suficiente (para suavizar)
                if (points.Count == 0 || Distance(points[^1], e.Location) > 4)
                {
                    points.Add(e.Location);
                    pictureBox.Invalidate();
                }
            }
        }

        // Calcula la distancia euclidiana entre dos puntos
        private static double Distance(Point a, Point b)
        {
            int dx = a.X - b.X;
            int dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Dibuja el trazo blanco siguiendo los puntos del mouse
        private void PictureBox_Paint(object? sender, PaintEventArgs e)
        {
            if (Config.MODE != "BOX" && points.Count > 2)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                // Usar un spline para suavizar el trazo
                e.Graphics.DrawCurve(tracePen, points.ToArray(), 0.5f);
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
                    // Clonar el bitmap para el hilo
                    Bitmap threadBitmap = (Bitmap)cropped.Clone();
                    // Animar brillo de regreso antes de cerrar
                    StartBrightnessAnimation(currentBrightness, 1f, true);
                    Thread t = new Thread(() =>
                    {
                        try { AutomateGoogleSearch(threadBitmap); }
                        catch (Exception ex) { MessageBox.Show($"Error al enviar a Chrome: {ex.Message}"); }
                    });
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                }
                else
                {
                    StartBrightnessAnimation(currentBrightness, 1f, true);
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
                    // Clonar el bitmap para el hilo
                    Bitmap threadBitmap = (Bitmap)cropped.Clone();
                    StartBrightnessAnimation(currentBrightness, 1f, true);
                    Thread t = new Thread(() =>
                    {
                        try { AutomateGoogleSearch(threadBitmap); }
                        catch (Exception ex) { MessageBox.Show($"Error al enviar a Chrome: {ex.Message}"); }
                    });
                    t.SetApartmentState(ApartmentState.STA);
                    t.Start();
                }
                else
                {
                    StartBrightnessAnimation(currentBrightness, 1f, true);
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

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void AutomateGoogleSearch(Bitmap image)
        {
            string targetUrl = urls[currentModeIndex];
            SendToClipboard(image);

            // Intentar encontrar una ventana de Chrome
            IntPtr chromeHandle = FindWindow("Chrome_WidgetWin_1", null);
            if (chromeHandle == IntPtr.Zero)
            {
                // Chrome no está abierto, abrirlo
                Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                Thread.Sleep((int)(Config.BROWSER_LOAD_WAIT_TIME * 1000));
                // Intentar encontrar la ventana de Chrome de nuevo
                int retries = 10;
                while (chromeHandle == IntPtr.Zero && retries-- > 0)
                {
                    Thread.Sleep(300);
                    chromeHandle = FindWindow("Chrome_WidgetWin_1", null);
                }
            }
            else
            {
                // Chrome ya está abierto, solo navegar a la URL
                Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                Thread.Sleep((int)(Config.BROWSER_LOAD_WAIT_TIME * 1000));
            }

            // Traer Chrome al frente
            if (chromeHandle != IntPtr.Zero)
            {
                SetForegroundWindow(chromeHandle);
                Thread.Sleep(300);
            }

            // Pegar desde el portapapeles
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
