using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;

/// <summary>
/// LAN Network Manager for RealCricket - enables LAN multiplayer without Photon Cloud
/// Uses Photon's LAN capabilities (PhotonServerSettings with AppId = "MasterServer")
/// </summary>
public class LANNetworkManager : MonoBehaviourPunCallbacks
{
    [Header("LAN Settings")]
    [SerializeField] private string lanServerAddress = "127.0.0.1";
    [SerializeField] private int lanServerPort = 5055;
    [SerializeField] private string gameVersion = "1.0.0";
    [SerializeField] private byte maxPlayersPerRoom = 4;
    
    [Header("Room Settings")]
    [SerializeField] private string defaultRoomName = "RealCricket_LAN_Room";
    [SerializeField] private bool autoCreateRoom = true;
    [SerializeField] private bool autoJoinLobby = true;
    
    [Header("References")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;
    
    private TypedLobby sqlLobby;
    private RoomOptions roomOptions;
    private bool isConnecting = false;
    
    public static LANNetworkManager Instance { get; private set; }
    
    public bool IsConnected => PhotonNetwork.IsConnected;
    public bool InRoom => PhotonNetwork.InRoom;
    public Room CurrentRoom => PhotonNetwork.CurrentRoom;
    public Player LocalPlayer => PhotonNetwork.LocalPlayer;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializePhotonSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void InitializePhotonSettings()
    {
        // Configure Photon for LAN (no AppId needed for local Photon server)
        PhotonNetwork.PhotonServerSettings.AppSettings.AppIdRealtime = "MasterServer";
        PhotonNetwork.PhotonServerSettings.AppSettings.Server = lanServerAddress;
        PhotonNetwork.PhotonServerSettings.AppSettings.Port = lanServerPort;
        PhotonNetwork.PhotonServerSettings.AppSettings.Protocol = ConnectionProtocol.Udp;
        
        // LAN specific settings
        PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = false;
        PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "";
        PhotonNetwork.PhotonServerSettings.AppSettings.EnableLobbyStatistics = true;
        
        // Network simulation for testing
        #if UNITY_EDITOR
        PhotonNetwork.NetworkClientState = ClientState.PeerCreated;
        #endif
        
        // Room options
        roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true,
            CleanupCacheOnLeave = true,
            DeleteNullProperties = true,
            PlayerTtl = 30000, // 30 seconds player TTL for reconnection
            EmptyRoomTtl = 60000 // Keep room for 60 seconds after last player leaves
        };
        
        // SQL lobby for filtering
        sqlLobby = new TypedLobby("RealCricket_LAN_Lobby", LobbyType.SqlLobby);
        
        // Enable automatic scene synchronization
        PhotonNetwork.AutomaticallySyncScene = true;
        
        // Set log level
        PhotonNetwork.LogLevel = PunLogLevel.Informational;
    }
    
    public void ConnectToLAN()
    {
        if (isConnecting || PhotonNetwork.IsConnected)
        {
            Debug.Log("[LANNetworkManager] Already connected or connecting");
            return;
        }
        
        isConnecting = true;
        Debug.Log($"[LANNetworkManager] Connecting to LAN server at {lanServerAddress}:{lanServerPort}");
        
        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.GameVersion = gameVersion;
    }
    
    public override void OnConnectedToMaster()
    {
        Debug.Log("[LANNetworkManager] Connected to Master Server");
        isConnecting = false;
        
        if (autoJoinLobby)
        {
            PhotonNetwork.JoinLobby(sqlLobby);
        }
        
        if (autoCreateRoom)
        {
            CreateOrJoinRoom();
        }
    }
    
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.LogWarning($"[LANNetworkManager] Disconnected: {cause}");
        isConnecting = false;
        
        // Auto-reconnect logic
        Invoke(nameof(ConnectToLAN), 5f);
    }
    
    public override void OnJoinedLobby()
    {
        Debug.Log("[LANNetworkManager] Joined lobby");
    }
    
    public void CreateOrJoinRoom(string roomName = null)
    {
        string name = roomName ?? defaultRoomName;
        
        Debug.Log($"[LANNetworkManager] Creating/joining room: {name}");
        
        RoomOptions opts = new RoomOptions(roomOptions);
        opts.CustomRoomProperties = new Hashtable
        {
            { "gameMode", "LAN" },
            { "map", "CricketStadium" },
            { "maxPlayers", maxPlayersPerRoom }
        };
        opts.CustomRoomPropertiesForLobby = new string[] { "gameMode", "map", "maxPlayers" };
        
        PhotonNetwork.JoinOrCreateRoom(name, opts, sqlLobby);
    }
    
    public override void OnJoinedRoom()
    {
        Debug.Log($"[LANNetworkManager] Joined room: {PhotonNetwork.CurrentRoom.Name} (Players: {PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})");
        
        // Spawn local player
        SpawnPlayer();
    }
    
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[LANNetworkManager] Join room failed: {returnCode} - {message}");
        
        // Try creating a new room with random name
        string newRoomName = $"{defaultRoomName}_{Random.Range(1000, 9999)}";
        CreateOrJoinRoom(newRoomName);
    }
    
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"[LANNetworkManager] Player entered: {newPlayer.NickName} (Total: {PhotonNetwork.CurrentRoom.PlayerCount})");
        
        // Notify game manager
        CricketGameManager.Instance?.OnPlayerJoined(newPlayer);
    }
    
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"[LANNetworkManager] Player left: {otherPlayer.NickName} (Remaining: {PhotonNetwork.CurrentRoom.PlayerCount})");
        
        CricketGameManager.Instance?.OnPlayerLeft(otherPlayer);
    }
    
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"[LANNetworkManager] Master client switched to: {newPlayer.NickName}");
        
        if (PhotonNetwork.IsMasterClient)
        {
            CricketGameManager.Instance?.OnBecameMasterClient();
        }
    }
    
    private void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("[LANNetworkManager] Player prefab not assigned!");
            return;
        }
        
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;
        
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int spawnIndex = PhotonNetwork.LocalPlayer.ActorNumber % spawnPoints.Length;
            spawnPos = spawnPoints[spawnIndex].position;
            spawnRot = spawnPoints[spawnIndex].rotation;
        }
        
        GameObject playerObj = PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRot);
        playerObj.name = $"Player_{PhotonNetwork.LocalPlayer.ActorNumber}";
        
        Debug.Log($"[LANNetworkManager] Spawned local player at {spawnPos}");
    }
    
    public void LeaveRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.LeaveRoom();
        }
    }
    
    public void Disconnect()
    {
        LeaveRoom();
        PhotonNetwork.Disconnect();
    }
    
    // RPC methods for game state synchronization
    [PunRPC]
    public void RPC_SyncGameState(string gameStateJson)
    {
        CricketGameManager.Instance?.ApplyGameState(gameStateJson);
    }
    
    [PunRPC]
    public void RPC_PlayerAction(int actorNumber, string action, string data)
    {
        CricketGameManager.Instance?.OnRemotePlayerAction(actorNumber, action, data);
    }
    
    [PunRPC]
    public void RPC_BallState(Vector3 position, Vector3 velocity, int ownerActorNumber)
    {
        CricketGameManager.Instance?.OnBallStateUpdate(position, velocity, ownerActorNumber);
    }
    
    // Public methods for game logic to send RPCs
    public void SendGameState(string gameStateJson)
    {
        if (PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_SyncGameState", RpcTarget.Others, gameStateJson);
        }
    }
    
    public void SendPlayerAction(string action, string data)
    {
        if (PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_PlayerAction", RpcTarget.Others, PhotonNetwork.LocalPlayer.ActorNumber, action, data);
        }
    }
    
    public void SendBallState(Vector3 position, Vector3 velocity)
    {
        if (PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_BallState", RpcTarget.Others, position, velocity, PhotonNetwork.LocalPlayer.ActorNumber);
        }
    }
}