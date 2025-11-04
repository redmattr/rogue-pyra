// -----------------------------------------------------------------------------
// TcpMainServer.cs  (place in: Networking/)
// -----------------------------------------------------------------------------
// Purpose
// Lightweight TCP server that handles:
//   - Simple chat (/list, /msg) over TCP
//   - Lobby registry (create/list/remove lobbies that point to UDP hosts)
//   - Join handoff (gives clients the UDP host IP:port to connect to)
//
// Protocol (newline-delimited text)
// Client → Server:
//   HELLO:<name>
//   LIST
//   MSG:<text>
//   QUIT
//   HOST_REGISTER <LobbyName> <UdpPort> [MaxPlayers]
//   HOST_UNREGISTER <LobbyId>
//   HOST_LIST
//   JOIN <LobbyId>
//
// Server → Client:
//   WELCOME <name>
//   INFO <text>
//   LIST <comma_separated_names>
//   SAY <name>: <text>
//   ERROR <text>
//   HOST_REGISTERED <id>
//   HOST_UNREGISTERED <id>
//   LOBBIES <count>    (then N lines: id|name|ip|udpPort|max|cur|inprog)
//   JOIN_INFO <ip> <udpPort>
//
// Notes
// - Uses one TcpListener + one Task per connection (simple and fine for small games).
// - Lobby info is in Shared.cs (RoguePyra.Lobby).
// - Keep this file TCP-only; UDP gameplay is handled in UdpGameHost/UdpGameClient.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using RoguePyra;                 // for Lobby DTO
using RoguePyra.Networking;      // for Protocol constants (optional)

namespace RoguePyra.Networking;

public sealed class TcpMainServer
{
    private readonly IPAddress _bindIp;
    private readonly int _tcpPort;

    private TcpListener? _listener;

    // Connected clients: we keep their writer to broadcast messages quickly
    private readonly ConcurrentDictionary<TcpClient, ClientSession> _clients = new();

    // Lobby registry in memory (id → Lobby)
    private readonly ConcurrentDictionary<int, Lobby> _lobbies = new();
    private int _nextLobbyId = 0;
    private readonly ConcurrentDictionary<int, TcpClient> _lobbyOwners = new();


    public TcpMainServer(IPAddress bindIp, int tcpPort = Protocol.DefaultTcpPort)
    {
        _bindIp  = bindIp;
        _tcpPort = tcpPort;
    }

    /// Starts the TCP server and handles clients until the token is cancelled.
    /// Call with: new TcpMainServer(IPAddress.Any).RunAsync(ct);
    public async Task RunAsync(CancellationToken ct)
    {
        _listener = new TcpListener(_bindIp, _tcpPort);
        _listener.Start();
        Console.WriteLine($"[TCP] Listening on {_bindIp}:{_tcpPort}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var tcp = await _listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(tcp, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            try { _listener?.Stop(); } catch { }
        }
    }


    // -------------------------------------------------------------------------
    // Per-connection handling
    // -------------------------------------------------------------------------
    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        tcp.NoDelay = true;
        Console.WriteLine("[TCP] Client connected");

        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true };

        var session = new ClientSession(tcp, writer);
        _clients[tcp] = session;

        try
        {
            // Expect HELLO:<name> as the first line
            var hello = await reader.ReadLineAsync(ct);
            if (hello is null || !hello.StartsWith("HELLO:", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("ERROR Expected HELLO:<name>");
                return;
            }

            session.Name = hello.Substring("HELLO:".Length).Trim();
            if (string.IsNullOrWhiteSpace(session.Name))
                session.Name = $"Player{Random.Shared.Next(1000, 9999)}";

            await writer.WriteLineAsync($"WELCOME {session.Name}");
            await BroadcastAsync($"INFO {session.Name} joined.", exclude: tcp, ct);

            // Main line loop
            while (!ct.IsCancellationRequested && tcp.Connected)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                if (line.Equals("LIST", StringComparison.OrdinalIgnoreCase))
                {
                    var names = string.Join(',', GetNamesSnapshot());
                    await writer.WriteLineAsync($"LIST {names}");
                }
                else if (line.StartsWith("MSG:", StringComparison.OrdinalIgnoreCase))
                {
                    var msg = line.Substring("MSG:".Length).Trim();
                    await BroadcastAsync($"SAY {session.Name}: {msg}", exclude: null, ct);
                }
                else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    break; // will fall into finally block and broadcast leave
                }
                else if (line.Equals("HOST_LIST", StringComparison.OrdinalIgnoreCase))
                {
                    await SendLobbyListAsync(writer);
                }
                else if (line.StartsWith("HOST_REGISTER ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3 || !int.TryParse(parts[2], out var udpPort))
                    {
                        await writer.WriteLineAsync("ERROR Usage: HOST_REGISTER <LobbyName> <UdpPort> [MaxPlayers]");
                        continue;
                    }

                    int maxPlayers = 8;
                    if (parts.Length >= 4) int.TryParse(parts[3], out maxPlayers);

                    var remoteEp = (IPEndPoint)tcp.Client.RemoteEndPoint!;
                    string hostIp = remoteEp.Address.ToString();

                    string lobbyName = parts[1];

                    int id = AddLobby(lobbyName, hostIp, udpPort, maxPlayers, session.Tcp);

                    await writer.WriteLineAsync($"HOST_REGISTERED {id}");
                    Console.WriteLine($"[TCP] Lobby registered #{id} '{lobbyName}' {hostIp}:{udpPort}");
                }

                else if (line.StartsWith("HOST_UNREGISTER ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || !int.TryParse(parts[1], out var lobId))
                    {
                        await writer.WriteLineAsync("ERROR Usage: HOST_UNREGISTER <LobbyId>");
                        continue;
                    }

                    if (RemoveLobby(lobId))
                    {
                        await writer.WriteLineAsync($"HOST_UNREGISTERED {lobId}");
                        Console.WriteLine($"[TCP] Lobby unregistered #{lobId}");

                        await BroadcastAsync($"HOST_UNREGISTERED {lobId}", exclude: null, ct);
                    }
                    else
                    {
                        await writer.WriteLineAsync("ERROR Lobby not found");
                    }
                }

                // JOIN <LobbyId>
                else if (line.StartsWith("JOIN ", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2 || !int.TryParse(parts[1], out var lobId))
                    {
                        await writer.WriteLineAsync("ERROR Usage: JOIN <LobbyId>");
                        continue;
                    }

                    if (_lobbies.TryGetValue(lobId, out var lob))
                    {
                        // Reject joining locked/started lobbies
                        if (!lob.InProgress)
                        {
                            lob.CurPlayers = Math.Min(lob.CurPlayers + 1, lob.MaxPlayers);
                            await writer.WriteLineAsync($"JOIN_INFO {lob.HostIp} {lob.UdpPort}");
                            // tell everyone the counts changed
                            await BroadcastAsync($"HOST_REGISTERED {lob.Id}", exclude: null, ct); // reuse event to trigger client refresh
                        }

                        else
                        {
                            await writer.WriteLineAsync($"JOIN_INFO {lob.HostIp} {lob.UdpPort}");
                        }
                    }
                    else
                    {
                        await writer.WriteLineAsync("ERROR Lobby not found");
                    }
                }

// HOST_START <LobbyId>
else if (line.StartsWith("HOST_START ", StringComparison.OrdinalIgnoreCase))
{
    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2 || !int.TryParse(parts[1], out var lobId))
    {
        await writer.WriteLineAsync("ERROR Usage: HOST_START <LobbyId>");
        continue;
    }

    if (_lobbies.TryGetValue(lobId, out var lob))
    {
        if (lob.InProgress)
        {
            await writer.WriteLineAsync("ERROR Already started");
        }
        else
        {
            lob.InProgress = true; // lock it
            Console.WriteLine($"[TCP] Lobby #{lob.Id} '{lob.Name}' locked/started");
            await BroadcastAsync($"LOBBY_LOCKED {lob.Id}", exclude: null, ct);
        }
    }
    else
    {
        await writer.WriteLineAsync("ERROR Lobby not found");
    }
}
                else
                {
                    await writer.WriteLineAsync("ERROR Unknown command");
                }
            }
        }
        catch (IOException) { /* client dropped connection */ }
        catch (ObjectDisposedException) { /* shutting down */ }
        finally
        {
            _clients.TryRemove(tcp, out _);
            Console.WriteLine($"[TCP] {(session.Name ?? "Client")} disconnected");
            await BroadcastAsync($"INFO {session.Name} left.", exclude: null, ct);
            try { tcp.Close(); } catch { }
        }
    }

    // -------------------------------------------------------------------------
    // Lobby helpers
    // -------------------------------------------------------------------------
    private int AddLobby(string name, string ip, int udpPort, int maxPlayers, TcpClient owner)
    {
        var id = Interlocked.Increment(ref _nextLobbyId);
        _lobbies[id] = new Lobby
        {
            Id = id,
            Name = name,
            HostIp = ip,
            UdpPort = udpPort,
            MaxPlayers = maxPlayers,
            CurPlayers = 0,
            InProgress = false
        };
        _lobbyOwners[id] = owner;  // remember who owns this lobby
        return id;
    }



    private bool RemoveLobby(int id) => _lobbies.TryRemove(id, out _);

    private async Task SendLobbyListAsync(StreamWriter w)
    {
        await w.WriteLineAsync($"LOBBIES {_lobbies.Count}");
        foreach (var kv in _lobbies)
        {
            var L = kv.Value;
            // id|name|ip|udpPort|max|cur|inprog
            await w.WriteLineAsync($"{L.Id}|{L.Name}|{L.HostIp}|{L.UdpPort}|{L.MaxPlayers}|{L.CurPlayers}|{(L.InProgress ? 1 : 0)}");
        }
    }

    // -------------------------------------------------------------------------
    // Chat helpers
    // -------------------------------------------------------------------------
    private IEnumerable<string> GetNamesSnapshot()
    {
        foreach (var kv in _clients)
        {
            var n = kv.Value.Name;
            if (!string.IsNullOrWhiteSpace(n))
                yield return n!;
        }
    }

    private async Task BroadcastAsync(string line, TcpClient? exclude, CancellationToken ct)
    {
        foreach (var kv in _clients)
        {
            if (exclude != null && kv.Key == exclude) continue;
            try { await kv.Value.Writer.WriteLineAsync(line); } catch { /* ignore broken pipes */ }
        }
    }

    // -------------------------------------------------------------------------
    // Small per-client record
    // -------------------------------------------------------------------------
    private sealed class ClientSession
    {
        public string? Name;
        public StreamWriter Writer { get; }
        public TcpClient Tcp { get; }

        public ClientSession(TcpClient tcp, StreamWriter writer)
        {
            Tcp = tcp;
            Writer = writer;
        }
    }

    private async Task RemoveLobbiesOwnedByAsync(TcpClient owner, CancellationToken ct)
    {
        foreach (var kv in _lobbyOwners)
        {
            if (kv.Value == owner)
            {
                var lobId = kv.Key;
                _lobbyOwners.TryRemove(lobId, out _);
                _lobbies.TryRemove(lobId, out _);
                Console.WriteLine($"[TCP] Auto-unlisted lobby #{lobId} (owner disconnected)");
                await BroadcastAsync($"HOST_UNREGISTERED {lobId}", exclude: null, ct);
            }
        }
    }

}
