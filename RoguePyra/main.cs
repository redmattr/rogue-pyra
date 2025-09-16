using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RoguePyra.Net;

namespace RoguePyra
{
    public static class Program
    {
        // Modes:
        //   TCP server (chat/lobby):  --server --ip 0.0.0.0 --port 5000
        //   TCP client (chat/lobby):  --client --host 127.0.0.1 --port 5000 --name Bob
        //   UDP host (game demo):     --hostgame --udpport 6000
        //   UDP client visualizer:    --clientviz --host 127.0.0.1 --udpport 6000

        [STAThread]
        public static async Task Main(string[] args)
        {
            string mode = "";
            string host = "127.0.0.1";
            int port = 5000;
            int udpport = UdpPorts.Default;
            string ip = "0.0.0.0";
            string name = $"Player{Random.Shared.Next(1000, 9999)}";

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--server": mode = "server"; break;
                    case "--client": mode = "client"; break;
                    case "--hostgame": mode = "hostgame"; break;
                    case "--clientviz": mode = "clientviz"; break;
                    case "--host": if (i + 1 < args.Length) host = args[++i]; break;
                    case "--port": if (i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p; break;
                    case "--udpport": if (i + 1 < args.Length && int.TryParse(args[++i], out var up)) udpport = up; break;
                    case "--ip":   if (i + 1 < args.Length) ip = args[++i]; break;
                    case "--name": if (i + 1 < args.Length) name = args[++i]; break;
                }
            }

            if (mode == "server")
            {
                var srv = new MainServer(IPAddress.Parse(ip), port);
                Console.WriteLine("=== RoguePyra Main Server (TCP) ===");
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                await srv.RunAsync(cts.Token);
                return;
            }

            if (mode == "client")
            {
                var cli = new Client(name, host, port);
                Console.WriteLine($"=== RoguePyra Client (TCP) ({name}) ===");
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                await cli.RunAsync(cts.Token);
                return;
            }

            if (mode == "hostgame")
            {
                var hostGame = new UdpGameHost(udpport);
                Console.WriteLine($"=== RoguePyra UDP Host (port {udpport}) ===  Ctrl+C to quit");
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                await hostGame.RunAsync(cts.Token);
                return;
            }

            if (mode == "clientviz")
            {
                Console.WriteLine($"=== RoguePyra UDP Client Visualizer → {host}:{udpport} ===");
                using var cts = new CancellationTokenSource();
                var ugc = new UdpGameClient(host, udpport);
                _ = ugc.RunAsync(cts.Token); // background UDP
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new GameForm(ugc));
                cts.Cancel();
                return;
            }

            Console.WriteLine("Usage:");
            Console.WriteLine("  --server [--ip <bind_ip>] [--port <p>]");
            Console.WriteLine("  --client --host <server_ip> --port <p> [--name <n>]");
            Console.WriteLine("  --hostgame [--udpport <p>]");
            Console.WriteLine("  --clientviz --host <ip> [--udpport <p>]");
        }
    }
}