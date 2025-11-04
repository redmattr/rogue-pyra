// GameForm.cs
// Simple WinForms window for the visual client.
// Now includes an in-game chat panel (TCP) while rendering gameplay (UDP).

using System;
using System.Net;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using RoguePyra.Networking;
using System.Collections.Generic;

// Avoid ambiguity with System.Threading.Timer
using WinFormsTimer = System.Windows.Forms.Timer;

using System.Drawing.Drawing2D; // for LinearGradientBrush

namespace RoguePyra.UI
{
    public sealed class GameForm : Form
    {
        // UI-thread copy of latest entities (avoid concurrent enumeration)
        private readonly Dictionary<string, (float x, float y, int hp)> _viewEntities = new();
        // World (render) constants
        private const float WorldW = 12000f;
        private const float WorldH = 6000f;
        private const float Box = 24f;
        private const float CamStep = 400f;

        // Camera/viewport (top-left in world coords)
        private float _camX = 0f, _camY = 0f;
        // Smooth follow params
        private const float CamLerp = 0.15f; // 0..1, higher is snappier
        // Which entity to follow
        private string? _followId = null;

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
        private Button? _btnStart;
        private Button? _btnLeave;
        private readonly int _lobbyId;
        private readonly bool _isHost;
        private readonly List<RectangleF> _platforms = new();
        private Panel _topBar;
        private const int TopBarH = 44;


        // Constructor now receives:
        // - hostIp + udpPort: from JOIN_INFO
        // - net: existing NetworkManager that is ALREADY connected to TCP
        public GameForm(IPAddress hostIp, int udpPort, NetworkManager net, bool isHost = false, int lobbyId = 0)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));

            // Window setup — widen by ChatPanelW so playfield stays 840x480
            Text = "RoguePyra — Client";
            ClientSize = new Size(1280, 720);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;


            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            KeyPreview = true;
            this.KeyDown += OnKeyDown;
            this.KeyUp   += OnKeyUp;

            // Status bar
            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Text = "Connecting…",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(_status);

            // --- Top bar (buttons live here; keeps them above the playfield) ---
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = TopBarH,
                BackColor = Color.FromArgb(245, 245, 245)
            };
            Controls.Add(_topBar);

            // Return to Menu (always shown)
            _btnLeave = new Button
            {
                Name = "btnLeave",
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
                catch { }
                Close();
            };
            _topBar.Controls.Add(_btnLeave);

            // Host-only Start button
            if (_isHost)
            {
                _btnStart = new Button
                {
                    Name = "btnStart",
                    Text = "Start Game",
                    Location = new Point(10, 8),
                    Size = new Size(100, 28)
                };
                _btnStart.Click += async (_, __) =>
                {
                    _btnStart!.Enabled = false;
                    await _net.SendTcpLineAsync($"HOST_START {_lobbyId}");
                    _btnStart.Text = "Locked";
                };
                _topBar.Controls.Add(_btnStart);
            }




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
                ReadOnly = true,
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
            KeyUp += OnKeyUp;
            FormClosing += OnFormClosing;

            _net.SnapshotReceived += (lavaY, ents) =>
            {
                _hasSnapshot = true;
                // Marshal to UI thread and copy entities into a safe cache
                BeginInvoke(new Action(() =>
                {
                    _status.Text = $"Players: {_viewEntities.Count} | LavaY: {_net.LavaY:F0} | Cam:({_camX:F0},{_camY:F0}) | World:{WorldW}x{WorldH}";

                    _viewEntities.Clear();
                    foreach (var kv in ents)
                        _viewEntities[kv.Key] = kv.Value;  // copy to UI-owned cache

                    Invalidate(); // trigger repaint

                    

                    // pick a follow target if none, AND seed camera immediately once
                    if (_followId == null && _viewEntities.Count > 0)
                    {
                        foreach (var k in _viewEntities.Keys) { _followId = k; break; }

                        if (_followId != null && _viewEntities.TryGetValue(_followId, out var first))
                        {
                            float playW = ClientSize.Width - ChatPanelW;
                            float playH = ClientSize.Height - _status.Height;
                            _camX = Math.Max(0, Math.Min(Math.Max(0, WorldW - playW), first.x - playW * 0.5f));
                            _camY = Math.Max(0, Math.Min(Math.Max(0, WorldH - playH), first.y - playH * 0.5f));
                        }
                    }


                    if (_followId != null && _viewEntities.TryGetValue(_followId, out var ft))
                    {
                        float playW = ClientSize.Width - ChatPanelW;
                        float playH = ClientSize.Height - _status.Height;

                        float targetCamX = ft.x + Box * 0.5f - playW * 0.5f;
                        float targetCamY = ft.y + Box * 0.5f - playH * 0.5f;

                        targetCamX = Math.Max(0, Math.Min(Math.Max(0, WorldW - playW), targetCamX));
                        targetCamY = Math.Max(0, Math.Min(Math.Max(0, WorldH - playH), targetCamY));

                        _camX = _camX + (targetCamX - _camX) * CamLerp;
                        _camY = _camY + (targetCamY - _camY) * CamLerp;
                    }
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



            _platforms.Clear();
            var rnd = new Random(1234);
            for (int i = 0; i < 30; i++)
            {
                float w = rnd.Next(120, 280);
                float h = 16;
                float x = rnd.Next(0, (int)(WorldW - w));
                float y = rnd.Next(100, (int)(WorldH - 100));
                _platforms.Add(new RectangleF(x, y, w, h));
            }
            _platforms.Sort((a, b) => a.Y.CompareTo(b.Y));


            // Render timer ~60 FPS
            _renderTimer = new WinFormsTimer { Interval = 16 };
            _renderTimer.Tick += RenderTick;
            _renderTimer.Start();

            _isHost = isHost;
            _lobbyId = lobbyId;

            // Return to Menu
            _btnLeave = new Button
            {
                Text = "Return to Menu",
                Location = new Point(120, 8),
                Size = new Size(120, 30)
            };
            _btnLeave.Click += async (_, __) =>
            {
                try
                {
                    // If I'm the host, unlist my lobby before closing
                    if (_isHost && _lobbyId > 0)
                        await _net.SendTcpLineAsync($"HOST_UNREGISTER {_lobbyId}");
                }
                catch { /* non-fatal */ }

                Close();   // back to host list
            };
            Controls.Add(_btnLeave);
            _btnLeave.BringToFront();

            if (_isHost)
            {
                _btnStart = new Button
                {
                    Text = "Start Game",
                    Location = new Point(10, 8),
                    Size = new Size(100, 30)
                };
                _btnStart.Click += async (_, __) =>
                {
                    try
                    {
                        await _net.SendTcpLineAsync($"HOST_START {_lobbyId}");
                        _btnStart.Enabled = false;
                        _btnStart.Text = "Locked";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to start: " + ex.Message);
                    }
                };
                Controls.Add(_btnStart);
                _btnStart.BringToFront();
            }



        }
        

        private void RenderTick(object? sender, EventArgs e)
        {
            // Smooth follow even without new snapshots
            if (_followId != null && _viewEntities.TryGetValue(_followId, out var ft))
            {
                float playW = ClientSize.Width - ChatPanelW;
                float playH = ClientSize.Height - _status.Height;

                float targetCamX = ft.x + Box * 0.5f - playW * 0.5f;
                float targetCamY = ft.y + Box * 0.5f - playH * 0.5f;

                targetCamX = Math.Max(0, Math.Min(Math.Max(0, WorldW - playW), targetCamX));
                targetCamY = Math.Max(0, Math.Min(Math.Max(0, WorldH - playH), targetCamY));

                _camX += (targetCamX - _camX) * CamLerp;
                _camY += (targetCamY - _camY) * CamLerp;
            }
            Invalidate();
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
            try
            {
                // Best-effort unlist if host closes via [X]
                if (_isHost && _lobbyId > 0)
                    await _net.SendTcpLineAsync($"HOST_UNREGISTER {_lobbyId}");
            }
            catch { /* ignore */ }

            _cts.Cancel();
            _renderTimer?.Stop();
            // Do NOT dispose _net here; HostListForm still uses the TCP connection
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

                case Keys.Escape:
                    _btnLeave?.PerformClick();
                    break;



                // Manual camera panning + follow toggle
                case Keys.I:
                    _camY = Math.Max(0, _camY - CamStep);
                    break;

                case Keys.K:
                {
                    float playH = ClientSize.Height - _status.Height;
                    _camY = Math.Min(Math.Max(0, WorldH - playH), _camY + CamStep);
                    break;
                }

                case Keys.J:
                    _camX = Math.Max(0, _camX - CamStep);
                    break;

                case Keys.L:
                {
                    float playW = ClientSize.Width - ChatPanelW;
                    _camX = Math.Min(Math.Max(0, WorldW - playW), _camX + CamStep);
                    break;
                }

                case Keys.Space:
                    // Toggle camera follow on/off
                    _followId = (_followId == null) ? FirstKeyOrNull() : null;
                    break;
  
            }
            if (changed) _net.SetKeys(_up, _left, _down, _right);
        }


        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            bool changed = false;
            switch (e.KeyCode)
            {
                case Keys.Up: if (_up) { _up = false; changed = true; } break;
                case Keys.Left: if (_left) { _left = false; changed = true; } break;
                case Keys.Down: if (_down) { _down = false; changed = true; } break;
                case Keys.Right: if (_right) { _right = false; changed = true; } break;
            }
            if (changed) _net.SetKeys(_up, _left, _down, _right);
        }

        private PointF W2S(float wx, float wy)
        {
            float sx = wx - _camX;
            float sy = wy - _camY;
            return new PointF(sx, sy);
        }

        private bool OnScreen(float wx, float wy, float w, float h)
        {
            float playW = ClientSize.Width - ChatPanelW;
            float playH = ClientSize.Height - _status.Height;
            float sx = wx - _camX, sy = wy - _camY;
            return !(sx + w < 0 || sy + h < 0 || sx > playW || sy > playH);
        }

        // Rendering — only draw the gameplay area (exclude the right chat panel)
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            // Space available for the playfield (exclude top bar, chat panel, and status bar)
            float playW = ClientSize.Width - ChatPanelW;
            float playH = ClientSize.Height - _status.Height - TopBarH;

             // Shift drawing down so (0,0) is just under the top bar
            g.TranslateTransform(0, TopBarH);

            // Background gradient
            using (var bg = new LinearGradientBrush(
                new RectangleF(0, 0, playW, playH),
                Color.FromArgb(235, 245, 255), Color.FromArgb(210, 220, 235), 90f))
            {
                g.FillRectangle(bg, 0, 0, playW, playH);
            }

            using (var pen = new Pen(Color.Black, 2f))
                g.DrawRectangle(pen, 1, 1, playW - 2, playH - 2);

            if (!_hasSnapshot)
            {
                var s = "Waiting for snapshots… (use arrows to move, TAB to change camera)";
                var sz = g.MeasureString(s, Font);
                g.DrawString(s, Font, Brushes.Gray,
                    (playW - sz.Width) / 2,
                    (playH - sz.Height) / 2);
                return;
            }

            
            // --- Platforms ---
            using (var platBrush = new SolidBrush(Color.SaddleBrown))
            {
                foreach (var p in _platforms)
                {
                    if (!OnScreen(p.X, p.Y, p.Width, p.Height)) continue;

                    var sPlat = W2S(p.X, p.Y);
                    g.FillRectangle(platBrush, sPlat.X, sPlat.Y, p.Width, p.Height);
                }
            }



            // Lava (world Y to screen)
            float lavaWorldTop = _net.LavaY;
            var lavaPt = W2S(0, lavaWorldTop);
            float lavaScreenY = lavaPt.Y;
            float lavaHeight = Math.Max(0, playH - lavaScreenY);
            using (var lavaBrush = new SolidBrush(Color.FromArgb(220, 240, 80, 60)))
                g.FillRectangle(lavaBrush, 0, lavaScreenY, playW, lavaHeight);


            // World grid every 200 units (makes scrolling obvious)
            using (var gridPen = new Pen(Color.FromArgb(40, 40, 40)))
            {
                // Visible world rectangle
                float wx0 = _camX, wy0 = _camY;
                float wx1 = _camX + playW, wy1 = _camY + playH;

                int gx0 = ((int)Math.Floor(wx0 / 200f)) * 200;
                int gy0 = ((int)Math.Floor(wy0 / 200f)) * 200;

                for (int x = gx0; x <= wx1 + 1; x += 200)
                {
                    var a = W2S(x, wy0);
                    e.Graphics.DrawLine(gridPen, a.X, 0, a.X, playH);
                }
                for (int y = gy0; y <= wy1 + 1; y += 200)
                {
                    var a = W2S(wx0, y);
                    e.Graphics.DrawLine(gridPen, 0, a.Y, playW, a.Y);
                }
            }

            // Players (only draw visible)
            foreach (var kv in _viewEntities)
            {
                var id = kv.Key;
                var (x, y, hp) = kv.Value;

                if (!OnScreen(x, y, Box, Box)) continue;

                var spt = W2S(x, y);
                var rect = new RectangleF(spt.X, spt.Y, Box, Box);

                using (var body = new SolidBrush(id == _followId ? Color.FromArgb(40, 100, 200) : Color.FromArgb(60, 60, 60)))
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
            g.ResetTransform();
        }

        private string? FirstKeyOrNull()
        {
            foreach (var k in _viewEntities.Keys) return k;
            return null;
        }


    }
}