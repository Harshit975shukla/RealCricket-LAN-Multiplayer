using UnityEngine;

/// <summary>
/// TV-style broadcast camera for RealCricket LAN multiplayer.
/// Tracks the ball like a broadcast camera during play, returns to the
/// classic behind-the-batsman angle between deliveries.
/// </summary>
public class BroadcastCamera : MonoBehaviour
{
    public enum CamState { BehindBatsman, BallFollow, WicketReplay, Resetting }

    [Header("Setup")]
    [SerializeField] private Camera cam;
    [SerializeField] private float followSmoothness = 2.2f;

    [Header("Angles")]
    [SerializeField] private Vector3 battingCamOffset = new Vector3(0f, 3.2f, -15.5f);
    [SerializeField] private Vector3 battingCamEuler = new Vector3(11f, 0f, 0f);
    [SerializeField] private float followMinHeight = 2.4f;
    [SerializeField] private float followDistance = 9f;

    private CamState state = CamState.BehindBatsman;
    private GameObject ball;
    private float stateTimer = 0f;
    private Vector3 desiredPos;
    private Quaternion desiredRot;

    public Camera Cam => cam;

    private void Awake()
    {
        if (cam == null) cam = GetComponent<Camera>();
    }

    private void Start()
    {
        transform.position = battingCamOffset;
        transform.rotation = Quaternion.Euler(battingCamEuler);
        desiredPos = battingCamOffset;
        desiredRot = Quaternion.Euler(battingCamEuler);
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
        }

        stateTimer += Time.deltaTime;

        switch (state)
        {
            case CamState.BehindBatsman:
                desiredPos = battingCamOffset;
                desiredRot = Quaternion.Euler(battingCamEuler);
                if (gm.IsBallLive && ball != null)
                {
                    state = CamState.BallFollow;
                }
                break;

            case CamState.BallFollow:
                if (ball == null || !gm.IsBallLive)
                {
                    state = CamState.Resetting;
                    stateTimer = 0f;
                    break;
                }
                Vector3 bp = ball.transform.position;
                Vector3 dir = new Vector3(bp.x, 0f, bp.z).normalized;
                if (dir == Vector3.zero) dir = Vector3.back;
                desiredPos = bp - dir * followDistance + Vector3.up * followDistance * 0.45f;
                if (desiredPos.y < followMinHeight) desiredPos.y = followMinHeight;
                desiredRot = Quaternion.LookRotation((bp + Vector3.up * 0.5f) - desiredPos);
                break;

            case CamState.WicketReplay:
                desiredPos = new Vector3(4.5f, 1.8f, -13.0f);
                desiredRot = Quaternion.LookRotation(new Vector3(0f, 0.6f, -10.06f) - desiredPos);
                if (stateTimer > 2.6f)
                {
                    state = CamState.Resetting;
                    stateTimer = 0f;
                }
                break;

            case CamState.Resetting:
                desiredPos = battingCamOffset;
                desiredRot = Quaternion.Euler(battingCamEuler);
                if (Vector3.Distance(transform.position, desiredPos) < 0.4f)
                {
                    state = CamState.BehindBatsman;
                }
                break;
        }

        float t = 1f - Mathf.Exp(-followSmoothness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPos, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, t);
    }

    public void TriggerWicketReplay()
    {
        state = CamState.WicketReplay;
        stateTimer = 0f;
    }
}
