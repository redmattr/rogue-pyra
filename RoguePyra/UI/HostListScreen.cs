// HostListScreen.cs
// Connects to the TCP lobby server, lists lobbies, lets you Join or Create.
// JOIN uses JOIN_INFO <ip> <udpPort> and opens GameScreen without hardcoded IP.

namespace RoguePyra.UI;

using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Globalization;

using RoguePyra.Networking;

internal class HostListScreen : UserControl {
	public event EventHandler ReturnToMainMenu; // Used to tell the containing form to switch to MainMenuScreen.

	private readonly TextBox _tbServerIp;   // Textbox for inputting IP of the server to use.
	private readonly TextBox _tbTcpPort;    // Textbox for inputting port of the server to use.
	private readonly TextBox _tbPlayerName; // Textbox for inputting the client's player name.
	private readonly Button _btnConnect;    // Button to attempt connection to the specified server.
	private readonly Button _btnRefresh;    // TODO: description.
	private readonly Button _btnJoin;       // Joins the highlighted lobby from the list provided by the connected server.
	private readonly Button _btnCreate;     // Creates a new lobby, spawning a host process (logical node) on the client's machine, then connects the client to that lobby.
	private readonly Button _btnMainMenu;	// Returns to main menu.
	private bool _netOwned = true;
	private bool _inGame; // Tracks whether this client is in a game (aka GameForm is open).
	private bool InGame {
		get => _inGame;
		set {
			_inGame = value;
			_btnJoin.Enabled = !value && _lvLobbies.SelectedItems.Count > 0;
			_btnCreate.Enabled = !value;
		}
	}			

	private readonly ListView _lvLobbies;   // List of lobbies registered with the connected server. Columns: Id, Name, IP, UdpPort, Max, Cur, InProg.
	private readonly Label _status;         // Displays messages relating to the most recent action.

	private NetworkManager? _net;  // used here only for TCP
	private int _expectLobbyLines = 0;
	private int _pendingJoinLobbyId = -1;
	//private TextBox _tbDirectIp;
	//private TextBox _tbDirectUdp;
	//private Button _btnDirectConnect;
	private string _playerName = "";

	public HostListScreen() {
		// --- Top controls (server + player) ---
		var lblIp = new Label { Text = "Server IP:", AutoSize = true, Location = new Point(12, 14) };
		_tbServerIp = new TextBox { Text = "74.109.192.95", Width = 120, Location = new Point(80, 10) };

		var lblPort = new Label { Text = "TCP Port:", AutoSize = true, Location = new Point(210, 14) };
		_tbTcpPort = new TextBox { Text = Protocol.DefaultTcpPort.ToString(CultureInfo.InvariantCulture), Width = 70, Location = new Point(275, 10) };

		var lblName = new Label { Text = "Name:", AutoSize = true, Location = new Point(360, 14) };
		_tbPlayerName = new TextBox { Text = "Player" + Random.Shared.Next(1000, 9999), Width = 140, Location = new Point(410, 10) };

		_btnConnect = new Button { Text = "Connect", Location = new Point(570, 8), Size = new Size(90, 28) };
		_btnConnect.Click += OnConnectClick;

		_btnRefresh = new Button { Text = "Refresh", Location = new Point(670, 8), Size = new Size(90, 28), Enabled = false };
		_btnRefresh.Click += (_, __) => RequestLobbyList();

		Controls.AddRange([lblIp, _tbServerIp, lblPort, _tbTcpPort, lblName, _tbPlayerName, _btnConnect, _btnRefresh]);

		// --- Lobby list ---
		_lvLobbies = new ListView {
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
		_btnMainMenu = new Button { Text = "Main Menu", Location = new Point(12, 402), Size = new Size(140, 40), Enabled = true };
		_btnMainMenu.Click += (_, __) => ReturnToMainMenu?.Invoke(this, EventArgs.Empty);

		_btnJoin = new Button { Text = "Join Selected", Location = new Point(160, 402), Size = new Size(140, 40), Enabled = false };
		_btnJoin.Click += OnJoinClick;

		_btnCreate = new Button { Text = "Create Lobby", Location = new Point(308, 402), Size = new Size(140, 40), Enabled = false };
		_btnCreate.Click += OnCreateClick;

		_status = new Label { Text = "Not connected.", AutoSize = true, Location = new Point(468, 413) };

		Controls.AddRange([_btnJoin, _btnCreate, _btnMainMenu, _status]);

		_lvLobbies.SelectedIndexChanged += (_, __) => _btnJoin.Enabled = _lvLobbies.SelectedItems.Count > 0 && !_inGame;

		// --- Direct Connect (IP:Port) ---
		//_tbDirectIp = new TextBox { Text = "127.0.0.1", Width = 160, Location = new Point(320, 410) };
		//_tbDirectUdp = new TextBox { Text = Protocol.DefaultUdpPort.ToString(), Width = 70, Location = new Point(485, 410) };
		//_btnDirectConnect = new Button { Text = "Direct Connect", Location = new Point(565, 402), Size = new Size(140, 40) };
		//_btnDirectConnect.Click += OnDirectConnectClick;

		//Controls.AddRange(new Control[] { _tbDirectIp, _tbDirectUdp, _btnDirectConnect });
	}

	// Connect to TCP server for lobby/chat over NetworkManager (TCP only here)
	private async void OnConnectClick(object? sender, EventArgs e) {
		try {
			_btnConnect.Enabled = false;

			_net = new NetworkManager();
			_net.ChatReceived += OnTcpLine;         // we parse lobby responses here
			_net.Error += s => BeginInvoke(new Action(() => _status.Text = "Error: " + s));

			var ip = _tbServerIp.Text.Trim();
			if (!int.TryParse(_tbTcpPort.Text, out var port)) port = Protocol.DefaultTcpPort;
			var name = string.IsNullOrWhiteSpace(_tbPlayerName.Text) ? "Player" + Random.Shared.Next(1000, 9999) : _tbPlayerName.Text.Trim();

			_playerName = name;

			await _net.ConnectTcpAsync(name, ip, port);
			_status.Text = $"Connected to {ip}:{port} as {name}.";
			_btnRefresh.Enabled = true;
			_btnCreate.Enabled = true;

			RequestLobbyList();
		} catch (Exception ex) {
			_status.Text = "Connect failed: " + ex.Message;
			_btnConnect.Enabled = true;
		}
	}

	// Ask server for lobby list
	private async void RequestLobbyList() {
		if (_net == null) return;
		_lvLobbies.Items.Clear();
		_status.Text = "Requesting lobby list...";
		_expectLobbyLines = 0;
		await _net.SendTcpLineAsync("HOST_LIST");
	}

	// Parse server lines for lobby flow:
	// LOBBIES <count>   then <count> lines: id|name|ip|udp|max|cur|inprog
	// JOIN_INFO <ip> <udpPort>
	private void OnTcpLine(string line) {
		if (line.StartsWith("HOST_REGISTERED ", StringComparison.OrdinalIgnoreCase) ||
		line.StartsWith("HOST_UNREGISTERED ", StringComparison.OrdinalIgnoreCase)) {
			RequestLobbyList();   // keep the list in sync without user pressing Refresh
			return;
		}

		if (line.StartsWith("ERROR_GAME_STARTED", StringComparison.OrdinalIgnoreCase)) {
			_status.Text = "Join rejected — that lobby already started.";
			return;
		}

		if (line.StartsWith("HOST_REGISTERED ", StringComparison.OrdinalIgnoreCase) ||
			line.StartsWith("HOST_UNREGISTERED ", StringComparison.OrdinalIgnoreCase)) {
			RequestLobbyList();
			return;
		}

		if (line.StartsWith("LOBBY_LOCKED ", StringComparison.OrdinalIgnoreCase)) {
			RequestLobbyList();
			return;
		}

		BeginInvoke(new Action(() => {
			// Normalize (strip CR/LF and trim)
			line = (line ?? string.Empty).Trim();

			// 1) Header: "LOBBIES <count>"
			if (line.StartsWith("LOBBIES ", StringComparison.OrdinalIgnoreCase)) {
				if (int.TryParse(line.Substring(8).Trim(), out var n)) {
					_expectLobbyLines = n;
					_lvLobbies.Items.Clear();
					_status.Text = n == 0 ? "No lobbies found." : $"Reading lobbies ({n})...";
				}
				return;
			}

			bool looksLikeLobbyRow = line.Contains('|') && char.IsDigit(line[0]);
			if (_expectLobbyLines > 0 || looksLikeLobbyRow) {
				// id|name|ip|udpPort|max|cur|inprog
				var parts = line.Split('|');
				if (parts.Length >= 7) {
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

					if (_expectLobbyLines != int.MaxValue) {
						_expectLobbyLines--;
						if (_expectLobbyLines == 0)
							_status.Text = $"Found {_lvLobbies.Items.Count} lobby(ies).";
					}
					return;
				}
			}

			if (line.StartsWith("JOIN_INFO ", StringComparison.OrdinalIgnoreCase)) {
				var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length == 3 && int.TryParse(parts[2], out var udpPort)) {
					var ip = parts[1];
					_status.Text = $"Join info received → {ip}:{udpPort}";

					if (_net == null) {
						_status.Text = "Error: TCP not connected.";
						return;
					}

					InGame = true; // Client now has a GameForm instance open.
					bool isHost = (_pendingJoinLobbyId == _lastHostedLobbyId);
					var gf = new GameForm(
						IPAddress.Parse(ip),
						udpPort,
						_net,
						isHost: isHost,
						lobbyId: _pendingJoinLobbyId,
						localPlayerId: _playerName
						);
					gf.FormClosed += (_, __) => InGame = false; // Runs when client no longer has a GameForm instance open.
					_netOwned = false;
					gf.Show();

					// reset
					_pendingJoinLobbyId = -1;
				}
				return;
			}

			if (line.StartsWith("LOBBY_LOCKED ", StringComparison.OrdinalIgnoreCase)) {
				RequestLobbyList();
				return;
			}

			if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
				line.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)) {
				_status.Text = line;
				return;
			}
		}));
	}

	// Join selected lobby (sends JOIN <id>, then we wait for JOIN_INFO)
	private async void OnJoinClick(object? sender, EventArgs e) {
		if (_net == null) return;
		if (_lvLobbies.SelectedItems.Count == 0) return;

		var idText = _lvLobbies.SelectedItems[0].SubItems[0].Text;
		if (!int.TryParse(idText, out var id)) return;

		// Block if it shows as Locked
		if (_lvLobbies.SelectedItems[0].SubItems[6].Text.Equals("Locked", StringComparison.OrdinalIgnoreCase)) {
			_status.Text = "Cannot join — lobby already started.";
			return;
		}

		_pendingJoinLobbyId = id;
		string lanIp = GetLocalLanIp();
		await _net.SendTcpLineAsync($"JOIN {id} {lanIp}");
		_status.Text = "Requested JOIN " + id + " …";
	}

	// Create lobby on the server (HOST_REGISTER <name> <udpPort> [max])
	private int _lastHostedLobbyId = -1;

	private async void OnCreateClick(object? sender, EventArgs e) {
    	if (_net == null) return;

    	try {
        	using var dlg = new CreateLobbyDialog();
        	if (dlg.ShowDialog(this) != DialogResult.OK)
            	return;

        	string name = dlg.LobbyName;
        	int udp = dlg.UdpPort;
        	int max = dlg.MaxPlayers;

        	string lanIp = GetLocalLanIp();

        	// One-time listener to catch HOST_REGISTERED and immediately launch GameForm as host
        	void onHostRegistered(string line) {
            	if (!line.StartsWith("HOST_REGISTERED ", StringComparison.OrdinalIgnoreCase))
                	return;

            	var idStr = line.Substring("HOST_REGISTERED ".Length).Trim();
            	if (!int.TryParse(idStr, out var lobbyId))
                	return;

            	_lastHostedLobbyId = lobbyId;

            	// stop listening after we captured it
            	_net!.ChatReceived -= onHostRegistered!;

            	BeginInvoke(new Action(() => {
                	_status.Text = $"Lobby created (ID {lobbyId}). Launching game as host…";

                	if (_net == null)
                    	return;

                	// Mark that GameForm now owns the NetworkManager
                	_netOwned = false;

					// Immediately enter the game as HOST, using our LAN IP + chosen UDP port
					InGame = true; // Client now has a GameForm instance open.
					var gf = new GameForm(
                    	IPAddress.Parse(lanIp),
                    	udp,
                    	_net,
                    	isHost: true,
                    	lobbyId: lobbyId,
                    	localPlayerId: _playerName
                	);
					gf.FormClosed += (_, __) => InGame = false; // Runs when client no longer has a GameForm instance open.
					gf.Show();
                	// Optional: hide this screen's parent form or leave it; GF is its own window.
                	// FindForm()?.Hide();
            	}));
        	}

        	// Hook BEFORE sending the register command
        	_net.ChatReceived += onHostRegistered!;

        	await _net.SendTcpLineAsync($"HOST_REGISTER {name} {udp} {max} {lanIp}");

        	_status.Text = $"Hosting '{name}' on UDP {udp} (LAN {lanIp}). Waiting for server confirmation…";
    	} catch (Exception ex) {
        	MessageBox.Show(this,
            	"Disconnected from server. Please reconnect.\n\n" + ex.Message,
            	"Connection Error",
            	MessageBoxButtons.OK,
            	MessageBoxIcon.Error);
    	}
	}

	protected override void OnHandleDestroyed(EventArgs e) {
		try {
			if (_netOwned)
				_net?.DisposeAsync().AsTask().Wait(50);
		} catch { }
		finally {
			base.OnHandleDestroyed(e);
		}
	}

	// Small dialog for lobby creation
	private sealed class CreateLobbyDialog : Form {
		private readonly TextBox _tbName, _tbUdp, _tbMax;
		private readonly Button _ok, _cancel;

		public string LobbyName => string.IsNullOrWhiteSpace(_tbName.Text) ? "Lobby" : _tbName.Text.Trim();
		public int UdpPort => int.TryParse(_tbUdp.Text, out var p) ? p : Protocol.DefaultUdpPort;
		public int MaxPlayers => int.TryParse(_tbMax.Text, out var m) ? m : 8;

		public CreateLobbyDialog() {
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

			Controls.AddRange([lbl1, _tbName, lbl2, _tbUdp, lbl3, _tbMax, _ok, _cancel]);
			AcceptButton = _ok; CancelButton = _cancel;
		}
	}

	private static string GetLocalLanIp() {
		try {
			var host = Dns.GetHostEntry(Dns.GetHostName());
			foreach (var ip in host.AddressList) {
				if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
					return ip.ToString();
			}
		} catch {
			// ignore and fall through
		}
		return "127.0.0.1"; // fallback, shouldn't matter on LAN
	}
}