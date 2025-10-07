// Program.cs
// Entry point for RoguePyra. Chooses a mode based on command-line args.
//
// Modes:
//   --server      → TCP lobby/chat server
//   --clientcli   → Console TCP client (chat + lobby commands)
//   --host        → Authoritative UDP game host
//   --clientviz   → WinForms client (launches MainMenuForm → HostList → GameForm)
//
// Common flags:
//   --bind <ip>       (server bind IP, default 0.0.0.0)
//   --tcpport <p>     (TCP port, default Protocol.DefaultTcpPort)
//   --udpport <p>     (UDP port, default Protocol.DefaultUdpPort)
//   --name <n>        (player name for clientcli)
//   --hostip <ip>     (server IP for clientcli; menu reads this from UI)

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RoguePyra.Networking;
using RoguePyra.UI;

namespace RoguePyra
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Mode switches
            bool isServer    = HasFlag(args, "--server");
            bool isClientCli = HasFlag(args, "--clientcli");
            bool isHost      = HasFlag(args, "--host");
            bool isClientViz = HasFlag(args, "--clientviz");

            // Shared options
            string bindIp   = GetArg(args, "--bind")    ?? "0.0.0.0";
            string hostIp   = GetArg(args, "--hostip")  ?? "127.0.0.1"; // note: hostip (not --host)
            int tcpPort     = ParseInt(GetArg(args, "--tcpport"), Protocol.DefaultTcpPort);
            int udpPort     = ParseInt(GetArg(args, "--udpport"), Protocol.DefaultUdpPort);
            string name     = GetArg(args, "--name")    ?? $"Player{Random.Shared.Next(1000, 9999)}";

            if (!isServer && !isClientCli && !isHost && !isClientViz)
            {
                PrintHelp();
                return;
            }

            // Console modes use a cancellation token for Ctrl+C
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; try { cts.Cancel(); } catch { } };

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
                RunClientVisualizer();
                return;
            }
        }

        // --- Mode runners ------------------------------------------------------

        private static async Task RunServer(string bindIp, int tcpPort, CancellationToken ct)
        {
            var ip = IPAddress.Parse(bindIp);
            var server = new TcpMainServer(ip, tcpPort);
            Console.WriteLine($"[ENTRY] TCP server on {bindIp}:{tcpPort} (Ctrl+C to stop)");
            await server.RunAsync(ct);
            Console.WriteLine("[ENTRY] TCP server stopped.");
        }

        private static async Task RunClientCli(string name, string hostIp, int tcpPort, CancellationToken ct)
        {
            var client = new TcpClientApp(name, hostIp, tcpPort);
            Console.WriteLine($"[ENTRY] TCP client → {hostIp}:{tcpPort} as '{name}' (type /quit to exit)");
            await client.RunAsync(ct);
            Console.WriteLine("[ENTRY] TCP client stopped.");
        }

        private static async Task RunUdpHost(int udpPort, CancellationToken ct)
        {
            var host = new UdpGameHost(udpPort);
            Console.WriteLine($"[ENTRY] UDP host on 0.0.0.0:{udpPort} (Ctrl+C to stop)");
            await host.RunAsync(ct);
            Console.WriteLine("[ENTRY] UDP host stopped.");
        }

        // WinForms entry: show the main menu → host list → game form
        private static void RunClientVisualizer()
        {
            Console.WriteLine("[ENTRY] Launching client menu");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainMenuForm());
            Console.WriteLine("[ENTRY] Client closed.");
        }

        // --- Helpers -----------------------------------------------------------

        private static bool HasFlag(string[] args, string name)
            => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

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
            Console.WriteLine("  --clientcli   --name <n> --hostip <ip> [--tcpport <p>]");
            Console.WriteLine("  --host        [--udpport <p>]");
            Console.WriteLine("  --clientviz   (launches WinForms menu)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- --server --bind 0.0.0.0 --tcpport 5000");
            Console.WriteLine("  dotnet run -- --clientcli --name Alice --hostip 127.0.0.1 --tcpport 5000");
            Console.WriteLine("  dotnet run -- --host --udpport 6000");
            Console.WriteLine("  dotnet run -- --clientviz");
        }
    }
}