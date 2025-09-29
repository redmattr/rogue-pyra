// -----------------------------------------------------------------------------
// LobbyForm.cs  (place in: UI/) WIP!!! Does not function yet!
// -----------------------------------------------------------------------------
// Purpose
// Minimal WinForms surface that:
//   - Connects the client to the UdpGameClient (IP + UDP)
//   - Receives snapshots from the UDPGameHost before game begins.
//   - Future: Send chats using msg.
//   - Shows other connected clients.
//
// 🧩 Notes
// - Keep networking out of the UI as much as possible; this form only "reads"
//   the public state exposed by UdpGameHost.
// - Only basic UI for now.
// -----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RoguePyra.Networking;
using RoguePyra.UI;

using WinFormsTimer = System.Windows.Forms.Timer;

namespace RoguePyre.UI
{

    public sealed class LobbyForm : Form
    {
        private const float WinX = 840f;

        private const float WinY = 480f;

        private readonly UdpGameClient _client;

        private readonly WinFormsTimer _renderTimer;

        private readonly CancellationToken _cts = new();

        public LobbyForm(string hostIp, int udpPort = Protocol.DefaultUdpPort)
        {
            Text = "RoguePyra — Lobby";
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

            FormClosing += OnFormClosing;

            _client = new UdpGameClient(hostIp, udpPort);
            _client.SnapshotApplied += () => { _hasSnapshot = true; _status.Text = $"Players: {_client.Entities.Count} | LavaY: {_client.LavaY:F1}"; };

            _ = RunClientAsync();

            _renderTimer = new WinFormsTimer { Interval = 16 };
            _renderTimer.Tick += (_, __) => Invalidate();
            _renderTimer.Start();
        }

    }
}