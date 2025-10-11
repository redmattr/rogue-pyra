// -----------------------------------------------------------------------------
// NetworkManager.cs  (place in: Networking/)
// -----------------------------------------------------------------------------
// Purpose
// One hub that owns both transports: TCP (chat/control) and UDP (gameplay).
// Exposes simple events and methods so the UI/game never talks to sockets directly.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoguePyra.Networking
{
    public sealed class NetworkManager : IAsyncDisposable
    {
        private TcpChatClient? _tcp;
        private UdpGameClient? _udp;
        private CancellationTokenSource? _cts;

        // ------ Events you can subscribe to from UI/game ------
        public event Action<string>? ChatReceived;          // raw server lines: "SAY Bob: hi", "INFO ..." etc.
        public event Action<string>? Error;                 // textual error notifications
        public event Action<float, Dictionary<string,(float x,float y,int hp)>>? SnapshotReceived;
        public event Action<string>? WinnerAnnounced;       // not used if host isn’t sending WIN

        // Current snapshot cache (optional to read without handling the event)
        public float LavaY { get; private set; }
        public IReadOnlyDictionary<string, (float x, float y, int hp)> Entities => _udp?.Entities ?? _emptyEntities;
        private static readonly Dictionary<string,(float x,float y,int hp)> _emptyEntities = new();

        // ------ Lifecycle ------------------------------------------------------

        /// Connect only to the TCP server (lobby/chat). Keeps the connection open.
        public async Task ConnectTcpAsync(string playerName, string tcpHost, int tcpPort, CancellationToken external = default)
        {
            _cts ??= CancellationTokenSource.CreateLinkedTokenSource(external);

            _tcp = new TcpChatClient(playerName, tcpHost, tcpPort);
            _tcp.LineReceived += s => ChatReceived?.Invoke(s);
            _tcp.Error += err => Error?.Invoke(err);
            await _tcp.StartAsync(_cts.Token);
        }

        /// Connect only to the UDP host for gameplay. Keeps the loops running.
        public Task ConnectUdpAsync(IPAddress udpHost, int udpPort, CancellationToken external = default)
        {
            _cts ??= CancellationTokenSource.CreateLinkedTokenSource(external);

            _udp = new UdpGameClient(udpHost, udpPort);
            _udp.SnapshotApplied += () =>
            {
                LavaY = _udp.LavaY;
                SnapshotReceived?.Invoke(_udp.LavaY, _udp.Entities);
            };
            _udp.WinnerAnnounced += id => WinnerAnnounced?.Invoke(id);

            // Fire-and-forget the UDP loops
            _ = _udp.RunAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// Convenience: connect to both at once (TCP then UDP).
        public async Task ConnectAllAsync(string playerName, string tcpHost, int tcpPort, IPAddress udpHost, int udpPort, CancellationToken external = default)
        {
            await ConnectTcpAsync(playerName, tcpHost, tcpPort, external);
            await ConnectUdpAsync(udpHost, udpPort, external);
        }

        // ------ Outbound API ---------------------------------------------------

        public Task SendChatAsync(string text)
        {
            if (_tcp == null) throw new InvalidOperationException("TCP not connected");
            return _tcp.SendChatAsync(text);
        }

        /// Sends a raw TCP command line (e.g., "HOST_LIST", "JOIN 3", etc.).
        /// Handy for menus until you wrap dedicated methods.
        public Task SendTcpLineAsync(string line)
        {
            if (_tcp == null) throw new InvalidOperationException("TCP not connected");
            return _tcp.SendRawAsync(line);
        }

        /// Sets the current input mask for UDP client (directional keys).
        public void SetKeys(bool up, bool left, bool down, bool right)
        {
            if (_udp == null) throw new InvalidOperationException("UDP not connected");
            _udp.SetKeys(up, left, down, right);
        }

        // ------ Shutdown -------------------------------------------------------
        public async ValueTask DisposeAsync()
        {
            try { _cts?.Cancel(); } catch { }
            if (_tcp is not null) await _tcp.DisposeAsync();
            // UdpGameClient has no explicit Dispose; it stops when token cancels.
            _cts?.Dispose();
        }
    }

    // -----------------------------------------------------------------------------
    // Minimal TCP chat/control client used by NetworkManager.
    // Keeps implementation here to avoid creating another file right now.
    // -----------------------------------------------------------------------------
    internal sealed class TcpChatClient : IAsyncDisposable
    {
        private readonly string _name;
        private readonly string _host;
        private readonly int _port;

        private TcpClient? _tcp;
        private StreamWriter? _w;
        private CancellationTokenSource? _cts;

        public event Action<string>? LineReceived;  // emits raw lines from server
        public event Action<string>? Error;

        public TcpChatClient(string name, string host, int port)
        {
            _name = string.IsNullOrWhiteSpace(name) ? $"Player{Random.Shared.Next(1000,9999)}" : name.Trim();
            _host = host;
            _port = port;
        }

        public async Task StartAsync(CancellationToken external)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
            var ct = _cts.Token;

            _tcp = new TcpClient();
            await _tcp.ConnectAsync(_host, _port, ct);

            var stream = _tcp.GetStream();
            var r = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            _w = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier:false), 1024, leaveOpen: true) { AutoFlush = true };

            // Greet
            await _w.WriteLineAsync(Protocol.BuildHello(_name));

            // Read loop
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var line = await r.ReadLineAsync();
                        if (line == null) break;
                        LineReceived?.Invoke(line);
                    }
                }
                catch (Exception ex)
                {
                    Error?.Invoke(ex.Message);
                }
            }, ct);
        }

        public Task SendChatAsync(string text)
        {
            if (_w == null) throw new InvalidOperationException("TCP not connected");
            return _w.WriteLineAsync(Protocol.BuildMsg(text));
        }

        public Task SendRawAsync(string line)
        {
            if (_w == null) throw new InvalidOperationException("TCP not connected");
            return _w.WriteLineAsync(line);
        }

        public async ValueTask DisposeAsync()
        {
            try { _cts?.Cancel(); } catch { }
            try { _tcp?.Close(); } catch { }
            await Task.CompletedTask;
        }
    }
}
