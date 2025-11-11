// GameForm.cs
// Visual client: renders UDP snapshots and shows TCP chat.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using RoguePyra.Networking;

// Avoid ambiguity with System.Threading.Timer
using WinFormsTimer = System.Windows.Forms.Timer;

namespace RoguePyra.UI
{
    public sealed class GameForm : Form
    {
        // ---- World / render constants ----
        private const float WorldW = 4000f;
        private const float WorldH = 2000f;
        private const float Box = 24f;
        private const int ChatPanelW = 280;
        private const int TopBarH = 44;
        private const float CamLerp = 0.15f;

        // Camera
        private float _camX;
        private float _camY;
        private string? _followId;

        // Latest snapshot (UI-thread copy)
        private readonly Dictionary<string, (float x, float y, int hp)> _viewEntities = new();
        private float _lavaY = WorldH;
        private volatile bool _hasSnapshot;

        // Input
        private bool _up, _left, _down, _right;

        // Networking
        private readonly NetworkManager _net;   // TCP: chat / lobby control
        private readonly IPAddress _hostIp;     // UDP host (from JOIN_INFO)
        private readonly int _udpPort;
        private readonly bool _isHost;          // this client created lobby? (for HOST_START / UNREGISTER)
        private readonly int _lobbyId;

        private readonly CancellationTokenSource _cts = new();
        private readonly UdpGameClient _udpClient;

        private readonly string? _localPlayerName; // purely cosmetic right now

        // UI
        private readonly Label _status;
        private readonly Panel _chatPanel;
        private readonly TextBox _chatLog;
        private readonly TextBox _chatInput;
        private readonly Button _chatSend;
        private readonly Panel _topBar;
        private Button? _btnStart;
        private Button? _btnLeave;

        private readonly List<RectangleF> _platforms = new();
        private readonly WinFormsTimer _renderTimer;

        public GameForm(IPAddress hostIp, int udpPort, NetworkManager net, bool isHost, int lobbyId, string? localPlayerId = null)
        {
            _hostIp = hostIp ?? throw new ArgumentNullException(nameof(hostIp));
            _udpPort = udpPort;
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _isHost = isHost;
            _lobbyId = lobbyId;
            _localPlayerName = localPlayerId;

            // --- Window basics ---
            Text = "RoguePyra — Client";
            ClientSize = new Size(1600, 720);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer,
                true);

            KeyPreview = true;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            FormClosing += OnFormClosing;

            // --- Status bar ---
            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Text = "Waiting for snapshots…",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(_status);

            // --- Top bar ---
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = TopBarH,
                BackColor = Color.FromArgb(245, 245, 245)
            };
            Controls.Add(_topBar);

            // Leave button
            _btnLeave = new Button
            {
                Text = "Return to Menu",
                Location = new Point(120, 8),
                Size = new Size(120, 28),
                TabStop = false
            };
            _btnLeave.Click += async (_, __) =>
            {
                try
                {
                    if (_isHost && _lobbyId > 0)
                        await _net.SendTcpLineAsync($"HOST_UNREGISTER {_lobbyId}");
                }
                catch
                {
                    // non-fatal
                }
                Close();
            };
            _topBar.Controls.Add(_btnLeave);

            // Host-only: Start Game
            if (_isHost)
            {
                _btnStart = new Button
                {
                    Text = "Start Game",
                    Location = new Point(10, 8),
                    Size = new Size(100, 28),
                    TabStop = false
                };
                _btnStart.Click += async (_, __) =>
                {
                    try
                    {
                        _btnStart.Enabled = false;
                        await _net.SendTcpLineAsync($"HOST_START {_lobbyId}");
                        _btnStart.Text = "Locked";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "HOST_START failed: " + ex.Message, "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        _btnStart.Enabled = true;
                        _btnStart.Text = "Start Game";
                    }
                };
                _topBar.Controls.Add(_btnStart);
            }

            // --- Chat panel (right) ---
            _chatPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = ChatPanelW,
                BackColor = Color.FromArgb(248, 248, 248),
                Padding = new Padding(8)
            };
            Controls.Add(_chatPanel);

            _chatLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill
            };
            _chatPanel.Controls.Add(_chatLog);

            var chatBottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
            _chatPanel.Controls.Add(chatBottom);

            _chatInput = new TextBox { Dock = DockStyle.Fill };
            chatBottom.Controls.Add(_chatInput);

            _chatSend = new Button { Text = "Send", Dock = DockStyle.Right, Width = 66 };
            chatBottom.Controls.Add(_chatSend);

            _chatSend.Click += async (_, __) => await SendChatAsync();
            _chatInput.KeyDown += async (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    await SendChatAsync();
                }
            };

            // --- Subscribe to TCP chat lines ---
            _net.ChatReceived += OnTcpChatLine;

            // --- Setup fake geometry (platforms etc) just for visuals ---
            var rnd = new Random(1234);
            _platforms.Clear();
            for (int i = 0; i < 30; i++)
            {
                float w = rnd.Next(120, 280);
                float h = 16;
                float x = rnd.Next(0, (int)(WorldW - w));
                float y = rnd.Next(100, (int)(WorldH - 100));
                _platforms.Add(new RectangleF(x, y, w, h));
            }
            _platforms.Sort((a, b) => a.Y.CompareTo(b.Y));

            // --- Start UDP client (NO UdpGameHost here) ---
            _udpClient = new UdpGameClient(_hostIp, _udpPort);
            _udpClient.SnapshotApplied += OnUdpSnapshot;
            _udpClient.WinnerAnnounced += OnWinner;

            _ = _udpClient.RunAsync(_cts.Token);

            // --- Render timer (~60 FPS) ---
            _renderTimer = new WinFormsTimer { Interval = 16 };
            _renderTimer.Tick += (_, __) => RenderTick();
            _renderTimer.Start();
        }

        // ----------------- Networking callbacks -----------------

        private void OnTcpChatLine(string line)
        {
            // marshal to UI
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                line = (line ?? string.Empty).Trim();
                if (line.StartsWith("SAY ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("INFO ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("ERROR ", StringComparison.OrdinalIgnoreCase))
                {
                    _chatLog.AppendText(line + Environment.NewLine);
                    _chatLog.SelectionStart = _chatLog.TextLength;
                    _chatLog.ScrollToCaret();
                }
            }));
        }

        private void OnUdpSnapshot()
        {
            if (!IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                _hasSnapshot = true;
                _lavaY = _udpClient.LavaY;

                _viewEntities.Clear();
                foreach (var kv in _udpClient.Entities)
                    _viewEntities[kv.Key] = kv.Value;

                // Choose follow target if none or target vanished
                if (_followId == null || !_viewEntities.ContainsKey(_followId))
                {
                    foreach (var k in _viewEntities.Keys)
                    {
                        _followId = k;
                        break;
                    }
                }

                // Smoothly move camera toward follow target
                if (_followId != null && _viewEntities.TryGetValue(_followId, out var ft))
                {
                    float playW = ClientSize.Width - ChatPanelW;
                    float playH = ClientSize.Height - _status.Height - TopBarH;

                    float targetX = ft.x + Box * 0.5f - playW * 0.5f;
                    float targetY = ft.y + Box * 0.5f - playH * 0.5f;

                    targetX = Math.Clamp(targetX, 0, Math.Max(0, WorldW - playW));
                    targetY = Math.Clamp(targetY, 0, Math.Max(0, WorldH - playH));

                    _camX += (targetX - _camX) * CamLerp;
                    _camY += (targetY - _camY) * CamLerp;
                }

                _status.Text = _hasSnapshot
                    ? $"Players: {_viewEntities.Count} | LavaY: {_lavaY:F0}"
                    : "Waiting for snapshots…";

                Invalidate();
            }));
        }

        private void OnWinner(string winnerId)
        {
            if (!IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                MessageBox.Show(this, $"Winner: {winnerId}", "Game Over",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        }

        private async System.Threading.Tasks.Task SendChatAsync()
        {
            var msg = _chatInput.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            _chatInput.Clear();
            try
            {
                await _net.SendTcpLineAsync("SAY " + msg);
            }
            catch (Exception ex)
            {
                _status.Text = "Chat send failed: " + ex.Message;
            }
        }

        // ----------------- Input handling -----------------

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.W:
                case Keys.Up: _up = true; break;
                case Keys.A:
                case Keys.Left: _left = true; break;
                case Keys.S:
                case Keys.Down: _down = true; break;
                case Keys.D:
                case Keys.Right: _right = true; break;

                case Keys.Tab:
                    // cycle follow target
                    e.SuppressKeyPress = true;
                    CycleFollowTarget();
                    break;
            }

            _udpClient.SetKeys(_up, _left, _down, _right);
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.W:
                case Keys.Up: _up = false; break;
                case Keys.A:
                case Keys.Left: _left = false; break;
                case Keys.S:
                case Keys.Down: _down = false; break;
                case Keys.D:
                case Keys.Right: _right = false; break;
            }

            _udpClient.SetKeys(_up, _left, _down, _right);
        }

        private void CycleFollowTarget()
        {
            if (_viewEntities.Count == 0) return;

            var keys = new List<string>(_viewEntities.Keys);
            if (_followId == null)
            {
                _followId = keys[0];
                return;
            }

            int idx = keys.IndexOf(_followId);
            idx = (idx + 1) % keys.Count;
            _followId = keys[idx];
        }

        // ----------------- Render -----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RenderWorld(e.Graphics);
        }

        private void RenderTick()
        {
            if (!IsHandleCreated) return;
            Invalidate();
        }

        private void RenderWorld(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Play area rect (excluding chat + top bar + status bar)
            int playX = 0;
            int playY = TopBarH;
            int playW = ClientSize.Width - ChatPanelW;
            int playH = ClientSize.Height - TopBarH - _status.Height;
            if (playW <= 0 || playH <= 0) return;

            // Background
            using (var bg = new LinearGradientBrush(
                new Rectangle(playX, playY, playW, playH),
                Color.FromArgb(10, 10, 30),
                Color.FromArgb(40, 40, 80),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, playX, playY, playW, playH);
            }

            if (!_hasSnapshot)
            {
                const string waiting = "Waiting for snapshots... (use arrows/WASD to move, TAB to change camera)";
                var sz = g.MeasureString(waiting, Font);
                g.DrawString(waiting, Font, Brushes.White,
                    playX + (playW - sz.Width) / 2,
                    playY + (playH - sz.Height) / 2);
                return;
            }

            // Lava
            float lavaScreenY = playY + (_lavaY - _camY);
            using (var lavaBrush = new LinearGradientBrush(
                new RectangleF(playX, lavaScreenY, playW, playH),
                Color.FromArgb(220, 40, 0),
                Color.FromArgb(120, 10, 0),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(lavaBrush, playX, lavaScreenY, playW, playH);
            }

            // Platforms
            using (var pfBrush = new SolidBrush(Color.FromArgb(60, 60, 60)))
            {
                foreach (var p in _platforms)
                {
                    float sx = playX + (p.X - _camX);
                    float sy = playY + (p.Y - _camY);
                    if (sx + p.Width < playX || sx > playX + playW) continue;
                    if (sy + p.Height < playY || sy > playY + playH) continue;
                    g.FillRectangle(pfBrush, sx, sy, p.Width, p.Height);
                }
            }

            // Players
            foreach (var (id, (x, y, hp)) in _viewEntities)
            {
                float sx = playX + (x - _camX);
                float sy = playY + (y - _camY);

                if (sx + Box < playX || sx > playX + playW) continue;
                if (sy + Box < playY || sy > playY + playH) continue;

                var rect = new RectangleF(sx, sy, Box, Box);
                var bodyColor = id == _followId ? Color.Lime : Color.Cyan;
                using (var b = new SolidBrush(bodyColor))
                    g.FillRectangle(b, rect);

                // HP bar
                float hpFrac = Math.Max(0, Math.Min(1, hp / 100f));
                var hpRect = new RectangleF(sx, sy - 6, Box * hpFrac, 4);
                using (var hb = new SolidBrush(Color.Lime))
                    g.FillRectangle(hb, hpRect);
            }
        }

        // ----------------- Cleanup -----------------

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            try
            {
                _cts.Cancel();
            }
            catch { }

            try
            {
                _renderTimer.Stop();
            }
            catch { }

            // Let NetworkManager be disposed by HostListForm; we don't own it here.
            _net.ChatReceived -= OnTcpChatLine;
            _udpClient.SnapshotApplied -= OnUdpSnapshot;
            _udpClient.WinnerAnnounced -= OnWinner;
        }
    }
}