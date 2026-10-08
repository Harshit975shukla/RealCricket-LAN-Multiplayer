using UnityEngine;

/// <summary>
/// AI fielder for RealCricket LAN multiplayer.
/// Waits at a home position, chases a hit ball (only the nearest fielder gets
/// the chase), attempts catches on lofted balls, collects, and throws back
/// to the batting-end stumps so the umpire can decide run-outs.
/// Works fully offline (no Photon dependency).
/// </summary>
public class FielderAI : MonoBehaviour
{
    public enum FielderState { Position, Chase, Collect, Throw }

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 7.5f;
    [SerializeField] private float collectRadius = 1.1f;
    [SerializeField] private float throwDelay = 0.7f;

    private FielderState state = FielderState.Position;
    private Vector3 homePosition;
    private GameObject ball;
    private Rigidbody ballRb;
    private float stateTimer = 0f;
    private Vector3 throwTarget;

    public bool IsChasing => state == FielderState.Chase || state == FielderState.Collect;
    public bool IsIdle => state == FielderState.Position;
    public Vector3 HomePosition => homePosition;

    private void Awake()
    {
        homePosition = transform.position;
    }

    private void Update()
    {
        CricketGameManager gm = CricketGameManager.Instance;
        if (gm == null) return;

        if (ball == null)
        {
            ball = GameObject.FindWithTag("Ball");
            if (ball == null)
            {
                BallController bc = FindObjectOfType<BallController>();
                if (bc != null) ball = bc.gameObject;
            }
            if (ball != null) ballRb = ball.GetComponent<Rigidbody>();
        }
        if (ball == null) return;

        // If the delivery is over, everyone goes home.
        if (!gm.IsBallLive)
        {
            if (state != FielderState.Position)
            {
                state = FielderState.Position;
                stateTimer = 0f;
            }
            transform.position = Vector3.MoveTowards(transform.position, homePosition, moveSpeed * Time.deltaTime);
            return;
        }

        switch (state)
        {
            case FielderState.Position:
                transform.position = Vector3.MoveTowards(transform.position, homePosition, moveSpeed * 0.5f * Time.deltaTime);
                if (gm.WasBallHit)
                {
                    float dist = Vector3.Distance(transform.position, ball.transform.position);
                    if (gm.RequestChase(this))
                    {
                        state = FielderState.Chase;
                    }
                }
                break;

            case FielderState.Chase:
            {
                Vector3 target = ball.transform.position;
                target.y = 0f;
                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
                transform.LookAt(new Vector3(target.x, transform.position.y, target.z));

                float dist3D = Vector3.Distance(transform.position + Vector3.up * 1.2f, ball.transform.position);

                // Catch window: ball airborne, on its way down, close to us.
                if (ballRb != null &&
                    ball.transform.position.y > 1.8f &&
                    ballRb.velocity.y < 1f &&
                    dist3D < 2.4f)
                {
                    gm.ReportCatchOpportunity(this, dist3D);
                }

                if (dist3D <= collectRadius && ball.transform.position.y < 1.1f)
                {
                    state = FielderState.Collect;
                    stateTimer = 0f;
                }
                break;
            }

            case FielderState.Collect:
                stateTimer += Time.deltaTime;
                // Track the ball on the ground while gathering it
                Vector3 gather = ball.transform.position; gather.y = 0f;
                transform.position = Vector3.MoveTowards(transform.position, gather, moveSpeed * Time.deltaTime);
                if (stateTimer >= 0.45f)
                {
                    gm.OnFielderCollectedBall(this);
                    throwTarget = gm.BattingStumpsPosition;
                    state = FielderState.Throw;
                    stateTimer = 0f;
                }
                break;

            case FielderState.Throw:
                stateTimer += Time.deltaTime;
                transform.LookAt(new Vector3(throwTarget.x, transform.position.y, throwTarget.z));
                if (stateTimer >= throwDelay)
                {
                    gm.OnFielderThrew(this, throwTarget);
                    state = FielderState.Position;
                    stateTimer = 0f;
                }
                break;
        }
    }

    public void SetHome(Vector3 pos)
    {
        homePosition = pos;
        transform.position = pos;
    }
}
