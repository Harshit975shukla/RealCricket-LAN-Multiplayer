using UnityEngine;

#if PHOTON_PUN_2 || PHOTON_REALTIME
using Photon.Pun;
using Photon.Realtime;
#endif

/// <summary>
/// Ball controller with networked physics for RealCricket LAN multiplayer
/// Uses PhotonRigidbodyView for position/rotation sync and custom RPCs for forces
/// </summary>
#if PHOTON_PUN_2 || PHOTON_REALTIME
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class BallController : MonoBehaviour, Photon.Pun.IPunObservable
#else
[RequireComponent(typeof(Rigidbody))]
public class BallController : MonoBehaviour
#endif
{
    [Header("Ball Physics")]
    [SerializeField] private float ballMass = 0.156f; // Cricket ball mass (kg)
    [SerializeField] private float dragCoefficient = 0.3f;
    [SerializeField] private float magnusCoefficient = 0.1f; // For spin
    [SerializeField] private float bounceDamping = 0.6f;
    [SerializeField] private float groundFriction = 0.4f;
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    [Header("Network Sync")]
    [SerializeField] private float positionSyncThreshold = 0.05f;
    [SerializeField] private float velocitySyncThreshold = 0.1f;
    [SerializeField] private int physicsUpdateRate = 20; // Hz
#endif
    
    // Components
    private Rigidbody rb;
#if PHOTON_PUN_2 || PHOTON_REALTIME
    private PhotonView photonView;
    private PhotonRigidbodyView photonRigidbodyView;
#endif
    
    // State
    private bool isBowlerControlled = false;
    private int lastInteractionActor = -1;
#if PHOTON_PUN_2 || PHOTON_REALTIME
    private float lastSyncTime = 0f;
    private Vector3 lastSyncedPosition;
    private Vector3 lastSyncedVelocity;
    private Vector3 lastSyncedAngularVelocity;
#endif
    
    // Physics
    private Vector3 magnusForce;
    private bool isGrounded = false;
    private ContactPoint[] contacts = new ContactPoint[8];
    
    public bool IsBowlerControlled => isBowlerControlled;
    public int LastInteractionActor => lastInteractionActor;
    public bool IsGrounded => isGrounded;
    public Rigidbody Rigidbody => rb;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        photonView = GetComponent<PhotonView>();
        photonRigidbodyView = GetComponent<PhotonRigidbodyView>();
        
        // Ensure we have PhotonRigidbodyView for automatic sync
        if (photonRigidbodyView == null)
        {
            photonRigidbodyView = gameObject.AddComponent<PhotonRigidbodyView>();
        }
        
        photonRigidbodyView.m_SynchronizePosition = true;
        photonRigidbodyView.m_SynchronizeRotation = true;
        photonRigidbodyView.m_SynchronizeVelocity = true;
        photonRigidbodyView.m_SynchronizeAngularVelocity = true;
        photonRigidbodyView.m_TeleportEnabled = true;
        photonRigidbodyView.m_TeleportIfDistanceGreaterThan = 1f;
#endif

        // Configure rigidbody
        rb.mass = ballMass;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }
    
    private void Start()
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Set initial sync values
        lastSyncedPosition = transform.position;
        lastSyncedVelocity = rb.velocity;
        lastSyncedAngularVelocity = rb.angularVelocity;
#endif
    }
    
    private void FixedUpdate()
    {
        // Apply custom physics (Magnus effect for spin)
        ApplyMagnusEffect();
        ApplyDrag();
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Network sync for non-owners
        if (photonView != null && !photonView.IsMine)
        {
            InterpolatePhysics();
        }
        else
        {
            // Check if we need to force sync
            CheckForceSync();
        }
#endif
    }
    
    private void ApplyMagnusEffect()
    {
        // Magnus force: F = 0.5 * rho * v^2 * A * Cl * (spin x velocity)
        // Simplified: F = magnusCoefficient * (angularVelocity x velocity)
        if (rb.angularVelocity.magnitude > 0.1f && rb.velocity.magnitude > 0.1f)
        {
            magnusForce = magnusCoefficient * Vector3.Cross(rb.angularVelocity, rb.velocity);
            rb.AddForce(magnusForce, ForceMode.Acceleration);
        }
    }
    
    private void ApplyDrag()
    {
        // Quadratic drag: F = -0.5 * rho * Cd * A * v^2 * v_hat
        // Simplified: F = -dragCoefficient * velocity * |velocity|
        if (rb.velocity.magnitude > 0.01f)
        {
            Vector3 dragForce = -dragCoefficient * rb.velocity * rb.velocity.magnitude;
            rb.AddForce(dragForce, ForceMode.Acceleration);
        }
    }
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    private void InterpolatePhysics()
    {
        // Smooth interpolation for remote ball
        // PhotonRigidbodyView handles most of this, but we add extra smoothing
        float lerpFactor = 1f / physicsUpdateRate * 5f; // Smooth over ~5 frames
        
        transform.position = Vector3.Lerp(transform.position, lastSyncedPosition, lerpFactor);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(lastSyncedAngularVelocity), lerpFactor);
        rb.velocity = Vector3.Lerp(rb.velocity, lastSyncedVelocity, lerpFactor);
        rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, lastSyncedAngularVelocity, lerpFactor);
    }
    
    private void CheckForceSync()
    {
        float distance = Vector3.Distance(transform.position, lastSyncedPosition);
        float velocityDiff = Vector3.Distance(rb.velocity, lastSyncedVelocity);
        float angularDiff = Vector3.Distance(rb.angularVelocity, lastSyncedAngularVelocity);
        
        if (distance > positionSyncThreshold || 
            velocityDiff > velocitySyncThreshold ||
            angularDiff > velocitySyncThreshold)
        {
            ForceSync();
        }
    }
    
    private void ForceSync()
    {
        lastSyncedPosition = transform.position;
        lastSyncedVelocity = rb.velocity;
        lastSyncedAngularVelocity = rb.angularVelocity;
        lastSyncTime = Time.time;
        
        // The PhotonRigidbodyView will handle sending the update
        // But we can also send an RPC for critical state changes
    }
#endif
    
    // Public methods for player interactions
    public void SetBowlerControl(bool controlled)
    {
        isBowlerControlled = controlled;
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (controlled && (photonView == null || photonView.IsMine))
        {
            // Give full physics control to bowler
            rb.isKinematic = false;
            rb.useGravity = true;
        }
        else if (!controlled)
        {
            // Release control
            rb.isKinematic = false;
        }
#else
        if (controlled)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
        }
        else
        {
            rb.isKinematic = false;
        }
#endif
    }
    
    public void Bowl(Vector3 direction, float force, float spin, int actorNumber = 1)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView != null && !photonView.IsMine) return;
#endif
        
        lastInteractionActor = actorNumber;
        isBowlerControlled = false;
        
        // Release from hand
        transform.SetParent(null);
        rb.isKinematic = false;
        rb.useGravity = true;
        
        // Apply bowl force
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(direction.normalized * force, ForceMode.Impulse);
        
        // Apply spin (angular velocity)
        Vector3 spinAxis = Vector3.Cross(direction.normalized, Vector3.up).normalized;
        rb.angularVelocity = spinAxis * spin * 10f; // Convert to rad/s
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Force immediate sync
        ForceSync();
        
        // RPC to notify others
        if (photonView != null)
        {
            photonView.RPC("RPC_BallBowled", RpcTarget.Others, direction, force, spin, actorNumber);
        }
#endif
    }
    
    public void ApplyBatForce(Vector3 direction, int actorNumber = 1)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView != null && !photonView.IsMine) return;
#endif
        
        lastInteractionActor = actorNumber;
        
        // Apply bat impact force
        rb.AddForce(direction.normalized * direction.magnitude, ForceMode.Impulse);
        
        // Add some random spin from bat contact
        Vector3 randomSpin = new Vector3(
            UnityEngine.Random.Range(-2f, 2f),
            UnityEngine.Random.Range(-1f, 1f),
            UnityEngine.Random.Range(-2f, 2f)
        ) * 50f;
        rb.angularVelocity += randomSpin;
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        ForceSync();
        
        if (photonView != null)
        {
            photonView.RPC("RPC_BallHit", RpcTarget.Others, direction, actorNumber);
        }
#endif
    }
    
    public void Throw(Vector3 direction, float force, int actorNumber)
    {
#if PHOTON_PUN_2 || PHOTON_REALTIME
        if (photonView != null && !photonView.IsMine) return;
#endif
        
        lastInteractionActor = actorNumber;
        
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(direction.normalized * force, ForceMode.Impulse);
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        ForceSync();
        
        if (photonView != null)
        {
            photonView.RPC("RPC_BallThrown", RpcTarget.Others, direction, force, actorNumber);
        }
#endif
    }
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    // RPC methods
    [PunRPC]
    private void RPC_BallBowled(Vector3 direction, float force, float spin, int actorNumber)
    {
        lastInteractionActor = actorNumber;
        isBowlerControlled = false;
        
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(direction.normalized * force, ForceMode.Impulse);
        
        Vector3 spinAxis = Vector3.Cross(direction.normalized, Vector3.up).normalized;
        rb.angularVelocity = spinAxis * spin * 10f;
        
        lastSyncedPosition = transform.position;
        lastSyncedVelocity = rb.velocity;
        lastSyncedAngularVelocity = rb.angularVelocity;
    }
    
    [PunRPC]
    private void RPC_BallHit(Vector3 direction, int actorNumber)
    {
        lastInteractionActor = actorNumber;
        
        rb.AddForce(direction.normalized * direction.magnitude, ForceMode.Impulse);
        
        Vector3 randomSpin = new Vector3(
            UnityEngine.Random.Range(-2f, 2f),
            UnityEngine.Random.Range(-1f, 1f),
            UnityEngine.Random.Range(-2f, 2f)
        ) * 50f;
        rb.angularVelocity += randomSpin;
        
        lastSyncedPosition = transform.position;
        lastSyncedVelocity = rb.velocity;
        lastSyncedAngularVelocity = rb.angularVelocity;
    }
    
    [PunRPC]
    private void RPC_BallThrown(Vector3 direction, float force, int actorNumber)
    {
        lastInteractionActor = actorNumber;
        
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.AddForce(direction.normalized * force, ForceMode.Impulse);
        
        lastSyncedPosition = transform.position;
        lastSyncedVelocity = rb.velocity;
        lastSyncedAngularVelocity = rb.angularVelocity;
    }
#endif
    
    // Collision handling
    private void OnCollisionEnter(Collision collision)
    {
        int contactCount = collision.GetContacts(contacts);
        isGrounded = false;
        
        for (int i = 0; i < contactCount; i++)
        {
            ContactPoint contact = contacts[i];
            
            // Check if hitting ground
            if (Vector3.Dot(contact.normal, Vector3.up) > 0.5f)
            {
                isGrounded = true;
                
                // Apply bounce damping
                Vector3 velocity = rb.velocity;
                velocity.y = Mathf.Abs(velocity.y) * bounceDamping;
                velocity.x *= (1f - groundFriction);
                velocity.z *= (1f - groundFriction);
                rb.velocity = velocity;
                
                // Reduce angular velocity on ground contact
                rb.angularVelocity *= 0.8f;
            }
        }
        
#if PHOTON_PUN_2 || PHOTON_REALTIME
        // Notify game manager of collision
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("RPC_BallCollision", RpcTarget.All, 
                collision.contacts[0].point, 
                collision.relativeVelocity.magnitude,
                collision.gameObject.layer);
        }
#else
        if (collision.contactCount > 0)
        {
            PlayCollisionEffect(collision.GetContact(0).point, collision.relativeVelocity.magnitude, collision.gameObject.layer);
        }
#endif
    }
    
    private void OnCollisionStay(Collision collision)
    {
        int contactCount = collision.GetContacts(contacts);
        isGrounded = false;
        
        for (int i = 0; i < contactCount; i++)
        {
            if (Vector3.Dot(contacts[i].normal, Vector3.up) > 0.5f)
            {
                isGrounded = true;
                break;
            }
        }
    }
    
    private void OnCollisionExit(Collision collision)
    {
        isGrounded = false;
    }
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    [PunRPC]
    private void RPC_BallCollision(Vector3 point, float impactForce, int layer)
    {
        // Play collision effects (sound, particles) on all clients
        PlayCollisionEffect(point, impactForce, layer);
    }
#endif
    
    private void PlayCollisionEffect(Vector3 point, float impactForce, int layer)
    {
        // Play sound based on impact force and surface
        // Spawn particles
        Debug.Log($"[BallController] Collision at {point}, force: {impactForce}, layer: {layer}");
    }
    
#if PHOTON_PUN_2 || PHOTON_REALTIME
    // IPunObservable for custom sync
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Owner sends authoritative state
            stream.SendNext(transform.position);
            stream.SendNext(transform.rotation);
            stream.SendNext(rb.velocity);
            stream.SendNext(rb.angularVelocity);
            stream.SendNext(lastInteractionActor);
            stream.SendNext(isBowlerControlled);
        }
        else
        {
            // Receiver updates interpolated values
            lastSyncedPosition = (Vector3)stream.ReceiveNext();
            Quaternion syncedRotation = (Quaternion)stream.ReceiveNext();
            lastSyncedVelocity = (Vector3)stream.ReceiveNext();
            lastSyncedAngularVelocity = (Vector3)stream.ReceiveNext();
            lastInteractionActor = (int)stream.ReceiveNext();
            isBowlerControlled = (bool)stream.ReceiveNext();
            
            // Apply rotation immediately for visual correctness
            transform.rotation = syncedRotation;
        }
    }
    
    // Called when ownership changes
    public void OnOwnershipTransfer(PhotonView view, Photon.Realtime.Player newOwner)
    {
        Debug.Log($"[BallController] Ownership transferred to {newOwner.NickName}");
    }
#endif
    
    // Debug
    private void OnDrawGizmosSelected()
    {
        if (rb != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, rb.velocity);
            
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(transform.position, rb.angularVelocity);
            
            Gizmos.color = Color.green;
            Gizmos.DrawRay(transform.position, magnusForce);
        }
    }
}