# RealCricket LAN Multiplayer

A LAN multiplayer version of RealCricket built with Unity and Photon PUN 2, enabling local network cricket matches without requiring Photon Cloud.

## Features

- **LAN Multiplayer**: Play on local network using a self-hosted Photon server
- **Photon PUN 2 Integration**: Uses Photon Unity Networking for reliable UDP networking
- **Real-time Ball Physics**: Networked ball physics with Magnus effect (swing/spin)
- **Role-based Gameplay**: Batting, bowling, and fielding with synchronized actions
- **Match Management**: Complete cricket match flow (toss, innings, overs, wickets)
- **Auto-reconnection**: Handles network interruptions gracefully

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     LAN Network Layer                        │
├─────────────────────────────────────────────────────────────┤
│  Photon Server (Self-hosted on LAN)                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │
│  │   Master    │  │   Game      │  │   Name      │         │
│  │   Server    │  │   Server    │  │   Server    │         │
│  │  (Port 5055)│  │  (Port 5056)│  │  (Optional) │         │
│  └─────────────┘  └─────────────┘  └─────────────┘         │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                     Unity Clients                            │
├─────────────────────────────────────────────────────────────┤
│  LANNetworkManager    CricketGameManager    PlayerController │
│  BallController       PhotonRigidbodyView   PhotonTransformView│
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

1. **Unity 2022.3 LTS or later**
2. **Photon PUN 2 Free** (from Unity Asset Store or Package Manager)
3. **Photon Server SDK** (for hosting the LAN server) - Download from [Photon Engine](https://www.photonengine.com/en-us/PhotonServer)

## Setup Instructions

### 1. Unity Project Setup

```bash
# Clone the repository
git clone https://github.com/Harshit975shukla/RealCricket-LAN-Multiplayer.git
cd RealCricket-LAN-Multiplayer

# Open in Unity 2022.3+
```

### 2. Install Photon PUN 2

**Option A: Unity Package Manager**
- Window → Package Manager → "+" → "Add package from git URL"
- Enter: `https://github.com/PhotonEngine/Pun2-Unity-Package.git`

**Option B: Asset Store**
- Search "Photon PUN 2 Free" in Unity Asset Store
- Download and import

### 3. Configure Photon Server Settings

The project includes `Assets/PhotonServerSettings.json` with LAN configuration:

```json
{
  "AppSettings": {
    "AppIdRealtime": "MasterServer",
    "Server": "127.0.0.1",  // Change to your LAN server IP
    "Port": 5055,
    "Protocol": "Udp",
    "UseNameServer": false
  }
}
```

### 4. Set Up Photon Server (LAN Host)

**Windows:**
```bash
# Download Photon Server SDK from photonengine.com
# Extract to C:\Photon\

# Configure PhotonServer.config for LAN
# Edit C:\Photon\deploy\bin_Win64\PhotonServer.config:
# - Set MasterServer IP to your LAN IP
# - Set GameServer IP to your LAN IP
# - Disable NameServer for pure LAN

# Start Photon Server
C:\Photon\deploy\bin_Win64\PhotonSocketServer.exe /run
```

**Linux/Mac:**
```bash
# Download and extract Photon Server SDK
# Configure PhotonServer.config
# Start with mono
mono PhotonSocketServer.exe /run
```

### 5. Configure Network Settings

In Unity Editor:
1. Open `Assets/Photon/PhotonUnityNetworking/Resources/PhotonServerSettings.asset`
2. Set:
   - **App Id Realtime**: `MasterServer`
   - **Server**: Your LAN server IP (e.g., `192.168.1.100`)
   - **Port**: `5055`
   - **Protocol**: `Udp`
   - **Use Name Server**: `False`
   - **Enable Lobby Stats**: `True`

### 6. Build and Run

1. Create a scene with the networking components
2. Add `LANNetworkManager` to a GameObject
3. Assign player prefab with `PlayerController` and `PhotonView`
4. Add `BallController` to ball prefab with `PhotonView` and `PhotonRigidbodyView`
5. Build for Windows/Mac/Linux
6. Run multiple instances on LAN machines

## Project Structure

```
Assets/
├── Scripts/Networking/
│   ├── LANNetworkManager.cs      # Photon connection & room management
│   ├── CricketGameManager.cs     # Game state, rules, match flow
│   ├── PlayerController.cs       # Player input, batting/bowling/fielding
│   └── BallController.cs         # Networked ball physics
├── Prefabs/
│   ├── Player.prefab             # Player with PhotonView + PlayerController
│   └── CricketBall.prefab        # Ball with PhotonView + BallController
├── Scenes/
│   └── CricketMatch.unity        # Main match scene
├── PhotonServerSettings.json     # LAN server configuration
└── Resources/
    └── PhotonServerSettings.asset # Unity asset (auto-generated)
```

## Network Flow

### Connection Sequence
```
Client          Photon Master          Photon Game
  │                  │                    │
  ├─ConnectUsingSettings()──────────────►│
  │◄──OnConnectedToMaster()─────────────┤
  ├─JoinLobby()─────────────────────────►│
  │◄──OnJoinedLobby()───────────────────┤
  ├─JoinOrCreateRoom()─────────────────►│
  │◄──OnJoinedRoom()────────────────────┤
  ├─Instantiate(Player)────────────────►│
  │◄──OnPlayerEnteredRoom()────────────┤
```

### Ball Physics Sync
```
Bowler (Owner)                          Other Clients
  │                                        │
  ├─Bowl(force, spin)─────────────────────►│ (RPC)
  │                                        │
  ├─Physics Update (20Hz)─────────────────►│ (PhotonRigidbodyView)
  │                                        │
  ├─OnCollisionEnter()────────────────────►│ (RPC)
  │                                        │
  └─Ownership Transfer (if caught)────────►│
```

### Game State Sync
```
Master Client                          All Clients
  │                                        │
  ├─OnRunScored()/OnWicket()──────────────►│ (RPC via LANNetworkManager)
  │                                        │
  ├─GetGameStateJson()────────────────────►│
  │                                        │
  └─SendGameState(json)───────────────────►│ (RPC)
       │                                        │
       └─ApplyGameState(json)────────────────►│
```

## Key Components

### LANNetworkManager
- Manages Photon connection to LAN server
- Handles room creation/joining
- Provides RPC methods for game state sync
- Auto-reconnection on disconnect

### CricketGameManager
- Authoritative game state (runs on Master Client)
- Manages cricket rules: overs, wickets, runs, innings
- Coordinates player roles (batsman, bowler, fielders)
- Broadcasts state changes via RPCs

### PlayerController
- Handles local player input
- Synchronizes position/rotation via PhotonTransformView
- Sends action RPCs (Swing, Bowl, Throw, Dive)
- Receives and plays remote actions

### BallController
- Authoritative physics on owner (bowler/batsman)
- PhotonRigidbodyView for continuous position/velocity sync
- Custom physics: Magnus effect (swing), drag, bounce
- RPCs for discrete events: Bowl, Hit, Throw, Collision

## LAN Server Configuration

### PhotonServer.config (Key Settings)
```xml
<MasterServer>
  <IPAddress>0.0.0.0</IPAddress>  <!-- Listen on all interfaces -->
  <Port>5055</Port>
</MasterServer>

<GameServer>
  <IPAddress>0.0.0.0</IPAddress>
  <Port>5056</Port>
</GameServer>

<!-- Disable NameServer for pure LAN -->
<NameServer>
  <Enabled>False</Enabled>
</NameServer>
```

### Firewall Rules
Allow inbound on host machine:
- UDP 5055 (Master Server)
- UDP 5056 (Game Server)
- UDP 5057 (WebSocket, if enabled)

## Testing LAN Multiplayer

1. **Start Photon Server** on host machine
2. **Find host IP**: `ipconfig` (Windows) / `ifconfig` (Linux/Mac)
3. **Update client settings** with host IP
4. **Build and run** on host (Master Client)
5. **Build and run** on client machines
6. **Verify connection** in Unity Console:
   ```
   [LANNetworkManager] Connected to Master Server
   [LANNetworkManager] Joined room: RealCricket_LAN_Room
   [CricketGameManager] Player joined: Player_2
   ```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Can't connect to server | Check firewall, verify IP/port, ensure Photon Server running |
| Players not seeing each other | Check PhotonTransformView on player prefab, verify PhotonView ID |
| Ball physics desync | Increase physicsUpdateRate, check PhotonRigidbodyView settings |
| High latency | Use UDP, reduce sendRate, enable NetworkSimulation for testing |
| Master client migration | Implement OnMasterClientSwitched for state transfer |

## Extending for Production

1. **Authentication**: Add custom auth via Photon AuthProvider
2. **Persistence**: Save match state to database
3. **Spectator Mode**: Add read-only clients
4. **Matchmaking**: Implement skill-based matching
5. **Anti-cheat**: Server-side validation of actions
6. **Replay System**: Record and replay matches

## License

MIT License - Feel free to use and modify for your projects.

## Credits

- Original RealCricket by Nautilus Mobile
- Photon PUN 2 by Exit Games
- Reverse engineered using REA (Reverse Engineer Anything)