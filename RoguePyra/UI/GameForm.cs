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

        // Latest snapshot state (raw from UDP)
        private readonly Dictionary<string, (float x, float y, int hp)> _snapshotEntities = new();

        // Smoothed positions used for rendering
        private readonly Dictionary<string, (float x, float y, int hp)> _viewEntities = new();

        private float _lavaY = WorldH;
        private volatile bool _hasSnapshot;


        // Input
        private bool _up, _left, _down, _right;

        // Networking
        private readonly NetworkManager _net;   // TCP: chat / lobby control
        private IPAddress _hostIp;              // UDP host (from JOIN_INFO / HOST_MIGRATE)
        private int _udpPort;
        private bool _isHost;                   // lobby host flag (initially from JOIN, may change on migration)
        private readonly int _lobbyId;

        private readonly CancellationTokenSource _cts = new();
        private UdpGameClient _udpClient;
        private UdpGameHost? _udpHost;          // only non-null if this client is currently acting as host

        private readonly string? _localPlayerName; // this player's TCP name


        // UI
        private readonly Label _status;
        private readonly Panel _chatPanel;
        private readonly TextBox _chatLog;
        private readonly TextBox _chatInput;
        private readonly Button _chatSend;
        private readonly Panel _topBar;
        private Button? _btnStart;
        private Button? _btnLeave;
        private bool _isReconnecting;

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
                    await _net.SendTcpLineAsync("QUIT");
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

            //
            // --- Start UDP host if this client is the lobby creator ---
            //
            if (_isHost)
            {
                try
                {
                    // Start the authoritative UDP simulation on this machine.
                    _udpHost = new UdpGameHost(_udpPort, WorldH, null);
                    _ = _udpHost.RunAsync(_cts.Token);
                    _status.Text = "Hosting game...";
                }
                catch (Exception ex)
                {
                    _status.Text = "Failed to start host: " + ex.Message;
                }
            }

            //
            // --- Start UDP client (all players, including the host) ---
            //
            var nameForUdp = _localPlayerName ?? string.Empty;
            _udpClient = new UdpGameClient(_hostIp, _udpPort, nameForUdp);

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

                // 1) Host migration control message
                if (line.StartsWith("HOST_MIGRATE ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var newHostName = parts[1];
                        var newHostIpStr = parts[2];
                        if (!int.TryParse(parts[3], out var newUdpPort))
                            newUdpPort = _udpPort;

                        HandleHostMigrate(newHostName, newHostIpStr, newUdpPort);
                    }
                    return;
                }

                // 2) Normal chat/info/error messages
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
                        _isReconnecting = false;
                        _lavaY = _udpClient.LavaY;

                        // 1) Copy raw snapshot into _snapshotEntities
                        _snapshotEntities.Clear();
                        foreach (var kv in _udpClient.Entities)
                            _snapshotEntities[kv.Key] = kv.Value;

                        // 2) First snapshot: snap render state directly so we don’t lerp from (0,0)
                        if (_viewEntities.Count == 0)
                        {
                            _viewEntities.Clear();
                            foreach (var kv in _snapshotEntities)
                                _viewEntities[kv.Key] = kv.Value;
                        }

                        // Prefer to follow our own player name if present
                        if (!string.IsNullOrEmpty(_localPlayerName) &&
                            _viewEntities.ContainsKey(_localPlayerName))
                        {
                            _followId = _localPlayerName;
                        }
                        else if (_followId == null || !_viewEntities.ContainsKey(_followId))
                        {
                            foreach (var k in _viewEntities.Keys)
                            {
                                _followId = k;
                                break;
                            }
                        }


                        // 4) Status only – camera movement will now happen in RenderWorld
                        _status.Text = _hasSnapshot
                            ? $"Players: {_snapshotEntities.Count} | LavaY: {_lavaY:F0}"
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


        private void HandleHostMigrate(string newHostName, string newHostIpStr, int newUdpPort)
        {
            // Try to parse IP
            if (!IPAddress.TryParse(newHostIpStr, out var newHostIp))
            {
                _status.Text = $"HOST_MIGRATE received with invalid IP: {newHostIpStr}";
                return;
            }

            bool iAmNewHost = !string.IsNullOrWhiteSpace(_localPlayerName) &&
                              string.Equals(_localPlayerName, newHostName, StringComparison.OrdinalIgnoreCase);

            _isReconnecting = true;
            _hasSnapshot = false;
            
            
            // Log in chat/status
            var infoLine = iAmNewHost
                ? $"INFO You are now the host @ {newHostIpStr}:{newUdpPort}"
                : $"INFO Host migrated to {newHostName} @ {newHostIpStr}:{newUdpPort}";

            _chatLog.AppendText(infoLine + Environment.NewLine);
            _chatLog.SelectionStart = _chatLog.TextLength;
            _chatLog.ScrollToCaret();
            _status.Text = infoLine;
            

            // Update our view of the host endpoint
            _hostIp = newHostIp;
            _udpPort = newUdpPort;

            // Stop listening to old UDP client events (old instance may still run, but won't affect UI)
            try
            {
                _udpClient.SnapshotApplied -= OnUdpSnapshot;
                _udpClient.WinnerAnnounced -= OnWinner;
            }
            catch { }

            // If we are the new host, start a UdpGameHost locally on this port.
            if (iAmNewHost)
            {
                // Optional: only create once
                if (_udpHost == null)
                {
                    try
                    {
                        // Build a seed snapshot from our last known entities
                        IDictionary<string, (float x, float y, int hp)>? seeds = null;

                        if (_viewEntities.Count > 0)
                        {
                            seeds = new Dictionary<string, (float x, float y, int hp)>(_viewEntities);
                        }

                        // Use our last known lava height + seeded players so world progression continues
                        _udpHost = new UdpGameHost(_udpPort, _lavaY, seeds);
                        _ = _udpHost.RunAsync(_cts.Token);
                        _isHost = true; // this client now truly owns the UDP simulation
                    }
                    catch (Exception ex)
                    {
                        _status.Text = "Failed to start local host: " + ex.Message;
                    }

                }
            }


            // In all cases, create a fresh UDP client that points to the new host endpoint
            try
            {
                var nameForUdp = _localPlayerName ?? string.Empty;

                var newClient = new UdpGameClient(_hostIp, _udpPort, nameForUdp);
                newClient.SnapshotApplied += OnUdpSnapshot;
                newClient.WinnerAnnounced += OnWinner;

                _udpClient = newClient;
                _ = _udpClient.RunAsync(_cts.Token);

            }
            catch (Exception ex)
            {
                _status.Text = "Failed to reconnect UDP client: " + ex.Message;
            }

        }




        private async System.Threading.Tasks.Task SendChatAsync()
        {
            var msg = _chatInput.Text.Trim();
            if (string.IsNullOrEmpty(msg)) return;

            _chatInput.Clear();
            try
            {
                // Use the proper MSG:<text> format via NetworkManager
                await _net.SendChatAsync(msg);
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
                string waiting = _isReconnecting
                    ? "Reconnecting to new host... (hold on a sec)"
                    : "Waiting for snapshots... (use arrows/WASD to move, TAB to change camera)";

                var sz = g.MeasureString(waiting, Font);
                g.DrawString(waiting, Font, Brushes.White,
                    playX + (playW - sz.Width) / 2,
                    playY + (playH - sz.Height) / 2);
                return;
            }

            // ----- Smooth render positions toward latest snapshot -----
            const float posLerp = 0.25f; // tweak 0.2–0.4 for more/less smoothing

            if (_snapshotEntities.Count > 0)
            {
                // Remove entities that disappeared in the latest snapshot
                var toRemove = new List<string>();
                foreach (var id in _viewEntities.Keys)
                {
                    if (!_snapshotEntities.ContainsKey(id))
                        toRemove.Add(id);
                }
                foreach (var id in toRemove)
                    _viewEntities.Remove(id);

                // Add / update entities from snapshot
                foreach (var kv in _snapshotEntities)
                {
                    var target = kv.Value;
                    if (_viewEntities.TryGetValue(kv.Key, out var cur))
                    {
                        float nx = cur.x + (target.x - cur.x) * posLerp;
                        float ny = cur.y + (target.y - cur.y) * posLerp;
                        int hp = target.hp; // HP snaps to server value

                        _viewEntities[kv.Key] = (nx, ny, hp);
                    }
                    else
                    {
                        // New entity: just snap it in
                        _viewEntities[kv.Key] = target;
                    }
                }
            }


            // ----- Camera follow using SMOOTHED positions -----
            if (_followId != null && _viewEntities.TryGetValue(_followId, out var ft))
            {
                float targetX = ft.x + Box * 0.5f - (playW * 0.5f);
                float targetY = ft.y + Box * 0.5f - (playH * 0.5f);

                targetX = Math.Clamp(targetX, 0, Math.Max(0, WorldW - playW));
                targetY = Math.Clamp(targetY, 0, Math.Max(0, WorldH - playH));

                _camX += (targetX - _camX) * CamLerp;
                _camY += (targetY - _camY) * CamLerp;
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
            using (var nameFont = new Font(Font.FontFamily, 8f))
            {
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

                    // --- Player name under the box ---
                    string label = id;  // id is already the player name
                    var nameSize = g.MeasureString(label, nameFont);

                    float nameX = sx + (Box - nameSize.Width) / 2f;
                    float nameY = sy + Box + 2; // a few pixels below the box

                    // Clamp so the text doesn't go below the play area
                    if (nameY + nameSize.Height > playY + playH)
                        nameY = playY + playH - nameSize.Height;

                    // Highlight local player's name
                    Brush nameBrush = (id == _localPlayerName) ? Brushes.Yellow : Brushes.White;
                    g.DrawString(label, nameFont, nameBrush, nameX, nameY);
                }
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