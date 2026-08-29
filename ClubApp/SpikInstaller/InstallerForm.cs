using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AetherShell.Installer
{
    public partial class InstallerForm : Form
    {
        private Label statusLabel;
        private Label serverHintLabel;
        private Label headerLeftLabel;
        private Label headerRightLabel;
        private Label brandTitleLabel;
        private Label brandSubLabel;
        private Label footerLabel;
        private TextBox serverUrlBox;
        private TextBox usernameBox;
        private TextBox passwordBox;
        private CheckBox autoLoginCheck;
        private Button installButton;
        private Button closeButton;
        private Panel optionsPanel;
        private Label userLabel;
        private Label passLabel;
        private Label serverLabel;

        private readonly string _installPath = @"C:\AetherShell";
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        private string _clubKey = "";
        private string _clubName = "";
        private int _progress;
        private float _animPhase;
        private float _ringAngle;
        private readonly System.Windows.Forms.Timer _animTimer;
        private bool _installing;
        private Image? _logoImage;

        private static readonly Color BgDeep = Color.FromArgb(4, 2, 8);
        private static readonly Color AccentPink = Color.FromArgb(255, 55, 95);
        private static readonly Color AccentPurple = Color.FromArgb(191, 90, 242);
        private static readonly Color Muted = Color.FromArgb(162, 153, 184);
        private static readonly Color FieldBg = Color.FromArgb(18, 12, 28);
        private static readonly Color FieldBorder = Color.FromArgb(40, 255, 255, 255);

        public InstallerForm()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            InitializeComponent();
            _animTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _animTimer.Tick += (_, __) =>
            {
                _animPhase += 0.018f;
                _ringAngle = (_ringAngle + 1.6f) % 360f;
                Invalidate();
            };
            _animTimer.Start();
            _logoImage = LoadEmbeddedLogo();
            TryLoadServerConfig();
        }

        private static Image? LoadEmbeddedLogo()
        {
            try
            {
                var asm = typeof(InstallerForm).Assembly;
                var name = Array.Find(asm.GetManifestResourceNames(),
                    n => n.EndsWith("aether-logo.png", StringComparison.OrdinalIgnoreCase));
                if (name == null) return null;
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) return null;
                return Image.FromStream(stream);
            }
            catch
            {
                return null;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            _logoImage?.Dispose();
            base.OnFormClosed(e);
        }

        private void TryLoadServerConfig()
        {
            var configPath = ResolveBesideInstaller("server.config");
            if (configPath == null)
            {
                serverHintLabel.Text = "server.config не найден — распакуйте zip из кабинета целиком";
                serverHintLabel.ForeColor = Color.FromArgb(240, 140, 100);
                return;
            }

            try
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (value.Length == 0) continue;

                    switch (key.ToUpperInvariant())
                    {
                        case "SERVER_URL":
                            serverUrlBox.Text = value;
                            serverUrlBox.ReadOnly = true;
                            serverUrlBox.ForeColor = Color.FromArgb(180, 255, 200);
                            break;
                        case "CLUB_KEY":
                            _clubKey = value;
                            break;
                        case "CLUB_NAME":
                            _clubName = value;
                            break;
                    }
                }

                if (_clubName.Length > 0)
                {
                    serverHintLabel.Text = "Клуб: " + _clubName;
                    serverHintLabel.ForeColor = Color.FromArgb(120, 220, 160);
                }
            }
            catch (Exception ex)
            {
                serverHintLabel.Text = "Не удалось прочитать server.config: " + ex.Message;
                serverHintLabel.ForeColor = Color.FromArgb(240, 140, 100);
            }
        }

        private static string GetInstallerDirectory()
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    var dir = Path.GetDirectoryName(processPath);
                    if (!string.IsNullOrEmpty(dir))
                        return dir;
                }
            }
            catch { }

            try
            {
                var main = System.Reflection.Assembly.GetEntryAssembly()?.Location
                    ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(main))
                {
                    var dir = Path.GetDirectoryName(main);
                    if (!string.IsNullOrEmpty(dir) && !dir.Contains($"{Path.DirectorySeparatorChar}.net{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                        return dir;
                }
            }
            catch { }

            var cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrEmpty(cwd))
                return cwd;

            return AppContext.BaseDirectory;
        }

        private static string? ResolveBesideInstaller(string fileName)
        {
            foreach (var dir in new[]
                     {
                         GetInstallerDirectory(),
                         Directory.GetCurrentDirectory(),
                         AppContext.BaseDirectory
                     })
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    var path = Path.GetFullPath(Path.Combine(dir, fileName));
                    if (File.Exists(path))
                        return path;
                }
                catch { }
            }
            return null;
        }

        private void InitializeComponent()
        {
            Text = "AetherShell — Установка";
            Size = new Size(1000, 700);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDeep;
            Font = new Font("Segoe UI", 10f);

            headerLeftLabel = MakeLabel("ИНИЦИАЛИЗАЦИЯ СИСТЕМЫ", 12f, FontStyle.Bold, Muted, ContentAlignment.MiddleLeft);
            headerLeftLabel.Location = new Point(48, 24);
            headerLeftLabel.Size = new Size(360, 24);
            headerLeftLabel.ForeColor = Color.FromArgb(110, Muted);

            headerRightLabel = MakeLabel("Desktop v2.4.0", 12f, FontStyle.Bold, Muted, ContentAlignment.MiddleRight);
            headerRightLabel.Location = new Point(Width - 220, 24);
            headerRightLabel.Size = new Size(170, 24);
            headerRightLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            headerRightLabel.ForeColor = Color.FromArgb(110, Muted);

            closeButton = new Button
            {
                Text = "✕",
                FlatStyle = FlatStyle.Flat,
                Size = new Size(36, 32),
                Location = new Point(Width - 48, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.Transparent,
                ForeColor = Muted,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 255, 55, 95);
            closeButton.Click += (_, __) => Close();

            brandTitleLabel = MakeLabel("AETHERSHELL", 36f, FontStyle.Bold, Color.White, ContentAlignment.MiddleCenter);
            brandTitleLabel.Size = new Size(600, 70);
            brandTitleLabel.Location = new Point((Width - 600) / 2, 250);

            brandSubLabel = MakeLabel("УСТАНОВКА ШЕЛЛА", 13f, FontStyle.Bold, Muted, ContentAlignment.MiddleCenter);
            brandSubLabel.Size = new Size(600, 24);
            brandSubLabel.Location = new Point((Width - 600) / 2, 318);

            optionsPanel = new Panel
            {
                Size = new Size(480, 210),
                Location = new Point((Width - 480) / 2, 360),
                BackColor = Color.FromArgb(18, 255, 255, 255)
            };
            // glass-ish via paint
            optionsPanel.Paint += OptionsPanel_Paint;

            serverLabel = MakeLabel("АДРЕС СЕРВЕРА", 9f, FontStyle.Bold, Muted, ContentAlignment.MiddleLeft);
            serverLabel.Location = new Point(20, 12);
            serverLabel.Size = new Size(200, 16);

            serverUrlBox = MakeField(new Point(20, 32), new Size(440, 32));

            serverHintLabel = MakeLabel("Заполняется из server.config", 8.5f, FontStyle.Regular, Color.FromArgb(100, 116, 139), ContentAlignment.MiddleLeft);
            serverHintLabel.Location = new Point(20, 68);
            serverHintLabel.Size = new Size(440, 18);

            autoLoginCheck = new CheckBox
            {
                Text = "Автоматический вход в Windows",
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.White,
                Location = new Point(20, 94),
                Size = new Size(300, 22),
                Checked = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent
            };
            autoLoginCheck.CheckedChanged += AutoLoginCheck_Changed;

            userLabel = MakeLabel("ПОЛЬЗОВАТЕЛЬ", 9f, FontStyle.Bold, Muted, ContentAlignment.MiddleLeft);
            userLabel.Location = new Point(20, 124);
            userLabel.Size = new Size(200, 16);

            usernameBox = MakeField(new Point(20, 144), new Size(200, 32));
            usernameBox.Text = Environment.UserName;

            passLabel = MakeLabel("ПАРОЛЬ", 9f, FontStyle.Bold, Muted, ContentAlignment.MiddleLeft);
            passLabel.Location = new Point(240, 124);
            passLabel.Size = new Size(200, 16);

            passwordBox = MakeField(new Point(240, 144), new Size(220, 32));
            passwordBox.UseSystemPasswordChar = true;

            optionsPanel.Controls.Add(serverLabel);
            optionsPanel.Controls.Add(serverUrlBox);
            optionsPanel.Controls.Add(serverHintLabel);
            optionsPanel.Controls.Add(autoLoginCheck);
            optionsPanel.Controls.Add(userLabel);
            optionsPanel.Controls.Add(usernameBox);
            optionsPanel.Controls.Add(passLabel);
            optionsPanel.Controls.Add(passwordBox);

            installButton = new Button
            {
                Text = "Установить",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Size = new Size(480, 44),
                Location = new Point((Width - 480) / 2, 582),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            installButton.FlatAppearance.BorderSize = 0;
            installButton.Paint += InstallButton_Paint;
            installButton.Click += InstallButton_Click;

            statusLabel = MakeLabel("ГОТОВ К УСТАНОВКЕ", 11f, FontStyle.Bold, Muted, ContentAlignment.MiddleCenter);
            statusLabel.Size = new Size(480, 22);
            statusLabel.Location = new Point((Width - 480) / 2, 548);

            footerLabel = MakeLabel("POWERED BY AETHERSHELL", 10f, FontStyle.Bold, Color.FromArgb(70, Muted), ContentAlignment.MiddleCenter);
            footerLabel.Size = new Size(480, 18);
            footerLabel.Location = new Point((Width - 480) / 2, 608);
            footerLabel.Anchor = AnchorStyles.Bottom;

            Controls.Add(headerLeftLabel);
            Controls.Add(headerRightLabel);
            Controls.Add(closeButton);
            Controls.Add(brandTitleLabel);
            Controls.Add(brandSubLabel);
            Controls.Add(optionsPanel);
            Controls.Add(statusLabel);
            Controls.Add(installButton);
            Controls.Add(footerLabel);

            // Logo sits in painted area above brand — leave space: move brand lower when options shown.
            // Adjust layout for splash-first: logo in paint (y~120), brand at 250.
            LayoutModeReady();
        }

        private void LayoutModeReady()
        {
            brandTitleLabel.Location = new Point((Width - 600) / 2, 150);
            brandSubLabel.Location = new Point((Width - 600) / 2, 218);
            optionsPanel.Visible = true;
            optionsPanel.Location = new Point((Width - 480) / 2, 260);
            statusLabel.Location = new Point((Width - 480) / 2, 500);
            installButton.Visible = true;
            installButton.Location = new Point((Width - 480) / 2, 560);
            footerLabel.Location = new Point((Width - 480) / 2, 630);
        }

        private void LayoutModeInstalling()
        {
            optionsPanel.Visible = false;
            installButton.Visible = false;
            brandTitleLabel.Location = new Point((Width - 600) / 2, 250);
            brandSubLabel.Location = new Point((Width - 600) / 2, 318);
            statusLabel.Location = new Point((Width - 480) / 2, 520);
            footerLabel.Location = new Point((Width - 480) / 2, 600);
        }

        private static Label MakeLabel(string text, float size, FontStyle style, Color color, ContentAlignment align)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", size, style),
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = align,
                AutoSize = false
            };
        }

        private static TextBox MakeField(Point location, Size size)
        {
            return new TextBox
            {
                Location = location,
                Size = size,
                Font = new Font("Segoe UI", 11f),
                BackColor = FieldBg,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private void OptionsPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(0, 0, optionsPanel.Width - 1, optionsPanel.Height - 1), 16);
            using (var brush = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                g.FillPath(brush, path);
            using (var pen = new Pen(Color.FromArgb(36, 255, 255, 255), 1f))
                g.DrawPath(pen, path);
        }

        private void InstallButton_Paint(object? sender, PaintEventArgs e)
        {
            var btn = (Button)sender!;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
            using var path = RoundedRect(rect, 8);
            using (var brush = new LinearGradientBrush(rect, AccentPink, AccentPurple, 0f))
                g.FillPath(brush, path);
            if (!btn.Enabled)
            {
                using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
                g.FillPath(dim, path);
            }
            TextRenderer.DrawText(g, btn.Text, btn.Font, rect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new LinearGradientBrush(ClientRectangle, BgDeep, Color.FromArgb(22, 8, 40), 45f))
                g.FillRectangle(bg, ClientRectangle);

            DrawAmbientBlob(g, 0.28f + 0.08f * MathF.Sin(_animPhase),
                new PointF(Width * 0.28f + 40 * MathF.Sin(_animPhase * 0.7f), Height * 0.35f + 30 * MathF.Cos(_animPhase * 0.5f)),
                420, Color.FromArgb(140, 191, 90, 242));
            DrawAmbientBlob(g, 0.32f + 0.1f * MathF.Cos(_animPhase * 0.9f),
                new PointF(Width * 0.72f + 50 * MathF.Cos(_animPhase * 0.6f), Height * 0.55f + 40 * MathF.Sin(_animPhase * 0.8f)),
                340, Color.FromArgb(150, 255, 55, 95));
            DrawAmbientBlob(g, 0.22f + 0.06f * MathF.Sin(_animPhase * 1.1f),
                new PointF(Width * 0.55f, Height * 0.2f + 25 * MathF.Sin(_animPhase)),
                280, Color.FromArgb(100, 139, 92, 246));

            // Logo circle above brand
            float logoY = _installing ? 120f : 70f;
            float logoSize = _installing ? 120f : 88f;
            DrawLogo(g, Width / 2f, logoY, logoSize);

            // Progress track (always, more visible while installing)
            float barWidth = 480f;
            float barX = (Width - barWidth) / 2f;
            float barY = _installing ? 480f : 488f;
            DrawProgressBar(g, barX, barY, barWidth, Math.Max(_progress, _installing ? 2 : 0));
        }

        private static void DrawAmbientBlob(Graphics g, float opacity, PointF center, float radius, Color color)
        {
            var rect = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            using var path = new GraphicsPath();
            path.AddEllipse(rect);
            using var brush = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb((int)(opacity * 255), color),
                SurroundColors = new[] { Color.FromArgb(0, color) }
            };
            g.FillPath(brush, path);
        }

        private void DrawLogo(Graphics g, float cx, float cy, float size)
        {
            var dest = new RectangleF(cx - size / 2f, cy - size / 2f, size, size);

            // Soft glow behind logo
            using (var glow = new SolidBrush(Color.FromArgb(50, AccentPurple)))
                g.FillEllipse(glow, dest.X - 10, dest.Y - 10, dest.Width + 20, dest.Height + 20);

            if (_logoImage != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_logoImage, dest);
                return;
            }

            // Fallback geometric mark if asset missing
            float r = size / 2f;
            using (var pen = new Pen(Color.FromArgb(90, AccentPurple), 2.2f))
                g.DrawEllipse(pen, cx - r, cy - r, size, size);
            using (var pen = new Pen(AccentPink, 3.2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawArc(pen, cx - r, cy - r, size, size, _ringAngle, 110);
            }
        }

        private void DrawProgressBar(Graphics g, float x, float y, float width, int progress)
        {
            float height = 6f;
            var track = new RectangleF(x, y, width, height);
            using (var path = RoundedRectF(track, 3))
            using (var brush = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
                g.FillPath(brush, path);

            float fillW = Math.Max(8f, width * Math.Clamp(progress, 0, 100) / 100f);
            var fill = new RectangleF(x, y, fillW, height);
            using (var path = RoundedRectF(fill, 3))
            using (var brush = new LinearGradientBrush(fill, AccentPink, AccentPurple, 0f))
                g.FillPath(brush, path);

            float bulbX = x + fillW;
            float bulbY = y + height / 2f;
            using (var glow = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                g.FillEllipse(glow, bulbX - 10, bulbY - 10, 20, 20);
            using (var core = new SolidBrush(Color.White))
                g.FillEllipse(core, bulbX - 5, bulbY - 5, 10, 10);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            return RoundedRectF(rect, radius);
        }

        private static GraphicsPath RoundedRectF(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void AutoLoginCheck_Changed(object? sender, EventArgs e)
        {
            usernameBox.Enabled = autoLoginCheck.Checked;
            passwordBox.Enabled = autoLoginCheck.Checked;
            userLabel.ForeColor = autoLoginCheck.Checked ? Muted : Color.Gray;
            passLabel.ForeColor = autoLoginCheck.Checked ? Muted : Color.Gray;
        }

        private async void InstallButton_Click(object? sender, EventArgs e)
        {
            installButton.Enabled = false;
            try
            {
                await InstallAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка установки: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _installing = false;
                LayoutModeReady();
                installButton.Enabled = true;
                Invalidate();
            }
        }

        private async Task InstallAsync()
        {
            var serverUrl = NormalizeServerUrl(serverUrlBox.Text);

            if (serverUrl == null)
            {
                MessageBox.Show("Укажите адрес сервера, например https://aether.example.com",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                installButton.Enabled = true;
                return;
            }

            if (string.IsNullOrEmpty(_clubKey))
            {
                var proceed = MessageBox.Show(
                    "В server.config нет ключа клуба (CLUB_KEY). Без него шелл не сможет подключиться.\n\n" +
                    "Скачайте установщик из кабинета клуба. Продолжить всё равно?",
                    "Нет ключа клуба", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (proceed != DialogResult.Yes)
                {
                    installButton.Enabled = true;
                    return;
                }
            }

            _installing = true;
            LayoutModeInstalling();
            brandSubLabel.Text = "УСТАНОВКА ШЕЛЛА";
            Invalidate();

            UpdateStatus("СОЗДАНИЕ ПАПКИ УСТАНОВКИ...", 10);
            if (!Directory.Exists(_installPath))
                Directory.CreateDirectory(_installPath);

            UpdateStatus("ПОЛУЧЕНИЕ ФАЙЛОВ КЛИЕНТА...", 20);
            await CopyClientFilesAsync(serverUrl);

            UpdateStatus("НАСТРОЙКА ПОДКЛЮЧЕНИЯ К СЕРВЕРУ...", 40);
            UpdateServerConfig(serverUrl);

            UpdateStatus("УСТАНОВКА СЛУЖБЫ МОНИТОРИНГА...", 50);
            InstallWatchdogService();

            UpdateStatus("ОТКЛЮЧЕНИЕ EXPLORER...", 60);
            DisableExplorerShell();

            UpdateStatus("НАСТРОЙКА АВТОЗАПУСКА...", 70);
            SetupAutoStart();

            if (autoLoginCheck.Checked)
            {
                UpdateStatus("НАСТРОЙКА АВТОМАТИЧЕСКОГО ВХОДА...", 85);
                SetupAutoLogin(usernameBox.Text, passwordBox.Text);
            }

            UpdateStatus("ОПТИМИЗАЦИЯ ОКРУЖЕНИЯ...", 95);
            await Task.Delay(400);
            UpdateStatus("УСТАНОВКА ЗАВЕРШЕНА", 100);

            var clientExe = Path.Combine(_installPath, "AetherShell.Client.exe");
            if (!File.Exists(clientExe))
                clientExe = Path.Combine(_installPath, "clubApp.exe");

            var filesInstalled = File.Exists(clientExe);
            var fileCount = filesInstalled ? Directory.GetFiles(_installPath, "*.*", SearchOption.AllDirectories).Length : 0;

            string? shellValue = null;
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                shellValue = key?.GetValue("Shell")?.ToString();
            }
            catch { }

            var shellReplaced = shellValue != null && (shellValue.Contains("clubApp") || shellValue.Contains("SpikClient") || shellValue.Contains("AetherShell.Client"));

            var summary = $"Установка завершена!\n\n" +
                $"Файлов установлено: {fileCount}\n" +
                $"Путь: {_installPath}\n" +
                $"Shell заменён: {(shellReplaced ? "Да" : "Нет")}\n" +
                $"Shell в реестре: {shellValue ?? "не найден"}\n\n" +
                $"Перезагрузить компьютер сейчас?";

            var result = MessageBox.Show(summary, "Установка завершена",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                Process.Start("shutdown", "/r /t 5 /c \"Перезагрузка для завершения установки AetherShell\"");
                Application.Exit();
            }
            else
            {
                _installing = false;
                LayoutModeReady();
                installButton.Enabled = true;
                Invalidate();
            }
        }

        private static string? NormalizeServerUrl(string raw)
        {
            var value = (raw ?? "").Trim().TrimEnd('/');
            if (value.Length == 0) return null;

            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out _) ? value : null;
        }

        private async Task CopyClientFilesAsync(string serverUrl)
        {
            var installerDir = GetInstallerDirectory();

            var possibleSources = new[]
            {
                Path.Combine(installerDir, "ClientFiles"),
                Path.Combine(installerDir, "..", "ClientFiles"),
                Path.Combine(installerDir, "..", "ClubApp", "bin", "Release"),
                Path.Combine(installerDir, "..", "ClubApp", "bin", "Debug"),
                Path.Combine(installerDir, "..", "..", "ClubApp", "ClubApp", "bin", "Release"),
                Path.Combine(installerDir, "..", "..", "ClubApp", "ClubApp", "bin", "Debug")
            };

            string? sourceDir = null;
            foreach (var src in possibleSources)
            {
                var fullPath = Path.GetFullPath(src);
                var hasClient = File.Exists(Path.Combine(fullPath, "AetherShell.Client.exe")) || File.Exists(Path.Combine(fullPath, "SpikClient.exe")) || File.Exists(Path.Combine(fullPath, "clubApp.exe"));

                if (Directory.Exists(fullPath) && hasClient)
                {
                    sourceDir = fullPath;
                    UpdateStatus("НАЙДЕНЫ ЛОКАЛЬНЫЕ ФАЙЛЫ...", 22);
                    break;
                }
            }

            if (sourceDir != null)
            {
                UpdateStatus("КОПИРОВАНИЕ ФАЙЛОВ КЛИЕНТА...", 25);
                int fileCount = 0;

                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var destPath = Path.Combine(_installPath, relativePath);

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, destPath, true);
                    fileCount++;
                }

                UpdateStatus($"СКОПИРОВАНО {fileCount} ФАЙЛОВ", 35);
                await CopyWatchdogFilesAsync(installerDir);
                return;
            }

            UpdateStatus("ЗАГРУЗКА С СЕРВЕРА...", 22);
            var downloaded = await TryDownloadFromServerAsync(serverUrl);

            if (downloaded)
                return;

            UpdateStatus("ВЫБЕРИТЕ ПАПКУ С КЛИЕНТОМ...", 25);
            using var dialog = new FolderBrowserDialog
            {
                Description = "Выберите папку с файлами AetherShell Client"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                sourceDir = dialog.SelectedPath;

                int fileCount = 0;
                foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceDir, file);
                    var destPath = Path.Combine(_installPath, relativePath);

                    var destDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, destPath, true);
                    fileCount++;
                }

                UpdateStatus($"СКОПИРОВАНО {fileCount} ФАЙЛОВ", 35);
                await CopyWatchdogFilesAsync(installerDir);
            }
            else
            {
                throw new Exception("Файлы клиента не найдены и не выбраны");
            }
        }

        private async Task<bool> TryDownloadFromServerAsync(string serverUrl)
        {
            var clientZipUrl = serverUrl + "/api/download/client";
            var watchdogZipUrl = serverUrl + "/api/download/watchdog";

            var clientZipPath = Path.Combine(Path.GetTempPath(), "AetherShell.Client.zip");
            var watchdogZipPath = Path.Combine(Path.GetTempPath(), "AetherShell.Watchdog.zip");

            try
            {
                UpdateStatus("СКАЧИВАНИЕ КЛИЕНТА...", 20);

                using (var response = await _httpClient.GetAsync(clientZipUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        UpdateStatus($"СЕРВЕР НЕ ВЕРНУЛ КЛИЕНТ ({(int)response.StatusCode})", 22);
                        return false;
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(clientZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    var buffer = new byte[8192];
                    long downloadedBytes = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)(20 + (downloadedBytes * 10 / totalBytes));
                            UpdateStatus($"СКАЧИВАНИЕ: {downloadedBytes / 1024} / {totalBytes / 1024} KB", progress);
                        }
                    }
                }

                UpdateStatus("РАСПАКОВКА КЛИЕНТА...", 32);
                if (Directory.Exists(_installPath))
                {
                    foreach (var file in Directory.GetFiles(_installPath))
                    {
                        if (!file.EndsWith(".config", StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
                ZipFile.ExtractToDirectory(clientZipPath, _installPath, true);

                UpdateStatus("СКАЧИВАНИЕ СЛУЖБЫ МОНИТОРИНГА...", 35);
                try
                {
                    using var watchdogResponse = await _httpClient.GetAsync(watchdogZipUrl);
                    if (watchdogResponse.IsSuccessStatusCode)
                    {
                        using var watchdogStream = await watchdogResponse.Content.ReadAsStreamAsync();
                        using var watchdogFileStream = new FileStream(watchdogZipPath, FileMode.Create);
                        await watchdogStream.CopyToAsync(watchdogFileStream);
                        watchdogFileStream.Close();

                        UpdateStatus("РАСПАКОВКА СЛУЖБЫ...", 38);
                        ZipFile.ExtractToDirectory(watchdogZipPath, _installPath, true);
                    }
                }
                catch
                {
                    UpdateStatus("WATCHDOG НЕДОСТУПЕН, ПРОПУСКАЕМ...", 38);
                }

                try { File.Delete(clientZipPath); } catch { }
                try { File.Delete(watchdogZipPath); } catch { }

                return true;
            }
            catch (HttpRequestException ex)
            {
                UpdateStatus($"ОШИБКА СЕРВЕРА: {ex.Message}", 22);
                return false;
            }
            catch (TaskCanceledException)
            {
                UpdateStatus("ТАЙМАУТ СКАЧИВАНИЯ", 22);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"ОШИБКА СКАЧИВАНИЯ: {ex.Message}", 22);
                return false;
            }
        }

        private async Task CopyWatchdogFilesAsync(string installerDir)
        {
            var watchdogSources = new[]
            {
                Path.Combine(installerDir, "WatchdogFiles"),
                Path.Combine(installerDir, "..", "SpikWatchdog", "bin", "Release", "net8.0-windows"),
                Path.Combine(installerDir, "..", "SpikWatchdog", "bin", "Debug", "net8.0-windows")
            };

            foreach (var src in watchdogSources)
            {
                var fullPath = Path.GetFullPath(src);
                if (Directory.Exists(fullPath) && (File.Exists(Path.Combine(fullPath, "AetherShell.Watchdog.exe")) || File.Exists(Path.Combine(fullPath, "SpikWatchdog.exe"))))
                {
                    foreach (var file in Directory.GetFiles(fullPath))
                        File.Copy(file, Path.Combine(_installPath, Path.GetFileName(file)), true);
                    break;
                }
            }

            await Task.Delay(100);
        }

        private void UpdateServerConfig(string serverUrl)
        {
            var lines = new System.Collections.Generic.List<string> { "SERVER_URL=" + serverUrl };

            if (_clubKey.Length > 0) lines.Add("CLUB_KEY=" + _clubKey);
            if (_clubName.Length > 0) lines.Add("CLUB_NAME=" + _clubName);

            var configPath = Path.Combine(_installPath, "server.config");
            var existingExitPassword = ReadExitPassword(configPath);
            if (existingExitPassword != null) lines.Add("EXIT_PASSWORD=" + existingExitPassword);

            File.WriteAllText(configPath, string.Join(Environment.NewLine, lines));
        }

        private static string? ReadExitPassword(string configPath)
        {
            if (!File.Exists(configPath)) return null;

            try
            {
                foreach (var line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("EXIT_PASSWORD=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("EXIT_PASSWORD=".Length).Trim();
                        return value.Length > 0 ? value : null;
                    }
                }
            }
            catch { }

            return null;
        }

        private void InstallWatchdogService()
        {
            var watchdogExe = Path.Combine(_installPath, "AetherShell.Watchdog.exe");
            if (!File.Exists(watchdogExe))
                return;

            var serviceName = "AetherShell.Watchdog";

            try
            {
                var stopInfo = new ProcessStartInfo("sc.exe", $"stop {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(stopInfo)?.WaitForExit();

                var deleteInfo = new ProcessStartInfo("sc.exe", $"delete {serviceName}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(deleteInfo)?.WaitForExit();

                System.Threading.Thread.Sleep(2000);
            }
            catch { }

            var createInfo = new ProcessStartInfo("sc.exe",
                $"create {serviceName} binPath= \"{watchdogExe}\" start= auto DisplayName= \"AetherShell Watchdog\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(createInfo)?.WaitForExit();

            var failureInfo = new ProcessStartInfo("sc.exe",
                $"failure {serviceName} reset= 86400 actions= restart/5000/restart/10000/restart/30000")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(failureInfo)?.WaitForExit();

            var startInfo = new ProcessStartInfo("sc.exe", $"start {serviceName}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo)?.WaitForExit();
        }

        private void DisableExplorerShell()
        {
            var clientExe = Path.Combine(_installPath, "AetherShell.Client.exe");
            if (!File.Exists(clientExe))
                clientExe = Path.Combine(_installPath, "clubApp.exe");

            if (!File.Exists(clientExe))
            {
                MessageBox.Show($"Клиент не найден в {_installPath}!\nПроверьте, что файлы скопированы.",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UpdateStatus("ЗАМЕНА SHELL...", 61);

            int successCount = 0;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                {
                    if (key != null)
                    {
                        var originalShell = key.GetValue("Shell")?.ToString();
                        if (!string.IsNullOrEmpty(originalShell) && originalShell != clientExe)
                            key.SetValue("Shell_Backup", originalShell, RegistryValueKind.String);
                        key.SetValue("Shell", clientExe, RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("SHELL ЗАМЕНЁН (HKLM)", 62);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKLM SHELL: {ex.Message}", 62);
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true)
                    ?? Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
                {
                    if (key != null)
                    {
                        key.SetValue("Shell", clientExe, RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("SHELL ЗАМЕНЁН (HKCU)", 63);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKCU SHELL: {ex.Message}", 63);
            }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        try { key.DeleteValue("Explorer", false); } catch { }
                        try { key.DeleteValue("explorer", false); } catch { }
                    }
                }
            }
            catch { }

            if (successCount > 0)
                UpdateStatus("EXPLORER ЗАМЕНЁН НА AETHERSHELL", 65);
            else
                MessageBox.Show("Не удалось заменить shell!\nЗапустите установщик от имени Администратора.",
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void SetupAutoStart()
        {
            var clientExe = Path.Combine(_installPath, "AetherShell.Client.exe");
            if (!File.Exists(clientExe))
                clientExe = Path.Combine(_installPath, "clubApp.exe");

            if (!File.Exists(clientExe))
            {
                UpdateStatus("КЛИЕНТ НЕ НАЙДЕН ДЛЯ АВТОЗАПУСКА", 72);
                return;
            }

            int successCount = 0;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.SetValue("AetherShell.Client", $"\"{clientExe}\"", RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("АВТОЗАПУСК: HKCU", 72);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKCU RUN: {ex.Message}", 72);
            }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.SetValue("AetherShell.Client", $"\"{clientExe}\"", RegistryValueKind.String);
                        successCount++;
                        UpdateStatus("АВТОЗАПУСК: HKLM", 74);
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"HKLM RUN: {ex.Message}", 74);
            }

            try
            {
                var taskName = "AetherShell.ClientAutoStart";

                var deleteInfo = new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{taskName}\" /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(deleteInfo)?.WaitForExit();

                var createInfo = new ProcessStartInfo("schtasks.exe",
                    $"/Create /TN \"{taskName}\" /TR \"\\\"{clientExe}\\\"\" /SC ONLOGON /RL HIGHEST /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var proc = Process.Start(createInfo);
                proc?.WaitForExit();

                if (proc?.ExitCode == 0)
                {
                    successCount++;
                    UpdateStatus("АВТОЗАПУСК: ПЛАНИРОВЩИК", 76);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"ЗАДАЧА: {ex.Message}", 76);
            }

            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                var shortcutPath = Path.Combine(startupFolder, "AetherShell.Client.lnk");

                CreateShortcut(shortcutPath, clientExe);

                if (File.Exists(shortcutPath))
                {
                    successCount++;
                    UpdateStatus("АВТОЗАПУСК: ЯРЛЫК", 78);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"ЯРЛЫК: {ex.Message}", 78);
            }

            UpdateStatus($"НАСТРОЕНО МЕТОДОВ АВТОЗАПУСКА: {successCount}", 80);
        }

        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            try
            {
                var vbsPath = Path.Combine(Path.GetTempPath(), "create_shortcut.vbs");
                var vbsContent = $@"
Set WshShell = WScript.CreateObject(""WScript.Shell"")
Set shortcut = WshShell.CreateShortcut(""{shortcutPath}"")
shortcut.TargetPath = ""{targetPath}""
shortcut.WorkingDirectory = ""{Path.GetDirectoryName(targetPath)}""
shortcut.Save
";
                File.WriteAllText(vbsPath, vbsContent);

                var psi = new ProcessStartInfo("cscript.exe", $"//nologo \"{vbsPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)?.WaitForExit();

                try { File.Delete(vbsPath); } catch { }
            }
            catch { }
        }

        private void SetupAutoLogin(string username, string password)
        {
            if (string.IsNullOrEmpty(username)) return;

            var regPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            using var key = Registry.LocalMachine.OpenSubKey(regPath, true);

            if (key != null)
            {
                key.SetValue("AutoAdminLogon", "1", RegistryValueKind.String);
                key.SetValue("DefaultUserName", username, RegistryValueKind.String);
                key.SetValue("DefaultPassword", password ?? "", RegistryValueKind.String);
                key.SetValue("DefaultDomainName", Environment.MachineName, RegistryValueKind.String);
            }
        }

        private void UpdateStatus(string message, int progress)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateStatus(message, progress)));
                return;
            }

            statusLabel.Text = message;
            _progress = Math.Clamp(progress, 0, 100);
            Invalidate();
            Application.DoEvents();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    var p = PointToClient(Cursor.Position);
                    if (p.Y < 48 && !closeButton.Bounds.Contains(p))
                        m.Result = (IntPtr)HTCAPTION;
                }
                return;
            }
            base.WndProc(ref m);
        }
    }
}
