// -----------------------------------------------------------------------------
// Program.cs  (place in project root)
// -----------------------------------------------------------------------------
// Purpose
// Single entry point that launches one of four modes based on command-line args:
//   1) --server      → TCP lobby/chat server. Connects clients to waiting hosts.
//   2) --host        → Authoritative UDP game host (simulation/broadcast).
//   3) --clientcli   → Console TCP client (chat + lobby commands).
//   4) --clientviz   → WinForms client that visualizes the game state via UDP.
//
// Examples
//   dotnet run -- --server --bind 0.0.0.0 --tcpport 5000
//   dotnet run -- --host --udpport 6000
//   dotnet run -- --clientcli --name Alice --host 127.0.0.1 --tcpport 5000
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

using RoguePyra.Networking; // TcpMainServer, UdpGameHost, TcpClientApp, UdpGameClient
using RoguePyra.UI;         // GameForm

namespace RoguePyra
{
	internal static class Program
    {
		enum ProgramMode { notSet, server, host, clientCLI, clientViz } // Possible program modes.

        // We only need STAThread when launching WinForms (clientviz),
        // but it doesn't hurt to have it here for the whole program.
        [STAThread]
        private static async Task Main(string[] args)
        {
			// Determine program mode to run in.
			ProgramMode programMode = ProgramMode.notSet;
			if (args.Contains("--server", StringComparer.OrdinalIgnoreCase)) programMode = ProgramMode.server;
			if (args.Contains("--host", StringComparer.OrdinalIgnoreCase)) {
				if (programMode == ProgramMode.notSet) programMode = ProgramMode.host;
				else { PrintHelp("Error: multiple program modes specified."); return; } // Errors help info and exits if mode is already set.
			}
			if (args.Contains("--clientcli", StringComparer.OrdinalIgnoreCase)) {
				if (programMode == ProgramMode.notSet) programMode = ProgramMode.clientCLI;
				else { PrintHelp("Error: multiple program modes specified."); return; }
			}
			if (args.Contains("--clientviz", StringComparer.OrdinalIgnoreCase)) {
				if (programMode == ProgramMode.notSet) programMode = ProgramMode.clientViz;
				else { PrintHelp("Error: multiple program modes specified."); return; }
			}

			// Determine other options with default values.
			IPAddress bindIP    = IPAddress.Parse(GetArg(args, "--bind") ?? "0.0.0.0");
            IPAddress hostIP    = IPAddress.Parse(GetArg(args, "--host") ?? "127.0.0.1");
            int tcpPort         = ParseInt(GetArg(args, "--tcpport"), Protocol.DefaultTcpPort);
            int udpPort         = ParseInt(GetArg(args, "--udpport"), Protocol.DefaultUdpPort);
            string playerName   = GetArg(args, "--name") ?? $"Player{Random.Shared.Next(1000, 9999)}";

			// Console modes share a CancellationToken that cancels on Ctrl+C.
			using CancellationTokenSource cts = new();
			Console.CancelKeyPress += (_, e) => {
				e.Cancel = true;
				try { cts.Cancel(); } catch { }
			};

			switch (programMode) {
				case ProgramMode.server:
					await RunServerAsync(bindIP, tcpPort, cts.Token);
					return;
				case ProgramMode.host:
					await RunUdpHostAsync(udpPort, cts.Token);
					return;
				case ProgramMode.clientCLI:
					await RunClientCLIAsync(playerName, hostIP, tcpPort, cts.Token);
					return;
				case ProgramMode.clientViz:
					RunClientVisualizer(hostIP, udpPort);
					return;
				case ProgramMode.notSet: // If no mode specified, show help and exit.
					PrintHelp("Error: no program mode was specified.");
					return;
				default: // This should be impossible to run, but just in case...
					PrintHelp("Unknown logic error. This code should be unreachable, please notify the developers.");
					return;
			}
        }

        // -----------------------------------------------------------------------------
        // Mode launchers
        // -----------------------------------------------------------------------------

        private static async Task RunServerAsync(IPAddress ip, int tcpPort, CancellationToken ct)
        {
            TcpMainServer server = new(ip, tcpPort);
            Console.WriteLine($"[MAIN] Starting TCP server on {ip}:{tcpPort}  (Ctrl+C to stop)");
            await server.RunAsync(ct);
            Console.WriteLine("[MAIN] TCP server stopped.");
        }

		private static async Task RunUdpHostAsync(int udpPort, CancellationToken ct)
		{
			UdpGameHost host = new(udpPort);
			Console.WriteLine($"[MAIN] Starting UDP host on 0.0.0.0:{udpPort}  (Ctrl+C to stop)");
			await host.RunAsync(ct);
			Console.WriteLine("[MAIN] UDP host stopped.");
		}

		private static async Task RunClientCLIAsync(string name, IPAddress hostIp, int tcpPort, CancellationToken ct)
        {
            TcpClientApp client = new(name, hostIp.ToString(), tcpPort);
            Console.WriteLine($"[MAIN] Starting TCP console client to {hostIp}:{tcpPort} as '{name}'  (type /quit to exit)");
            await client.RunAsync(ct);
            Console.WriteLine("[MAIN] TCP console client stopped.");
        }

        // WinForms visualizer entry
        private static void RunClientVisualizer(IPAddress hostIp, int udpPort)
        {
            Console.WriteLine($"[MAIN] Launching WinForms visualizer → {hostIp}:{udpPort}");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GameForm(hostIp, udpPort));
            Console.WriteLine("[MAIN] Visualizer closed.");
        }

        // -----------------------------------------------------------------------------
        // Helpers (arg parsing + help)
        // -----------------------------------------------------------------------------

		// Returns the value after the specified argument, or null if not found.
		private static string? GetArg(string[] args, string name)
		{
			bool returnNext = false;
			foreach (string arg in args) {
				if (returnNext) return arg;
				if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) returnNext = true;
			}
			return null;
		}

		// Attempts to parse int, returns fallback on fail (defaults to -1 if fallback not provided).
		private static int ParseInt(string? s, int fallback = -1)
            => int.TryParse(s, out int v) ? v : fallback;

		// Prints info about the program arguments, with a preceeding message if specified.
        private static void PrintHelp(string? message = null)
        {
			if (message != null) Console.WriteLine(message + '\n');
            Console.WriteLine("RoguePyra — modes:");
            Console.WriteLine("  --server      [--bind <ip>] [--tcpport <p>]");
			Console.WriteLine("  --host        [--udpport <p>]");
			Console.WriteLine("  --clientcli   --name <n> --host <ip> [--tcpport <p>]");
            Console.WriteLine("  --clientviz   --host <ip> [--udpport <p>]");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run -- --server --bind 0.0.0.0 --tcpport 5000");
			Console.WriteLine("  dotnet run -- --host --udpport 6000");
			Console.WriteLine("  dotnet run -- --clientcli --name Alice --host 127.0.0.1 --tcpport 5000");
            Console.WriteLine("  dotnet run -- --clientviz --host 127.0.0.1 --udpport 6000");
        }
    }
}
