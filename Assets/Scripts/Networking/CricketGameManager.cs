using UnityEngine;
using System.Collections.Generic;
using System;

#if PHOTON_PUN_2 || PHOTON_REALTIME
using Photon.Pun;
using Photon.Realtime;
#endif

/// <summary>
/// Main game manager for RealCricket LAN multiplayer
/// Handles game state, player actions, ball physics synchronization.
/// Offline/practice mode implements a full cricket match loop:
/// auto-bowling AI, batting with aim, running between wickets, fielder AI
/// (catches, collection, throws, run-outs), boundaries, innings and target chase.
/// </summary>
public class CricketGameManager : MonoBehaviour
#if PHOTON_PUN_2 || PHOTON_REALTIME
    , Photon.Pun.IMonoBehaviourPunCallbacks
#endif
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
    public enum GamePhase { Waiting, Toss, Batting, Bowling, InningsBreak, MatchEnded }
    private GamePhase currentPhase = GamePhase.Waiting;

    private int currentOver = 0;
    private int currentBall = 0;
    private int battingTeamScore = 0;
    private int battingTeamWickets = 0;
    private int targetScore = 0;
    private bool isFirstInnings = true;
    private int firstInningsScore = 0;
    private int strikerActorNumber = -1;
    private int nonStrikerActorNumber = -1;
    private int bowlerActorNumber = -1;

    // Ball physics
    private GameObject spawnedBall;
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

#if PHOTON_PUN_2 || PHOTON_REALTIME
    private PhotonView photonView;
#endif

    public GamePhase CurrentPhase => currentPhase;
    public int CurrentOver => currentOver;
    public int CurrentBall => currentBall;
    public int BattingScore => battingTeamScore;
    public int BattingWickets => battingTeamWickets;
    public int TargetScore => targetScore;
    public bool IsFirstInnings => isFirstInnings;

    // ---------- Offline match state (also used by practice mode) ----------
    private bool autoBowl = true;
    private float nextAutoBowlTime = 0f;
    private float autoBowlInterval = 5f;
    private bool isBallInPlay = false;
    private bool hasScoredThisBall = false;
    private bool wasBallHit = false;
    private float maxBallHeightThisDelivery = 0f;
    private float deliveryStartTime = 0f;
    private float ballHitTime = -999f;

    // Running between wickets
    private GameObject batsmanObj;
    private bool isRunning = false;
    private float runLegTimer = 0f;
    private int runsThisBall = 0;
    private bool runningTowardBowler = true;
    private const float LEG_TIME = 2.6f;
    private const float BAT_Z_BATTING = -9.5f;
    private const float BAT_Z_BOWLING = 9.5f;

    // Fielding
    private FielderAI assignedChaser = null;
    private bool ballThrownForRunout = false;
    private float catchRollCooldown = 0f;
    private BroadcastCamera broadcastCam;

    public bool IsBallLive => isBallInPlay;
    public bool WasBallHit => wasBallHit;
    public bool DeliveryResolved => hasScoredThisBall;
    public Vector3 BattingStumpsPosition => new Vector3(0f, 0.71f, -10.06f);

    private string bannerMessage = "REAL CRICKET: Press SPACE to swing, A/D to aim!";
    private float bannerTimer = 6.0f;

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

#if PHOTON_PUN_2 || PHOTON_REALTIME
        photonView = GetComponent<PhotonView>();
#endif
    }

    private void Start()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (LANNetworkManager.Instance != null)
        {
            LANNetworkManager.Instance.OnPlayerJoined += OnPlayerJoined;
            LANNetworkManager.Instance.OnPlayerLeft += OnPlayerLeft;
            LANNetworkManager.Instance.OnBecameMasterClient += OnBecameMasterClient;
        }
#endif

        // Find or bind ball
        if (spawnedBall == null)
        {
            spawnedBall = GameObject.FindWithTag("Ball");
            if (spawnedBall == null)
            {
                BallController bc = FindObjectOfType<BallController>();
                if (bc != null) spawnedBall = bc.gameObject;
            }
        }

        if (spawnedBall != null)
        {
            ballRb = spawnedBall.GetComponent<Rigidbody>();
        }

        batsmanObj = GameObject.Find("Batsman");
        broadcastCam = FindObjectOfType<BroadcastCamera>();

        currentPhase = GamePhase.Batting;
        targetScore = 0;
        nextAutoBowlTime = Time.time + 3.0f;

        UpdateUI();
    }

    private void Update()
    {
        // Countdown banner timer
        if (bannerTimer > 0f)
        {
            bannerTimer -= Time.deltaTime;
        }

#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (LANNetworkManager.Instance != null && LANNetworkManager.Instance.InRoom)
        {
            // Sync ball physics at fixed interval
            if (currentPhase == GamePhase.Batting || currentPhase == GamePhase.Bowling)
            {
                if (Time.time - lastBallSyncTime >= ballSyncInterval)
                {
                    SyncBallPhysics();
                    lastBallSyncTime = Time.time;
                }
            }

            UpdateConnectionStatus();
            return;
        }
#endif

        // ---- Offline practice match loop ----
        if (Input.GetKeyDown(KeyCode.N))
        {
            NewMatch();
        }

        if (spawnedBall != null)
        {
            if (ballRb == null) ballRb = spawnedBall.GetComponent<Rigidbody>();
            Vector3 ballPos = spawnedBall.transform.position;
            float distFromCenter = new Vector2(ballPos.x, ballPos.z).magnitude;

            // Track height of delivery
            if (ballPos.y > maxBallHeightThisDelivery)
            {
                maxBallHeightThisDelivery = ballPos.y;
            }

            if (isBallInPlay)
            {
                // 1. Boundary check: 65m boundary rope
                if (distFromCenter >= 64.0f && !hasScoredThisBall)
                {
                    hasScoredThisBall = true;
                    isRunning = false;
                    if (ballPos.y > 1.2f || maxBallHeightThisDelivery > 8f)
                    {
                        RecordShotOutcome(6, "SIX!! Massive hit into the stands!");
                    }
                    else
                    {
                        RecordShotOutcome(4, "FOUR!! Pierced the boundary rope!");
                    }
                    Invoke(nameof(AutoResetBall), 3.0f);
                }

                // 2. Wicket check: Ball hits batting stumps
                if (ballPos.z <= -9.8f && ballPos.z >= -10.35f && Mathf.Abs(ballPos.x) <= 0.35f && ballPos.y <= 0.85f && !hasScoredThisBall)
                {
                    hasScoredThisBall = true;
                    isRunning = false;
                    battingTeamWickets++;
                    bannerMessage = wasBallHit ? "HIT WICKET!! Clattered into the stumps!" : "BOWLED!! Timber! Off-stump knocked back!";
                    bannerTimer = 3.5f;
                    broadcastCam?.TriggerWicketReplay();
                    RecordShotOutcome(0, bannerMessage, suppressBanner: true);
                    Invoke(nameof(AutoResetBall), 3.0f);
                }

                // 3. Keeper takes it clean (no bat contact) -> dot ball
                if (ballPos.z < -12.8f && !hasScoredThisBall && !wasBallHit && !ballThrownForRunout)
                {
                    hasScoredThisBall = true;
                    RecordShotOutcome(0, "Dot Ball - Collected by the keeper");
                    Invoke(nameof(AutoResetBall), 2.5f);
                }

                // 4. Run-out check on fielder throw
                if (ballThrownForRunout && !hasScoredThisBall)
                {
                    float distToStumps = Vector3.Distance(ballPos, BattingStumpsPosition);
                    if (distToStumps < 2.2f)
                    {
                        if (isRunning && runLegTimer / LEG_TIME > 0.45f)
                        {
                            // RUN OUT!
                            hasScoredThisBall = true;
                            isRunning = false;
                            battingTeamWickets++;
                            RecordShotOutcome(runsThisBall, "RUN OUT!! Direct hit - brilliant fielding!", suppressBanner: true);
                            bannerMessage = "RUN OUT!! Direct hit - brilliant fielding!";
                            bannerTimer = 3.5f;
                            broadcastCam?.TriggerWicketReplay();
                        }
                        else
                        {
                            // Batsman home safe; ball is dead
                            hasScoredThisBall = true;
                            isRunning = false;
                            RecordShotOutcome(runsThisBall, runsThisBall > 0 ? $"{runsThisBall} run(s) taken, fielded well" : "No run - fielded sharply");
                        }
                        ballThrownForRunout = false;
                        Invoke(nameof(AutoResetBall), 2.5f);
                    }
                }

                // 5. Stalemate: ball stuck in the deep with no resolution
                if (!hasScoredThisBall && wasBallHit && Time.time - deliveryStartTime > 22f)
                {
                    hasScoredThisBall = true;
                    isRunning = false;
                    RecordShotOutcome(runsThisBall, runsThisBall > 0 ? "Ball dead in the deep" : "Dot ball");
                    Invoke(nameof(AutoResetBall), 2.5f);
                }
            }

            // 6. Auto-bowl delivery timer
            if (autoBowl && !isBallInPlay && Time.time >= nextAutoBowlTime && (currentPhase == GamePhase.Batting || currentPhase == GamePhase.InningsBreak))
            {
                if (currentPhase == GamePhase.InningsBreak)
                {
                    StartSecondInningsOffline();
                }
                else
                {
                    BowlNextDelivery();
                }
            }
        }

        // 7. Animate running between wickets
        if (isRunning && batsmanObj != null)
        {
            runLegTimer += Time.deltaTime;
            if (runLegTimer >= LEG_TIME)
            {
                runLegTimer -= LEG_TIME;
                runsThisBall++;
                runningTowardBowler = !runningTowardBowler;
                if (!hasScoredThisBall)
                {
                    bannerMessage = $"Running... {runsThisBall} completed";
                    bannerTimer = 1.2f;
                }
            }
            float legProgress = runLegTimer / LEG_TIME;
            float zFrom = runningTowardBowler ? BAT_Z_BATTING : BAT_Z_BOWLING;
            float zTo = runningTowardBowler ? BAT_Z_BOWLING : BAT_Z_BATTING;
            Vector3 bp = batsmanObj.transform.position;
            bp.z = Mathf.Lerp(zFrom, zTo, legProgress);
            batsmanObj.transform.position = bp;
        }
    }

    private void UpdateConnectionStatus()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#else
        if (connectionStatusText != null)
        {
            connectionStatusText.text = "Photon not installed";
            connectionStatusText.color = Color.yellow;
        }
#endif
    }

#if PHOTON_PUN_2 || PHOTON_REALTIME
    public void OnPlayerJoined(Photon.Realtime.Player newPlayer)
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

    public void OnPlayerLeft(Photon.Realtime.Player otherPlayer)
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
#endif

    public void OnBecameMasterClient()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        Debug.Log("[CricketGameManager] Became master client");

        // Re-sync game state to new clients
        if (currentPhase != GamePhase.Waiting && currentPhase != GamePhase.MatchEnded)
        {
            string gameState = GetGameStateJson();
            LANNetworkManager.Instance.SendGameState(gameState);
        }
#endif
    }

    private void StartToss()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        currentPhase = GamePhase.Toss;

        // Random toss winner (master client decides)
        bool batFirst = UnityEngine.Random.value > 0.5f;

        // Notify all clients of toss result
        photonView.RPC("RPC_TossResult", RpcTarget.All, batFirst);
#endif
    }

#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif

    private void SetupBattingTeam(int actorNumber)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        currentPhase = GamePhase.Batting;
        isFirstInnings = true;

        // Assign roles
        strikerActorNumber = actorNumber;
        nonStrikerActorNumber = GetNextBatsman();
        bowlerActorNumber = GetNextBowler();

        // Spawn ball
        SpawnBall();

        UpdateUI();
#endif
    }

    private void SetupBowlingTeam(int actorNumber)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        currentPhase = GamePhase.Bowling;
        isFirstInnings = true;

        // Assign roles
        bowlerActorNumber = actorNumber;
        strikerActorNumber = GetNextBatsman();
        nonStrikerActorNumber = GetNextBatsman();

        // Spawn ball
        SpawnBall();

        UpdateUI();
#endif
    }

    private void SpawnBall()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (ballPrefab != null && ballSpawnPoint != null)
        {
            spawnedBall = PhotonNetwork.Instantiate(ballPrefab.name, ballSpawnPoint.position, ballSpawnPoint.rotation);
            ballRb = spawnedBall.GetComponent<Rigidbody>();

            // Only bowler can apply force to ball initially
            if (PhotonNetwork.LocalPlayer.ActorNumber == bowlerActorNumber)
            {
                BallController ballController = spawnedBall.GetComponent<BallController>();
                if (ballController != null)
                {
                    ballController.SetBowlerControl(true);
                }
            }
        }
#endif
    }

    private void SyncBallPhysics()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (ballRb != null && spawnedBall != null)
        {
            Vector3 pos = spawnedBall.transform.position;
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
#endif
    }

    public void OnBallStateUpdate(Vector3 position, Vector3 velocity, int ownerActorNumber)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Only update if not the owner (to avoid conflicts)
        if (ownerActorNumber != PhotonNetwork.LocalPlayer.ActorNumber && spawnedBall != null && ballRb != null)
        {
            // Smooth interpolation for remote ball
            spawnedBall.transform.position = Vector3.Lerp(spawnedBall.transform.position, position, 0.5f);
            ballRb.velocity = Vector3.Lerp(ballRb.velocity, velocity, 0.5f);
        }
#endif
    }

    public void OnRemotePlayerAction(int actorNumber, string action, string data)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Handle remote player actions (shot, run, fielding, etc.)
        if (playerControllers.TryGetValue(actorNumber, out PlayerController controller))
        {
            controller.HandleRemoteAction(action, data);
        }
#endif
    }

    public void ApplyGameState(string gameStateJson)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif
    }

    private string GetGameStateJson()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#else
        return "";
#endif
    }

    // Public methods for player controllers to call
    public void OnRunScored(int runs)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif
    }

    public void OnWicket()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif
    }

    private void EndInnings()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
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
#endif
    }

    private void StartSecondInnings()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        currentPhase = isFirstInnings ? GamePhase.Batting : GamePhase.Bowling;

        // Assign new roles
        strikerActorNumber = GetNextBatsman();
        nonStrikerActorNumber = GetNextBatsman();
        bowlerActorNumber = GetNextBowler();

        SpawnBall();

        string gameState = GetGameStateJson();
        LANNetworkManager.Instance.SendGameState(gameState);

        UpdateUI();
#endif
    }

    private void DetermineWinner()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        Debug.Log($"[CricketGameManager] Match ended. Final score: {battingTeamScore}/{battingTeamWickets}, Target: {targetScore}");
#endif
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
        if (wicketsText != null) wicketsText.text = isFirstInnings ? "1st Innings" : $"Target: {targetScore}";

#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Show/hide UI panels based on phase
        if (waitingForPlayersUI != null)
            waitingForPlayersUI.SetActive(currentPhase == GamePhase.Waiting);

        if (gameUI != null)
            gameUI.SetActive(currentPhase != GamePhase.Waiting && currentPhase != GamePhase.MatchEnded);
#else
        if (waitingForPlayersUI != null)
            waitingForPlayersUI.SetActive(true);

        if (gameUI != null)
            gameUI.SetActive(false);
#endif
    }

    // ====================================================================
    // OFFLINE / PRACTICE MATCH: bowling, batting outcomes, fielding, flow
    // ====================================================================

    public void BowlNextDelivery()
    {
        if (spawnedBall == null || currentPhase == GamePhase.MatchEnded) return;

        isBallInPlay = true;
        hasScoredThisBall = false;
        wasBallHit = false;
        maxBallHeightThisDelivery = 0f;
        deliveryStartTime = Time.time;
        runsThisBall = 0;
        runLegTimer = 0f;
        isRunning = false;
        runningTowardBowler = true;
        ballThrownForRunout = false;
        assignedChaser = null;

        currentBall++;
        if (currentBall >= 6)
        {
            currentBall = 0;
            currentOver++;
        }

        Vector3 startPos = ballSpawnPoint != null ? ballSpawnPoint.position : new Vector3(0, 1.8f, 9.5f);
        spawnedBall.transform.position = startPos;
        spawnedBall.transform.rotation = Quaternion.identity;

        if (ballRb != null)
        {
            ballRb.isKinematic = false;
            ballRb.velocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;

            // Aim at batting end pitch (Z ~ -5.5m to -8.0m)
            float pitchLength = UnityEngine.Random.Range(-5.5f, -8.0f);
            float lateralVariation = UnityEngine.Random.Range(-0.25f, 0.25f);
            Vector3 targetPitchPoint = new Vector3(lateralVariation, 0.04f, pitchLength);

            Vector3 trajectory = (targetPitchPoint - startPos).normalized;
            float bowlSpeed = UnityEngine.Random.Range(22f, 26f);

            BallController bc = spawnedBall.GetComponent<BallController>();
            if (bc != null)
            {
                bc.Bowl(trajectory, bowlSpeed, UnityEngine.Random.Range(-4f, 4f),
#if PHOTON_PUN_2 || PHOTON_REALTIME
                PhotonNetwork.LocalPlayer.ActorNumber
#else
                1
#endif
                );
            }
            else
            {
                ballRb.AddForce(trajectory * bowlSpeed, ForceMode.Impulse);
            }
        }

        bannerMessage = "Delivery bowled! SPACE to swing, A/D to aim!";
        bannerTimer = 2.0f;
        UpdateUI();
    }

    public void ResetBallForBowling()
    {
        if (spawnedBall == null) return;
        isBallInPlay = false;
        hasScoredThisBall = false;
        wasBallHit = false;
        isRunning = false;
        ballThrownForRunout = false;
        assignedChaser = null;

        Vector3 startPos = ballSpawnPoint != null ? ballSpawnPoint.position : new Vector3(0, 1.8f, 9.5f);
        spawnedBall.transform.position = startPos;
        spawnedBall.transform.rotation = Quaternion.identity;

        if (ballRb != null)
        {
            ballRb.velocity = Vector3.zero;
            ballRb.angularVelocity = Vector3.zero;
            ballRb.isKinematic = true;
        }

        // Batsman back at the crease
        if (batsmanObj != null)
        {
            Vector3 bp = batsmanObj.transform.position;
            bp.z = BAT_Z_BATTING;
            batsmanObj.transform.position = bp;
            runningTowardBowler = true;
        }

        nextAutoBowlTime = Time.time + autoBowlInterval;
        bannerMessage = "Ball ready! SPACE to swing, B to bowl!";
        bannerTimer = 3.0f;
    }

    private void AutoResetBall()
    {
        // Innings / match transitions happen on delivery resolution
        if (CheckMatchFlow()) return;
        ResetBallForBowling();
    }

    /// <summary>
    /// Handles innings break, chase victory, and match end after a delivery resolves.
    /// Returns true if the match state changed (skip normal ball reset).
    /// </summary>
    private bool CheckMatchFlow()
    {
        // Second innings: target chased?
        if (!isFirstInnings && currentPhase == GamePhase.Batting && battingTeamScore >= targetScore && targetScore > 0)
        {
            currentPhase = GamePhase.MatchEnded;
            bannerMessage = $"TARGET CHASED! Batting side wins with {battingTeamScore}/{battingTeamWickets}!";
            bannerTimer = 9999f;
            UpdateUI();
            return true;
        }

        bool oversDone = currentOver >= oversPerInnings;
        bool allOut = battingTeamWickets >= 10;

        if (currentPhase != GamePhase.MatchEnded && (oversDone || allOut))
        {
            if (isFirstInnings)
            {
                firstInningsScore = battingTeamScore;
                targetScore = battingTeamScore + 1;
                isFirstInnings = false;

                currentOver = 0;
                currentBall = 0;
                battingTeamScore = 0;
                battingTeamWickets = 0;

                bannerMessage = $"INNINGS BREAK! 1st innings: {firstInningsScore}. Target: {targetScore}";
                bannerTimer = 8.0f;
                currentPhase = GamePhase.InningsBreak;

                // Resume second innings shortly
                Invoke(nameof(StartSecondInningsOffline), 8.0f);
                UpdateUI();
                return true;
            }
            else
            {
                currentPhase = GamePhase.MatchEnded;
                string result;
                if (battingTeamScore >= targetScore)
                {
                    result = $"Batting side WINS: {battingTeamScore}/{battingTeamWickets} vs target {targetScore}";
                }
                else if (battingTeamScore == targetScore - 1)
                {
                    result = $"MATCH TIED at {battingTeamScore}!";
                }
                else
                {
                    result = $"Bowling side WINS! Held them to {battingTeamScore}/{battingTeamWickets} (target {targetScore})";
                }
                bannerMessage = result + "  [Press N for a new match]";
                bannerTimer = 9999f;
                UpdateUI();
                return true;
            }
        }

        return false;
    }

    private void StartSecondInningsOffline()
    {
        currentPhase = GamePhase.Batting;
        bannerMessage = "2nd INNINGS - the chase is on!";
        bannerTimer = 4.0f;
        ResetBallForBowling();
        nextAutoBowlTime = Time.time + 4.0f;
        UpdateUI();
    }

    public void NewMatch()
    {
        CancelInvoke();
        isFirstInnings = true;
        currentOver = 0;
        currentBall = 0;
        battingTeamScore = 0;
        battingTeamWickets = 0;
        targetScore = 0;
        firstInningsScore = 0;
        currentPhase = GamePhase.Batting;
        autoBowl = true;
        bannerMessage = "NEW MATCH! Batting first. Good luck!";
        bannerTimer = 4.0f;
        ResetBallForBowling();
        nextAutoBowlTime = Time.time + 3.0f;
        UpdateUI();
    }

    public void OnBallHit(Vector3 hitForce)
    {
        wasBallHit = true;
        ballHitTime = Time.time;
        // Batsmen take off on contact, like real cricket
        if (!hasScoredThisBall && !isRunning && batsmanObj != null)
        {
            isRunning = true;
            runLegTimer = 0f;
            runsThisBall = 0;
            runningTowardBowler = true;
        }
        bannerMessage = "CRACK! Shot played - they're running!";
        bannerTimer = 1.5f;
    }

    public void RecordShotOutcome(int runs, string message)
    {
        RecordShotOutcome(runs, message, false);
    }

    public void RecordShotOutcome(int runs, string message, bool suppressBanner)
    {
        battingTeamScore += runs;
        if (!suppressBanner)
        {
            bannerMessage = message;
            bannerTimer = 3.5f;
        }
        UpdateUI();
    }

    // ---------- Fielding integration (called by FielderAI) ----------

    /// <summary>Fielders ask to chase; only the nearest idle fielder is granted it.</summary>
    public bool RequestChase(FielderAI asker)
    {
        if (assignedChaser != null) return false;
        if (!wasBallHit || hasScoredThisBall || !isBallInPlay || spawnedBall == null) return false;

        // Grant only if asker is the nearest idle fielder to the ball
        FielderAI[] fielders = FindObjectsOfType<FielderAI>();
        FielderAI nearest = null;
        float nearestDist = float.MaxValue;
        foreach (FielderAI f in fielders)
        {
            if (!f.IsIdle) continue;
            float d = Vector3.Distance(f.transform.position, spawnedBall.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = f;
            }
        }
        if (nearest != asker) return false;

        assignedChaser = asker;
        return true;
    }

    /// <summary>Fielder in position for a catch; umpire decides.</summary>
    public void ReportCatchOpportunity(FielderAI fielder, float dist)
    {
        if (hasScoredThisBall || !isBallInPlay || !wasBallHit) return;
        if (ballRb == null) return;
        if (Time.time < catchRollCooldown) return;
        if (maxBallHeightThisDelivery < 3.2f) return; // not a lofted shot

        // Higher catch probability the closer the fielder is
        float chance = Mathf.Clamp(0.55f + (2.4f - dist) * 0.15f, 0.4f, 0.9f);
        catchRollCooldown = Time.time + 1.0f;

        if (UnityEngine.Random.value <= chance)
        {
            hasScoredThisBall = true;
            isRunning = false;
            battingTeamWickets++;
            bannerMessage = "CAUGHT!! Superb catch in the deep!";
            bannerTimer = 3.5f;
            RecordShotOutcome(0, bannerMessage, suppressBanner: true);
            broadcastCam?.TriggerWicketReplay();
            Invoke(nameof(AutoResetBall), 3.0f);
        }
        else
        {
            bannerMessage = "DROPPED! Put down in the deep!";
            bannerTimer = 2.0f;
        }
    }

    /// <summary>Fielder gathered the ball: running stops, runs are finalized.</summary>
    public void OnFielderCollectedBall(FielderAI fielder)
    {
        ballThrownForRunout = false;
        if (hasScoredThisBall) return;

        hasScoredThisBall = true;
        isRunning = false;

        string msg;
        switch (runsThisBall)
        {
            case 0: msg = "Dot ball - fielded sharply"; break;
            case 1: msg = "Quick single taken!"; break;
            case 2: msg = "Two runs - good running!"; break;
            case 3: msg = "Three runs! Excellent running between wickets"; break;
            default: msg = $"{runsThisBall} runs accumulated!"; break;
        }
        RecordShotOutcome(runsThisBall, msg);
        Invoke(nameof(AutoResetBall), 2.5f);
    }

    /// <summary>Fielder threw the ball to the stumps: run-out chance is evaluated in Update.</summary>
    public void OnFielderThrew(FielderAI fielder, Vector3 target)
    {
        if (spawnedBall == null || ballRb == null) return;
        ballThrownForRunout = true;

        Vector3 dir = (target - spawnedBall.transform.position).normalized;
        ballRb.isKinematic = false;
        ballRb.velocity = dir * 26f;
        ballRb.angularVelocity = Vector3.zero;
    }

    /// <summary>
    /// Poorly-timed edge behind the wicket: chance of being caught by slip/keeper.
    /// Called by PlayerController when contact quality was terrible.
    /// </summary>
    public void RegisterEdgeChance()
    {
        if (hasScoredThisBall || !isBallInPlay || !wasBallHit) return;
        if (UnityEngine.Random.value <= 0.35f)
        {
            hasScoredThisBall = true;
            isRunning = false;
            battingTeamWickets++;
            bannerMessage = "CAUGHT BEHIND!! Edged and taken - great take by the keeper!";
            bannerTimer = 3.5f;
            RecordShotOutcome(0, bannerMessage, suppressBanner: true);
            broadcastCam?.TriggerWicketReplay();
            Invoke(nameof(AutoResetBall), 3.0f);
        }
        else
        {
            bannerMessage = "Thick edge - flies past the keeper! Lucky!";
            bannerTimer = 2.0f;
        }
    }

    // Cached GUI Styles
    private GUIStyle cachedTitleStyle;
    private GUIStyle cachedScoreStyle;
    private GUIStyle cachedBannerStyle;
    private GUIStyle cachedControlStyle;
    private bool guiStylesReady = false;

    private void EnsureGUIStyles()
    {
        if (guiStylesReady && cachedTitleStyle != null) return;

        cachedTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        cachedTitleStyle.normal.textColor = Color.yellow;

        cachedScoreStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        cachedScoreStyle.normal.textColor = Color.white;

        cachedBannerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        cachedControlStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13
        };
        cachedControlStyle.normal.textColor = Color.white;

        guiStylesReady = true;
    }

    private void OnGUI()
    {
        EnsureGUIStyles();

        // 1. Top Scoreboard Bar
        GUI.Box(new Rect(10, 10, Screen.width - 20, 50), "");
        GUI.Label(new Rect(25, 15, 300, 40), "REAL CRICKET (PC LAN)", cachedTitleStyle);

        string inningsLabel = isFirstInnings ? "1st INN" : $"TARGET: {targetScore}";
        string scoreStr = $"SCORE: {battingTeamScore}/{battingTeamWickets}   |   OVERS: {currentOver}.{currentBall}   |   {inningsLabel}";
        GUI.Label(new Rect(320, 15, Screen.width - 640, 40), scoreStr, cachedScoreStyle);

        // Quick action buttons in top bar
        if (GUI.Button(new Rect(Screen.width - 310, 18, 90, 32), "Bowl (B)"))
        {
            BowlNextDelivery();
        }

        string autoText = autoBowl ? "Auto: ON" : "Auto: OFF";
        if (GUI.Button(new Rect(Screen.width - 210, 18, 95, 32), autoText))
        {
            autoBowl = !autoBowl;
            if (autoBowl) nextAutoBowlTime = Time.time + 1.5f;
        }

        if (GUI.Button(new Rect(Screen.width - 105, 18, 85, 32), "New (N)"))
        {
            NewMatch();
        }

        // 2. Banner Notification in Center
        if (bannerTimer > 0f)
        {
            bool redText = bannerMessage.Contains("BOWLED") || bannerMessage.Contains("CAUGHT") || bannerMessage.Contains("RUN OUT") || bannerMessage.Contains("WICKET");
            if (bannerMessage.Contains("SIX")) cachedBannerStyle.normal.textColor = Color.cyan;
            else if (bannerMessage.Contains("FOUR")) cachedBannerStyle.normal.textColor = Color.green;
            else if (redText) cachedBannerStyle.normal.textColor = Color.red;
            else cachedBannerStyle.normal.textColor = Color.yellow;

            GUI.Box(new Rect(Screen.width / 2 - 320, 80, 640, 45), "");
            GUI.Label(new Rect(Screen.width / 2 - 315, 82, 630, 40), bannerMessage, cachedBannerStyle);
        }

        // 3. Controls legend in Bottom-Left
        GUI.Box(new Rect(15, Screen.height - 165, 360, 150), "CONTROLS GUIDE");
        GUI.Label(new Rect(25, Screen.height - 140, 340, 20), "• [SPACE] : Swing Bat (time it as ball arrives!)", cachedControlStyle);
        GUI.Label(new Rect(25, Screen.height - 120, 340, 20), "• [A] / [D] : Aim Shot (Off-side / Leg-side)", cachedControlStyle);
        GUI.Label(new Rect(25, Screen.height - 100, 340, 20), "• [B] / Left Click : Bowl Delivery", cachedControlStyle);
        GUI.Label(new Rect(25, Screen.height - 80, 340, 20), "• [R] : Reset Ball to Bowler", cachedControlStyle);
        GUI.Label(new Rect(25, Screen.height - 60, 340, 20), "• [N] : Start New Match", cachedControlStyle);
        GUI.Label(new Rect(25, Screen.height - 40, 340, 20), "• Batsmen auto-run on contact; fielders catch & throw!", cachedControlStyle);
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
