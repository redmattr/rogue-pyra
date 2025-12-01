// Program.cs
// Entry point for RoguePyra. Chooses a mode based on command-line args.
//
// Modes:
//   --server      → TCP lobby/chat server. Connects clients to waiting hosts.
//   --host        → Authoritative UDP game host.
//   --client      → WinForms client (launches MainMenuForm → HostList → GameForm).
//
// Common flags:
//   --ip <ip>         When running server: server bind IP, default 0.0.0.0; When running
//   --port <p>        When running server: TCP port, default Protocol.DefaultTcpPort; When running host: UDP port, default Protocol.DefaultUdpPort.

namespace RoguePyra;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using RoguePyra.Networking; // TcpMainServer, UdpGameHost, UdpGameClient
using RoguePyra.UI;         // GameForm

internal static class Program {
	enum ProgramMode { notSet, server, host, client, devbox } // Possible program modes.

	// We only need STAThread when launching WinForms (client), but it doesn't hurt to have it here for the whole program.
	[STAThread]
	private static async Task Main(string[] args) {

		// Determine program mode to run in.
		ProgramMode programMode = ProgramMode.notSet;
		Dictionary<string, ProgramMode> modeMap = new(StringComparer.OrdinalIgnoreCase) { // Maps flags to modes, case insensitive.
				{ "--server", ProgramMode.server },
				{ "--host", ProgramMode.host },
				{ "--client", ProgramMode.client },
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

		// Run the apropriate mode.
		switch (programMode) {
			case ProgramMode.server:
				IPAddress serverIP = IPAddress.Parse(GetArg(args, "--ip") ?? "0.0.0.0");
				int serverPort = ParseInt(GetArg(args, "--port"), Protocol.DefaultTcpPort);
				await RunServerAsync(serverIP, serverPort, cts.Token);
				return;
			case ProgramMode.host:
				int hostPort = ParseInt(GetArg(args, "--port"), Protocol.DefaultUdpPort);
				await RunHostAsync(hostPort, cts.Token);
				return;
			case ProgramMode.client:
				RunClient();
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

	private static async Task RunHostAsync(int port, CancellationToken ct) {
		UdpGameHost host = new(port);
		Console.WriteLine($"[ENTRY] Starting UDP host on 0.0.0.0:{port}  (Ctrl+C to stop)");
		await host.RunAsync(ct);
		Console.WriteLine("[ENTRY] UDP host stopped.");
	}

	// WinForms entry: show the main menu → host list → game form
	private static void RunClient() {
		Console.WriteLine($"[ENTRY] Launching main form.");
		Application.EnableVisualStyles();
		Application.Run(new MainForm());
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
		Console.WriteLine("  --client");
		Console.WriteLine();
		Console.WriteLine("Examples:");
		Console.WriteLine("  dotnet run --server --ip 0.0.0.0 --port 5000");
		Console.WriteLine("  dotnet run --host --port 6000");
		Console.WriteLine("  dotnet run --client");
	}
}