// -----------------------------------------------------------------------------
// UdpGameClient.cs  (place in: Networking/)
// -----------------------------------------------------------------------------
// Purpose
// Minimal UDP-side client for your visualizer/UI layer.
// - Sends INPUT packets at a fixed cadence (keys mask + client ms timestamp)
// - Receives SNAPSHOT packets and maintains an entity map
// - Receives WIN packet and exposes WinnerId
//
// Wire format (see Protocol.cs):
//   INPUT:<seq>:<keysMask>:<ms>
//   SNAPSHOT:<seq>:<lavaY>:<n>;<id>|<x>|<y>|<hp>;...
//   WIN:<id>
//
// How the UI uses this class
// - Call RunAsync(ct) once (e.g., from Program.cs or your GameForm).
// - On key events, call SetKeys(up,left,down,right).
// - Read Entities + LavaY on your render tick.
// - Check WinnerId to know when to show a victory banner.
//
// Notes
// - This client is "dumb": it doesn't predict; it just sends inputs and renders
//   whatever the host says. That keeps it simple and robust.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoguePyra.Networking;

public sealed class UdpGameClient
{
    // ------------ Public state read by the UI/renderer ------------
    ///  Latest lava surface Y
    public float LavaY { get; private set; } = 450f;

    ///  Winner id when a WIN packet is received; null otherwise
    public string? WinnerId { get; private set; }

    /// Snapshot of entities keyed by player id.
    /// Tuple = (x, y, hp)
    public readonly Dictionary<string, (float x, float y, int hp)> Entities = new();

    // ------------ Networking ------------
    private readonly UdpClient _udp;
    private readonly IPEndPoint _hostEp;

    // ------------ Input state ------------
    private int _seq = 0;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private Protocol.KeysMask _keys = Protocol.KeysMask.None;

    // ------------ Optional callbacks ------------
    /// Fired after each snapshot is ap
    public event Action? SnapshotApplied;

    /// Fired when a WIN packet is rec
    public event Action<string>? WinnerAnnounced;

    public UdpGameClient(string hostIp, int udpPort = Protocol.DefaultUdpPort)
    {
        _udp = new UdpClient(0); // bind to ephemeral local port
        _udp.Client.ReceiveTimeout = 0;

        _hostEp = new IPEndPoint(IPAddress.Parse(hostIp), udpPort);
        Console.WriteLine($"[CliUDP] Local {((IPEndPoint)_udp.Client.LocalEndPoint!).ToString()} → Host {_hostEp}");
    }

    /// Starts the receive (SNAPSHOT/WIN) and send (INPUT) loops.
    /// Cancels when the provided token is signaled.
    public async Task RunAsync(CancellationToken ct)
    {
        var recvTask = Task.Run(() => ReceiveLoop(ct), ct);
        var sendTask = Task.Run(() => SendInputsLoop(ct), ct);
        await Task.WhenAny(recvTask, sendTask);
    }

    // -------------------------------------------------------------------------
    // INPUT handling
    // -------------------------------------------------------------------------

    /// Convenience method for UI layers: set directional keys in one call.
    public void SetKeys(bool up, bool left, bool down, bool right)
    {
        Protocol.KeysMask m = Protocol.KeysMask.None;
        if (up)    m |= Protocol.KeysMask.Up;
        if (left)  m |= Protocol.KeysMask.Left;
        if (down)  m |= Protocol.KeysMask.Down;
        if (right) m |= Protocol.KeysMask.Right;
        _keys = m;
    }

    /// If we want to use precomputed mask settings directly.
    //public void SetKeysMask(Protocol.KeysMask mask) => _keys = mask;

    private async Task SendInputsLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int ms = (int)_sw.ElapsedMilliseconds;
            string payload = Protocol.BuildInput(_seq++, _keys, ms);
            byte[] bytes = Encoding.UTF8.GetBytes(payload);

            try { await _udp.SendAsync(bytes, bytes.Length, _hostEp); } catch { /* ignore transient send errors */ }

            // 20 Hz input cadence (~50ms). Adjust if needed.
            await Task.Delay(50, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Receive handling (SNAPSHOT / WIN)
    // -------------------------------------------------------------------------
    private async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult res;
            try { res = await _udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch { continue; } // socket may throw on shutdown; ignore and loop

            string text = Encoding.UTF8.GetString(res.Buffer);

            // SNAPSHOT
            if (text.StartsWith("SNAPSHOT:", StringComparison.OrdinalIgnoreCase))
            {
                if (Protocol.TryParseSnapshot(text, out _, out var lava, out var players))
                {
                    LavaY = lava;
                    Entities.Clear();
                    foreach (var p in players)
                        Entities[p.id] = (p.x, p.y, p.hp);

                    SnapshotApplied?.Invoke();
                }
                continue;
            }

            // WIN
            if (text.StartsWith("WIN:", StringComparison.OrdinalIgnoreCase))
            {
                if (Protocol.TryParseWin(text, out var id))
                {
                    WinnerId = id;
                    WinnerAnnounced?.Invoke(id);
                }
                continue;
            }
        }
    }
}
