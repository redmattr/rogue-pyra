// -----------------------------------------------------------------------------
// TcpClientForm.cs  (place in: UI/) WIP!!!! Does not function yet!
// -----------------------------------------------------------------------------
// Purpose
// Minimal WinForms surface that:
//   - Connects the TCPClientApp to the TCPMainServer (IP + TCP port)
//   - Sends commands to the TCPMainServer (List, HOST_REGISTER, etc.)
//   - Send chats using msg.
//   - Shows the host lobby information for connection
//
// 🧩 Notes
// - Keep networking out of the UI as much as possible; this form only "reads"
//   the public state exposed by TCPClientForm.
// - Only basic UI for now.
// -----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RoguePyra.Networking;

namespace RoguePyra.UI
{
    public sealed class TcpClientForm : Form
    {
        private const float WinX = 840f;

        private const float WinY = 480f;

        private readonly TcpMainServer _client;

        private readonly WinFormsTimer _renderTimer;

        private readonly CancellationToken _cts = new();

        public LobbyForm(string serverIp, int tcpPort = Protocol.DefaultTcpPort)
        {
            Text = "RoguePyra — Main";
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

            _ = RunClientAsync();
        }

    }
}