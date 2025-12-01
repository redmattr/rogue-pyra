// -----------------------------------------------------------------------------
// Shared.cs
// -----------------------------------------------------------------------------
// Purpose:
// This file holds small, simple data objects (DTOs) that both the server and
// clients need to know about. Keeping these here avoids circular references
// and keeps networking files cleaner.
//
// In our project, the "Lobby" object is used by the TCP server to keep track
// of game lobbies (rooms) that players can create, list, and join.
// -----------------------------------------------------------------------------

namespace RoguePyra;

/// <summary>
/// Represents a game lobby entry that is visible to clients.
/// The TCP server keeps a collection of these to know what games are available.
/// </summary>
public sealed class Lobby {
	/// <summary>Unique numeric ID assigned by the server when the lobby is created.</summary>
	public int Id { get; init; }

	/// <summary>Display name for the lobby (set by the host player).</summary>
	public string Name { get; set; } = "Lobby";

	/// <summary>IP address of the host that runs the UDP game server.</summary>
	public string HostIp { get; init; } = "127.0.0.1";

	/// <summary>UDP port where the host is listening for game traffic.</summary>
	public int UdpPort { get; set; }

	/// <summary>Maximum number of players allowed in this lobby.</summary>
	public int MaxPlayers { get; set; } = 8;

	/// <summary>Current number of players connected.</summary>
	public int CurPlayers { get; set; } = 0;

	/// <summary>True if the game has started; false if still in the lobby phase.</summary>
	public bool InProgress { get; set; } = false;
}