// HostListForm.cs
// Connects to the TCP lobby server, lists lobbies, lets you Join or Create.
// JOIN uses JOIN_INFO <ip> <udpPort> and opens GameForm without hardcoded IP.

using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using RoguePyra.Networking;

namespace RoguePyra.UI
{
    public sealed class HostListForm : Form
    {
        private TextBox _tbServerIp;
        private TextBox _tbTcpPort;
        private TextBox _tbPlayerName;
        private Button _btnConnect;
        private Button _btnRefresh;
        private Button _btnJoin;
        private Button _btnCreate;
        private bool _netOwned = true;

        private ListView _lvLobbies; // columns: Id, Name, IP, UdpPort, Max, Cur, InProg
        private Label _status;

        private NetworkManager? _net;  // used here only for TCP
        private int _expectLobbyLines = 0;

        public HostListForm()
        {
            Text = "RoguePyra — Host List";
            ClientSize = new Size(800, 480);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.White;

            // --- Top controls (server + player) ---
            var lblIp = new Label { Text = "Server IP:", AutoSize = true, Location = new Point(12, 14) };
            _tbServerIp = new TextBox { Text = "127.0.0.1", Width = 120, Location = new Point(80, 10) };

            var lblPort = new Label { Text = "TCP Port:", AutoSize = true, Location = new Point(210, 14) };
            _tbTcpPort = new TextBox { Text = Protocol.DefaultTcpPort.ToString(CultureInfo.InvariantCulture), Width = 70, Location = new Point(275, 10) };

            var lblName = new Label { Text = "Name:", AutoSize = true, Location = new Point(360, 14) };
            _tbPlayerName = new TextBox { Text = "Player" + Random.Shared.Next(1000, 9999), Width = 140, Location = new Point(410, 10) };

            _btnConnect = new Button { Text = "Connect", Location = new Point(570, 8), Size = new Size(90, 28) };
            _btnConnect.Click += OnConnectClick;

            _btnRefresh = new Button { Text = "Refresh", Location = new Point(670, 8), Size = new Size(90, 28), Enabled = false };
            _btnRefresh.Click += (_, __) => RequestLobbyList();

            Controls.AddRange(new Control[] { lblIp, _tbServerIp, lblPort, _tbTcpPort, lblName, _tbPlayerName, _btnConnect, _btnRefresh });

            // --- Lobby list ---
            _lvLobbies = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Location = new Point(12, 50),
                Size = new Size(760, 340)
            };
            _lvLobbies.Columns.Add("Id", 60);
            _lvLobbies.Columns.Add("Name", 200);
            _lvLobbies.Columns.Add("IP", 140);
            _lvLobbies.Columns.Add("UdpPort", 80);
            _lvLobbies.Columns.Add("Max", 60);
            _lvLobbies.Columns.Add("Cur", 60);
            _lvLobbies.Columns.Add("InProg", 80);
            Controls.Add(_lvLobbies);

            // --- Bottom buttons ---
            _btnJoin = new Button { Text = "Join Selected", Location = new Point(12, 402), Size = new Size(140, 40), Enabled = false };
            _btnJoin.Click += OnJoinClick;

            _btnCreate = new Button { Text = "Create Lobby", Location = new Point(160, 402), Size = new Size(140, 40), Enabled = false };
            _btnCreate.Click += OnCreateClick;

            _status = new Label { Text = "Not connected.", AutoSize = true, Location = new Point(320, 413) };

            Controls.AddRange(new Control[] { _btnJoin, _btnCreate, _status });

            _lvLobbies.SelectedIndexChanged += (_, __) => _btnJoin.Enabled = _lvLobbies.SelectedItems.Count > 0;
        }

        // Connect to TCP server for lobby/chat over NetworkManager (TCP only here)
        private async void OnConnectClick(object? sender, EventArgs e)
        {
            try
            {
                _btnConnect.Enabled = false;

                _net = new NetworkManager();
                _net.ChatReceived += OnTcpLine;         // we parse lobby responses here
                _net.Error += s => BeginInvoke(new Action(() => _status.Text = "Error: " + s));

                var ip = _tbServerIp.Text.Trim();
                if (!int.TryParse(_tbTcpPort.Text, out var port)) port = Protocol.DefaultTcpPort;
                var name = string.IsNullOrWhiteSpace(_tbPlayerName.Text) ? "Player" + Random.Shared.Next(1000, 9999) : _tbPlayerName.Text.Trim();

                await _net.ConnectTcpAsync(name, ip, port);
                _status.Text = $"Connected to {ip}:{port} as {name}.";
                _btnRefresh.Enabled = true;
                _btnCreate.Enabled = true;

                RequestLobbyList();
            }
            catch (Exception ex)
            {
                _status.Text = "Connect failed: " + ex.Message;
                _btnConnect.Enabled = true;
            }
        }

        // Ask server for lobby list
        private async void RequestLobbyList()
        {
            if (_net == null) return;
            _lvLobbies.Items.Clear();
            _status.Text = "Requesting lobby list...";
            _expectLobbyLines = 0;
            await _net.SendTcpLineAsync("HOST_LIST");
        }

        // Parse server lines for lobby flow:
        // LOBBIES <count>   then <count> lines: id|name|ip|udp|max|cur|inprog
        // JOIN_INFO <ip> <udpPort>
        private void OnTcpLine(string line)
        {
            BeginInvoke(new Action(async () =>
            {
                // LOBBIES header
                if (line.StartsWith("LOBBIES ", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Substring(8).Trim(), out var n))
                    {
                        _expectLobbyLines = n;
                        _lvLobbies.Items.Clear();
                        _status.Text = $"Reading lobbies ({n})...";
                    }
                    return;
                }

                // Lobby rows while expecting them
                if (_expectLobbyLines > 0 && line.Contains('|'))
                {
                    // id|name|ip|udpPort|max|cur|inprog
                    var parts = line.Split('|');
                    if (parts.Length >= 7)
                    {
                        var lvi = new ListViewItem(parts[0]);       // id
                        lvi.SubItems.Add(parts[1]);                  // name
                        lvi.SubItems.Add(parts[2]);                  // ip
                        lvi.SubItems.Add(parts[3]);                  // udp
                        lvi.SubItems.Add(parts[4]);                  // max
                        lvi.SubItems.Add(parts[5]);                  // cur
                        lvi.SubItems.Add(parts[6]);                  // inprog
                        _lvLobbies.Items.Add(lvi);
                    }
                    _expectLobbyLines--;
                    if (_expectLobbyLines == 0)
                        _status.Text = $"Found {_lvLobbies.Items.Count} lobby(ies).";
                    return;
                }

                // JOIN_INFO -> launch GameForm with returned IP:port (reuse TCP connection)
                if (line.StartsWith("JOIN_INFO ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 3 && int.TryParse(parts[2], out var udpPort))
                    {
                        var ip = parts[1];
                        _status.Text = $"Join info received → {ip}:{udpPort}";

                        if (_net == null)
                        {
                            _status.Text = "Error: TCP not connected.";
                            return;
                        }

                        var gf = new GameForm(ip, udpPort, _net); // pass existing NetworkManager
                        _netOwned = false;                        // GameForm now uses it; don't dispose here
                        gf.Show();
                    }
                    return;
                }


                // Generic status lines can be shown in status or ignored
                if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("INFO", StringComparison.OrdinalIgnoreCase))
                {
                    _status.Text = line;
                }
            }));
        }

        // Join selected lobby (sends JOIN <id>, then we wait for JOIN_INFO)
        private async void OnJoinClick(object? sender, EventArgs e)
        {
            if (_net == null) return;
            if (_lvLobbies.SelectedItems.Count == 0) return;

            var id = _lvLobbies.SelectedItems[0].SubItems[0].Text;
            await _net.SendTcpLineAsync($"JOIN {id}");
            _status.Text = "Requested JOIN " + id + " …";
        }

        // Create lobby on the server (HOST_REGISTER <name> <udpPort> [max])
        private async void OnCreateClick(object? sender, EventArgs e)
        {
            if (_net == null) return;

            using var dlg = new CreateLobbyDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                string name = dlg.LobbyName;
                int udp = dlg.UdpPort;
                int max = dlg.MaxPlayers;

                await _net.SendTcpLineAsync($"HOST_REGISTER {name} {udp} {max}");
                _status.Text = $"Hosted '{name}' on UDP {udp}. Refreshing…";
                RequestLobbyList();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            try
            {
                if (_netOwned)
                    _net?.DisposeAsync().AsTask().Wait(50);
            }
            catch { }
        }


        // Small dialog for lobby creation
        private sealed class CreateLobbyDialog : Form
        {
            private TextBox _tbName, _tbUdp, _tbMax;
            private Button _ok, _cancel;

            public string LobbyName => string.IsNullOrWhiteSpace(_tbName.Text) ? "Lobby" : _tbName.Text.Trim();
            public int UdpPort => int.TryParse(_tbUdp.Text, out var p) ? p : Protocol.DefaultUdpPort;
            public int MaxPlayers => int.TryParse(_tbMax.Text, out var m) ? m : 8;

            public CreateLobbyDialog()
            {
                Text = "Create Lobby";
                ClientSize = new Size(320, 180);
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false; MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent;

                var lbl1 = new Label { Text = "Name:", AutoSize = true, Location = new Point(16, 20) };
                _tbName = new TextBox { Text = "MyLobby", Width = 220, Location = new Point(80, 16) };

                var lbl2 = new Label { Text = "UDP Port:", AutoSize = true, Location = new Point(16, 56) };
                _tbUdp = new TextBox { Text = Protocol.DefaultUdpPort.ToString(CultureInfo.InvariantCulture), Width = 100, Location = new Point(80, 52) };

                var lbl3 = new Label { Text = "Max Players:", AutoSize = true, Location = new Point(16, 92) };
                _tbMax = new TextBox { Text = "8", Width = 100, Location = new Point(100, 88) };

                _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(160, 130) };
                _cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(240, 130) };

                Controls.AddRange(new Control[] { lbl1, _tbName, lbl2, _tbUdp, lbl3, _tbMax, _ok, _cancel });
                AcceptButton = _ok; CancelButton = _cancel;
            }
        }
    }
}
