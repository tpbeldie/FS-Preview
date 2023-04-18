using FSPreview.Controls;
using FSPreview.MediaPlayer;
using Jacobi.Vst.Core;
using System.IO;
using System.Reflection;

namespace FSPreview
{
    public partial class PluginEditorUi : UserControl
    {

        private Player m_player;

        public Plugin m_plugin;

        private BitwigServer m_bwServer;

        private TimeSpan m_startedPlayingFrom;

        private double m_initialStartPoint = 0.0;

        private int m_mouseX, m_mouseY, m_sizeWidth, m_sizeHeight;

        private bool m_isResizing;

        private const int ResizePadding = 30;

        private float m_aspectRatio;

        private Form m_fullscreenForm;

        private bool m_fullscreen;

        private float m_progress = 0.0f;

        private bool m_muted;

        private int m_mutedVolume;

        private readonly string m_guiDevMessage;

        private readonly Font m_smallFont;

        private readonly string m_guiTutMessage;

        public string StrDuration => new TimeSpan(Player.Duration).ToString(@"mm\:ss\.fff");

        public string StrCurTime => new TimeSpan(Player.curTime).ToString(@"mm\:ss\.fff");

        Rectangle BottomRight => new Rectangle(Width - ResizePadding, Height - ResizePadding, ResizePadding, ResizePadding);

        private double m_tempo = 60.00;

        public Player Player {
            get => m_player;
            set {
                m_player = value;
            }
        }

        string[] m_ffmpeg64compiled = new string[] {
            "avcodec-58.dll",
            "avdevice-58.dll",
            "avformat-58.dll", 
            "avutil-56.dll", 
            "postproc-55.dll",
            "swresample-3.dll", 
            "swscale-5.dll"
        };

        string[] m_allowedExtensions = {
            "mp4",
            "m4v", 
            "m4e", 
            "mkv",
            "mpg", 
            "mpeg", 
            "mpv", 
            "mp4p", 
            "mpeg",
            "m1v", 
            "m2ts", 
            "m2p", 
            "m2v",
            "movhd",
            "moov",
            "movie", 
            "movx", 
            "mjp",
            "mjpeg", 
            "mjpg", 
            "amv", 
            "asf",
            "m4v",
            "3gp", 
            "ogm", 
            "ogg", 
            "vob", 
            "ts",
            "rm",
            "3gp", 
            "3gp2", 
            "3gpp", 
            "3g2", 
            "f4v", 
            "f4a", 
            "f4p",
            "f4b", 
            "mts", 
            "m2ts", 
            "gifv",
            "avi",
            "mov", 
            "flv", 
            "wmv",
            "qt",
            "avchd",
            "swf", 
            "cam", 
            "nsv", 
            "ram",
            "rm", 
            "x264",
            "xvid",
            "wmx", 
            "wvx",
            "wx", 
            "video", 
            "viv", 
            "vivo",
            "vid", 
            "dat", 
            "bik",
            "bix", 
            "dmf", 
            "divx" 
        };

        public PluginEditorUi() {
            InitializeComponent();
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            DoubleBuffered = true;
            Config config = new Config();
            config.Player.AutoPlay = false;
            Player = new Player(config);
            Player.Control = new FsPlayer() { Parent = panel1 };
            Player.Control.Dock = DockStyle.Fill;
            m_aspectRatio = (float)Width / Height;
            MinimumSize = new Size(Width - (int)(Width * 0.3), Height - (int)(Height * 0.3));
            PluginFr.s_bwServer.ResponseReceived += (s, r) => {
                HandleBitwigStateChanges(s, r);
            };
            Player.Control.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Right) {
                    contextMenuStrip1.Show(MousePosition);
                }
            };
            panel1.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Right) {
                    contextMenuStrip2.Show(MousePosition);
                }
            };
            Player.Control.DoubleClick += (s, e) => {
                ToggleFullScreen();
            };
            Player.Control.MouseMove += (s, e) => {
                Cursor = Cursors.Default;
            };
            PluginFr.s_bwServer.Listen();
            timer1.Start();
            Player.Control.Hide();
            m_smallFont = new Font("Consolas", 8);
            m_guiDevMessage = "Project created by tpbeldie \r\n https://github.com/tpbeldie";
            m_guiTutMessage = "Right click or drag and drop a video file to begin. \r\nScroll wheel to control volume.";
            contextMenuStrip2ToolStripMenuItem.Click += loadVideoToolStripMenuItem_Click;
            LoadFile(string.Empty);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string tempoPath = Path.Combine(userProfile, ".tpbeldie", "tempo");
            m_tempo = double.Parse(File.ReadAllText(tempoPath));
            lblTime.Text = "00:00:00 / 00:00:00 - " + m_tempo.ToString("0.00##") + " BPM";
            string FFmpegPath = Path.Combine(userProfile, ".tpbeldie", "FFmpeg");
            Directory.CreateDirectory(FFmpegPath);
            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach(var ffdll in m_ffmpeg64compiled) {
                Stream resourceStream = assembly.GetManifestResourceStream($"FSPreview.FFmpeg.{ffdll}");
                using (FileStream fileStream = File.Create(Path.Combine(FFmpegPath, ffdll))) {
                    resourceStream.CopyTo(fileStream);
                }
            }
            Master.RegisterFFmpeg(FFmpegPath);
        }

        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            var cursor = PointToClient(MousePosition);
            if (!BottomRight.Contains(cursor)) {
                return;
            }
            m_isResizing = true;
            m_mouseY = MousePosition.Y;
            m_mouseX = MousePosition.X;
            m_sizeWidth = Width;
            m_sizeHeight = Height;
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            m_isResizing = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);
            var cursor = PointToClient(Cursor.Position);
            if (BottomRight.Contains(cursor)) {
                Cursor = Cursors.SizeNWSE;
            }
            else {
                Cursor = Cursors.Default;
            }
            Invalidate();
            if (m_isResizing == true) {
                int newWidth = Math.Max(MousePosition.X - m_mouseX + m_sizeWidth, MinimumSize.Width);
                int newHeight = (int)(newWidth / m_aspectRatio);
                if (newHeight < MinimumSize.Height) {
                    newHeight = MinimumSize.Height;
                    newWidth = (int)(newHeight * m_aspectRatio);
                }
                Width = newWidth;
                Height = newHeight;
                PluginFr.Plugin.Host?.GetInstance<IVstHostCommands20>()?.SizeWindow(Width, Height);
                PluginFr.Plugin.Host?.GetInstance<IVstHostCommands20>()?.UpdateDisplay();
                Refresh();
            }
        }

        private void HandleBitwigStateChanges(object sender, BitwigReply reply) {

            // Tempo changed.
            if (reply.MessageType == BitwigChangeNotifyType.Tempo) {
                // lblTempo.Text = reply.Message.ToString("0.00##");
                // lblTempo.Invalidate();
                m_tempo = reply.Message;
            }

            // Transport position/time changed.
            if (reply.MessageType == BitwigChangeNotifyType.TransportPosition) {
                /*  m_initialStartPoint = reply.Message; */
                if (!Player.IsPlaying) {
                    Player.SeekAccurate((int)(m_initialStartPoint * 1_000));
                }
                else {
                    // Ensure sync.
                    if (Player.IsSeeking == false) {
                        double tempoBpm = m_tempo;
                        double beatDuration = 60.0 / tempoBpm;
                        TimeSpan range = TimeSpan.FromSeconds(3);
                        TimeSpan currentPlayTime = TimeSpan.FromTicks(Player.curTime);
                        TimeSpan transportPosTime = TimeSpan.FromSeconds(reply.Message * beatDuration);
                        TimeSpan playTimePlus = currentPlayTime.Add(range);
                        TimeSpan playTimeMinus = currentPlayTime.Subtract(range);
                        // label1.Text = currentPlayTime.ToString() + " | " + transportPosTime.ToString();
                        // label1.Invalidate();
                        if (transportPosTime > playTimePlus || transportPosTime < playTimeMinus) {
                            // Seek to the adjusted transport position
                            Player.SeekAccurate((int)(transportPosTime.Ticks / TimeSpan.TicksPerMillisecond));
                        }
                    }
                }
            }

            // Transport playback start time changed.
            if (reply.MessageType == BitwigChangeNotifyType.TransportStart) {
                m_startedPlayingFrom = TimeSpan.FromSeconds(reply.Message);
                if (!Player.IsPlaying) {
                    Player.SeekAccurate((int)(reply.Message * 1_000));
                }
                m_initialStartPoint = reply.Message;
            }

            // Play state change.
            if (reply.MessageType == BitwigChangeNotifyType.PlayState) {
                // Playing
                if (Convert.ToBoolean(reply.Message)) {
                    if (!Player.IsPlaying) {
                        double transportInitialPosition = AskTransmitter();
                        m_initialStartPoint = transportInitialPosition;
                        Player.SeekAccurate((int)(transportInitialPosition * 1_000));
                        Player.Play();
                    }
                }
                else {
                    // Paused/Stopped.
                    if (Player.IsPlaying) {
                        Player.SeekAccurate((int)(m_initialStartPoint * 1_000));
                        Player.TogglePlayPause();
                    }
                }
            }
        }

        private void timer1_Tick(object sender, EventArgs e) {
            lblTime.Text = $"{StrCurTime} / {StrDuration} - " + m_tempo.ToString("0.00##") + " BPM";
            if (Player.IsPlaying) {
                Invalidate();
            }

            if (Player.Duration <= 0) {
                return;
            }
            var newProgress = Player.curTime / Player.Duration * (float)Width;
            if (m_progress != newProgress) {
                m_progress = newProgress;
                Invalidate();
            }
        }

        private void fullScreenToolStripMenuItem_Click(object sender, EventArgs e) {
            ToggleFullScreen();
        }

        private void ToggleFullScreen() {
            if (!m_fullscreen) {
                m_fullscreenForm = new Form();
                m_fullscreenForm.ShowInTaskbar = false;
                m_fullscreenForm.FormBorderStyle = FormBorderStyle.None;
                m_fullscreenForm.TopMost = true;
                m_fullscreenForm.Size = Screen.FromControl(m_fullscreenForm).Bounds.Size;
                m_fullscreenForm.Location = new Point(0, 0);
                Player.Control.Parent = m_fullscreenForm;
                Player.Control.Dock = DockStyle.Fill;
                m_fullscreenForm.Show();
                Player.FullScreen();
                m_fullscreen = true;
                fullScreenToolStripMenuItem.Text = "Normal Screen";
                Refresh();
            }
            else {
                fullScreenToolStripMenuItem.Text = "Full Screen";
                Player.Control.Parent = panel1;
                Player.NormalScreen();
                Player.Control.Dock = DockStyle.Fill;
                m_fullscreenForm.Dispose();
                Refresh();
                m_fullscreen = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var fr = new RectangleF(0, Height - 20, m_progress, 20);
            e.Graphics.FillRectangle(Brushes.DarkGray, fr);
            int negOffset = 2;
            for (int i = 0; i < 4; i++) {
                e.Graphics.FillRectangle(Brushes.White, Width - negOffset - 4, Height - 16, 4, 4);
                e.Graphics.FillRectangle(Brushes.White, Width - negOffset - 4, Height - 8, 4, 4);
                negOffset += 8;
            }
        }

        private void loadVideoToolStripMenuItem_Click(object sender, EventArgs e) {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            string filters = string.Join(";", m_allowedExtensions.Select(ext => $"*.{ext}"));
            openFileDialog.Filter = $"Video Files |{filters}|All Files (*.*)|*.*";
            if (openFileDialog.ShowDialog() == DialogResult.OK) {
                string filePath = openFileDialog.FileName;
                LoadFile(filePath);
            }
        }

        private void LoadFile(string filePath = "") {
            try {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string lastFilePath = Path.Combine(userProfile, ".tpbeldie", "lastfile");
                if (string.IsNullOrEmpty(filePath)) {
                    if (File.Exists(lastFilePath)) {
                        filePath = File.ReadAllText(lastFilePath).Trim();
                    }
                    else { return; }
                }
                else {
                    File.WriteAllText(lastFilePath, filePath);
                }
                Player.Open(filePath);
                double transportInitialPosition = AskTransmitter();
                m_initialStartPoint = transportInitialPosition;
                // Player.Seek((int)(transportInitialPosition * 1_000));
                Player.SeekAccurate((int)(transportInitialPosition * 1_000));
                Player.Control.Show();
            } catch {
                MessageBox.Show("Could not load video!");
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            if (e.Delta > 1) {
                Player.Audio.VolumeUp();
            }
            else {
                Player.Audio.VolumeDown();
            }
        }

        private void panel1_Paint(object sender, PaintEventArgs e) {
            e.Graphics.Clear(Color.FromArgb(60, 60, 60));
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            TextRenderer.DrawText(e.Graphics, "(ﾉ◕ヮ◕)ﾉ*:･ﾟ✧", m_smallFont, DisplayRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var measurement = TextRenderer.MeasureText(m_guiDevMessage, m_smallFont);
            TextRenderer.DrawText(e.Graphics, m_guiDevMessage, m_smallFont, new Point(Width / 2 - measurement.Width / 2, Height - 60), Color.White);
            TextRenderer.DrawText(e.Graphics, m_guiTutMessage, m_smallFont, new Rectangle(0, 0, Width, 50), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void panel1_DragEnter(object sender, DragEventArgs e) {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                e.Effect = DragDropEffects.Copy;
            }
        }

        private bool IsVideoFile(string file) {
            string extension = Path.GetExtension(file).ToLower();
            return m_allowedExtensions.Any(x => x == extension);
        }

        private void panel1_DragDrop(object sender, DragEventArgs e) {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0) {
                string file = files[0];
                if (IsVideoFile(file)) {
                    LoadFile(file);
                }
            }
        }

        private void forceSyncToolStripMenuItem_Click(object sender, EventArgs e) {
            double transportInitialPosition = AskTransmitter();
            m_initialStartPoint = transportInitialPosition;
            Player.SeekAccurate((int)(transportInitialPosition * 1_000));
        }

        private void muteToolStripMenuItem_Click(object sender, EventArgs e) {
            if (!m_muted) {
                m_mutedVolume = Player.Audio.Volume;
                Player.Audio.Volume = 0;
                m_muted = true;
                muteToolStripMenuItem.Text = "Unmute";
            }
            else {
                Player.Audio.Volume = m_mutedVolume;
                m_muted = false;
                muteToolStripMenuItem.Text = "Mute";
            }
        }

        private double AskTransmitter() {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string transmitterPath = Path.Combine(userProfile, ".tpbeldie", "transport");
            double transportInitialPosition = double.Parse(File.ReadAllText(transmitterPath));
            return transportInitialPosition;
        }
    }
}