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
        private RoundedTextBox searchBox = null!;
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

            // Crear la barra de búsqueda
            searchBox = new RoundedTextBox();
            searchBox.Font = new Font("Google Sans Flex", 14, FontStyle.Regular); // Fuente un poco más pequeña
            searchBox.Width = 600; // Más grande
            searchBox.Height = 110; // Más alto para acomodar descendentes
            searchBox.Left = (Screen.PrimaryScreen.Bounds.Width - searchBox.Width) / 2;
            searchBox.Top = Screen.PrimaryScreen.Bounds.Height - 200; // 200px desde abajo
            searchBox.BackColor = ColorTranslator.FromHtml("#191919");
            searchBox.ForeColor = ColorTranslator.FromHtml("#C3C6D6");
            searchBox.BorderColor = Color.Black; // Bordes negros
            searchBox.BorderRadius = 40; // Radio proporcionalmente más grande para la nueva altura
            searchBox.BorderWidth = 30; // Bordes más gruesos
            searchBox.KeyDown += SearchBox_KeyDown;
            overlayForm.Controls.Add(searchBox);
            searchBox.BringToFront();
            searchBox.Focus();

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

        private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                string searchText = searchBox.Text.Trim();
                if (!string.IsNullOrEmpty(searchText))
                {
                    PerformTextSearch(searchText);
                    e.Handled = true;
                    e.SuppressKeyPress = true; // Evita el sonido de beep
                }
            }
        }

        private void PerformTextSearch(string searchText)
        {
            try
            {
                // URL de búsqueda de Google con el texto
                string searchUrl = $"https://www.google.com/search?q={Uri.EscapeDataString(searchText)}";
                
                // Abrir Chrome con la búsqueda
                Process.Start(new ProcessStartInfo(searchUrl) { UseShellExecute = true });
                
                // Cerrar el overlay después de la búsqueda
                StartBrightnessAnimation(currentBrightness, 1f, true);
            }
            catch (Exception ex)
            {
                // En caso de error, intentar con el navegador predeterminado
                MessageBox.Show($"Error al abrir la búsqueda: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            searchBox?.Dispose();
        }
    }

    public class RoundedTextBox : UserControl
    {
        private TextBox textBox;
        private PictureBox leftIcon;
        private PictureBox rightIcon;
        private int borderRadius = 20;
        private int borderWidth = 2;
        private Color borderColor = Color.White;

        [System.ComponentModel.Browsable(true)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
        public int BorderRadius
        {
            get => borderRadius;
            set { borderRadius = value; Invalidate(); }
        }

        [System.ComponentModel.Browsable(true)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
        public int BorderWidth
        {
            get => borderWidth;
            set { borderWidth = value; Invalidate(); }
        }

        [System.ComponentModel.Browsable(true)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Visible)]
        public Color BorderColor
        {
            get => borderColor;
            set { borderColor = value; Invalidate(); }
        }

        public override string Text 
        { 
            get => textBox?.Text ?? string.Empty; 
            set { if (textBox != null) textBox.Text = value; } 
        }

        public override Font Font 
        { 
            get => textBox?.Font ?? base.Font; 
            set { if (textBox != null) textBox.Font = value; } 
        }

        public override Color ForeColor 
        { 
            get => textBox?.ForeColor ?? base.ForeColor; 
            set {
                if (textBox != null)
                    textBox.ForeColor = value;
                base.ForeColor = value;
            }
        }

        public override Color BackColor
        {
            get => base.BackColor;
            set {
                base.BackColor = value;
                if (textBox != null)
                    textBox.BackColor = value;
                Invalidate();
            }
        }

        public new event KeyEventHandler KeyDown
        {
            add => textBox.KeyDown += value;
            remove => textBox.KeyDown -= value;
        }

        public RoundedTextBox()
        {
            // Configuración avanzada para soportar transparencia y antialiasing
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | 
                         ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw | 
                         ControlStyles.SupportsTransparentBackColor | ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();

            // Crear textBox PRIMERO para evitar NullReferenceException en propiedades override
            textBox = new TextBox();
            textBox.BorderStyle = BorderStyle.None;
            textBox.Multiline = true; // Permite ajustar la altura manualmente
            textBox.WordWrap = false; // Evitar saltos de línea visuales
            textBox.BackColor = ColorTranslator.FromHtml("#1F1F1F");
            textBox.ForeColor = Color.White;
            textBox.TextAlign = HorizontalAlignment.Left;
            textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            // Altura suficiente para la fuente con descendentes
            textBox.Height = textBox.PreferredHeight + 8;
            int iconSpace = 40; // Espacio reservado para cada icono
            int verticalOffset = (Height - textBox.Height) / 2;
            textBox.Location = new Point(borderRadius / 2 + iconSpace + 10, Math.Max(2, verticalOffset));
            textBox.Width = Width - borderRadius - iconSpace * 2 - 40;
            
            // Suprimir el salto de línea cuando se presiona Enter
            textBox.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true; // Evita el salto de línea en Multiline
                }
            };
            
            this.Controls.Add(textBox);

            // Icono izquierdo (google.png)
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            leftIcon = new PictureBox();
            string googlePath = Path.Combine(basePath, "assets", "icons", "google.png");
            try
            {
                if (File.Exists(googlePath))
                    leftIcon.Image = Image.FromFile(googlePath);
            }
            catch
            {
                // Silenciar error si el archivo no se puede cargar (ej: WebP como PNG)
            }
            leftIcon.SizeMode = PictureBoxSizeMode.Zoom;
            leftIcon.BackColor = Color.Transparent;
            leftIcon.Size = new Size(28, 28);
            this.Controls.Add(leftIcon);

            // Icono derecho (search.png)
            rightIcon = new PictureBox();
            string searchPath = Path.Combine(basePath, "assets", "icons", "search.png");
            try
            {
                if (File.Exists(searchPath))
                    rightIcon.Image = Image.FromFile(searchPath);
            }
            catch
            {
                // Silenciar error si el archivo no se puede cargar
            }
            rightIcon.SizeMode = PictureBoxSizeMode.Zoom;
            rightIcon.BackColor = Color.Transparent;
            rightIcon.Size = new Size(28, 28);
            rightIcon.Cursor = Cursors.Hand; // Cursor de mano para indicar que es clickeable
            rightIcon.Click += (s, e) => {
                // Disparar el evento KeyDown del textBox con Enter
                var args = new KeyEventArgs(Keys.Enter);
                textBox.GetType().GetMethod("OnKeyDown", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(textBox, new object[] { args });
            };
            this.Controls.Add(rightIcon);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            Graphics g = e.Graphics;
            // Configuración avanzada de antialiasing para bordes suaves
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            // Crear el rectángulo para el borde
            Rectangle rect = new Rectangle(borderWidth / 2, borderWidth / 2, 
                                         Width - borderWidth, Height - borderWidth);
            
            // Crear el path redondeado
            using (System.Drawing.Drawing2D.GraphicsPath path = CreateRoundedRectangle(rect, borderRadius))
            {
                // Rellenar el fondo
                using (SolidBrush brush = new SolidBrush(this.BackColor))
                {
                    g.FillPath(brush, path);
                }

                // Dibujar el borde con antialiasing mejorado
                using (Pen pen = new Pen(borderColor, borderWidth))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    g.DrawPath(pen, path);
                }

                // Establecer la región del control
                this.Region = new Region(path);
            }
        }

        private System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            
            if (radius <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            // Esquina superior izquierda
            path.AddArc(arc, 180, 90);

            // Esquina superior derecha
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Esquina inferior derecha
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Esquina inferior izquierda
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int iconSize = 28;
            int iconPadding = 30; // Padding desde los bordes

            if (leftIcon != null)
            {
                leftIcon.Location = new Point(
                    borderRadius / 2 + iconPadding,
                    (Height - iconSize) / 2
                );
            }

            if (rightIcon != null)
            {
                rightIcon.Location = new Point(
                    Width - borderRadius / 2 - iconPadding - iconSize,
                    (Height - iconSize) / 2
                );
            }

            if (textBox != null)
            {
                int leftEdge = borderRadius / 2 + iconPadding + iconSize + 10;
                int rightEdge = borderRadius / 2 + iconPadding + iconSize + 10;
                textBox.Height = textBox.PreferredHeight + 8;
                int verticalOffset = (Height - textBox.Height) / 2;
                textBox.Location = new Point(leftEdge, Math.Max(2, verticalOffset));
                textBox.Width = Width - leftEdge - rightEdge;
            }
            Invalidate();
        }

        public new void Focus()
        {
            textBox.Focus();
        }
    }
}
