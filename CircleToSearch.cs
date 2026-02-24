using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

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

        // OCR y selección de texto
        private List<TextRegion> textRegions = new List<TextRegion>();
        private bool ocrReady;
        private bool ocrErrorShown;
        private bool selectionIsText;
        private List<TextRegion> activeTextRegions = new List<TextRegion>();
        private List<WordBox> selectedWords = new List<WordBox>();

        // Selección rectangular animada con handles y menú de acciones

        private Rectangle animatedRect;
        private bool selectionActive;
        private bool animatingSelection;
        private System.Windows.Forms.Timer? selectionAnimTimer;
        private DateTime selectionAnimStart;
        private int selectionAnimDuration = 180;
        private const int handleSize = 28;
        private const int handleThickness = 6;
        private const int handleLength = 32;
        private const int glowSize = 100;
        private const int screenGlowSize = 500;
        private const int screenGlowBaseAlpha = 90;
        private const int minSelection = 100;
        private FlowLayoutPanel? actionMenu;
        private bool actionMenuPending;

        private bool isResizingSelection;
        private bool isMovingSelection;
        private Point dragStart;
        private Rectangle selectionAtDragStart;
        private ResizeHandle activeHandle = ResizeHandle.None;

        // Animación slide de la barra de búsqueda
        private System.Windows.Forms.Timer? searchBarSlideTimer;
        private int searchBarTargetTop;
        private int searchBarStartTop;
        private int searchBarSlideElapsed;
        private int searchBarSlideDuration; // en ms, se calcula para igualar la animación de brillo
        private bool searchBarSlideCloseAfter;

        // Pen blanco más grueso y suave para el trazo
        private readonly Pen tracePen = new Pen(Color.White, 8)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };

        private enum ResizeHandle
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

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
                StartSearchBarSlideAnimation(false);
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
            searchBarTargetTop = Screen.PrimaryScreen.Bounds.Height - 200; // Posición final
            searchBox.Top = Screen.PrimaryScreen.Bounds.Height; // Empieza fuera de pantalla (abajo)
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

            CreateActionMenu();

            StartOcrScan();

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

            // Si es cierre, iniciar también la animación de slide hacia abajo
            if (closeAfter)
            {
                StartSearchBarSlideAnimation(true);
            }

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
        }

        private void StartSearchBarSlideAnimation(bool closing)
        {
            if (searchBarSlideTimer != null)
            {
                searchBarSlideTimer.Stop();
                searchBarSlideTimer.Dispose();
            }

            int screenBottom = Screen.PrimaryScreen.Bounds.Height;

            // Calcular la duración de la animación de brillo para igualarla
            // Brillo: rango 0.5 / step 0.045 ≈ 11.11 ticks * 4ms ≈ 44ms
            float brightnessRange = Math.Abs(targetBrightness - currentBrightness);
            if (brightnessRange < 0.01f) brightnessRange = 0.5f; // valor por defecto
            int totalTicks = (int)Math.Ceiling(brightnessRange / 0.045f);
            searchBarSlideDuration = totalTicks * 4; // misma duración que la animación de brillo
            if (searchBarSlideDuration < 20) searchBarSlideDuration = 44; // mínimo razonable

            if (closing)
            {
                searchBarStartTop = searchBox.Top;
                searchBarTargetTop = screenBottom; // fuera de pantalla abajo
            }
            else
            {
                searchBarStartTop = screenBottom; // empieza fuera de pantalla
                searchBarTargetTop = screenBottom - 200; // posición final normal
                searchBox.Top = searchBarStartTop;
            }

            searchBarSlideElapsed = 0;
            searchBarSlideCloseAfter = closing;

            searchBarSlideTimer = new System.Windows.Forms.Timer();
            searchBarSlideTimer.Interval = 4; // mismo intervalo que el brillo
            searchBarSlideTimer.Tick += (s, e) =>
            {
                searchBarSlideElapsed += searchBarSlideTimer!.Interval;
                float t = Math.Min(1f, (float)searchBarSlideElapsed / searchBarSlideDuration);
                float easedT = EaseOutCubic(t);

                searchBox.Top = searchBarStartTop + (int)((searchBarTargetTop - searchBarStartTop) * easedT);

                if (t >= 1f)
                {
                    searchBarSlideTimer.Stop();
                    searchBox.Top = searchBarTargetTop;
                }
            };
            searchBarSlideTimer.Start();
        }

        private static float EaseOutCubic(float t)
        {
            return 1f - (float)Math.Pow(1f - t, 3);
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
            if (selectionActive)
            {
                var handle = HitTestHandle(e.Location, selectionRect);
                if (handle != ResizeHandle.None)
                {
                    isResizingSelection = true;
                    activeHandle = handle;
                    dragStart = e.Location;
                    selectionAtDragStart = selectionRect;
                    HideActionMenu();
                    actionMenuPending = true;
                    return;
                }

                if (selectionRect.Contains(e.Location))
                {
                    isMovingSelection = true;
                    dragStart = e.Location;
                    selectionAtDragStart = selectionRect;
                    HideActionMenu();
                    actionMenuPending = true;
                    return;
                }

                // Si se hace clic fuera, empezar nueva selección
                HideSelectionUI();
            }

            isDrawing = true;
            startPoint = e.Location;
            points.Clear();
            points.Add(e.Location);
        }

        private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (isResizingSelection)
            {
                HandleResize(e.Location);
                return;
            }

            if (isMovingSelection)
            {
                HandleMove(e.Location);
                return;
            }

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

            DrawScreenGlow(e.Graphics);

            DrawTextRegions(e.Graphics);

            if (selectionActive || animatingSelection)
            {
                DrawSelectionOverlay(e.Graphics, animatingSelection ? animatedRect : selectionRect);
            }
        }

        private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            if (isResizingSelection || isMovingSelection)
            {
                isResizingSelection = false;
                isMovingSelection = false;
                activeHandle = ResizeHandle.None;
                if (actionMenuPending)
                {
                    PositionActionMenu();
                    actionMenuPending = false;
                }
                return;
            }

            isDrawing = false;
            if (Config.MODE == "BOX")
            {
                if (selectionRect.Width >= 6 && selectionRect.Height >= 6)
                {
                    FinalizeSelection(selectionRect);
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
                    if (rect.Width >= 6 && rect.Height >= 6)
                    {
                        FinalizeSelection(rect);
                    }
                }
            }
        }

        private void FinalizeSelection(Rectangle rect)
        {
            points.Clear();
            HideSelectionUI();

            selectionIsText = false;
            selectedWords.Clear();
            activeTextRegions.Clear();

            bool useText = ShouldUseTextMode(rect);
            if (useText)
            {
                activeTextRegions = FindRegionsForRect(rect);
                if (activeTextRegions.Count == 0)
                {
                    pictureBox.Invalidate();
                    return;
                }

                selectionIsText = true;
                selectionRect = ClampToRegions(rect, activeTextRegions);
                UpdateTextSelection(selectionRect, true);
                if (selectedWords.Count == 0)
                {
                    pictureBox.Invalidate();
                    return;
                }
                StartSelectionAnimation(selectionRect);
                return;
            }

            if (rect.Width < minSelection || rect.Height < minSelection)
            {
                pictureBox.Invalidate();
                return;
            }

            // Normalizar para evitar valores negativos
            selectionRect = new Rectangle(
                Math.Max(0, rect.X),
                Math.Max(0, rect.Y),
                Math.Max(minSelection, rect.Width),
                Math.Max(minSelection, rect.Height)
            );

            StartSelectionAnimation(selectionRect);
        }

        private void StartSelectionAnimation(Rectangle target)
        {
            if (selectionAnimTimer != null)
            {
                selectionAnimTimer.Stop();
                selectionAnimTimer.Dispose();
            }

            selectionAnimStart = DateTime.Now;
            animatingSelection = true;
            selectionActive = false;

            int startWidth = Math.Max(6, target.Width / 4);
            int startHeight = Math.Max(6, target.Height / 4);
            int startX = target.Left + (target.Width - startWidth) / 2;
            int startY = target.Top + (target.Height - startHeight) / 2;
            Rectangle startRect = new Rectangle(startX, startY, startWidth, startHeight);
            animatedRect = startRect;

            selectionAnimTimer = new System.Windows.Forms.Timer();
            selectionAnimTimer.Interval = 12;
            selectionAnimTimer.Tick += (s, e) =>
            {
                double elapsed = (DateTime.Now - selectionAnimStart).TotalMilliseconds;
                double t = Math.Min(1.0, elapsed / selectionAnimDuration);
                double eased = EaseOutCubic((float)t);
                animatedRect = LerpRect(startRect, target, eased);
                pictureBox.Invalidate();

                if (t >= 1.0)
                {
                    selectionAnimTimer!.Stop();
                    animatingSelection = false;
                    selectionActive = true;
                    selectionRect = target;
                    PositionActionMenu();
                    pictureBox.Invalidate();
                }
            };
            selectionAnimTimer.Start();
        }

        private Rectangle LerpRect(Rectangle from, Rectangle to, double t)
        {
            int x = (int)(from.X + (to.X - from.X) * t);
            int y = (int)(from.Y + (to.Y - from.Y) * t);
            int w = (int)(from.Width + (to.Width - from.Width) * t);
            int h = (int)(from.Height + (to.Height - from.Height) * t);
            return new Rectangle(x, y, w, h);
        }

        private void DrawSelectionOverlay(Graphics g, Rectangle rect)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int radius = 20;

            DrawGlow(g, rect, radius);

            using (var path = CreateRoundedPath(rect, radius))
            {
                using (var fill = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                {
                    g.FillPath(fill, path);
                }
            }

            DrawHandles(g, rect, radius);
        }

        private System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(rect.Location, new Size(diameter, diameter));

            // Top-left
            path.AddArc(arc, 180, 90);

            // Top-right
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom-right
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom-left
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }

        private void DrawGlow(Graphics g, Rectangle rect, int radius)
        {
            int spread = glowSize*2;
            int overlap = spread; // para que se mezclen los colores

            // Esquinas con PathGradientBrush (color en el vértice, transparente hacia afuera) solo fuera del rectángulo
            DrawCornerGlow(g, rect, new Rectangle(rect.Left - spread, rect.Top - spread, spread + overlap, spread + overlap),
                new Point(rect.Left, rect.Top), Color.FromArgb(50, 235, 73, 59)); // TL (rojo)

            DrawCornerGlow(g, rect, new Rectangle(rect.Right - overlap, rect.Top - spread, spread + overlap, spread + overlap),
                new Point(rect.Right, rect.Top), Color.FromArgb(50, 251, 190, 13)); // TR (amarillo)

            DrawCornerGlow(g, rect, new Rectangle(rect.Right - overlap, rect.Bottom - overlap, spread + overlap, spread + overlap),
                new Point(rect.Right, rect.Bottom), Color.FromArgb(50, 58, 171, 88)); // BR (verde)

            DrawCornerGlow(g, rect, new Rectangle(rect.Left - spread, rect.Bottom - overlap, spread + overlap, spread + overlap),
                new Point(rect.Left, rect.Bottom), Color.FromArgb(50, 72, 137, 244)); // BL (azul)
        }

        private void DrawCornerGlow(Graphics g, Rectangle innerRect, Rectangle area, Point center, Color centerColor)
        {
            // Amplía la exclusión para evitar sangrado hacia adentro
            Rectangle exclude = Rectangle.Inflate(innerRect, 15, 15);

            using (var region = new Region(area))
            {
                region.Exclude(exclude); // no pintar dentro ni justo en el borde
                var state = g.Save();
                g.SetClip(region, System.Drawing.Drawing2D.CombineMode.Replace);

                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddRectangle(area);
                    using (var brush = new System.Drawing.Drawing2D.PathGradientBrush(path))
                    {
                        brush.CenterPoint = center;
                        brush.CenterColor = centerColor;
                        brush.SurroundColors = new[] { Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B) };
                        brush.FocusScales = new PointF(0.05f, 0.05f); // concentra color en el vértice
                        g.FillPath(brush, path);
                    }
                }

                g.Restore(state);
            }
        }

        private void DrawScreenGlow(Graphics g)
        {
            int spread = screenGlowSize;
            int overlap = spread / 2;
            int alpha = GetScreenGlowAlpha();
            if (alpha <= 0) return;

            Rectangle screenRect = new Rectangle(0, 0, pictureBox.Width, pictureBox.Height);

            DrawCornerGlowScreen(g, screenRect, new Rectangle(-spread, -spread, spread + overlap, spread + overlap),
                new Point(0, 0), Color.FromArgb(alpha, 235, 73, 59)); // TL rojo

            DrawCornerGlowScreen(g, screenRect, new Rectangle(screenRect.Width - overlap, -spread, spread + overlap, spread + overlap),
                new Point(screenRect.Width, 0), Color.FromArgb(alpha, 251, 190, 13)); // TR amarillo

            DrawCornerGlowScreen(g, screenRect, new Rectangle(screenRect.Width - overlap, screenRect.Height - overlap, spread + overlap, spread + overlap),
                new Point(screenRect.Width, screenRect.Height), Color.FromArgb(alpha, 58, 171, 88)); // BR verde

            DrawCornerGlowScreen(g, screenRect, new Rectangle(-spread, screenRect.Height - overlap, spread + overlap, spread + overlap),
                new Point(0, screenRect.Height), Color.FromArgb(alpha, 72, 137, 244)); // BL azul
        }

        private void DrawCornerGlowScreen(Graphics g, Rectangle innerRect, Rectangle area, Point center, Color centerColor)
        {
            using (var region = new Region(area))
            {
                var state = g.Save();
                g.SetClip(region, System.Drawing.Drawing2D.CombineMode.Replace);

                using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    path.AddRectangle(area);
                    using (var brush = new System.Drawing.Drawing2D.PathGradientBrush(path))
                    {
                        brush.CenterPoint = center;
                        brush.CenterColor = centerColor;
                        brush.SurroundColors = new[] { Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B) };
                        brush.FocusScales = new PointF(0.05f, 0.05f);
                        g.FillPath(brush, path);
                    }
                }

                g.Restore(state);
            }
        }

        private int GetScreenGlowAlpha()
        {
            int a = (int)(screenGlowBaseAlpha * (1f - currentBrightness));
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            return a;
        }

        private void DrawTextRegions(Graphics g)
        {
            if (!ocrReady) return;

            if (selectionIsText && selectedWords.Count > 0)
            {
                using var selFill = new SolidBrush(Color.FromArgb(120, 66, 135, 244));
                foreach (var word in selectedWords)
                {
                    g.FillRectangle(selFill, word.Bounds);
                }
            }
        }

        private void DrawHandles(Graphics g, Rectangle rect, int radius)
        {
            Color handleColor = Color.White;
            DrawCornerArc(g, rect, radius, handleColor, Corner.TopLeft);
            DrawCornerArc(g, rect, radius, handleColor, Corner.TopRight);
            DrawCornerArc(g, rect, radius, handleColor, Corner.BottomLeft);
            DrawCornerArc(g, rect, radius, handleColor, Corner.BottomRight);
        }

        private enum Corner
        {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private void DrawCornerArc(Graphics g, Rectangle rect, int radius, Color color, Corner corner)
        {
            float arcRadius = radius; // comparte el radio del rectángulo
            float size = arcRadius * 2;
            float extraSweep = 110f; // más largo para tocar y cubrir
            float delta = (extraSweep - 90f) / 2f;
            RectangleF arcRect;
            float startAngle;

            switch (corner)
            {
                case Corner.TopLeft:
                    arcRect = new RectangleF(rect.Left - arcRadius, rect.Top - arcRadius, size, size);
                    startAngle = 180f - delta;
                    break;
                case Corner.TopRight:
                    arcRect = new RectangleF(rect.Right - arcRadius, rect.Top - arcRadius, size, size);
                    startAngle = 270f - delta;
                    break;
                case Corner.BottomLeft:
                    arcRect = new RectangleF(rect.Left - arcRadius, rect.Bottom - arcRadius, size, size);
                    startAngle = 90f - delta;
                    break;
                default: // BottomRight
                    arcRect = new RectangleF(rect.Right - arcRadius, rect.Bottom - arcRadius, size, size);
                    startAngle = -delta;
                    break;
            }

            using (var pen = new Pen(color, handleThickness))
            {
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawArc(pen, arcRect, startAngle, extraSweep);
            }
        }

        private void HandleResize(Point current)
        {
            var start = selectionAtDragStart;
            int minW = selectionIsText ? 1 : minSelection;
            int minH = selectionIsText ? 1 : minSelection;

            int left = start.Left;
            int top = start.Top;
            int right = start.Right;
            int bottom = start.Bottom;

            int newLeft = left;
            int newTop = top;
            int newRight = right;
            int newBottom = bottom;

            switch (activeHandle)
            {
                case ResizeHandle.TopLeft:
                    newLeft = Math.Min(Math.Max(0, current.X), right - minW);
                    newTop = Math.Min(Math.Max(0, current.Y), bottom - minH);
                    break;
                case ResizeHandle.TopRight:
                    newRight = Math.Max(Math.Min(pictureBox.Width, current.X), left + minW);
                    newTop = Math.Min(Math.Max(0, current.Y), bottom - minH);
                    break;
                case ResizeHandle.BottomLeft:
                    newLeft = Math.Min(Math.Max(0, current.X), right - minW);
                    newBottom = Math.Max(Math.Min(pictureBox.Height, current.Y), top + minH);
                    break;
                case ResizeHandle.BottomRight:
                    newRight = Math.Max(Math.Min(pictureBox.Width, current.X), left + minW);
                    newBottom = Math.Max(Math.Min(pictureBox.Height, current.Y), top + minH);
                    break;
            }

            Rectangle r = Rectangle.FromLTRB(newLeft, newTop, newRight, newBottom);

            if (selectionIsText)
            {
                activeTextRegions = FindRegionsForRect(r);
                r = ClampToRegions(r, activeTextRegions);
                UpdateTextSelection(r, false);
            }
            else
            {
                selectionRect = r;
            }
            pictureBox.Invalidate();
        }

        private void HandleMove(Point current)
        {
            int dx = current.X - dragStart.X;
            int dy = current.Y - dragStart.Y;
            Rectangle r = selectionAtDragStart;
            r.X = Math.Max(0, Math.Min(pictureBox.Width - r.Width, r.X + dx));
            r.Y = Math.Max(0, Math.Min(pictureBox.Height - r.Height, r.Y + dy));

            if (selectionIsText)
            {
                activeTextRegions = FindRegionsForRect(r);
                r = ClampToRegions(r, activeTextRegions);
                UpdateTextSelection(r, false);
            }
            else
            {
                selectionRect = r;
            }
            pictureBox.Invalidate();
        }

        private ResizeHandle HitTestHandle(Point point, Rectangle rect)
        {
            var handles = GetHandleRects(rect);
            if (handles[0].Contains(point)) return ResizeHandle.TopLeft;
            if (handles[1].Contains(point)) return ResizeHandle.TopRight;
            if (handles[2].Contains(point)) return ResizeHandle.BottomLeft;
            if (handles[3].Contains(point)) return ResizeHandle.BottomRight;
            return ResizeHandle.None;
        }

        private Rectangle[] GetHandleRects(Rectangle rect)
        {
            int detect = radiusForHit();
            return new[]
            {
                new Rectangle(rect.Left - detect, rect.Top - detect, detect * 2, detect * 2),
                new Rectangle(rect.Right - detect, rect.Top - detect, detect * 2, detect * 2),
                new Rectangle(rect.Left - detect, rect.Bottom - detect, detect * 2, detect * 2),
                new Rectangle(rect.Right - detect, rect.Bottom - detect, detect * 2, detect * 2)
            };
        }

        private int radiusForHit()
        {
            return Math.Max((handleLength + handleThickness) * 2, 64);
        }

        private void HideSelectionUI()
        {
            selectionActive = false;
            animatingSelection = false;
            isResizingSelection = false;
            isMovingSelection = false;
            activeHandle = ResizeHandle.None;
            selectionAnimTimer?.Stop();
            selectionAnimTimer?.Dispose();
            HideActionMenu();
            actionMenuPending = false;
            selectionIsText = false;
            selectedWords.Clear();
            activeTextRegions.Clear();
        }

        private void HideActionMenu()
        {
            if (actionMenu != null)
            {
                actionMenu.Visible = false;
            }
        }

        private void CreateActionMenu()
        {
            actionMenu = new FlowLayoutPanel();
            actionMenu.AutoSize = true;
            actionMenu.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            actionMenu.BackColor = Color.FromArgb(31, 31, 31);
            actionMenu.Padding = new Padding(0);
            actionMenu.Margin = new Padding(0);
            actionMenu.FlowDirection = FlowDirection.LeftToRight;
            actionMenu.WrapContents = false;
            actionMenu.Visible = false;
            actionMenu.Paint += (s, e) => ApplyPillMask(actionMenu);
            actionMenu.Resize += (s, e) => ApplyPillMask(actionMenu);

            Button BuildButton(string text, EventHandler onClick)
            {
                var btn = new Button();
                btn.Text = text;
                btn.AutoSize = true;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
                btn.Font = new Font("Google Sans Flex", 10, FontStyle.Regular);
                btn.ForeColor = Color.White;
                btn.BackColor = Color.FromArgb(31, 31, 31);
                btn.Padding = new Padding(18, 4, 18, 4);
                btn.Margin = new Padding(0);
                btn.Cursor = Cursors.Hand;
                btn.Click += onClick;
                btn.Paint += (s, e) => ApplyPillMask(btn);
                btn.Resize += (s, e) => ApplyPillMask(btn);
                return btn;
            }

            actionMenu.Controls.Add(BuildButton("Copiar", (s, e) => PerformSelectionAction(SelectionAction.Copy)));
            actionMenu.Controls.Add(BuildButton("Buscar", (s, e) => PerformSelectionAction(SelectionAction.Search)));
            actionMenu.Controls.Add(BuildButton("Preguntar", (s, e) => PerformSelectionAction(SelectionAction.Ask)));

            overlayForm.Controls.Add(actionMenu);
            actionMenu.BringToFront();
        }

        private void ApplyPillMask(Control control)
        {
            int radius = 20;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                Rectangle r = new Rectangle(0, 0, control.Width, control.Height);
                int d = radius * 2;
                path.AddArc(r.Left, r.Top, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                control.Region = new Region(path);
            }
        }

        private enum SelectionAction
        {
            Copy,
            Search,
            Ask
        }

        private void PerformSelectionAction(SelectionAction action)
        {
            if (!selectionActive && !animatingSelection) return;
            Rectangle rect = selectionRect;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            if (selectionIsText)
            {
                string selectedText = GetSelectedText();
                selectedText = selectedText
                    .Replace("\r\n", " ")
                    .Replace("\n", " ")
                    .Replace("\r", " ")
                    .Trim();
                if (string.IsNullOrWhiteSpace(selectedText)) return;

                switch (action)
                {
                    case SelectionAction.Copy:
                        try { Clipboard.SetText(selectedText); }
                        catch (Exception ex) { MessageBox.Show($"No se pudo copiar el texto: {ex.Message}"); }
                        StartBrightnessAnimation(currentBrightness, 1f, true);
                        break;
                    case SelectionAction.Search:
                        StartSimpleGoogleSearch(selectedText);
                        break;
                    case SelectionAction.Ask:
                        StartTextSearchThread(selectedText, false);
                        break;
                }
                return;
            }

            Bitmap cropped = CropSelection(rect);

            switch (action)
            {
                case SelectionAction.Copy:
                    try
                    {
                        Clipboard.SetImage(cropped);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"No se pudo copiar la imagen: {ex.Message}");
                    }
                    StartBrightnessAnimation(currentBrightness, 1f, true);
                    break;
                case SelectionAction.Search:
                    StartSearchThread(cropped, true);
                    break;
                case SelectionAction.Ask:
                    StartSearchThread(cropped, false);
                    break;
            }
        }

        private void StartSearchThread(Bitmap cropped, bool submitSearch)
        {
            Bitmap threadBitmap = (Bitmap)cropped.Clone();
            StartBrightnessAnimation(currentBrightness, 1f, true);
            Thread t = new Thread(() =>
            {
                try { AutomateGoogleSearch(threadBitmap, submitSearch); }
                catch (Exception ex) { MessageBox.Show($"Error al enviar a Chrome: {ex.Message}"); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void StartTextSearchThread(string text, bool submitSearch)
        {
            string textToSend = text;
            StartBrightnessAnimation(currentBrightness, 1f, true);
            Thread t = new Thread(() =>
            {
                try { AutomateGoogleSearch(textToSend, submitSearch); }
                catch (Exception ex) { MessageBox.Show($"Error al enviar a Chrome: {ex.Message}"); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void StartSimpleGoogleSearch(string text)
        {
            StartBrightnessAnimation(currentBrightness, 1f, true);
            try
            {
                string url = $"https://www.google.com/search?q={Uri.EscapeDataString(text)}";
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo abrir la búsqueda: {ex.Message}");
            }
        }

        private Bitmap CropSelection(Rectangle rect)
        {
            Bitmap cropped = new Bitmap(rect.Width, rect.Height);
            using (Graphics g = Graphics.FromImage(cropped))
            {
                g.DrawImage(originalImage, 0, 0, rect, GraphicsUnit.Pixel);
            }
            return cropped;
        }

        private void PositionActionMenu()
        {
            if (actionMenu == null) return;
            if (!(selectionActive || animatingSelection))
            {
                actionMenu.Visible = false;
                return;
            }

            if (isResizingSelection || isMovingSelection)
            {
                actionMenu.Visible = false;
                actionMenuPending = true;
                return;
            }

            Rectangle rect = animatingSelection ? animatedRect : selectionRect;
            int menuMargin = 20;
            int screenWidth = overlayForm.Width;
            int screenHeight = overlayForm.Height;

            actionMenu.PerformLayout();

            int x = rect.Left + rect.Width / 2 - actionMenu.Width / 2;
            x = Math.Max(menuMargin, Math.Min(screenWidth - actionMenu.Width - menuMargin, x));

            int spaceAbove = rect.Top;
            int spaceBelow = screenHeight - rect.Bottom;
            int y;
            if (spaceAbove > actionMenu.Height + 20)
            {
                y = rect.Top - actionMenu.Height - menuMargin;
            }
            else
            {
                y = rect.Bottom + menuMargin;
            }

            // Si queda fuera, reajustar dentro de pantalla
            y = Math.Max(menuMargin, Math.Min(screenHeight - actionMenu.Height - menuMargin, y));

            actionMenu.Location = new Point(x, y);
            actionMenu.Visible = true;
            actionMenu.BringToFront();
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

        private void AutomateGoogleSearch(Bitmap image, bool submitSearch)
        {
            SendToBrowser(urls[currentModeIndex], () => Clipboard.SetImage(image), submitSearch);
        }

        private void AutomateGoogleSearch(string text, bool submitSearch)
        {
            SendToBrowser(urls[currentModeIndex], () => Clipboard.SetText(text), submitSearch);
        }

        private void SendToBrowser(string targetUrl, Action prepareClipboard, bool submitSearch)
        {
            prepareClipboard();

            IntPtr chromeHandle = FindWindow("Chrome_WidgetWin_1", null);
            if (chromeHandle == IntPtr.Zero)
            {
                Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                Thread.Sleep((int)(Config.BROWSER_LOAD_WAIT_TIME * 1000));
                int retries = 10;
                while (chromeHandle == IntPtr.Zero && retries-- > 0)
                {
                    Thread.Sleep(300);
                    chromeHandle = FindWindow("Chrome_WidgetWin_1", null);
                }
            }
            else
            {
                Process.Start(new ProcessStartInfo(targetUrl) { UseShellExecute = true });
                Thread.Sleep((int)(Config.BROWSER_LOAD_WAIT_TIME * 1000));
            }

            if (chromeHandle != IntPtr.Zero)
            {
                SetForegroundWindow(chromeHandle);
                Thread.Sleep(300);
            }

            SendKeys.SendWait("^v");
            Thread.Sleep(500);
            if (submitSearch)
            {
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(500);
                SendKeys.SendWait("{ENTER}");
            }
        }

        private async void StartOcrScan()
        {
            ocrReady = false;
            textRegions.Clear();

            try
            {
                var engine = OcrEngine.TryCreateFromUserProfileLanguages() ??
                             OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
                if (engine == null)
                {
                    ShowOcrError("OCR no disponible en este sistema.");
                    return;
                }

                var softwareBitmap = await ToSoftwareBitmapAsync(originalImage);
                var result = await engine.RecognizeAsync(softwareBitmap);
                BuildTextRegions(result);

                Debug.WriteLine($"OCR listo. Regiones detectadas: {textRegions.Count}");

                if (overlayForm.IsHandleCreated)
                {
                    overlayForm.BeginInvoke(new Action(() =>
                    {
                        ocrReady = true;
                        pictureBox.Invalidate();
                    }));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OCR error: {ex}");
                ShowOcrError($"No se pudo inicializar OCR: {ex.Message}");
            }
        }

        private async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;

            using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(ms.ToArray().AsBuffer());
            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            return SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        private void BuildTextRegions(OcrResult result)
        {
            var regions = new List<TextRegion>();
            if (result?.Lines == null || result.Lines.Count == 0)
            {
                textRegions = regions;
                return;
            }
            foreach (var line in result.Lines)
            {
                Rectangle lineBounds = RectFromWords(line.Words);
                if (lineBounds.Width < 4 || lineBounds.Height < 4) continue;

                TextRegion region = new TextRegion { Bounds = lineBounds };
                foreach (var word in line.Words)
                {
                    region.Words.Add(new WordBox
                    {
                        Bounds = RectFromRect(word.BoundingRect),
                        Text = word.Text
                    });
                }
                regions.Add(region);
            }

            textRegions = regions;
        }

        private void ShowOcrError(string message)
        {
            if (ocrErrorShown) return;
            ocrErrorShown = true;
            if (overlayForm.IsHandleCreated)
            {
                overlayForm.BeginInvoke(new Action(() =>
                    MessageBox.Show(overlayForm, message, "OCR", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            }
        }

        private Rectangle RectFromWords(IReadOnlyList<OcrWord> words)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var word in words)
            {
                Rectangle r = RectFromRect(word.BoundingRect);
                minX = Math.Min(minX, r.Left);
                minY = Math.Min(minY, r.Top);
                maxX = Math.Max(maxX, r.Right);
                maxY = Math.Max(maxY, r.Bottom);
            }

            if (minX == int.MaxValue) return Rectangle.Empty;
            return Rectangle.FromLTRB(minX, minY, maxX, maxY);
        }

        private Rectangle RectFromRect(dynamic rect)
        {
            return new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        }

        private bool ShouldUseTextMode(Rectangle rect)
        {
            if (!ocrReady) return false;
            var regions = FindRegionsForRect(rect);
            if (regions.Count == 0) return false;

            double rectArea = Math.Max(1.0, rect.Width * rect.Height);
            double bestOverlapRectRatio = 0;
            double bestAreaFactor = double.MaxValue;

            foreach (var region in regions)
            {
                double overlapArea = OverlapArea(rect, region.Bounds);
                double overlapRectRatio = overlapArea / rectArea;
                double areaFactor = rectArea / Math.Max(1.0, region.Bounds.Width * region.Bounds.Height);

                if (overlapRectRatio > bestOverlapRectRatio)
                {
                    bestOverlapRectRatio = overlapRectRatio;
                    bestAreaFactor = areaFactor;
                }
            }

            if (bestOverlapRectRatio < 0.3) return false; // más laxo

            if (bestAreaFactor > 3.0 && bestOverlapRectRatio < 0.75) return false;

            return true;
        }

        private List<TextRegion> FindRegionsForRect(Rectangle rect)
        {
            // Amplía más la ventana para incluir líneas arriba/abajo del párrafo
            Rectangle expanded = Rectangle.Inflate(rect, 32, 32);
            return textRegions.Where(r => r.Bounds.IntersectsWith(expanded)).ToList();
        }

        private Rectangle ClampToRegions(Rectangle rect, List<TextRegion> regions)
        {
            if (regions.Count == 0) return rect;
            Rectangle union = UnionBounds(regions.Select(r => r.Bounds));

            int maxWidth = Math.Max(1, union.Width);
            int maxHeight = Math.Max(1, union.Height);

            int x = Math.Max(union.Left, Math.Min(rect.X, union.Right - 1));
            int y = Math.Max(union.Top, Math.Min(rect.Y, union.Bottom - 1));

            int w = Math.Min(rect.Width, union.Right - x);
            int h = Math.Min(rect.Height, union.Bottom - y);

            w = Math.Max(1, Math.Min(w, maxWidth));
            h = Math.Max(1, Math.Min(h, maxHeight));

            return new Rectangle(x, y, w, h);
        }

        private double OverlapArea(Rectangle a, Rectangle b)
        {
            int x1 = Math.Max(a.Left, b.Left);
            int y1 = Math.Max(a.Top, b.Top);
            int x2 = Math.Min(a.Right, b.Right);
            int y2 = Math.Min(a.Bottom, b.Bottom);
            if (x2 <= x1 || y2 <= y1) return 0;
            return (x2 - x1) * (y2 - y1);
        }

        private void UpdateTextSelection(Rectangle candidate, bool ensureWord)
        {
            if (activeTextRegions.Count == 0) return;
            Rectangle clamped = ClampToRegions(candidate, activeTextRegions);

            // Mantener el orden original de las líneas OCR: por Bounds.Top y luego por Bounds.Left
            var orderedRegions = activeTextRegions
                .OrderBy(r => r.Bounds.Top)
                .ThenBy(r => r.Bounds.Left)
                .ToList();

            var words = new List<WordBox>();
            foreach (var region in orderedRegions)
            {
                var lineWords = region.Words
                    .Where(w => clamped.IntersectsWith(w.Bounds))
                    .OrderBy(w => w.Bounds.Left)
                    .ToList();
                if (lineWords.Count > 0)
                {
                    words.AddRange(lineWords);
                }
            }

            if (words.Count == 0 && ensureWord)
            {
                var center = new Point(clamped.Left + clamped.Width / 2, clamped.Top + clamped.Height / 2);
                var nearest = activeTextRegions
                    .SelectMany(r => r.Words)
                    .OrderBy(w => DistanceToRect(center, w.Bounds))
                    .FirstOrDefault();
                if (nearest != null)
                {
                    words.Add(nearest);
                }
            }

            if (words.Count == 0) return;

            selectedWords = words;
            selectionRect = UnionBounds(words.Select(w => w.Bounds));
        }

        private Rectangle UnionBounds(IEnumerable<Rectangle> rects)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var r in rects)
            {
                minX = Math.Min(minX, r.Left);
                minY = Math.Min(minY, r.Top);
                maxX = Math.Max(maxX, r.Right);
                maxY = Math.Max(maxY, r.Bottom);
            }

            if (minX == int.MaxValue) return Rectangle.Empty;
            return Rectangle.FromLTRB(minX, minY, maxX, maxY);
        }

        private double DistanceToRect(Point p, Rectangle rect)
        {
            int dx = Math.Max(rect.Left - p.X, 0);
            dx = Math.Max(dx, p.X - rect.Right);
            int dy = Math.Max(rect.Top - p.Y, 0);
            dy = Math.Max(dy, p.Y - rect.Bottom);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private string GetSelectedText()
        {
            if (selectedWords.Count == 0) return string.Empty;

            // selectedWords ya está en orden de líneas y de izquierda a derecha
            var sb = new System.Text.StringBuilder();
            int? currentTop = null;
            int lineGap = 12; // tolerancia vertical más amplia

            foreach (var word in selectedWords)
            {
                if (currentTop == null || Math.Abs(word.Bounds.Top - currentTop.Value) > lineGap)
                {
                    if (sb.Length > 0) sb.AppendLine();
                    currentTop = word.Bounds.Top;
                }
                else
                {
                    if (sb.Length > 0 && sb[^1] != '\n') sb.Append(' ');
                }
                sb.Append(word.Text);
            }

            return sb.ToString();
        }

        private class TextRegion
        {
            public Rectangle Bounds { get; set; }
            public List<WordBox> Words { get; set; } = new List<WordBox>();
        }

        private class WordBox
        {
            public Rectangle Bounds { get; set; }
            public string Text { get; set; } = string.Empty;
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
