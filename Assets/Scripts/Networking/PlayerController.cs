using UnityEngine;
using System;

#if PHOTON_PUN_2 || PHOTON_REALTIME
using Photon.Pun;
using Photon.Realtime;
#endif

/// <summary>
/// Player controller for RealCricket LAN multiplayer
/// Handles batting, bowling, fielding actions with network synchronization
/// </summary>
#if PHOTON_PUN_2 || PHOTON_REALTIME
public class PlayerController : MonoBehaviourPun, Photon.Pun.IPunObservable
#else
public class PlayerController : MonoBehaviour
#endif
{
    [Header("Player Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float rotationSpeed = 10f;
    
    [Header("Batting")]
    [SerializeField] private Transform batTransform;
    [SerializeField] private float swingForce = 20f;
    [SerializeField] private AnimationCurve swingCurve;
    
    [Header("Bowling")]
    [SerializeField] private Transform ballReleasePoint;
    [SerializeField] private float bowlForce = 25f;
    [SerializeField] private float bowlSpin = 5f;
    
    [Header("Fielding")]
    [SerializeField] private float throwForce = 30f;
    [SerializeField] private LayerMask ballLayer;
    
    // Components
    private Rigidbody rb;
    private Animator animator;
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    private PhotonView photonView;
#endif
    
    private CharacterController charController;
    
    // State
    private bool isBatting = false;
    private bool isBowling = false;
    private bool isFielding = false;
    private bool hasBall = false;
    private GameObject currentBall;
    private BallController ballController;
    
    // Network sync
    private Vector3 networkPosition;
    private Quaternion networkRotation;
    private float lastSyncTime = 0f;
    private const float SYNC_INTERVAL = 0.1f; // 10Hz position sync
    
    // Procedural Bat Swing & Local State
    private Quaternion originalBatLocalRot;
    private Vector3 originalBatLocalPos;
    private bool isSwinging = false;
    private float swingProgress = 0f;
    private bool hasHitBallThisSwing = false;

    // Input
    private Vector2 moveInput;
    private bool swingInput;
    private bool bowlInput;
    private bool throwInput;
    private bool diveInput;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        photonView = GetComponent<PhotonView>();
        
        // Register with game manager
        if (photonView != null && photonView.IsMine)
        {
            CricketGameManager.Instance?.RegisterPlayerController(photonView.Owner.ActorNumber, this);
        }
#endif
        
        charController = GetComponent<CharacterController>();
    }

    private void Start()
    {
        if (batTransform != null)
        {
            originalBatLocalRot = batTransform.localRotation;
            originalBatLocalPos = batTransform.localPosition;
        }
    }
    
    private void OnDestroy()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView.IsMine)
        {
            CricketGameManager.Instance?.UnregisterPlayerController(photonView.Owner.ActorNumber);
        }
#endif
    }
    
    private void Update()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView != null && !photonView.IsMine) return;
#endif
        
        HandleInput();
        HandleMovement();
        HandleActions();
        UpdateBatSwing();
    }
    
    private void FixedUpdate()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView != null && !photonView.IsMine) return;
        
        // Network position sync
        if (Time.time - lastSyncTime >= SYNC_INTERVAL)
        {
            SyncPosition();
            lastSyncTime = Time.time;
        }
#endif
    }
    
    private void HandleInput()
    {
        // Movement
        moveInput.x = Input.GetAxis("Horizontal");
        moveInput.y = Input.GetAxis("Vertical");
        
        // Actions
        swingInput = Input.GetKeyDown(KeyCode.Space); // Bat swing
        bowlInput = Input.GetKeyDown(KeyCode.Mouse0) || Input.GetKeyDown(KeyCode.B); // Bowl delivery
        throwInput = Input.GetKeyDown(KeyCode.Mouse1); // Throw ball
        diveInput = Input.GetKeyDown(KeyCode.LeftControl); // Dive
    }
    
    private void HandleMovement()
    {
        if (isBatting)
        {
            // Restricted movement in crease during batting
            Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            if (moveDir.magnitude > 0.1f)
            {
                float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : moveSpeed;
                Vector3 newPos = transform.position + moveDir * speed * Time.deltaTime;
                
                // Keep batsman within crease bounds: X in [-1.5, 1.5], Z in [-10.8, -8.0]
                newPos.x = Mathf.Clamp(newPos.x, -1.5f, 1.5f);
                newPos.z = Mathf.Clamp(newPos.z, -10.8f, -8.0f);
                
                if (charController != null)
                {
                    charController.Move(newPos - transform.position);
                }
                else if (rb != null)
                {
                    rb.MovePosition(newPos);
                }
                else
                {
                    transform.position = newPos;
                }
            }
        }
        
        // Update animator
        if (animator != null)
        {
            animator.SetFloat("Speed", moveInput.magnitude);
            animator.SetBool("IsBatting", isBatting);
            animator.SetBool("IsBowling", isBowling);
            animator.SetBool("HasBall", hasBall);
        }
    }
    
    private void HandleActions()
    {
        // Batting action
        if (swingInput)
        {
            PerformSwing();
        }
        
        // Bowling action
        if (bowlInput)
        {
            if (CricketGameManager.Instance != null)
            {
                CricketGameManager.Instance.BowlNextDelivery();
            }
            else
            {
                PerformBowl();
            }
        }
        
        // Fielding throw
        if (throwInput)
        {
            PerformThrow();
        }
        
        // Dive
        if (diveInput)
        {
            PerformDive();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            if (CricketGameManager.Instance != null)
            {
                CricketGameManager.Instance.ResetBallForBowling();
            }
        }
    }

    private void UpdateBatSwing()
    {
        if (batTransform == null) return;

        if (isSwinging)
        {
            swingProgress += Time.deltaTime * 5.0f; // Rapid, crisp bat swing

            if (swingProgress < 0.3f)
            {
                // Backswing: raise bat
                float t = swingProgress / 0.3f;
                batTransform.localRotation = Quaternion.Slerp(originalBatLocalRot, Quaternion.Euler(-40, -30, 25), t);
            }
            else if (swingProgress < 0.75f)
            {
                // Downswing & follow-through
                float t = (swingProgress - 0.3f) / 0.45f;
                batTransform.localRotation = Quaternion.Slerp(Quaternion.Euler(-40, -30, 25), Quaternion.Euler(65, 35, -20), t);

                CheckBatContact();
            }
            else if (swingProgress <= 1.0f)
            {
                // Recovery to stance
                float t = (swingProgress - 0.75f) / 0.25f;
                batTransform.localRotation = Quaternion.Slerp(Quaternion.Euler(65, 35, -20), originalBatLocalRot, t);
            }
            else
            {
                isSwinging = false;
                batTransform.localRotation = originalBatLocalRot;
                batTransform.localPosition = originalBatLocalPos;
            }
        }
    }

    private void CheckBatContact()
    {
        if (hasHitBallThisSwing) return;

        GameObject ballObj = null;
        ballObj = GameObject.FindWithTag("Ball");
        if (ballObj == null)
        {
            BallController bc = FindObjectOfType<BallController>();
            if (bc != null) ballObj = bc.gameObject;
        }

        if (ballObj != null)
        {
            Vector3 ballPos = ballObj.transform.position;
            float dist = Vector3.Distance(transform.position, ballPos);
            Rigidbody ballRb = ballObj.GetComponent<Rigidbody>();

            // Batsman hitting range
            if (dist <= 3.2f)
            {
                hasHitBallThisSwing = true;

                // ---- Timing quality: how close the ball is to the ideal strike zone ----
                // Perfect contact happens ~0.8-1.2m in front of the batsman at knee-waist height.
                Vector3 idealStrike = transform.position + transform.forward * 1.0f + Vector3.up * 0.6f;
                float timingError = Vector3.Distance(ballPos, idealStrike);

                // 0 = perfectly middled, >=1.2 = edged/mistimed
                float timingQuality = Mathf.Clamp01(1.0f - (timingError / 1.2f));

                // Aim: A/D steers off-side (-X) / leg-side (+X)
                float aimX = moveInput.x * 1.5f + UnityEngine.Random.Range(-0.2f, 0.2f) * (1f - timingQuality * 0.5f);

                // Loft scales with timing - perfect timing lets you keep it down or launch it
                float loftAngle = Mathf.Lerp(0.12f, 0.65f, UnityEngine.Random.Range(0f, 1f) * (0.4f + timingQuality * 0.6f));
                Vector3 shotDir = new Vector3(aimX, loftAngle, 1.0f).normalized;

                // Power: heavily timing-dependent (edges dribble, middled shots fly)
                float powerMultiplier = Mathf.Lerp(0.45f, 2.1f, timingQuality) * UnityEngine.Random.Range(0.9f, 1.15f);
                float shotPower = swingForce * powerMultiplier;

                BallController bc = ballObj.GetComponent<BallController>();
                if (bc != null)
                {
                    bc.ApplyBatForce(shotDir * shotPower, 1);
                }
                else if (ballRb != null)
                {
                    ballRb.velocity = Vector3.zero;
                    ballRb.AddForce(shotDir * shotPower, ForceMode.Impulse);
                }

                if (CricketGameManager.Instance != null)
                {
                    CricketGameManager.Instance.OnBallHit(shotDir * shotPower);
                    // Very poor timing (edge, high ball) risks a catch by slip/keeper
                    if (timingQuality < 0.25f && ballPos.y > 1.1f)
                    {
                        CricketGameManager.Instance.RegisterEdgeChance();
                    }
                }
            }
        }
    }
    
    private void PerformSwing()
    {
        if (!isBatting) return;
        if (isSwinging) return;
        isSwinging = true;
        swingProgress = 0f;
        hasHitBallThisSwing = false;

        // Trigger swing animation
        if (animator != null)
        {
            animator.SetTrigger("Swing");
        }
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView != null && photonView.IsMine && PhotonNetwork.IsConnected)
        {
            Vector3 swingDir = batTransform != null ? batTransform.forward : transform.forward;
            LANNetworkManager.Instance?.SendPlayerAction("Swing", JsonUtility.ToJson(new SwingData
            {
                direction = swingDir,
                force = swingForce,
                timestamp = PhotonNetwork.Time
            }));
        }
#endif
    }
    
    private void PerformBowl()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (animator != null)
        {
            animator.SetTrigger("Bowl");
        }
        
        if (currentBall != null && ballController != null && ballReleasePoint != null)
        {
            Vector3 bowlDir = ballReleasePoint.forward;
            ballController.Bowl(bowlDir * bowlForce, bowlSpin, photonView.Owner.ActorNumber);
            
            // Send action to network
            LANNetworkManager.Instance.SendPlayerAction("Bowl", JsonUtility.ToJson(new BowlData
            {
                direction = bowlDir,
                force = bowlForce,
                spin = bowlSpin,
                timestamp = PhotonNetwork.Time
            }));
            
            hasBall = false;
            currentBall = null;
        }
#endif
    }
    
    private void PerformThrow()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (animator != null)
        {
            animator.SetTrigger("Throw");
        }
        
        if (currentBall != null && ballController != null)
        {
            Vector3 throwDir = transform.forward;
            ballController.Throw(throwDir * throwForce, photonView.Owner.ActorNumber);
            
            LANNetworkManager.Instance.SendPlayerAction("Throw", JsonUtility.ToJson(new ThrowData
            {
                direction = throwDir,
                force = throwForce,
                timestamp = PhotonNetwork.Time
            }));
            
            hasBall = false;
            currentBall = null;
        }
#endif
    }
    
    private void PerformDive()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (animator != null)
        {
            animator.SetTrigger("Dive");
        }
        
        LANNetworkManager.Instance.SendPlayerAction("Dive", JsonUtility.ToJson(new DiveData
        {
            direction = transform.forward,
            timestamp = PhotonNetwork.Time
        }));
#endif
    }
    
    public void SetRole(bool batting, bool bowling, bool fielding)
        {
            isBatting = batting;
            isBowling = bowling;
            isFielding = fielding;
        
            if (animator != null)
            {
                animator.SetBool("IsBatting", isBatting);
                animator.SetBool("IsBowling", isBowling);
            }
        }
    
        public void GiveBall(GameObject ball)
        {
    #if PHOTON_PUN_2 || PHOTON_REALTIME
            currentBall = ball;
            ballController = ball.GetComponent<BallController>();
            hasBall = true;
        
            // Attach ball to hand (for bowling)
            if (isBowling && ballReleasePoint != null)
            {
                ball.transform.SetParent(ballReleasePoint);
                ball.transform.localPosition = Vector3.zero;
                ball.transform.localRotation = Quaternion.identity;
            
                // Disable physics while held
                Rigidbody ballRb = ball.GetComponent<Rigidbody>();
                if (ballRb != null)
                {
                    ballRb.isKinematic = true;
                }
            }
    #endif
        }
    
        public void ReleaseBall()
        {
    #if PHOTON_PUN_2 || PHOTON_REALTIME
            if (currentBall != null)
            {
                currentBall.transform.SetParent(null);
            
                Rigidbody ballRb = currentBall.GetComponent<Rigidbody>();
                if (ballRb != null)
                {
                    ballRb.isKinematic = false;
                }
            
                currentBall = null;
                ballController = null;
                hasBall = false;
            }
    #endif
        }
    
        public void HandleRemoteAction(string action, string data)
        {
    #if PHOTON_PUN_2 || PHOTON_REALTIME
            switch (action)
            {
                case "Swing":
                    SwingData swingData = JsonUtility.FromJson<SwingData>(data);
                    // Play swing animation for remote player
                    if (animator != null) animator.SetTrigger("Swing");
                    break;
            
                case "Bowl":
                    BowlData bowlData = JsonUtility.FromJson<BowlData>(data);
                    if (animator != null) animator.SetTrigger("Bowl");
                    break;
            
                case "Throw":
                    ThrowData throwData = JsonUtility.FromJson<ThrowData>(data);
                    if (animator != null) animator.SetTrigger("Throw");
                    break;
            
                case "Dive":
                    if (animator != null) animator.SetTrigger("Dive");
                    break;
            }
    #endif
        }
    
        private void SyncPosition()
        {
    #if PHOTON_PUN_2 || PHOTON_REALTIME
            // Position and rotation are synced via PhotonTransformView
            // This is handled automatically by PhotonTransformView component
    #endif
        }
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send local data
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(isBatting);
            stream.SendNext(isBowling);
            stream.SendNext(isFielding);
            stream.SendNext(hasBall);
        }
        else
        {
            // Receive remote data
            networkPosition = (Vector3)stream.ReceiveNext();
            networkRotation = (Quaternion)stream.ReceiveNext();
            isBatting = (bool)stream.ReceiveNext();
            isBowling = (bool)stream.ReceiveNext();
            isFielding = (bool)stream.ReceiveNext();
            hasBall = (bool)stream.ReceiveNext();
        
            // Interpolate for smooth movement
            if (!photonView.IsMine)
            {
                transform.position = Vector3.Lerp(transform.position, networkPosition, 0.2f);
                transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, 0.2f);
            }
        }
    }
#endif
    
    // Animation events (called from animations)
    public void OnSwingHit()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Called at the moment of bat-ball contact in animation
        if (currentBall != null && ballController != null && photonView.IsMine)
        {
            Vector3 hitDir = batTransform != null ? batTransform.forward : transform.forward;
            ballController.ApplyBatForce(hitDir * swingForce * 1.5f, photonView.Owner.ActorNumber);
        }
#endif
    }
    
    public void OnBallReleased()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Called when ball leaves hand in bowling animation
        ReleaseBall();
#endif
    }
    
    [Serializable]
    private class SwingData
    {
        public Vector3 direction;
        public float force;
        public double timestamp;
    }
    
    [Serializable]
    private class BowlData
    {
        public Vector3 direction;
        public float force;
        public float spin;
        public double timestamp;
    }
    
    [Serializable]
    private class ThrowData
    {
        public Vector3 direction;
        public float force;
        public double timestamp;
    }
    
    [Serializable]
    private class DiveData
    {
        public Vector3 direction;
        public double timestamp;
    }
}