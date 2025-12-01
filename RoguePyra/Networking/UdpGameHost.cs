// -----------------------------------------------------------------------------
// UdpGameHost.cs  (place in: Networking/)
// -----------------------------------------------------------------------------
// Purpose
// Authoritative UDP host WITHOUT any win/lose state.
// - Receives INPUT packets
// - Simulates movement + rising lava
// - Applies damage in lava
// - Broadcasts periodic SNAPSHOT packets
// No WIN packets are ever sent, and the game never currently ends.
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

internal sealed class UdpGameHost {
	// ---- Networking ----
	private readonly UdpClient _udp;
	private readonly IPEndPoint _listenEp;

	// ---- Player state tracked by the host (keyed by client endpoint) ----
	private sealed class Player {
		public readonly string ID = Guid.NewGuid().ToString("N").Substring(0, 6);
		public float posX, posY;
		public int health = 100;

		public Protocol.KeysMask Keys = Protocol.KeysMask.None; // last input
		public long LastSeenHostMs;                             // host time (ms)
	}

	private readonly Dictionary<IPEndPoint, Player> _players = [];
	private readonly HashSet<IPEndPoint> _knownClients = [];

	// ---- World constants ----
	private int _snapSeq = 0;
	private const float WORLD_W = 840.0f, WORLD_H = 480.0f; // Width and height of world.
	private const float BOX = 24.0f;

	// Lava rises forever; game never ends
	private const float LavaRiseRate = 18.0f;   // px/sec rising speed
	private const int LavaDPS = 60;    // damage per second when submerged
	private float _lavaY = WORLD_H; // Y of lava surface (0 = top)

	public UdpGameHost(int port = Protocol.DefaultUdpPort) {
		_listenEp = new IPEndPoint(IPAddress.Any, port);
		_udp = new UdpClient(_listenEp);
		Console.WriteLine($"[HostUDP] Listening on 0.0.0.0:{port}");
	}

	/// Starts the host: one loop to receive INPUTs, one loop to simulate & broadcast.
	/// Returns when the token is cancelled.
	public async Task RunAsync(CancellationToken ct) {
		var recvTask = Task.Run(() => ReceiveLoop(ct), ct);
		var simTask = Task.Run(() => SimLoop(ct), ct);
		await Task.WhenAny(recvTask, simTask);
	}

	// -----------------------------------------------------------------------------
	// INPUT receiver
	// -----------------------------------------------------------------------------
	private async Task ReceiveLoop(CancellationToken ct) {
		while (!ct.IsCancellationRequested) {
			UdpReceiveResult res;
			try { res = await _udp.ReceiveAsync(ct); } catch (OperationCanceledException) { break; } catch { continue; }

			_knownClients.Add(res.RemoteEndPoint);
			var text = Encoding.UTF8.GetString(res.Buffer);

			if (!Protocol.TryParseInput(text, out _, out var keys, out _))
				continue;

			if (!_players.TryGetValue(res.RemoteEndPoint, out var pl)) {
				// Spawn with a soft-random start position
				pl = new Player {
					posX = 60 + Random.Shared.Next(0, 600),
					posY = 60 + Random.Shared.Next(0, 300)
				};
				_players[res.RemoteEndPoint] = pl;
				Console.WriteLine($"[HostUDP] +Player {pl.ID} @ {res.RemoteEndPoint}");
			}

			pl.Keys = keys;
			pl.LastSeenHostMs = CurrentMs();
		}
	}

	// -----------------------------------------------------------------------------
	// Physics/Simulation: movement + rising lava + damage. NO win logic.
	// -----------------------------------------------------------------------------
	private async Task SimLoop(CancellationToken ct) {
		var sw = Stopwatch.StartNew();
		long last = sw.ElapsedMilliseconds;

		// Movement tuning
		const float speed = 140f;           // px/sec
		const float maxX = WORLD_W - BOX;   // stay inside world bounds
		const float maxY = WORLD_H - BOX;
		const long dropMs = 5000;           // if idle > 5s, drop player

		while (!ct.IsCancellationRequested) {
			long now = sw.ElapsedMilliseconds;
			float dt = (now - last) / 1000f;
			if (dt <= 0f) { await Task.Delay(1, ct); continue; }
			last = now;

			// ---- Update players & prune stale players ----
			var toRemove = new List<IPEndPoint>();
			foreach (var (ep, pl) in _players) {
				// Drop idle clients
				if (pl.LastSeenHostMs != 0 && (CurrentMs() - pl.LastSeenHostMs) > dropMs) {
					toRemove.Add(ep);
					continue;
				}

				float dx = 0, dy = 0;
				if ((pl.Keys & Protocol.KeysMask.Up) != 0) dy -= 1;
				if ((pl.Keys & Protocol.KeysMask.Down) != 0) dy += 1;
				if ((pl.Keys & Protocol.KeysMask.Left) != 0) dx -= 1;
				if ((pl.Keys & Protocol.KeysMask.Right) != 0) dx += 1;

				if (dx != 0 || dy != 0) {
					var len = MathF.Sqrt(dx * dx + dy * dy);
					dx /= len; dy /= len;

					pl.posX = Protocol.Clamp(pl.posX + dx * speed * dt, 0, maxX);
					pl.posY = Protocol.Clamp(pl.posY + dy * speed * dt, 0, maxY);
				}
			}
			foreach (var ep in toRemove) {
				Console.WriteLine($"[HostUDP] -Player {_players[ep].ID}");
				_players.Remove(ep);
			}

			// ---- Lava rises forever; apply damage to submerged players ----
			_lavaY = Math.Max(0f, _lavaY - LavaRiseRate * dt);

			foreach (var pl in _players.Values) {
				bool inLava = (pl.posY + BOX) > _lavaY;
				if (inLava)
					pl.health = Math.Max(0, pl.health - (int)(LavaDPS * dt));
			}

			// ---- Broadcast snapshot (~12 Hz) ----
			if (now % 80 < 16) {
				var arr = new (string id, float x, float y, int hp)[_players.Count];
				int i = 0;
				foreach (var p in _players.Values) arr[i++] = (p.ID, p.posX, p.posY, p.health);

				var snap = Protocol.BuildSnapshot(++_snapSeq, _lavaY, arr);
				var bytes = Encoding.UTF8.GetBytes(snap);

				foreach (var ep in _knownClients) {
					try { await _udp.SendAsync(bytes, bytes.Length, ep); } catch { }
				}
			}

			await Task.Delay(10, ct); // small sleep to avoid a hot loop
		}
	}

	private static long CurrentMs() {
		long ticks = Stopwatch.GetTimestamp();
		return ticks * 1000 / Stopwatch.Frequency;
	}
}