// -----------------------------------------------------------------------------
// TcpClientApp.cs  (place in: Networking/)
// -----------------------------------------------------------------------------
// Purpose
// Minimal console TCP client to interact with the lobby/chat server.
// - Sends HELLO, LIST, MSG, QUIT
// - Manages lobby commands: HOST_LIST / HOST_REGISTER / HOST_UNREGISTER / JOIN
// - Prints all server responses to the console
//
// Usage idea (example)
// var cts = new CancellationTokenSource();
// var client = new TcpClientApp("MyName", "127.0.0.1", Protocol.DefaultTcpPort);
// await client.RunAsync(cts.Token);
//
// Console commands (type and press Enter)
//   /list
//   /msg <text>
//   /quit
//   /hosts
//   /hostreg <LobbyName> <UdpPort> [MaxPlayers]
//   /hostunreg <LobbyId>
//   /join <LobbyId>
//
// Keep this file TCP-only. UDP gameplay is handled by UdpGameClient/UdpGameHost.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoguePyra.Networking;

public sealed class TcpClientApp
{
    private readonly string _name;
    private readonly string _host;
    private readonly int _port;

    public TcpClientApp(string name, string host, int port = Protocol.DefaultTcpPort)
    {
        _name = string.IsNullOrWhiteSpace(name)
            ? $"Player{Random.Shared.Next(1000, 9999)}"
            : name.Trim();
        _host = host;
        _port = port;
    }

    /// Connects to the server, starts a console input loop, and prints all server lines.
    /// Call once; cancels when token is signaled or when user types /quit.
    public async Task RunAsync(CancellationToken ct)
    {
        using var tcp = new TcpClient();
        Console.WriteLine($"[Client] Connecting to {_host}:{_port} ...");
        await tcp.ConnectAsync(_host, _port, ct);
        Console.WriteLine("[Client] Connected.");

        using var stream = tcp.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        // Send greeting first
        await writer.WriteLineAsync(Protocol.BuildHello(_name));

        // Background task: read and print every server line
        var recvTask = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    Console.WriteLine($"[SRV] {line}");
                }
            }
            catch (IOException) { /* server closed */ }
            catch (ObjectDisposedException) { /* shutting down */ }
            catch { /* ignore */ }
        }, ct);

        // Foreground loop: read console → send to server
        PrintHelp();
        while (!ct.IsCancellationRequested)
        {
            var s = Console.ReadLine();
            if (s is null) break;
            s = s.Trim();

            if (s.Length == 0) continue;

            // --- Basic chat/lists ---
            if (s.Equals("/list", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("LIST");
            }
            else if (s.StartsWith("/msg ", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync(Protocol.BuildMsg(s.Substring(5)));
            }
            else if (s.Equals("/quit", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("QUIT");
                break;
            }
            // --- Lobby commands ---
            else if (s.Equals("/hosts", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("HOST_LIST");
            }
            else if (s.StartsWith("/hostreg ", StringComparison.OrdinalIgnoreCase))
            {
                // /hostreg <LobbyName> <UdpPort> [MaxPlayers]
                var args = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length < 3)
                {
                    Console.WriteLine("Usage: /hostreg <LobbyName> <UdpPort> [MaxPlayers]");
                }
                else
                {
                    var lobbyName = args[1];
                    var udpPort = args[2];
                    var max = (args.Length >= 4) ? args[3] : null;

                    var cmd = max is null
                        ? $"HOST_REGISTER {lobbyName} {udpPort}"
                        : $"HOST_REGISTER {lobbyName} {udpPort} {max}";

                    await writer.WriteLineAsync(cmd);
                }
            }
            else if (s.StartsWith("/hostunreg ", StringComparison.OrdinalIgnoreCase))
            {
                // /hostunreg <LobbyId>
                var args = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length != 2)
                {
                    Console.WriteLine("Usage: /hostunreg <LobbyId>");
                }
                else
                {
                    await writer.WriteLineAsync($"HOST_UNREGISTER {args[1]}");
                }
            }
            else if (s.StartsWith("/join ", StringComparison.OrdinalIgnoreCase))
            {
                // /join <LobbyId>
                var args = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length != 2)
                {
                    Console.WriteLine("Usage: /join <LobbyId>");
                }
                else
                {
                    await writer.WriteLineAsync($"JOIN {args[1]}");
                    Console.WriteLine("➡ If you receive:  JOIN_INFO <ip> <udpPort>");
                    Console.WriteLine("   start the UDP visualizer like:");
                    Console.WriteLine("   dotnet run -- --clientviz --host <ip> --udpport <udpPort>");
                }
            }
            else
            {
                PrintHelp();
            }
        }

        try { tcp.Close(); } catch { }
        await Task.WhenAny(recvTask, Task.Delay(200, ct));
        Console.WriteLine("[Client] Disconnected.");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  /list");
        Console.WriteLine("  /msg <text>");
        Console.WriteLine("  /quit");
        Console.WriteLine("  /hosts");
        Console.WriteLine("  /hostreg <LobbyName> <UdpPort> [MaxPlayers]");
        Console.WriteLine("  /hostunreg <LobbyId>");
        Console.WriteLine("  /join <LobbyId>");
    }
}
