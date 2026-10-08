using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System;

/// <summary>
/// Main game manager for RealCricket LAN multiplayer
/// Handles game state, player actions, ball physics synchronization
/// </summary>
public class CricketGameManager : MonoBehaviourPunCallbacks
{
    public static CricketGameManager Instance { get; private set; }
    
    [Header("Game Settings")]
    [SerializeField] private int oversPerInnings = 5;
    [SerializeField] private int playersPerTeam = 11;
    [SerializeField] private float ballSyncInterval = 0.05f; // 20Hz for ball physics
    
    [Header("References")]
    [SerializeField] private Transform ballSpawnPoint;
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private Camera mainCamera;
    
    [Header("UI")]
    [SerializeField] private GameObject waitingForPlayersUI;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private TMPro.TextMeshProUGUI scoreText;
    [SerializeField] private TMPro.TextMeshProUGUI oversText;
    [SerializeField] private TMPro.TextMeshProUGUI wicketsText;
    [SerializeField] private TMPro.TextMeshProUGUI playerCountText;
    [SerializeField] private TMPro.TextMeshProUGUI connectionStatusText;
    
    // Game state
    private enum GamePhase { Waiting, Toss, Batting, Bowling, InningsBreak, MatchEnded }
    private GamePhase currentPhase = GamePhase.Waiting;
    
    private int currentOver = 0;
    private int currentBall = 0;
    private int battingTeamScore = 0;
    private int battingTeamWickets = 0;
    private int targetScore = 0;
    private bool isFirstInnings = true;
    private int strikerActorNumber = -1;
    private int nonStrikerActorNumber = -1;
    private int bowlerActorNumber = -1;
    
    // Ball physics
    private GameObject currentBall;
    private Rigidbody ballRb;
    private float lastBallSyncTime = 0f;
    private Vector3 lastKnownBallPosition;
    private Vector3 lastKnownBallVelocity;
    
    // Player management
    private Dictionary<int, PlayerController> playerControllers = new Dictionary<int, PlayerController>();
    private List<int> battingOrder = new List<int>();
    private List<int> bowlingOrder = new List<int>();
    private int currentBatsmanIndex = 0;
    private int currentBowlerIndex = 0;
    
    public GamePhase CurrentPhase => currentPhase;
    public int CurrentOver => currentOver;
    public int CurrentBall => currentBall;
    public int BattingScore => battingTeamScore;
    public int BattingWickets => battingTeamWickets;
    public int TargetScore => targetScore;
    public bool IsFirstInnings => isFirstInnings;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        // Register for LAN network events
        LANNetworkManager.Instance.OnPlayerJoined += OnPlayerJoined;
        LANNetworkManager.Instance.OnPlayerLeft += OnPlayerLeft;
        LANNetworkManager.Instance.OnBecameMasterClient += OnBecameMasterClient;
        
        UpdateUI();
    }
    
    private void Update()
    {
        if (!LANNetworkManager.Instance.InRoom) return;
        
        // Sync ball physics at fixed interval
        if (currentPhase == GamePhase.Batting || currentPhase == GamePhase.Bowling)
        {
            if (Time.time - lastBallSyncTime >= ballSyncInterval)
            {
                SyncBallPhysics();
                lastBallSyncTime = Time.time;
            }
        }
        
        // Update connection status UI
        UpdateConnectionStatus();
    }
    
    private void UpdateConnectionStatus()
    {
        if (connectionStatusText != null)
        {
            if (LANNetworkManager.Instance.IsConnected)
            {
                connectionStatusText.text = LANNetworkManager.Instance.InRoom ? 
                    $"Connected - Room: {LANNetworkManager.Instance.CurrentRoom.Name}" : 
                    "Connected - Not in room";
                connectionStatusText.color = Color.green;
            }
            else
            {
                connectionStatusText.text = "Disconnected";
                connectionStatusText.color = Color.red;
            }
        }
        
        if (playerCountText != null && LANNetworkManager.Instance.InRoom)
        {
            playerCountText.text = $"Players: {LANNetworkManager.Instance.CurrentRoom.PlayerCount}/{LANNetworkManager.Instance.CurrentRoom.MaxPlayers}";
        }
    }
    
    public void OnPlayerJoined(Player newPlayer)
    {
        Debug.Log($"[CricketGameManager] Player joined: {newPlayer.NickName}");
        
        // Add to appropriate team list
        if (battingOrder.Count <= bowlingOrder.Count)
        {
            battingOrder.Add(newPlayer.ActorNumber);
        }
        else
        {
            bowlingOrder.Add(newPlayer.ActorNumber);
        }
        
        // If we have enough players, start the game
        if (LANNetworkManager.Instance.CurrentRoom.PlayerCount >= 2 && currentPhase == GamePhase.Waiting)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                StartToss();
            }
        }
        
        UpdateUI();
    }
    
    public void OnPlayerLeft(Player otherPlayer)
    {
        Debug.Log($"[CricketGameManager] Player left: {otherPlayer.NickName}");
        
        battingOrder.Remove(otherPlayer.ActorNumber);
        bowlingOrder.Remove(otherPlayer.ActorNumber);
        playerControllers.Remove(otherPlayer.ActorNumber);
        
        // Check if we need to pause
        if (LANNetworkManager.Instance.CurrentRoom.PlayerCount < 2)
        {
            PauseGame();
        }
        
        UpdateUI();
    }
    
    public void OnBecameMasterClient()
    {
        Debug.Log("[CricketGameManager] Became master client");
        
        // Re-sync game state to new clients
        if (currentPhase != GamePhase.Waiting && currentPhase != GamePhase.MatchEnded)
        {
            string gameState = GetGameStateJson();
            LANNetworkManager.Instance.SendGameState(gameState);
        }
    }
    
    private void StartToss()
    {
        currentPhase = GamePhase.Toss;
        
        // Random toss winner (master client decides)
        bool batFirst = UnityEngine.Random.value > 0.5f;
        
        // Notify all clients of toss result
        photonView.RPC("RPC_TossResult", RpcTarget.All, batFirst);
    }
    
    [PunRPC]
    private void RPC_TossResult(bool batFirst)
    {
        if (batFirst)
        {
            // Local player's team bats first
            SetupBattingTeam(PhotonNetwork.LocalPlayer.ActorNumber);
        }
        else
        {
            // Local player's team bowls first
            SetupBowlingTeam(PhotonNetwork.LocalPlayer.ActorNumber);
        }
    }
    
    private void SetupBattingTeam(int actorNumber)
    {
        currentPhase = GamePhase.Batting;
        isFirstInnings = true;
        
        // Assign roles
        strikerActorNumber = actorNumber;
        nonStrikerActorNumber = GetNextBatsman();
        bowlerActorNumber = GetNextBowler();
        
        // Spawn ball
        SpawnBall();
        
        UpdateUI();
    }
    
    private void SetupBowlingTeam(int actorNumber)
    {
        currentPhase = GamePhase.Bowling;
        isFirstInnings = true;
        
        // Assign roles
        bowlerActorNumber = actorNumber;
        strikerActorNumber = GetNextBatsman();
        nonStrikerActorNumber = GetNextBatsman();
        
        // Spawn ball
        SpawnBall();
        
        UpdateUI();
    }
    
    private void SpawnBall()
    {
        if (ballPrefab != null && ballSpawnPoint != null)
        {
            currentBall = PhotonNetwork.Instantiate(ballPrefab.name, ballSpawnPoint.position, ballSpawnPoint.rotation);
            ballRb = currentBall.GetComponent<Rigidbody>();
            
            // Only bowler can apply force to ball initially
            if (PhotonNetwork.LocalPlayer.ActorNumber == bowlerActorNumber)
            {
                BallController ballController = currentBall.GetComponent<BallController>();
                if (ballController != null)
                {
                    ballController.SetBowlerControl(true);
                }
            }
        }
    }
    
    private void SyncBallPhysics()
    {
        if (ballRb != null && currentBall != null)
        {
            Vector3 pos = currentBall.transform.position;
            Vector3 vel = ballRb.velocity;
            
            // Only send if significant change
            if (Vector3.Distance(pos, lastKnownBallPosition) > 0.01f || 
                Vector3.Distance(vel, lastKnownBallVelocity) > 0.01f)
            {
                LANNetworkManager.Instance.SendBallState(pos, vel);
                lastKnownBallPosition = pos;
                lastKnownBallVelocity = vel;
            }
        }
    }
    
    public void OnBallStateUpdate(Vector3 position, Vector3 velocity, int ownerActorNumber)
    {
        // Only update if not the owner (to avoid conflicts)
        if (ownerActorNumber != PhotonNetwork.LocalPlayer.ActorNumber && currentBall != null && ballRb != null)
        {
            // Smooth interpolation for remote ball
            currentBall.transform.position = Vector3.Lerp(currentBall.transform.position, position, 0.5f);
            ballRb.velocity = Vector3.Lerp(ballRb.velocity, velocity, 0.5f);
        }
    }
    
    public void OnRemotePlayerAction(int actorNumber, string action, string data)
    {
        // Handle remote player actions (shot, run, fielding, etc.)
        if (playerControllers.TryGetValue(actorNumber, out PlayerController controller))
        {
            controller.HandleRemoteAction(action, data);
        }
    }
    
    public void ApplyGameState(string gameStateJson)
    {
        // Parse and apply game state from master client
        try
        {
            GameState state = JsonUtility.FromJson<GameState>(gameStateJson);
            
            currentPhase = state.phase;
            currentOver = state.currentOver;
            currentBall = state.currentBall;
            battingTeamScore = state.battingScore;
            battingTeamWickets = state.battingWickets;
            targetScore = state.targetScore;
            isFirstInnings = state.isFirstInnings;
            strikerActorNumber = state.strikerActorNumber;
            nonStrikerActorNumber = state.nonStrikerActorNumber;
            bowlerActorNumber = state.bowlerActorNumber;
            
            UpdateUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CricketGameManager] Failed to parse game state: {e.Message}");
        }
    }
    
    private string GetGameStateJson()
    {
        GameState state = new GameState
        {
            phase = currentPhase,
            currentOver = currentOver,
            currentBall = currentBall,
            battingScore = battingTeamScore,
            battingWickets = battingTeamWickets,
            targetScore = targetScore,
            isFirstInnings = isFirstInnings,
            strikerActorNumber = strikerActorNumber,
            nonStrikerActorNumber = nonStrikerActorNumber,
            bowlerActorNumber = bowlerActorNumber
        };
        
        return JsonUtility.ToJson(state);
    }
    
    // Public methods for player controllers to call
    public void OnRunScored(int runs)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        battingTeamScore += runs;
        currentBall++;
        
        if (currentBall >= 6)
        {
            currentBall = 0;
            currentOver++;
            
            // Swap strikers
            int temp = strikerActorNumber;
            strikerActorNumber = nonStrikerActorNumber;
            nonStrikerActorNumber = temp;
            
            // Check end of over
            if (currentOver >= oversPerInnings)
            {
                EndInnings();
            }
            else
            {
                // New bowler
                bowlerActorNumber = GetNextBowler();
            }
        }
        else
        {
            // Swap strikers on odd runs
            if (runs % 2 == 1)
            {
                int temp = strikerActorNumber;
                strikerActorNumber = nonStrikerActorNumber;
                nonStrikerActorNumber = temp;
            }
        }
        
        // Broadcast update
        string gameState = GetGameStateJson();
        LANNetworkManager.Instance.SendGameState(gameState);
        
        UpdateUI();
    }
    
    public void OnWicket()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        battingTeamWickets++;
        currentBall++;
        
        // Next batsman
        strikerActorNumber = GetNextBatsman();
        
        if (battingTeamWickets >= playersPerTeam - 1 || currentBall >= 6)
        {
            EndInnings();
        }
        else if (currentBall >= 6)
        {
            currentBall = 0;
            currentOver++;
            bowlerActorNumber = GetNextBowler();
            
            // Swap strikers
            int temp = strikerActorNumber;
            strikerActorNumber = nonStrikerActorNumber;
            nonStrikerActorNumber = temp;
        }
        
        string gameState = GetGameStateJson();
        LANNetworkManager.Instance.SendGameState(gameState);
        
        UpdateUI();
    }
    
    private void EndInnings()
    {
        if (isFirstInnings)
        {
            // First innings ended, set target and switch
            targetScore = battingTeamScore + 1;
            isFirstInnings = false;
            currentPhase = GamePhase.InningsBreak;
            
            // Reset for second innings
            currentOver = 0;
            currentBall = 0;
            battingTeamScore = 0;
            battingTeamWickets = 0;
            
            // Swap teams
            List<int> temp = battingOrder;
            battingOrder = bowlingOrder;
            bowlingOrder = temp;
            
            // After brief break, start second innings
            Invoke(nameof(StartSecondInnings), 5f);
        }
        else
        {
            // Match ended
            currentPhase = GamePhase.MatchEnded;
            DetermineWinner();
        }
        
        string gameState = GetGameStateJson();
        LANNetworkManager.Instance.SendGameState(gameState);
        
        UpdateUI();
    }
    
    private void StartSecondInnings()
    {
        currentPhase = isFirstInnings ? GamePhase.Batting : GamePhase.Bowling;
        
        // Assign new roles
        strikerActorNumber = GetNextBatsman();
        nonStrikerActorNumber = GetNextBatsman();
        bowlerActorNumber = GetNextBowler();
        
        // Respawn ball
        if (currentBall != null)
        {
            PhotonNetwork.Destroy(currentBall);
        }
        SpawnBall();
        
        string gameState = GetGameStateJson();
        LANNetworkManager.Instance.SendGameState(gameState);
        
        UpdateUI();
    }
    
    private void DetermineWinner()
    {
        // Winner determination logic
        Debug.Log($"[CricketGameManager] Match ended. Final score: {battingTeamScore}/{battingTeamWickets}, Target: {targetScore}");
    }
    
    private int GetNextBatsman()
    {
        if (currentBatsmanIndex < battingOrder.Count)
        {
            return battingOrder[currentBatsmanIndex++];
        }
        return -1;
    }
    
    private int GetNextBowler()
    {
        if (currentBowlerIndex < bowlingOrder.Count)
        {
            return bowlingOrder[currentBowlerIndex++];
        }
        // Cycle bowlers
        currentBowlerIndex = 0;
        return bowlingOrder.Count > 0 ? bowlingOrder[0] : -1;
    }
    
    private void PauseGame()
    {
        currentPhase = GamePhase.Waiting;
        UpdateUI();
    }
    
    private void UpdateUI()
    {
        if (scoreText != null) scoreText.text = $"{battingTeamScore}/{battingTeamWickets}";
        if (oversText != null) oversText.text = $"Overs: {currentOver}.{currentBall}";
        if (wicketsText != null) wicketsText.text = $"Target: {targetScore}";
        
        // Show/hide UI panels based on phase
        if (waitingForPlayersUI != null)
            waitingForPlayersUI.SetActive(currentPhase == GamePhase.Waiting);
        
        if (gameUI != null)
            gameUI.SetActive(currentPhase != GamePhase.Waiting && currentPhase != GamePhase.MatchEnded);
    }
    
    // Player controller registration
    public void RegisterPlayerController(int actorNumber, PlayerController controller)
    {
        playerControllers[actorNumber] = controller;
    }
    
    public void UnregisterPlayerController(int actorNumber)
    {
        playerControllers.Remove(actorNumber);
    }
    
    [Serializable]
    private class GameState
    {
        public GamePhase phase;
        public int currentOver;
        public int currentBall;
        public int battingScore;
        public int battingWickets;
        public int targetScore;
        public bool isFirstInnings;
        public int strikerActorNumber;
        public int nonStrikerActorNumber;
        public int bowlerActorNumber;
    }
}