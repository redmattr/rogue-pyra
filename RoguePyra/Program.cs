// Program.cs
// Entry point for RoguePyra. Chooses a mode based on command-line args.
//
// Modes:
//   --server      → TCP lobby/chat server. Connects clients to waiting hosts.
//   --host        → Authoritative UDP game host.
//   --clientcli   → Console TCP client (chat + lobby commands).
//   --clientviz   → WinForms client (launches MainMenuForm → HostList → GameForm).
//
// Common flags:
//   --ip <ip>         When running server: server bind IP, default 0.0.0.0; When running
//   --port <p>        When running server: TCP port, default Protocol.DefaultTcpPort; When running host: UDP port, default Protocol.DefaultUdpPort.
//   --name <n>        Player name for client.
//   --hostip <ip>     (server IP for clientcli; menu reads this from UI)

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using RoguePyra.Networking; // TcpMainServer, UdpGameHost, TcpClientApp, UdpGameClient
using RoguePyra.UI;         // GameForm

namespace RoguePyra {
	internal static class Program {
		enum ProgramMode { notSet, server, host, clientCLI, clientViz, devbox } // Possible program modes.

		// We only need STAThread when launching WinForms (clientviz), but it doesn't hurt to have it here for the whole program.
		[STAThread]
		private static async Task Main(string[] args) {

			// Determine program mode to run in.
			ProgramMode programMode = ProgramMode.notSet;
			Dictionary<string, ProgramMode> modeMap = new(StringComparer.OrdinalIgnoreCase) { // Maps flags to modes, case insensitive.
				{ "--server", ProgramMode.server },
				{ "--host", ProgramMode.host },
				{ "--clientcli", ProgramMode.clientCLI },
				{ "--clientviz", ProgramMode.clientViz },
				{ "--devbox", ProgramMode.devbox }
			};
			foreach (string arg in args) {
				if (modeMap.TryGetValue(arg, out ProgramMode mode)) { // Only executes if arg is a valid key.
					if (programMode != ProgramMode.notSet) { PrintHelp("Error: multiple program modes specified."); return; } // Errors help info and exits if mode is already set.
					else programMode = mode;
				}
			}
			if (programMode == ProgramMode.notSet) { PrintHelp("Error: no program mode specified."); return; }

			// Console modes share a CancellationToken that cancels on Ctrl+C.
			using CancellationTokenSource cts = new();
			Console.CancelKeyPress += (_, e) => {
				e.Cancel = true;
				try { cts.Cancel(); } catch { }
			};

			// TEMP: Determine server port. (TODO: Move to switch below.)
			int serverPort = ParseInt(GetArg(args, "--port"), Protocol.DefaultTcpPort);

			// Run the apropriate mode.
			switch (programMode) {
				case ProgramMode.server:
					IPAddress serverIP = IPAddress.Parse(GetArg(args, "--ip") ?? "0.0.0.0");
					await RunServerAsync(serverIP, serverPort, cts.Token);
					return;
				case ProgramMode.host:
					int hostPort = ParseInt(GetArg(args, "--port"), Protocol.DefaultUdpPort);
					await RunUdpHostAsync(hostPort, cts.Token);
					return;
				case ProgramMode.clientCLI:
					string playerName = GetArg(args, "--name") ?? $"Player{Random.Shared.Next(1000, 9999)}";
					IPAddress hostIP = IPAddress.Parse(GetArg(args, "--hostip") ?? "127.0.0.1");
					await RunClientCLIAsync(playerName, hostIP, serverPort, cts.Token);
					return;
				case ProgramMode.clientViz:
					RunClientVisualizer();
					return;
				case ProgramMode.devbox:
					RunDevBox();
					return;
				case ProgramMode.notSet: // If no mode specified, show help and exit.
					PrintHelp("Error: no program mode was specified.");
					return;
				default: // This should be impossible to run, but just in case...
					PrintHelp("Unknown logic error. This code should be unreachable, please notify the developers.");
					return;
			}
		}

		// --- Mode runners ------------------------------------------------------

		private static async Task RunServerAsync(IPAddress ip, int port, CancellationToken ct) {
			TcpMainServer server = new(ip, port);
			Console.WriteLine($"[ENTRY] Starting TCP server on {ip}:{port}  (Ctrl+C to stop)");
			await server.RunAsync(ct);
			Console.WriteLine("[ENTRY] TCP server stopped.");
		}

		private static async Task RunUdpHostAsync(int port, CancellationToken ct) {
			UdpGameHost host = new(port);
			Console.WriteLine($"[ENTRY] Starting UDP host on 0.0.0.0:{port}  (Ctrl+C to stop)");
			await host.RunAsync(ct);
			Console.WriteLine("[ENTRY] UDP host stopped.");
		}

		// This might be redundant now??? I might try removing this at some point, hopefully nothing explodes - Ian.
		private static async Task RunClientCLIAsync(string name, IPAddress hostIp, int port, CancellationToken ct) {
			TcpClientApp client = new(name, hostIp.ToString(), port);
			Console.WriteLine($"[ENTRY] Starting TCP console client to {hostIp}:{port} as '{name}'  (type /quit to exit)");
			await client.RunAsync(ct);
			Console.WriteLine("[ENTRY] TCP console client stopped.");
		}

		// WinForms entry: show the main menu → host list → game form
		private static void RunClientVisualizer() {
			Console.WriteLine($"[ENTRY] Launching main form.");
			Application.EnableVisualStyles();
			Application.Run(new MainMenuForm());
			Console.WriteLine("[ENTRY] Client closed.");
		}

		private static void RunDevBox() {
			Console.WriteLine($"[ENTRY] Starting Physics Box...");
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new RoguePyra.Physics.PhysicsBox());
			Console.WriteLine($"[ENTRY] Physics Box Stopped.");
		}

		// -----------------------------------------------------------------------------
		// Helpers
		// -----------------------------------------------------------------------------

		// Returns the value after the specified argument, or null if not found.
		private static string? GetArg(string[] args, string name) {
			bool returnNext = false;
			foreach (string arg in args) {
				if (returnNext) return arg;
				if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) returnNext = true;
			}
			return null;
		}

		// Attempts to parse int, returns fallback on fail (defaults to -1 if fallback not provided).
		private static int ParseInt(string? s, int fallback = -1) => int.TryParse(s, out int v) ? v : fallback;

		// Prints info about the program arguments, with a preceeding message if specified.
		private static void PrintHelp(string? message = null) {
			if (message != null) Console.WriteLine(message + '\n');
			Console.WriteLine("RoguePyra — modes:");
			Console.WriteLine("  --server      [--ip <ip>] [--port <p>]");
			Console.WriteLine("  --host        [--port <p>]");
			Console.WriteLine("  --clientcli   --name <n> --hostip <ip> [--port <p>]");
			Console.WriteLine("  --clientviz   (launches WinForms menu)");
			Console.WriteLine();
			Console.WriteLine("Examples:");
			Console.WriteLine("  dotnet run --server --ip 0.0.0.0 --port 5000");
			Console.WriteLine("  dotnet run --host --port 6000");
			Console.WriteLine("  dotnet run --clientcli --name Alice --hostip 127.0.0.1 --port 5000");
			Console.WriteLine("  dotnet run --clientviz");
		}
	}
}