using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Player controller for RealCricket LAN multiplayer
/// Handles batting, bowling, fielding actions with network synchronization
/// </summary>
public class PlayerController : MonoBehaviourPun, IPunObservable
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
    private PhotonView photonView;
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
        photonView = GetComponent<PhotonView>();
        charController = GetComponent<CharacterController>();
        
        // Register with game manager
        if (photonView.IsMine)
        {
            CricketGameManager.Instance?.RegisterPlayerController(photonView.Owner.ActorNumber, this);
        }
    }
    
    private void OnDestroy()
    {
        if (photonView.IsMine)
        {
            CricketGameManager.Instance?.UnregisterPlayerController(photonView.Owner.ActorNumber);
        }
    }
    
    private void Update()
    {
        if (!photonView.IsMine) return;
        
        HandleInput();
        HandleMovement();
        HandleActions();
    }
    
    private void FixedUpdate()
    {
        if (!photonView.IsMine) return;
        
        // Network position sync
        if (Time.time - lastSyncTime >= SYNC_INTERVAL)
        {
            SyncPosition();
            lastSyncTime = Time.time;
        }
    }
    
    private void HandleInput()
    {
        // Movement
        moveInput.x = Input.GetAxis("Horizontal");
        moveInput.y = Input.GetAxis("Vertical");
        
        // Actions
        swingInput = Input.GetKeyDown(KeyCode.Space); // Bat swing
        bowlInput = Input.GetKeyDown(KeyCode.Mouse0); // Bowl/Throw
        throwInput = Input.GetKeyDown(KeyCode.Mouse1); // Throw ball
        diveInput = Input.GetKeyDown(KeyCode.LeftControl); // Dive
    }
    
    private void HandleMovement()
    {
        if (isBatting || isBowling)
        {
            // Restricted movement during batting/bowling
            Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            if (moveDir.magnitude > 0.1f)
            {
                float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : moveSpeed;
                Vector3 targetPos = transform.position + moveDir * speed * Time.deltaTime;
                
                if (charController != null)
                {
                    charController.Move(moveDir * speed * Time.deltaTime);
                }
                else if (rb != null)
                {
                    rb.MovePosition(targetPos);
                }
                
                // Rotate towards movement
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
            }
        }
        else
        {
            // Free movement for fielding
            Vector3 moveDir = new Vector3(moveInput.x, 0, moveInput.y).normalized;
            if (moveDir.magnitude > 0.1f)
            {
                float speed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : moveSpeed;
                
                if (charController != null)
                {
                    charController.Move(moveDir * speed * Time.deltaTime);
                }
                else if (rb != null)
                {
                    rb.MovePosition(transform.position + moveDir * speed * Time.deltaTime);
                }
                
                Quaternion targetRot = Quaternion.LookRotation(moveDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
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
        if (isBatting && swingInput && photonView.IsMine)
        {
            PerformSwing();
        }
        
        // Bowling action
        if (isBowling && bowlInput && photonView.IsMine && hasBall)
        {
            PerformBowl();
        }
        
        // Fielding throw
        if (isFielding && throwInput && photonView.IsMine && hasBall)
        {
            PerformThrow();
        }
        
        // Dive
        if (diveInput && photonView.IsMine)
        {
            PerformDive();
        }
    }
    
    private void PerformSwing()
    {
        // Trigger swing animation
        if (animator != null)
        {
            animator.SetTrigger("Swing");
        }
        
        // Apply force to ball if near
        if (currentBall != null && ballController != null)
        {
            Vector3 swingDir = batTransform != null ? batTransform.forward : transform.forward;
            ballController.ApplyBatForce(swingDir * swingForce, photonView.Owner.ActorNumber);
            
            // Send action to network
            LANNetworkManager.Instance.SendPlayerAction("Swing", JsonUtility.ToJson(new SwingData
            {
                direction = swingDir,
                force = swingForce,
                timestamp = PhotonNetwork.Time
            }));
        }
    }
    
    private void PerformBowl()
    {
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
    }
    
    private void PerformThrow()
    {
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
    }
    
    private void PerformDive()
    {
        if (animator != null)
        {
            animator.SetTrigger("Dive");
        }
        
        LANNetworkManager.Instance.SendPlayerAction("Dive", JsonUtility.ToJson(new DiveData
        {
            direction = transform.forward,
            timestamp = PhotonNetwork.Time
        }));
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
    }
    
    public void ReleaseBall()
    {
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
    }
    
    public void HandleRemoteAction(string action, string data)
    {
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
    }
    
    private void SyncPosition()
    {
        // Position and rotation are synced via PhotonTransformView
        // This is handled automatically by PhotonTransformView component
    }
    
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
    
    // Animation events (called from animations)
    public void OnSwingHit()
    {
        // Called at the moment of bat-ball contact in animation
        if (currentBall != null && ballController != null && photonView.IsMine)
        {
            Vector3 hitDir = batTransform != null ? batTransform.forward : transform.forward;
            ballController.ApplyBatForce(hitDir * swingForce * 1.5f, photonView.Owner.ActorNumber);
        }
    }
    
    public void OnBallReleased()
    {
        // Called when ball leaves hand in bowling animation
        ReleaseBall();
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