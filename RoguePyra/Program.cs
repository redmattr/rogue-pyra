// -----------------------------------------------------------------------------
// Program.cs  (place in project root)
// -----------------------------------------------------------------------------
// Purpose
// Single entry point that launches one of four modes based on command-line args:
//   1) --server      → TCP lobby/chat server
//   2) --clientcli   → Console TCP client (chat + lobby commands)
//   3) --host        → Authoritative UDP game host (simulation/broadcast)
//   4) --clientviz   → WinForms client that visualizes the UDP game state
//
// Examples
//   dotnet run -- --server --bind 0.0.0.0 --tcpport 5000
//   dotnet run -- --clientcli --name Alice --host 127.0.0.1 --tcpport 5000
//   dotnet run -- --host --udpport 6000
//   dotnet run -- --clientviz --host 127.0.0.1 --udpport 6000
//
// Notes
// - No external parsing library to keep it simple.
// - Ctrl+C cleanly cancels async loops (server/host/clientcli).
// - WinForms requires [STAThread]. We only initialize WinForms for --clientviz.
// -----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using RoguePyra.Networking; // TcpMainServer, TcpClientApp, UdpGameHost, UdpGameClient
using RoguePyra.UI;         // GameForm, LobbyForm, TcpClientForm

namespace RoguePyra
{
    internal static class Program
    {
        // We only need STAThread when launching WinForms (clientviz),
        // but it doesn't hurt to have it here for the whole program.
        [STAThread]
        private static void Main(string[] args)
        {
            // Quick switches
            bool isServer    = args.Contains("--server",    StringComparer.OrdinalIgnoreCase);
            bool isClientCli = args.Contains("--clientcli", StringComparer.OrdinalIgnoreCase);
            bool isHost      = args.Contains("--host",      StringComparer.OrdinalIgnoreCase);
            bool isClientViz = args.Contains("--clientviz", StringComparer.OrdinalIgnoreCase);

            // Shared options with defaults
            string bindIp   = GetArg(args, "--bind")    ?? "0.0.0.0";
            string hostIp   = GetArg(args, "--host")    ?? "127.0.0.1";
            int tcpPort     = ParseInt(GetArg(args, "--tcpport"), Protocol.DefaultTcpPort);
            int udpPort     = ParseInt(GetArg(args, "--udpport"), Protocol.DefaultUdpPort);
            string name     = GetArg(args, "--name")    ?? $"Player{Random.Shared.Next(1000, 9999)}";

            // If no mode specified, show help and exit.
            if (!isServer && !isClientCli && !isHost && !isClientViz)
            {
                PrintHelp();
                return;
            }

            // Console modes share a CancellationToken that cancels on Ctrl+C.
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                try { cts.Cancel(); } catch { }
            };

            if (isServer)
            {
                RunServer(bindIp, tcpPort, cts.Token).GetAwaiter().GetResult();
                return;
            }

            if (isClientCli)
            {
                RunClientCli(name, hostIp, tcpPort, cts.Token).GetAwaiter().GetResult();
                return;
            }

            if (isHost)
            {
                RunUdpHost(udpPort, cts.Token).GetAwaiter().GetResult();
                return;
            }

            if (isClientViz)
            {
                RunClientVisualizer(hostIp, udpPort);
                return;
            }
        }

        // -----------------------------------------------------------------------------
        // Mode launchers
        // -----------------------------------------------------------------------------

        private static async Task RunServer(string bindIp, int tcpPort, CancellationToken ct)
        {
            var ip = IPAddress.Parse(bindIp);
            var server = new TcpMainServer(ip, tcpPort);
            Console.WriteLine($"[ENTRY] Starting TCP server on {bindIp}:{tcpPort}  (Ctrl+C to stop)");
            await server.RunAsync(ct);
            Console.WriteLine("[ENTRY] TCP server stopped.");
        }

        private static async Task RunClientCli(string name, string hostIp, int tcpPort, CancellationToken ct)
        {
            var client = new TcpClientApp(name, hostIp, tcpPort);
            Console.WriteLine($"[ENTRY] Starting TCP console client to {hostIp}:{tcpPort} as '{name}'  (type /quit to exit)");
            await client.RunAsync(ct);
            Console.WriteLine("[ENTRY] TCP console client stopped.");
        }

        private static async Task RunUdpHost(int udpPort, CancellationToken ct)
        {
            var host = new UdpGameHost(udpPort);
            Console.WriteLine($"[ENTRY] Starting UDP host on 0.0.0.0:{udpPort}  (Ctrl+C to stop)");
            await host.RunAsync(ct);
            Console.WriteLine("[ENTRY] UDP host stopped.");
        }

        // WinForms visualizer entry
        private static void RunClientVisualizer(string hostIp, int udpPort)
        {
            Console.WriteLine($"[ENTRY] Launching WinForms visualizer → {hostIp}:{udpPort}");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GameForm(hostIp, udpPort));
            Console.WriteLine("[ENTRY] Visualizer closed.");
        }

        // -----------------------------------------------------------------------------
        // Helpers (arg parsing + help)
        // -----------------------------------------------------------------------------

        private static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static int ParseInt(string? s, int fallback)
            => int.TryParse(s, out var v) ? v : fallback;

        private static void PrintHelp()
        {
            Console.WriteLine("RoguePyra — modes:");
            Console.WriteLine("  --server      [--bind <ip>] [--tcpport <p>]");
            Console.WriteLine("  --clientcli   --name <n> --host <ip> [--tcpport <p>]");
            Console.WriteLine("  --host        [--udpport <p>]");
            Console.WriteLine("  --clientviz   --host <ip> [--udpport <p>]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- --server --bind 0.0.0.0 --tcpport 5000");
            Console.WriteLine("  dotnet run -- --clientcli --name Alice --host 127.0.0.1 --tcpport 5000");
            Console.WriteLine("  dotnet run -- --host --udpport 6000");
            Console.WriteLine("  dotnet run -- --clientviz --host 127.0.0.1 --udpport 6000");
        }
    }
}
