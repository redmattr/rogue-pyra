// network.cs
// TCP (lobby/chat + host registry) and UDP (gameplay demo)

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoguePyra.Net {
	// ======================
	// Lobby model
	// ======================
	public sealed class Lobby {
		public int Id { get; init; }
		public string Name { get; set; } = "Lobby";
		public string HostIp { get; init; } = "127.0.0.1";
		public int UdpPort { get; set; }
		public int MaxPlayers { get; set; } = 8;
		public int CurPlayers { get; set; } = 0;
		public bool InProgress { get; set; } = false;
	}

	// ======================
	// TCP: Chat + Lobby
	// ======================
	public static class Protocol {
		// Client -> Server (newline-delimited):
		//   HELLO:<name>
		//   LIST
		//   MSG:<text>
		//   QUIT
		//
		// Lobby commands:
		//   HOST_REGISTER <LobbyName> <UdpPort> [MaxPlayers]
		//   HOST_UNREGISTER <LobbyId>
		//   HOST_LIST
		//   JOIN <LobbyId>
		//
		// Server -> Client:
		//   WELCOME <name>
		//   INFO <text>
		//   LIST <comma_separated_names>
		//   SAY <name>: <text>
		//   ERROR <text>
		//
		// Lobby replies:
		//   HOST_REGISTERED <id>
		//   HOST_UNREGISTERED <id>
		//   LOBBIES <count>  (then N lines: id|name|ip|udpPort|max|cur|inprog)
		//   JOIN_INFO <ip> <udpPort>
	}

	public sealed class MainServer {
		private readonly IPAddress _ip;
		private readonly int _port;
		private TcpListener? _listener;

		private readonly ConcurrentDictionary<TcpClient, ClientSession> _clients = new();

		// Lobby registry
		private readonly ConcurrentDictionary<int, Lobby> _lobbies = new();
		private int _nextLobbyId = 0;

		public MainServer(IPAddress ip, int port) {
			_ip = ip;
			_port = port;
		}

		public async Task RunAsync(CancellationToken ct) {
			_listener = new TcpListener(_ip, _port);
			_listener.Start();
			Console.WriteLine($"[Server] Listening on {_ip}:{_port}");

			try {
				while (!ct.IsCancellationRequested) {
					var tcp = await _listener.AcceptTcpClientAsync(ct);
					_ = HandleClientAsync(tcp, ct);
				}
			} catch (OperationCanceledException) { } finally {
				_listener?.Stop();
			}
		}

		private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct) {
			tcp.NoDelay = true;
			Console.WriteLine("[Server] Client connected");

			using var stream = tcp.GetStream();
			using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
			using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

			var session = new ClientSession(tcp, writer);
			_clients[tcp] = session;

			try {
				// Expect HELLO first
				string? line = await reader.ReadLineAsync(ct);
				if (line == null || !line.StartsWith("HELLO:", StringComparison.OrdinalIgnoreCase)) {
					await writer.WriteLineAsync("ERROR Expected HELLO:<name>");
					return;
				}

				session.Name = line.Substring("HELLO:".Length).Trim();
				if (string.IsNullOrWhiteSpace(session.Name))
					session.Name = $"Player{Random.Shared.Next(1000, 9999)}";

				await writer.WriteLineAsync($"WELCOME {session.Name}");
				await BroadcastAsync($"INFO {session.Name} joined.", exclude: tcp, ct);

				// Main loop
				while (!ct.IsCancellationRequested && tcp.Connected) {
					line = await reader.ReadLineAsync();
					if (line == null) break;

					if (line.Equals("LIST", StringComparison.OrdinalIgnoreCase)) {
						var names = string.Join(',', GetNamesSnapshot());
						await writer.WriteLineAsync($"LIST {names}");
					} else if (line.StartsWith("MSG:", StringComparison.OrdinalIgnoreCase)) {
						var msg = line.Substring("MSG:".Length).Trim();
						await BroadcastAsync($"SAY {session.Name}: {msg}", exclude: null, ct);
					} else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase)) {
						break;
					}
					  // ===== Lobby commands =====
					  else if (line.StartsWith("HOST_REGISTER ", StringComparison.OrdinalIgnoreCase)) {
						// HOST_REGISTER <LobbyName> <UdpPort> [MaxPlayers]
						var remoteEp = (IPEndPoint)tcp.Client.RemoteEndPoint!;
						string hostIp = remoteEp.Address.ToString();

						var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length < 3 || !int.TryParse(parts[2], out var udpPort)) {
							await writer.WriteLineAsync("ERROR Usage: HOST_REGISTER <LobbyName> <UdpPort> [MaxPlayers]");
						} else {
							int max = 8;
							if (parts.Length >= 4) int.TryParse(parts[3], out max);
							string lobbyName = parts[1];
							int id = AddLobby(lobbyName, hostIp, udpPort, max);
							await writer.WriteLineAsync($"HOST_REGISTERED {id}");
							Console.WriteLine($"[Server] Lobby registered #{id} '{lobbyName}' {hostIp}:{udpPort}");
						}
					} else if (line.StartsWith("HOST_UNREGISTER ", StringComparison.OrdinalIgnoreCase)) {
						var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length != 2 || !int.TryParse(parts[1], out var lobId)) {
							await writer.WriteLineAsync("ERROR Usage: HOST_UNREGISTER <LobbyId>");
						} else {
							if (RemoveLobby(lobId)) {
								await writer.WriteLineAsync($"HOST_UNREGISTERED {lobId}");
								Console.WriteLine($"[Server] Lobby unregistered #{lobId}");
							} else {
								await writer.WriteLineAsync("ERROR Lobby not found");
							}
						}
					} else if (line.Equals("HOST_LIST", StringComparison.OrdinalIgnoreCase)) {
						await SendLobbyListAsync(writer);
					} else if (line.StartsWith("JOIN ", StringComparison.OrdinalIgnoreCase)) {
						var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
						if (parts.Length != 2 || !int.TryParse(parts[1], out var lobId)) {
							await writer.WriteLineAsync("ERROR Usage: JOIN <LobbyId>");
						} else if (_lobbies.TryGetValue(lobId, out var lob)) {
							await writer.WriteLineAsync($"JOIN_INFO {lob.HostIp} {lob.UdpPort}");
						} else {
							await writer.WriteLineAsync("ERROR Lobby not found");
						}
					} else {
						await writer.WriteLineAsync("ERROR Unknown command");
					}
				}
			} catch (IOException) { /* client dropped */ } catch (ObjectDisposedException) { } finally {
				_clients.TryRemove(tcp, out _);
				Console.WriteLine($"[Server] {(session.Name ?? "Client")} disconnected");
				await BroadcastAsync($"INFO {session.Name} left.", exclude: null, ct);
				tcp.Close();
			}
		}

		// ---- Lobby helpers ----
		private int AddLobby(string name, string ip, int udpPort, int maxPlayers) {
			var id = Interlocked.Increment(ref _nextLobbyId);
			var lob = new Lobby {
				Id = id,
				Name = name,
				HostIp = ip,
				UdpPort = udpPort,
				MaxPlayers = maxPlayers
			};
			_lobbies[id] = lob;
			return id;
		}

		private bool RemoveLobby(int id) => _lobbies.TryRemove(id, out _);

		private async Task SendLobbyListAsync(StreamWriter w) {
			await w.WriteLineAsync($"LOBBIES {_lobbies.Count}");
			foreach (var kv in _lobbies) {
				var L = kv.Value;
				await w.WriteLineAsync($"{L.Id}|{L.Name}|{L.HostIp}|{L.UdpPort}|{L.MaxPlayers}|{L.CurPlayers}|{(L.InProgress ? 1 : 0)}");
			}
		}

		// ---- Chat helpers ----
		private IEnumerable<string> GetNamesSnapshot() {
			foreach (var kv in _clients) {
				var n = kv.Value.Name;
				if (!string.IsNullOrWhiteSpace(n)) yield return n!;
			}
		}

		private async Task BroadcastAsync(string line, TcpClient? exclude, CancellationToken ct) {
			foreach (var kv in _clients) {
				if (exclude != null && kv.Key == exclude) continue;
				try { await kv.Value.Writer.WriteLineAsync(line); } catch { }
			}
		}

		private sealed class ClientSession {
			public string? Name;
			public StreamWriter Writer { get; }
			public TcpClient Tcp { get; }

			public ClientSession(TcpClient tcp, StreamWriter writer) {
				Tcp = tcp; Writer = writer;
			}
		}
	}

	public sealed class Client {
		private readonly string _name;
		private readonly string _host;
		private readonly int _port;

		public Client(string name, string host, int port) {
			_name = string.IsNullOrWhiteSpace(name) ? $"Player{Random.Shared.Next(1000, 9999)}" : name.Trim();
			_host = host;
			_port = port;
		}

		public async Task RunAsync(CancellationToken ct) {
			using var tcp = new TcpClient();
			Console.WriteLine($"[Client] Connecting to {_host}:{_port}...");
			await tcp.ConnectAsync(_host, _port, ct);
			Console.WriteLine("[Client] Connected.");

			using var stream = tcp.GetStream();
			using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
			using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

			await writer.WriteLineAsync($"HELLO:{_name}");

			// Background receiver
			var recvTask = Task.Run(async () => {
				try {
					while (!ct.IsCancellationRequested) {
						var line = await reader.ReadLineAsync();
						if (line == null) break;
						Console.WriteLine($"[SRV] {line}");
					}
				} catch { }
			}, ct);

			// Stdin loop
			Console.WriteLine("Commands: /list, /msg <text>, /quit, /hosts, /hostreg <name> <udpPort> [max], /hostunreg <id>, /join <id>");
			while (!ct.IsCancellationRequested) {
				var s = Console.ReadLine();
				if (s == null) break;

				if (s.StartsWith("/list", StringComparison.OrdinalIgnoreCase))
					await writer.WriteLineAsync("LIST");
				else if (s.StartsWith("/msg ", StringComparison.OrdinalIgnoreCase))
					await writer.WriteLineAsync("MSG:" + s.Substring(5));
				else if (s.Equals("/quit", StringComparison.OrdinalIgnoreCase)) {
					await writer.WriteLineAsync("QUIT");
					break;
				} else if (s.StartsWith("/hosts", StringComparison.OrdinalIgnoreCase)) {
					await writer.WriteLineAsync("HOST_LIST");
				} else if (s.StartsWith("/hostreg ", StringComparison.OrdinalIgnoreCase)) {
					// /hostreg <LobbyName> <UdpPort> [MaxPlayers]
					var args = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (args.Length < 3) Console.WriteLine("Usage: /hostreg <LobbyName> <UdpPort> [MaxPlayers]");
					else {
						await writer.WriteLineAsync($"HOST_REGISTER {args[1]} {args[2]} {(args.Length >= 4 ? args[3] : "")}".TrimEnd());
					}
				} else if (s.StartsWith("/hostunreg ", StringComparison.OrdinalIgnoreCase)) {
					var args = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (args.Length != 2) Console.WriteLine("Usage: /hostunreg <LobbyId>");
					else {
						await writer.WriteLineAsync($"HOST_UNREGISTER {args[1]}");
					}
				} else if (s.StartsWith("/join ", StringComparison.OrdinalIgnoreCase)) {
					var args = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
					if (args.Length != 2) Console.WriteLine("Usage: /join <LobbyId>");
					else {
						await writer.WriteLineAsync($"JOIN {args[1]}");
						Console.WriteLine("If you receive JOIN_INFO <ip> <udpPort>, run:");
						Console.WriteLine("  dotnet run -- --clientviz --host <ip> --udpport <udpPort>");
					}
				} else {
					Console.WriteLine("Commands: /list, /msg <text>, /quit, /hosts, /hostreg <name> <udpPort> [max], /hostunreg <id>, /join <id>");
				}
			}

			try { tcp.Close(); } catch { }
			await Task.WhenAny(recvTask, Task.Delay(200));
			Console.WriteLine("[Client] Disconnected.");
		}
	}

	// ======================
	// UDP gameplay demo
	// ======================
	public static class UdpPorts {
		public const int Default = 6000;
	}

	public sealed class UdpGameHost {
		private readonly UdpClient _udp;
		private readonly IPEndPoint _listenEp;
		private readonly HashSet<IPEndPoint> _knownClients = new();

		// Authoritative entity state (single box for now)
		private float _x = 100f, _y = 100f;
		private byte _lastKeysMask = 0;

		public UdpGameHost(int port) {
			_listenEp = new IPEndPoint(IPAddress.Any, port);
			_udp = new UdpClient(_listenEp);
			Console.WriteLine($"[HostUDP] Listening on 0.0.0.0:{port}");
		}

		public async Task RunAsync(CancellationToken ct) {
			var recvTask = Task.Run(() => ReceiveLoop(ct), ct);
			var simTask = Task.Run(() => SimLoop(ct), ct);
			await Task.WhenAny(recvTask, simTask);
		}

		private async Task ReceiveLoop(CancellationToken ct) {
			while (!ct.IsCancellationRequested) {
				UdpReceiveResult res;
				try { res = await _udp.ReceiveAsync(ct); } catch { break; }

				var text = Encoding.UTF8.GetString(res.Buffer);
				_knownClients.Add(res.RemoteEndPoint);

				if (text.StartsWith("INPUT:", StringComparison.OrdinalIgnoreCase)) {
					// INPUT:<seq>:<keysMask>:<ms>
					var parts = text.Split(':');
					if (parts.Length >= 4 &&
						int.TryParse(parts[1], out var seq) &&
						byte.TryParse(parts[2], out var mask) &&
						int.TryParse(parts[3], out var ms)) {
						_lastKeysMask = mask; // remember latest keys from a client
						Console.WriteLine($"[HostUDP] INPUT {res.RemoteEndPoint} seq={seq} mask={mask} ms={ms}");
					}
				}
			}
		}

		private static float Clamp(float v, float min, float max) {
			if (v < min) return min;
			if (v > max) return max;
			return v;
		}

		private async Task SimLoop(CancellationToken ct) {
			var sw = Stopwatch.StartNew();
			long last = sw.ElapsedMilliseconds;
			const float speed = 140f; // px/sec
			const float maxX = 816f;  // 840 - 24
			const float maxY = 456f;  // 480 - 24

			while (!ct.IsCancellationRequested) {
				long now = sw.ElapsedMilliseconds;
				float dt = (now - last) / 1000f;
				if (dt <= 0) { await Task.Delay(1, ct); continue; }
				last = now;

				// Apply movement from keys
				float dx = 0, dy = 0;
				if ((_lastKeysMask & (1 << 0)) != 0) dy -= 1; // Up
				if ((_lastKeysMask & (1 << 2)) != 0) dy += 1; // Down
				if ((_lastKeysMask & (1 << 1)) != 0) dx -= 1; // Left
				if ((_lastKeysMask & (1 << 3)) != 0) dx += 1; // Right
				if (dx != 0 || dy != 0) {
					var len = MathF.Sqrt(dx * dx + dy * dy);
					dx /= len; dy /= len;
					_x += dx * speed * dt;
					_y += dy * speed * dt;
				}

				_x = Clamp(_x, 0f, maxX);
				_y = Clamp(_y, 0f, maxY);

				// Broadcast snapshots ~12Hz
				if (now % 80 < 16) {
					var msg = $"SNAPSHOT:{_x:F1}:{_y:F1}";
					var bytes = Encoding.UTF8.GetBytes(msg);
					foreach (var ep in _knownClients) {
						try { await _udp.SendAsync(bytes, bytes.Length, ep); } catch { }
					}
				}

				await Task.Delay(10, ct);
			}
		}
	}

	public sealed class UdpGameClient {
		private readonly UdpClient _udp;
		private readonly IPEndPoint _hostEp;

		public volatile float EntityX = 100f, EntityY = 100f;

		private int _seq = 0;
		private readonly Stopwatch _sw = Stopwatch.StartNew();
		private byte _keysMask = 0;

		public UdpGameClient(string hostIp, int port) {
			_udp = new UdpClient(0); // auto-bind
			_udp.Client.ReceiveTimeout = 0;
			_hostEp = new IPEndPoint(IPAddress.Parse(hostIp), port);
			Console.WriteLine($"[CliUDP] Local {((IPEndPoint)_udp.Client.LocalEndPoint!).ToString()} → Host {_hostEp}");
		}

		public void SetKey(bool up, bool left, bool down, bool right) {
			byte m = 0;
			if (up) m |= 1 << 0;
			if (left) m |= 1 << 1;
			if (down) m |= 1 << 2;
			if (right) m |= 1 << 3;
			_keysMask = m;
		}

		public async Task RunAsync(CancellationToken ct) {
			var recvTask = Task.Run(() => ReceiveLoop(ct), ct);
			var sendTask = Task.Run(() => SendInputsLoop(ct), ct);
			await Task.WhenAny(recvTask, sendTask);
		}

		private async Task ReceiveLoop(CancellationToken ct) {
			while (!ct.IsCancellationRequested) {
				try {
					var res = await _udp.ReceiveAsync(ct);
					var text = Encoding.UTF8.GetString(res.Buffer);
					if (text.StartsWith("SNAPSHOT:", StringComparison.OrdinalIgnoreCase)) {
						var p = text.Split(':');
						if (p.Length >= 3 &&
							float.TryParse(p[1], out var x) &&
							float.TryParse(p[2], out var y)) {
							EntityX = x; EntityY = y;
						}
					}
				} catch (OperationCanceledException) { break; } catch { }
			}
		}

		private async Task SendInputsLoop(CancellationToken ct) {
			while (!ct.IsCancellationRequested) {
				var ms = (int)_sw.ElapsedMilliseconds;
				var payload = $"INPUT:{_seq++}:{_keysMask}:{ms}";
				var bytes = Encoding.UTF8.GetBytes(payload);
				try { await _udp.SendAsync(bytes, bytes.Length, _hostEp); } catch { }
				await Task.Delay(50, ct); // 20 Hz
			}
		}
	}
}