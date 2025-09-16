# RoguePyra – Distributed Systems Final Project

## Game Pitch
A dungeon crawler that is procedurally generated.  
Players spawn at random across the bottom of a pyramid.  
Using PvP and PvE, fight your way to the top for victory!  
Rising lava pushes everyone upward, while stronger enemies and better loot await higher levels.  
Last player standing wins.

---

## What is this?
RoguePyra is our demo of a distributed multiplayer system.  
We split things into three roles:

- **Main Server (TCP)** – handles chat + lobby/host registry.  
- **Host (UDP)** – runs the authoritative game loop.  
- **Clients (UDP)** – join a host, send their inputs (WASD), and render the game state (blue box + lava bar for now).

---

## How to run it

### Build first
```powershell
cd RoguePyra
dotnet clean
dotnet build
```


## Start the main TCP server
dotnet run -- --server --ip 0.0.0.0 --port 5000

## Start a TCP client (chat + lobby control)
dotnet run -- --client --host 127.0.0.1 --port 5000 --name HostDude

Register a lobby (assumes your UDP host will be on port 6000):
/hostreg MyLobby 6000 6

## Start the UDP host (game authority)
dotnet run -- --hostgame --udpport 6000

## Start a UDP client with a window
dotnet run -- --clientviz --host 127.0.0.1 --udpport 6000

Use **W/A/S/D** to move the blue box.  
The host logs `INPUT` packets, and sends back `SNAPSHOT` packets → the client updates.

---

## Features
- **Lobby + Chat:** players connect through a main server, see hosts, and chat.
- **Host Authority:** one player hosts the game state; clients can’t cheat.
- **TCP + UDP:** TCP for reliable lobby/chat, UDP for fast gameplay.
- **Scalable:** works across multiple computers on the same network.
- **Demo-ready:** already runs a multiplayer loop with inputs and snapshots.

---

## Next steps
- Multiple players (each client gets its own box).
- NPCs, lava rising, and loot drops.
- Polished launcher scripts.