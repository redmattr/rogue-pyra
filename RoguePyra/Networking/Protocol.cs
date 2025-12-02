// -----------------------------------------------------------------------------
// Protocol.cs  (place in: Networking/)
// -----------------------------------------------------------------------------
// Purpose
// Central place for all message formats, ports, and tiny helpers shared by
// both server and client. Keeping these together avoids drift and "magic
// strings" sprinkled around the codebase.
//
// Transport overview
// - TCP (lobby + chat + coordination)   → newline-delimited text commands
// - UDP (gameplay snapshots + inputs)   → single line text payloads
//
// Tip
// If you ever change a message format here, BOTH sides (host and client)
// need to be rebuilt; tests/console tools can reuse the builder/parsers below.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;

namespace RoguePyra.Networking;

public static class Protocol
{
    // -------------------------------------------------------------------------
    // Ports
    // -------------------------------------------------------------------------
    public const int DefaultTcpPort = 8000;
    public const int DefaultUdpPort = 80;

    // -------------------------------------------------------------------------
    // TCP Commands (Client → Server)  [newline-delimited]
    // -------------------------------------------------------------------------
    // HELLO:<name>
    // LIST
    // MSG:<text>
    // QUIT
    //
    // Lobby:
    // HOST_REGISTER <LobbyName> <UdpPort> [MaxPlayers]
    // HOST_UNREGISTER <LobbyId>
    // HOST_LIST
    // JOIN <LobbyId>

    // -------------------------------------------------------------------------
    // TCP Replies (Server → Client)
    // -------------------------------------------------------------------------
    // WELCOME <name>
    // INFO <text>
    // LIST <comma_separated_names>
    // SAY <name>: <text>
    // ERROR <text>
    //
    // Lobby replies:
    // HOST_REGISTERED <id>
    // HOST_UNREGISTERED <id>
    // LOBBIES <count>   (then N lines: id|name|ip|udpPort|max|cur|inprog)
    // JOIN_INFO <ip> <udpPort>

    // -------------------------------------------------------------------------
    // UDP Gameplay messages
    // -------------------------------------------------------------------------
    // INPUT:<seq>:<keysMask>:<ms>
    // SNAPSHOT:<seq>:<lavaY>:<n>;<id>|<x>|<y>|<hp>;...
    // WIN:<id>

    // -------------------------------------------------------------------------
    // Extra lobby commands (Nov 2025 additions)
    // -------------------------------------------------------------------------
    // HOST_START <LobbyId>     (host → server)
    // LOBBY_LOCKED <LobbyId>   (server → all clients; lobby locked, game started)
    // ERROR_GAME_STARTED        (server → client trying to join locked lobby)


    // We keep a couple of separators in one place to avoid typos.
    private const char Colon = ':';
    private const char Pipe  = '|';
    private const char Semi  = ';';

    // -------------------------------------------------------------------------
    // Keys mask (client input) — 4 bits: Up, Left, Down, Right
    // -------------------------------------------------------------------------
    // Bit order is arbitrary, but fixed here so both sides agree.
    [Flags]
    public enum KeysMask : byte
    {
        None  = 0,
        Up    = 1 << 0,
        Left  = 1 << 1,
        Down  = 1 << 2,
        Right = 1 << 3
    }

    // =========================================================================
    // -----------------------  TCP: Small helpers  -----------------------------
    // =========================================================================

    /// Builds the initial greeting line.
    public static string BuildHello(string name) => $"HELLO:{name}";

    /// Builds a chat line (client &rarr; server).
    public static string BuildMsg(string text) => $"MSG:{text}";

    /// Parses "WELCOME &lt;name&gt;" or returns null on mismatch.
    public static string? ParseWelcome(string line)
    {
        const string prefix = "WELCOME ";
        return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? line.Substring(prefix.Length)
            : null;
    }

    /// Parses "JOIN_INFO &lt;ip&gt; &lt;udpPort&gt;".
    public static bool TryParseJoinInfo(string line, out string ip, out int udpPort)
    {
        ip = "";
        udpPort = 0;
        const string prefix = "JOIN_INFO ";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var parts = line.Substring(prefix.Length).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[1], out udpPort)) return false;

        ip = parts[0];
        return true;
    }

    // =========================================================================
    // -----------------------  UDP: INPUT helpers  -----------------------------
    // =========================================================================

    /// 
    /// Build an INPUT line: "INPUT:&lt;seq&gt;:&lt;keysMask&gt;:&lt;ms&gt;".
    /// 
    public static string BuildInput(int seq, KeysMask keys, int clientMs)
        => $"INPUT{Colon}{seq}{Colon}{(byte)keys}{Colon}{clientMs}";

    /// Try parse an INPUT line; returns false on format errors.
    public static bool TryParseInput(string line, out int seq, out KeysMask keys, out int clientMs)
    {
        seq = 0; keys = KeysMask.None; clientMs = 0;

        if (!line.StartsWith("INPUT:", StringComparison.OrdinalIgnoreCase)) return false;

        var p = line.Split(Colon);
        if (p.Length < 4) return false;

        if (!int.TryParse(p[1], out seq)) return false;

        if (!byte.TryParse(p[2], out var mask)) return false;
        keys = (KeysMask)mask;

        if (!int.TryParse(p[3], out clientMs)) clientMs = 0;
        return true;
    }

    // =========================================================================
    // ---------------------  UDP: SNAPSHOT helpers  ----------------------------
    // =========================================================================

    /// 
    /// Build one snapshot line:
    /// "SNAPSHOT:&lt;seq&gt;:&lt;lavaY&gt;:&lt;n&gt;;&lt;id&gt;|&lt;x&gt;|&lt;y&gt;|&lt;hp&gt;;..."
    /// Builds a UDP "SNAPSHOT:<seq>:<lavaY>:<n>;id|x|y|hp;..." packet describing lava and all players.
    /// Parses a UDP "SNAPSHOT:<seq>:<lavaY>:<n>;id|x|y|hp;..." packet into game state.
    /// Use invariant culture so decimals are '.' regardless of locale.
    /// 
    public static string BuildSnapshot(int seq, float lavaY, (string id, float x, float y, int hp)[] players)
    {
        var sb = new StringBuilder(128);
        sb.Append("SNAPSHOT").Append(Colon).Append(seq).Append(Colon)
          .Append(lavaY.ToString("F1", CultureInfo.InvariantCulture)).Append(Colon)
          .Append(players.Length).Append(Semi);

        foreach (var p in players)
        {
            sb.Append(p.id).Append(Pipe)
              .Append(p.x.ToString("F1", CultureInfo.InvariantCulture)).Append(Pipe)
              .Append(p.y.ToString("F1", CultureInfo.InvariantCulture)).Append(Pipe)
              .Append(p.hp).Append(Semi);
        }
        return sb.ToString();
    }

    /// 
    /// Parse a snapshot line. Returns false if the text is not a valid snapshot.
    /// Caller receives lavaY and a temporary array of player tuples.
    /// 
    public static bool TryParseSnapshot(
        string line,
        out int seq,
        out float lavaY,
        out (string id, float x, float y, int hp)[] players)
    {
        seq = 0; lavaY = 0; players = Array.Empty<(string, float, float, int)>();

        if (!line.StartsWith("SNAPSHOT:", StringComparison.OrdinalIgnoreCase)) return false;

        var headBody = line.Split(Semi, 2);
        if (headBody.Length < 2) return false;

        var header = headBody[0].Split(Colon); // [SNAPSHOT, seq, lavaY, n]
        if (header.Length < 4) return false;

        if (!int.TryParse(header[1], out seq)) return false;

        if (!float.TryParse(header[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lavaY))
            lavaY = 0;

        if (!int.TryParse(header[3], out var n)) n = 0;

        var items = headBody[1].Split(Semi, StringSplitOptions.RemoveEmptyEntries);
        if (items.Length == 0 && n == 0) { players = Array.Empty<(string, float, float, int)>(); return true; }

        var list = new (string id, float x, float y, int hp)[items.Length];
        int idx = 0;

        foreach (var it in items)
        {
            var f = it.Split(Pipe);
            if (f.Length < 4) continue;

            var id = f[0];

            float x = float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var tx) ? tx : 0f;
            float y = float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ty) ? ty : 0f;
            int hp   = int.TryParse(f[3], out var thp) ? thp : 100;

            list[idx++] = (id, x, y, hp);
        }

        if (idx != list.Length) Array.Resize(ref list, idx);
        players = list;
        return true;
    }

    // =========================================================================
    // -----------------------  UDP: WIN helpers  --------------------------------
    // =========================================================================

    public static string BuildWin(string id) => $"WIN{Colon}{id}";

    public static bool TryParseWin(string line, out string id)
    {
        id = "";
        if (!line.StartsWith("WIN:", StringComparison.OrdinalIgnoreCase)) return false;
        id = line.Substring(4).Trim();
        return id.Length > 0;
    }

    // =========================================================================
    // -------------------------- Misc tiny utils --------------------------------
    // =========================================================================

    /// Clamp a value into [min, max]. Kept here so both sides share it.
    public static float Clamp(float v, float min, float max)
        => v < min ? min : (v > max ? max : v);
}