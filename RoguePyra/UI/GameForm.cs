// -----------------------------------------------------------------------------
// GameForm.cs  (place in: UI/)
// -----------------------------------------------------------------------------
// Purpose
// Minimal WinForms surface that:
//   - Connects a UdpGameClient to a host (IP + UDP port)
//   - Sends input (arrow keys) via SetKeys(...)
//   - Renders players, HP, and rising lava from client snapshots
//   - Shows a "WINNER" banner when the host declares a winner
//
// 🧩 Notes
// - Keep networking out of the UI as much as possible; this form only "reads"
//   the public state exposed by UdpGameClient and calls SetKeys on key events.
// - Double-buffering is enabled to avoid flicker.
// - Rendering is intentionally simple; swap in your own sprites later.
// -----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RoguePyra.Networking;

// Explicit alias so we don’t conflict with System.Threading.Timer
using WinFormsTimer = System.Windows.Forms.Timer;

namespace RoguePyra.UI
{
    public sealed class GameForm : Form
    {
        private const float WorldW = 840f;
        private const float WorldH = 480f;
        private const float Box    = 24f;

        private readonly UdpGameClient _client;
        private readonly CancellationTokenSource _cts = new();

        // Input state
        private bool _up, _left, _down, _right;

        // Rendering timer
        private readonly WinFormsTimer _renderTimer;
        private volatile bool _hasSnapshot = false;

        private readonly Label _status;

        public GameForm(string hostIp, int udpPort = Protocol.DefaultUdpPort)
        {
            Text = "RoguePyra — Client";
            ClientSize = new Size((int)WorldW, (int)WorldH);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            KeyPreview = true;

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 22,
                Text = "Connecting...",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(_status);

            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            FormClosing += OnFormClosing;

            _client = new UdpGameClient(hostIp, udpPort);
            _client.SnapshotApplied += () => { _hasSnapshot = true; _status.Text = $"Players: {_client.Entities.Count} | LavaY: {_client.LavaY:F1}"; };
            _client.WinnerAnnounced += id => { _status.Text = $"WINNER: {id}"; };

            _ = RunClientAsync();

            _renderTimer = new WinFormsTimer { Interval = 16 };
            _renderTimer.Tick += (_, __) => Invalidate();
            _renderTimer.Start();
        }

        private async Task RunClientAsync()
        {
            try { await _client.RunAsync(_cts.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _status.Text = "Network error: " + ex.Message; }
        }

        private void OnFormClosing(object? sender, FormClosingEventArgs e)
        {
            _cts.Cancel();
            _renderTimer?.Stop();
        }

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
            if (changed) _client.SetKeys(_up, _left, _down, _right);
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
            if (changed) _client.SetKeys(_up, _left, _down, _right);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            g.Clear(Color.White);
            using (var pen = new Pen(Color.Black, 2f))
                g.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 24 - 2);

            if (!_hasSnapshot)
            {
                var s = "Waiting for snapshots… (move with arrow keys)";
                var sz = g.MeasureString(s, Font);
                g.DrawString(s, Font, Brushes.Gray, (ClientSize.Width - sz.Width) / 2, (ClientSize.Height - 24 - sz.Height) / 2);
                return;
            }

            float lavaTop = _client.LavaY;
            float lavaHeight = Math.Max(0, (ClientSize.Height - 24) - lavaTop);
            using (var lavaBrush = new SolidBrush(Color.FromArgb(220, 240, 80, 60)))
                g.FillRectangle(lavaBrush, 0, lavaTop, ClientSize.Width, lavaHeight);

            foreach (var kv in _client.Entities)
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
                    g.DrawString(id, f, textBrush, rect.X - 2, rect.Y + rect.Height + 1);
            }

            if (!string.IsNullOrEmpty(_client.WinnerId))
            {
                string s = $"WINNER: {_client.WinnerId}";
                using (var f = new Font(FontFamily.GenericSansSerif, 18f, FontStyle.Bold))
                {
                    var size = g.MeasureString(s, f);
                    var x = (ClientSize.Width - size.Width) / 2;
                    var y = 8f;
                    using (var bg = new SolidBrush(Color.FromArgb(200, 255, 245, 200)))
                        g.FillRectangle(bg, x - 8, y - 4, size.Width + 16, size.Height + 8);
                    g.DrawString(s, f, Brushes.Black, x, y);
                }
            }
        }
    }
}