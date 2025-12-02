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
		// Logical player id, usually the TCP player name (e.g. "Luke")
		public string ID = string.Empty;
		public float posX, posY;
		public int health = 100;

		public Protocol.KeysMask Keys = Protocol.KeysMask.None; // last input
		public long LastSeenHostMs;                             // host time (ms)
	}

	private readonly Dictionary<IPEndPoint, Player> _players = [];
	private readonly HashSet<IPEndPoint> _knownClients = [];

	// Logical player id (name) -> Player (used to seed new host on migration)
	private readonly Dictionary<string, Player> _playersById = new(StringComparer.OrdinalIgnoreCase);

	// Maps UDP endpoints to logical player names (from HELLOUDP:<name>)
	private readonly Dictionary<IPEndPoint, string> _endpointNames = [];


	// ---- World constants ----
	private int _snapSeq = 0;
	private const float WORLD_W = 4000.0f, WORLD_H = 2000.0f; // Width and height of world.
	private const float BOX = 24.0f;

	// Lava rises forever; game never ends
	private float _lavaY;						// Y of lava surface (0 = top)
	private const float LavaRiseRate = 5.0f;    // px/sec rising speed
	private const int LavaDPS = 60;				// damage per second when submerged

	public UdpGameHost(
		int udpPort = Protocol.DefaultUdpPort,
		float initialLavaY = WORLD_H,
		IDictionary<string, (float x, float y, int hp)>? seedPlayers = null) {
		_listenEp = new IPEndPoint(IPAddress.Any, udpPort);
		_udp = new UdpClient(_listenEp);
		_lavaY = initialLavaY;

		Console.WriteLine($"[HostUDP] Listening on 0.0.0.0:{udpPort}, lavaY={_lavaY:F1}");

		// If we were given seed players (e.g. during host migration),
		// populate playersById so we can attach endpoints to them later.
		if (seedPlayers != null) {
			foreach (var kv in seedPlayers) {
				var id = kv.Key;
				var (x, y, hp) = kv.Value;

				var pl = new Player {
					ID = id,
					posX = x,
					posY = y,
					health = hp
				};

				_playersById[id] = pl;
			}

			Console.WriteLine($"[HostUDP] Seeded {seedPlayers.Count} players from snapshot.");
		}
	}

	/// Starts the host: one loop to receive INPUTs, one loop to simulate & broadcast.
	/// Returns when the token is cancelled.
	public async Task RunAsync(CancellationToken ct) {
		try {
			var recvTask = Task.Run(() => ReceiveLoop(ct), ct);
			var simTask = Task.Run(() => SimLoop(ct), ct);
			await Task.WhenAny(recvTask, simTask);
		} finally {
			// Ensure the UDP socket is released when the host stops
			try { _udp.Close(); } catch { }
		}
	}

	// -----------------------------------------------------------------------------
	// INPUT receiver
	// -----------------------------------------------------------------------------
	private async Task ReceiveLoop(CancellationToken ct) {
		while (!ct.IsCancellationRequested) {
			UdpReceiveResult res;
			try { res = await _udp.ReceiveAsync(ct); } catch (OperationCanceledException) { break; } catch { continue; }

			var ep = res.RemoteEndPoint;
			_knownClients.Add(ep);
			var text = Encoding.UTF8.GetString(res.Buffer);

			// 1) HELLOUDP:<name> — remember which player name belongs to this endpoint
			if (text.StartsWith("HELLOUDP:", StringComparison.OrdinalIgnoreCase)) {
				var name = text.Substring("HELLOUDP:".Length).Trim();
				if (string.IsNullOrWhiteSpace(name))
					name = ep.ToString();

				_endpointNames[ep] = name;
				Console.WriteLine($"[HostUDP] HELLOUDP from {ep} -> '{name}'");
				continue; // do not treat this as INPUT
			}

			// 2) Normal INPUT packet
			if (!Protocol.TryParseInput(text, out _, out var keys, out _))
				continue;

			if (!_players.TryGetValue(ep, out var pl)) {

				// Look up logical name for this endpoint (from HELLOUDP)
				if (!_endpointNames.TryGetValue(ep, out var name) ||
					string.IsNullOrWhiteSpace(name)) {
					name = ep.ToString(); // fallback
				}

				// If we already have a seeded Player for this name, reuse it
				if (!_playersById.TryGetValue(name, out pl)) {
					// No seed: spawn with a soft-random start position
					pl = new Player {
						ID = name,
						posX = 60 + Random.Shared.Next(0, 600),
						posY = 60 + Random.Shared.Next(0, 300),
						health = 100
					};
					_playersById[name] = pl;

					Console.WriteLine($"[HostUDP] +Player {pl.ID} @ {ep} (new spawn)");
				} else {
					Console.WriteLine($"[HostUDP] +Player {pl.ID} @ {ep} (from seed)");
				}

				_players[ep] = pl;
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
		const float speed = 140.0f;			// px/sec
		const float maxX = WORLD_W - BOX;	// stay inside world bounds
		const float maxY = WORLD_H - BOX;
		const long dropMs = 5000;			// if idle > 5s, drop player

		while (!ct.IsCancellationRequested) {
			long now = sw.ElapsedMilliseconds;
			float dt = (now - last) / 1000.0f;
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
				if (_players.TryGetValue(ep, out var pl)) {
					Console.WriteLine($"[HostUDP] -Player {pl.ID}");
					_playersById.Remove(pl.ID);
				}
				_players.Remove(ep);
				_endpointNames.Remove(ep);
				_knownClients.Remove(ep);
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