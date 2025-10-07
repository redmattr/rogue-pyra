// GameForm.cs
// Simple WinForms window for the visual client.
// Now includes an in-game chat panel (TCP) while rendering gameplay (UDP).

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using RoguePyra.Networking;

// Avoid ambiguity with System.Threading.Timer
using WinFormsTimer = System.Windows.Forms.Timer;

namespace RoguePyra.UI
{
    public sealed class GameForm : Form
    {
        // UI-thread copy of latest entities (avoid concurrent enumeration)
        private readonly Dictionary<string, (float x, float y, int hp)> _viewEntities = new();
        // World (render) constants
        private const float WorldW = 840f;
        private const float WorldH = 480f;
        private const float Box    = 24f;

        // Chat panel width (UI)
        private const int ChatPanelW = 280;

        // Networking hub (passed in so we reuse the TCP connection from HostListForm)
        private readonly NetworkManager _net;
        private readonly CancellationTokenSource _cts = new();

        // Input state
        private bool _up, _left, _down, _right;

        // Rendering
        private readonly WinFormsTimer _renderTimer;
        private volatile bool _hasSnapshot;

        // UI controls
        private readonly Label _status;
        private readonly Panel _chatPanel;
        private readonly TextBox _chatLog;
        private readonly TextBox _chatInput;
        private readonly Button _chatSend;

        // Constructor now receives:
        // - hostIp + udpPort: from JOIN_INFO
        // - net: existing NetworkManager that is ALREADY connected to TCP
        public GameForm(string hostIp, int udpPort, NetworkManager net)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));

            // Window setup — widen by ChatPanelW so playfield stays 840x480
            Text = "RoguePyra — Client";
            ClientSize = new Size((int)WorldW + ChatPanelW, (int)WorldH);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            KeyPreview = true;

            // Status bar
            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Text = "Connecting…",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(_status);

            // ---- Chat panel (right) ----
            _chatPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = ChatPanelW,
                BackColor = Color.FromArgb(248, 248, 248),
                Padding = new Padding(8, 8, 8, 8)
            };
            Controls.Add(_chatPanel);

            _chatLog = new TextBox
            {
                Multiline = true,
                ReadOnly  = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill
            };
            _chatPanel.Controls.Add(_chatLog);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            _chatPanel.Controls.Add(bottomPanel);

            _chatInput = new TextBox { Dock = DockStyle.Fill };
            bottomPanel.Controls.Add(_chatInput);

            _chatSend = new Button { Text = "Send", Dock = DockStyle.Right, Width = 66 };
            bottomPanel.Controls.Add(_chatSend);

            _chatSend.Click += async (_, __) => await SendChatAsync();
            _chatInput.KeyDown += async (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await SendChatAsync();
                }
            };

            // Wire events
            KeyDown += OnKeyDown;
            KeyUp   += OnKeyUp;
            FormClosing += OnFormClosing;

            _net.SnapshotReceived += (lavaY, ents) =>
            {
                _hasSnapshot = true;
                // Marshal to UI thread and copy entities into a safe cache
                BeginInvoke(new Action(() =>
                {
                    _status.Text = $"Players: {ents.Count} | LavaY: {lavaY:F1}";

                    _viewEntities.Clear();
                    foreach (var kv in ents)
                    _viewEntities[kv.Key] = kv.Value;  // copy to UI-owned cache

                    Invalidate(); // trigger repaint
                }));
            };


            // Subscribe to TCP chat lines (already connected in HostListForm)
            _net.ChatReceived += line =>
            {
                BeginInvoke(new Action(() =>
                {
                    // Show only relevant lines; you can relax this if you want every INFO too.
                    if (line.StartsWith("SAY ", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("INFO ", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("ERROR ", StringComparison.OrdinalIgnoreCase))
                    {
                        _chatLog.AppendText(line + Environment.NewLine);
                        _chatLog.SelectionStart = _chatLog.TextLength;
                        _chatLog.ScrollToCaret();
                    }
                }));
            };

            // Connect only UDP here (TCP was done back in HostListForm)
            _ = _net.ConnectUdpAsync(hostIp, udpPort, _cts.Token);

            // Render timer ~60 FPS
            _renderTimer = new WinFormsTimer { Interval = 16 };
            _renderTimer.Tick += (_, __) => Invalidate();
            _renderTimer.Start();
        }

        private async System.Threading.Tasks.Task SendChatAsync()
        {
            var text = _chatInput.Text.Trim();
            if (text.Length == 0) return;
            try
            {
                await _net.SendChatAsync(text);
                _chatInput.Clear();
            }
            catch (Exception ex)
            {
                _chatLog.AppendText("ERROR sending: " + ex.Message + Environment.NewLine);
            }
        }

        // Shutdown
        private async void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            try { _cts.Cancel(); } catch { }
            try { _renderTimer?.Stop(); } catch { }
            // Do NOT dispose _net here — HostListForm owns it (we reused the TCP connection).
            await System.Threading.Tasks.Task.CompletedTask;
        }

        // Input → NetworkManager (UDP inputs)
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            bool changed = false;
            switch (e.KeyCode)
            {
                case Keys.Up:    if (!_up)    { _up = true;    changed = true; } break;
                case Keys.Left:  if (!_left)  { _left = true;  changed = true; } break;
                case Keys.Down:  if (!_down)  { _down = true;  changed = true; } break;
                case Keys.Right: if (!_right) { _right = true; changed = true; } break;
            }
            if (changed) _net.SetKeys(_up, _left, _down, _right);
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            bool changed = false;
            switch (e.KeyCode)
            {
                case Keys.Up:    if (_up)    { _up = false;    changed = true; } break;
                case Keys.Left:  if (_left)  { _left = false;  changed = true; } break;
                case Keys.Down:  if (_down)  { _down = false;  changed = true; } break;
                case Keys.Right: if (_right) { _right = false; changed = true; } break;
            }
            if (changed) _net.SetKeys(_up, _left, _down, _right);
        }

        // Rendering — only draw the gameplay area (exclude the right chat panel)
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            // Background
            g.Clear(Color.White);

            // Play area is left region: width = WorldW, height = WorldH - status
            float playW = WorldW;
            float playH = ClientSize.Height - 24; // minus status bar
            using (var pen = new Pen(Color.Black, 2f))
                g.DrawRectangle(pen, 1, 1, playW - 2, playH - 2);

            if (!_hasSnapshot)
            {
                var s = "Waiting for snapshots… (use arrow keys to move)";
                var sz = g.MeasureString(s, Font);
                g.DrawString(s, Font, Brushes.Gray,
                    (playW - sz.Width) / 2,
                    (playH - sz.Height) / 2);
                return;
            }

            // Lava
            float lavaTop = _net.LavaY;
            float lavaHeight = Math.Max(0, playH - lavaTop);
            using (var lavaBrush = new SolidBrush(Color.FromArgb(220, 240, 80, 60)))
                g.FillRectangle(lavaBrush, 0, lavaTop, playW, lavaHeight);

            // Players (draw from UI-side cache to avoid concurrent modification)
            foreach (var kv in _viewEntities)
            {
                var id = kv.Key;
                var (x, y, hp) = kv.Value;

                var rect = new RectangleF(x, y, Box, Box);

                using (var body = new SolidBrush(Color.FromArgb(60, 60, 60)))
                    g.FillRectangle(body, rect);
                using (var outline = new Pen(Color.Black, 1.5f))
                    g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);

                var hpPct = Math.Max(0, Math.Min(100, hp)) / 100f;
                using (var hpBrush = new SolidBrush(
                    hpPct > 0.5f ? Color.FromArgb(60, 160, 60) :
                    hpPct > 0.2f ? Color.FromArgb(220, 160, 60) :
                                    Color.FromArgb(200, 60, 60)))
                {
                    g.FillRectangle(hpBrush, rect.X, rect.Y - 6, Box * hpPct, 4);
                }

                using (var f = new Font(FontFamily.GenericSansSerif, 8f, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.Black))
                {
                    g.DrawString(id, f, textBrush, rect.X - 2, rect.Y + rect.Height + 1);
                }
            }

        }
    }
}