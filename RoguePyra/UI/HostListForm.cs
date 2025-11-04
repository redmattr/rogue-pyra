// HostListForm.cs
// Connects to the TCP lobby server, lists lobbies, lets you Join or Create.
// JOIN uses JOIN_INFO <ip> <udpPort> and opens GameForm without hardcoded IP.

using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using RoguePyra.Networking;
using System.Net;

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
        private int _pendingJoinLobbyId = -1;
        private TextBox _tbDirectIp;
        private TextBox _tbDirectUdp;
        private Button _btnDirectConnect;



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

            // --- Direct Connect (IP:Port) ---
            _tbDirectIp = new TextBox { Text = "127.0.0.1", Width = 160, Location = new Point(320, 410) };
            _tbDirectUdp = new TextBox { Text = Protocol.DefaultUdpPort.ToString(), Width = 70, Location = new Point(485, 410) };
            _btnDirectConnect = new Button { Text = "Direct Connect", Location = new Point(565, 402), Size = new Size(140, 40) };
            _btnDirectConnect.Click += OnDirectConnectClick;

            Controls.AddRange(new Control[] { _tbDirectIp, _tbDirectUdp, _btnDirectConnect });

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
            if (line.StartsWith("HOST_REGISTERED ", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("HOST_UNREGISTERED ", StringComparison.OrdinalIgnoreCase))
        {
            RequestLobbyList();   // keep the list in sync without user pressing Refresh
            return;
        }


            if (line.StartsWith("ERROR_GAME_STARTED", StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "Join rejected — that lobby already started.";
                return;
            }

            if (line.StartsWith("HOST_REGISTERED ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("HOST_UNREGISTERED ", StringComparison.OrdinalIgnoreCase))
            {
                RequestLobbyList();
                return;
            }

            if (line.StartsWith("LOBBY_LOCKED ", StringComparison.OrdinalIgnoreCase))
            {
                RequestLobbyList();
                return;
            }

            BeginInvoke(new Action(() =>
            {
                // Normalize (strip CR/LF and trim)
                line = (line ?? string.Empty).Trim();

                // 1) Header: "LOBBIES <count>"
                if (line.StartsWith("LOBBIES ", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(line.Substring(8).Trim(), out var n))
                    {
                        _expectLobbyLines = n;
                        _lvLobbies.Items.Clear();
                        _status.Text = n == 0 ? "No lobbies found." : $"Reading lobbies ({n})...";
                    }
                    return;
                }

                bool looksLikeLobbyRow = line.IndexOf('|') >= 0 && char.IsDigit(line[0]);
                if (_expectLobbyLines > 0 || looksLikeLobbyRow)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 7)
                    {
                        if (_expectLobbyLines == 0) // fallback: we missed the header, start fresh
                        {
                            _lvLobbies.Items.Clear();
                            _status.Text = "Reading lobbies (unknown count)…";
                            _expectLobbyLines = int.MaxValue; // consume until we get a non-row line
                        }

                        var inProg = parts[6].Trim() == "1";
                        var lvi = new ListViewItem(parts[0].Trim());  // id
                        lvi.SubItems.Add(parts[1].Trim());            // name
                        lvi.SubItems.Add(parts[2].Trim());            // ip
                        lvi.SubItems.Add(parts[3].Trim());            // udp
                        lvi.SubItems.Add(parts[4].Trim());            // max
                        lvi.SubItems.Add(parts[5].Trim());            // cur
                        lvi.SubItems.Add(inProg ? "Locked" : "Open"); // inprog → label
                        if (inProg) lvi.ForeColor = Color.Gray;
                        _lvLobbies.Items.Add(lvi);

                        if (_expectLobbyLines != int.MaxValue)
                        {
                            _expectLobbyLines--;
                            if (_expectLobbyLines == 0)
                                _status.Text = $"Found {_lvLobbies.Items.Count} lobby(ies).";
                        }
                        return;
                    }
                }

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

                        bool isHost = (_pendingJoinLobbyId == _lastHostedLobbyId);
                        var gf = new GameForm(IPAddress.Parse(ip), udpPort, _net, isHost: isHost, lobbyId: _pendingJoinLobbyId);
                        _netOwned = false;
                        gf.Show();

                        // reset
                        _pendingJoinLobbyId = -1;
                    }
                    return;
                }

                if (line.StartsWith("LOBBY_LOCKED ", StringComparison.OrdinalIgnoreCase))
                {
                    RequestLobbyList();
                    return;
                }

                if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("INFO", StringComparison.OrdinalIgnoreCase))
                {
                    _status.Text = line;
                    return;
                }
            }));
        }


        // Join selected lobby (sends JOIN <id>, then we wait for JOIN_INFO)
        private async void OnJoinClick(object? sender, EventArgs e)
        {
            if (_net == null) return;
            if (_lvLobbies.SelectedItems.Count == 0) return;

            var idText = _lvLobbies.SelectedItems[0].SubItems[0].Text;
            if (!int.TryParse(idText, out var id)) return;

            // Block if it shows as Locked
            if (_lvLobbies.SelectedItems[0].SubItems[6].Text.Equals("Locked", StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "Cannot join — lobby already started.";
                return;
            }

            _pendingJoinLobbyId = id;
            await _net.SendTcpLineAsync($"JOIN {id}");
            _status.Text = "Requested JOIN " + id + " …";
        }


        // Create lobby on the server (HOST_REGISTER <name> <udpPort> [max])
        private int _lastHostedLobbyId = -1;

        private async void OnCreateClick(object? sender, EventArgs e)
        {
            if (_net == null) return;

            using var dlg = new CreateLobbyDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string name = dlg.LobbyName;
            int udp = dlg.UdpPort;
            int max = dlg.MaxPlayers;

            // One-time listener to catch HOST_REGISTERED and store the ID
            Action<string>? onHostRegistered = null;
            onHostRegistered = line =>
            {
                if (!line.StartsWith("HOST_REGISTERED ", StringComparison.OrdinalIgnoreCase)) return;

                var idStr = line.Substring(16).Trim();
                if (int.TryParse(idStr, out var lobbyId))
                {
                    _lastHostedLobbyId = lobbyId;
                    // stop listening after we captured it
                    _net!.ChatReceived -= onHostRegistered!;
                    BeginInvoke(new Action(() =>
                    {
                        _status.Text = $"Lobby created (ID {lobbyId}).";
                        RequestLobbyList();
                    }));
                }
            };

            _net.ChatReceived += onHostRegistered;  // hook BEFORE sending
            await _net.SendTcpLineAsync($"HOST_REGISTER {name} {udp} {max}");
            _status.Text = $"Hosting '{name}' on UDP {udp}. Waiting for confirmation…";
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
        private async void OnDirectConnectClick(object? sender, EventArgs e)
        {
            try
            {
                var ipText = _tbDirectIp.Text.Trim();
                var portText = _tbDirectUdp.Text.Trim();
                if (string.IsNullOrWhiteSpace(ipText)) { _status.Text = "Enter an IP or hostname."; return; }
                if (!int.TryParse(portText, out var udpPort)) udpPort = Protocol.DefaultUdpPort;

                // Resolve IP or hostname
                System.Net.IPAddress? ip;
                if (!System.Net.IPAddress.TryParse(ipText, out ip))
                {
                    // Prefer IPv4 if available
                    var addrs = System.Net.Dns.GetHostAddresses(ipText);
                    ip = addrs.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        ?? addrs.FirstOrDefault();
                    if (ip == null) { _status.Text = "Could not resolve host."; return; }
                }

                // Ensure we have a NetworkManager (it can do UDP without TCP)
                if (_net == null)
                {
                    _net = new NetworkManager();
                    _net.ChatReceived += OnTcpLine;
                    _net.Error += s => BeginInvoke(new Action(() => _status.Text = "Error: " + s));
                    _netOwned = true; // HostListForm owns it unless GameForm takes it
                }

                _status.Text = $"Direct connecting to {ip}:{udpPort} …";

                // Open the game view; it will call ConnectUdpAsync inside its ctor
                var gf = new GameForm(ip, udpPort, _net, isHost: false, lobbyId: 0);
                _netOwned = false; // GameForm now reuses the manager instance
                gf.Show();
            }
            catch (Exception ex)
            {
                _status.Text = "Direct connect failed: " + ex.Message;
            }
            await System.Threading.Tasks.Task.CompletedTask;
        }

    }
}
