using UnityEngine;

/// <summary>
/// Step 3: the invigilator's eyes. Answers one question every frame — can they
/// see the player right now? Angle, then distance, then a raycast so walls and
/// desks actually block sight.
///
/// The player side only has to honour the Step 0 contract: root tagged "Player",
/// with a child called "Head". Nothing else is assumed.
/// </summary>
public class VisionCone : MonoBehaviour
{
    [Header("Cone")]
    [Tooltip("Where sight starts. Leave empty to use this object.")]
    [SerializeField] private Transform eye;
    [Tooltip("Full width of the cone in degrees — 100 means 50 to each side.")]
    [SerializeField] private float viewAngle = 100f;
    [SerializeField] private float viewDistance = 12f;
    [Tooltip("What blocks sight. Walls and desks need to be in here.")]
    [SerializeField] private LayerMask sightBlockers = ~0;

    [Header("Looking away")]
    [Tooltip("Head turned further than this off their own paper counts as looking sideways.")]
    [SerializeField] private float lookAwayAngle = 45f;

    [Header("Player lookup")]
    [SerializeField] private string headChildName = "Head";
    [SerializeField] private float refindInterval = 1f;

    [Header("Debug")]
    [Tooltip("Small readout in the Game view while testing. Turn off for builds.")]
    [SerializeField] private bool showReadout = true;

    /// <summary>In the cone, in range, and nothing solid in the way.</summary>
    public bool CanSeePlayer { get; private set; }
    public float DistanceToPlayer { get; private set; } = Mathf.Infinity;
    /// <summary>Degrees the head is turned off the player root's forward. Yaw only.</summary>
    public float PlayerLookAwayAngle { get; private set; }
    public bool PlayerIsLookingAway => PlayerHead != null && PlayerLookAwayAngle > lookAwayAngle;
    public bool HasPlayer => PlayerHead != null;

    public Transform PlayerRoot { get; private set; }
    public Transform PlayerHead { get; private set; }
    public Transform Eye => eye != null ? eye : transform;

    private float refindTimer;
    private bool warnedNoPlayer;

    private void Awake()
    {
        if (eye == null) eye = transform;
        FindPlayer();
    }

    private void Update()
    {
        if (PlayerHead == null)
        {
            refindTimer -= Time.deltaTime;
            if (refindTimer <= 0f) FindPlayer();
            CanSeePlayer = false;
            DistanceToPlayer = Mathf.Infinity;
            return;
        }

        Vector3 origin = Eye.position;
        Vector3 toHead = PlayerHead.position - origin;
        DistanceToPlayer = toHead.magnitude;

        // Yaw-only angle test, so a tall invigilator doesn't lose you to height.
        Vector3 flatToHead = Vector3.ProjectOnPlane(toHead, Vector3.up);
        Vector3 flatForward = Vector3.ProjectOnPlane(Eye.forward, Vector3.up);
        float angle = Vector3.Angle(flatForward, flatToHead);

        CanSeePlayer = DistanceToPlayer <= viewDistance
                       && angle <= viewAngle * 0.5f
                       && HasClearLine(origin, toHead);

        PlayerLookAwayAngle = MeasureLookAway();
    }

    private bool HasClearLine(Vector3 origin, Vector3 toHead)
    {
        if (!Physics.Raycast(origin, toHead.normalized, out RaycastHit hit, toHead.magnitude,
                             sightBlockers, QueryTriggerInteraction.Ignore))
            return true;

        // Hitting the player themselves still counts as seeing them.
        return PlayerRoot != null && hit.transform.IsChildOf(PlayerRoot);
    }

    private float MeasureLookAway()
    {
        if (PlayerRoot == null || PlayerHead == null) return 0f;

        Vector3 deskForward = Vector3.ProjectOnPlane(PlayerRoot.forward, Vector3.up);
        Vector3 headForward = Vector3.ProjectOnPlane(PlayerHead.forward, Vector3.up);
        if (deskForward.sqrMagnitude < 0.001f || headForward.sqrMagnitude < 0.001f) return 0f;

        return Vector3.Angle(deskForward, headForward);
    }

    private void FindPlayer()
    {
        refindTimer = refindInterval;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            if (!warnedNoPlayer)
            {
                Debug.LogWarning("[VisionCone] Nothing tagged 'Player' in the scene yet.", this);
                warnedNoPlayer = true;
            }
            return;
        }

        PlayerRoot = player.transform;
        PlayerHead = FindDeep(PlayerRoot, headChildName);

        if (PlayerHead == null)
        {
            Debug.LogWarning($"[VisionCone] '{player.name}' has no child named '{headChildName}'. " +
                             "Aiming at the root instead — sightlines will read low until it's added.", this);
            PlayerHead = PlayerRoot;
        }

        warnedNoPlayer = false;
    }

    private static Transform FindDeep(Transform parent, string childName)
    {
        if (parent.name == childName) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeep(parent.GetChild(i), childName);
            if (found != null) return found;
        }
        return null;
    }

    private void OnGUI()
    {
        if (!showReadout) return;

        string state = !HasPlayer ? "no player found"
                     : CanSeePlayer ? "SEES PLAYER"
                     : "clear";
        string dist = DistanceToPlayer == Mathf.Infinity ? "-" : DistanceToPlayer.ToString("0.0") + " m";

        GUI.Label(new Rect(12f, 12f, 420f, 60f),
                  $"vision: {state}\ndistance: {dist}   looking away: {PlayerLookAwayAngle:0}°");
    }

    private void OnDrawGizmos()
    {
        Transform e = eye != null ? eye : transform;

        Vector3 flatForward = Vector3.ProjectOnPlane(e.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;

        Gizmos.color = CanSeePlayer ? Color.red : new Color(1f, 0.92f, 0.2f, 0.7f);

        Vector3 left = Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * flatForward;
        Vector3 right = Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * flatForward;
        Gizmos.DrawRay(e.position, left * viewDistance);
        Gizmos.DrawRay(e.position, right * viewDistance);

        const int segments = 20;
        Vector3 prev = e.position + left * viewDistance;
        for (int i = 1; i <= segments; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, viewAngle * (i / (float)segments) - viewAngle * 0.5f, 0f) * flatForward;
            Vector3 next = e.position + dir * viewDistance;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        if (PlayerHead != null)
        {
            Gizmos.color = CanSeePlayer ? Color.red : Color.grey;
            Gizmos.DrawLine(e.position, PlayerHead.position);
        }
    }
}
