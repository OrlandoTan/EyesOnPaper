using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Moves the invigilator, and — importantly — is the *only* thing that rotates them.
///
/// The NavMeshAgent's own updateRotation is switched off for good in Awake. Every
/// heading goes through FaceYaw, which turns at a fixed rate, so the vision cone
/// can never snap. Three things want to aim the body (walking, scanning, staring
/// at a suspect) and letting any two of them both write the transform is what made
/// the cone teleport.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class InvigilatorController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SuspicionMeter suspicion;
    [SerializeField] private VisionCone vision;
    [Tooltip("Middle of the room. Also what they turn to face when they stop.")]
    [SerializeField] private Transform roomCentre;

    [Header("Wander area")]
    [SerializeField] private float wanderRadius = 7f;
    [SerializeField] private float sampleTolerance = 2f;
    [Tooltip("Reject stopping spots closer than this to the last one, so they don't loiter.")]
    [SerializeField] private float minTravel = 3f;

    [Header("Pace")]
    [SerializeField] private float walkSpeed = 1.2f;
    [SerializeField] private Vector2 pauseRange = new Vector2(1f, 3f);

    [Header("Investigating")]
    [SerializeField] private float investigateSpeed = 1.9f;
    [SerializeField] private float investigateStandoff = 1.6f;
    [SerializeField] private float investigateRepathInterval = 0.3f;

    [Header("Scanning")]
    [Range(0f, 1f)]
    [SerializeField] private float scanChance = 0.45f;
    [SerializeField] private Vector2 scanPause = new Vector2(3f, 5f);
    [SerializeField] private float scanSweep = 70f;
    [SerializeField] private float scanSpeed = 40f;
    [Tooltip("Chance a stop faces the player's desk rather than the middle of the room.")]
    [Range(0f, 1f)]
    [SerializeField] private float lookAtPlayerChance = 0.4f;

    [Header("Turning")]
    [Tooltip("Degrees per second. Everything turns at this rate — nothing ever snaps.")]
    [SerializeField] private float turnSpeed = 160f;
    [Tooltip("Below this speed they're treated as standing still.")]
    [SerializeField] private float movingThreshold = 0.15f;

    public bool IsStopped { get; private set; }
    public bool IsScanning { get; private set; }

    private NavMeshAgent agent;
    private Vector3 home;
    private Vector3 lastStop;

    private float pauseTimer;
    private float repathTimer;
    private bool wasLocked;
    private bool arrivalHandled;

    private float restingYaw;     // heading a stop settles on
    private float scanPhase;
    private float scanDirection = 1f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (suspicion == null) suspicion = GetComponent<SuspicionMeter>();
        if (vision == null) vision = GetComponentInChildren<VisionCone>();

        home = roomCentre != null ? roomCentre.position : transform.position;
        lastStop = transform.position;
        restingYaw = transform.eulerAngles.y;

        // One owner for rotation. This never gets turned back on.
        agent.updateRotation = false;
    }

    private void OnEnable()
    {
        GameEvents.OnCaught    += Stop;
        GameEvents.OnExamEnded += Stop;
    }

    private void OnDisable()
    {
        GameEvents.OnCaught    -= Stop;
        GameEvents.OnExamEnded -= Stop;
    }

    private void Start()
    {
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning("[Invigilator] Not on a NavMesh. Bake the surface, or drop the " +
                             "capsule so it's standing on the floor.", this);
            return;
        }

        agent.speed = walkSpeed;
        PickDestination();
    }

    private void Update()
    {
        if (IsStopped || !agent.isOnNavMesh) return;

        bool locked = suspicion != null && suspicion.IsLocked && vision != null && vision.HasPlayer;
        if (locked)
        {
            Investigate();
            wasLocked = true;
            return;
        }

        if (wasLocked)
        {
            wasLocked = false;
            agent.speed = walkSpeed;
            PickDestination();
        }

        Patrol();
    }

    // ---------- patrol ----------

    private void Patrol()
    {
        if (agent.pathPending || !Arrived())
        {
            IsScanning = false;
            FaceTravel();
            return;
        }

        if (!arrivalHandled)
        {
            arrivalHandled = true;
            BeginStop();
        }

        FaceRest();

        pauseTimer -= Time.deltaTime;
        if (pauseTimer <= 0f) PickDestination();
    }

    private bool Arrived() =>
        agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, 0.35f);

    /// <summary>Decide what this stop is: a breather, or a look around the room.</summary>
    private void BeginStop()
    {
        lastStop = transform.position;
        IsScanning = Random.value < scanChance;

        // Face something worth facing. Standing nose-to-the-wall is what a
        // random heading gives you, and it reads as a broken robot.
        Vector3 lookAt = ChooseLookTarget();
        Vector3 flat = Vector3.ProjectOnPlane(lookAt - transform.position, Vector3.up);
        restingYaw = flat.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(flat).eulerAngles.y
            : transform.eulerAngles.y;

        if (!IsScanning) return;

        pauseTimer = Random.Range(scanPause.x, scanPause.y);
        scanPhase = 0f;                                        // starts centred: no snap
        scanDirection = Random.value < 0.5f ? -1f : 1f;        // vary which way they look first
    }

    private Vector3 ChooseLookTarget()
    {
        bool towardPlayer = vision != null && vision.HasPlayer && Random.value < lookAtPlayerChance;
        if (towardPlayer) return vision.PlayerRoot.position;

        // Middle of the room means most of the class, and never a wall.
        if ((home - transform.position).sqrMagnitude > 1f) return home;

        // Standing on the centre already — pick a heading at random instead.
        return transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
    }

    private void PickDestination()
    {
        IsScanning = false;
        arrivalHandled = false;

        if (TrySamplePoint(home, wanderRadius, out Vector3 point))
            agent.SetDestination(point);

        pauseTimer = Random.Range(pauseRange.x, pauseRange.y);
    }

    // ---------- investigating ----------

    private void Investigate()
    {
        Transform player = vision.PlayerRoot;
        agent.speed = investigateSpeed;
        IsScanning = false;

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = investigateRepathInterval;

            Vector3 fromPlayer = transform.position - player.position;
            fromPlayer.y = 0f;
            if (fromPlayer.sqrMagnitude < 0.01f) fromPlayer = -player.forward;

            Vector3 target = player.position + fromPlayer.normalized * investigateStandoff;
            if (TrySamplePoint(target, 1.2f, out Vector3 point))
                agent.SetDestination(point);
        }

        FaceTowards(player.position);
    }

    // ---------- rotation, the only place the transform is turned ----------

    private void FaceTravel()
    {
        Vector3 velocity = agent.velocity;
        velocity.y = 0f;
        if (velocity.magnitude < movingThreshold) return;   // keep the last heading, don't spin

        FaceYaw(Quaternion.LookRotation(velocity).eulerAngles.y);
    }

    private void FaceRest()
    {
        float yaw = restingYaw;

        if (IsScanning)
        {
            scanPhase += scanSpeed * Mathf.Deg2Rad * Time.deltaTime * scanDirection;
            yaw += Mathf.Sin(scanPhase) * scanSweep;
        }

        FaceYaw(yaw);
    }

    private void FaceTowards(Vector3 worldPoint)
    {
        Vector3 flat = Vector3.ProjectOnPlane(worldPoint - transform.position, Vector3.up);
        if (flat.sqrMagnitude < 0.01f) return;

        FaceYaw(Quaternion.LookRotation(flat).eulerAngles.y);
    }

    private void FaceYaw(float targetYaw)
    {
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.Euler(0f, targetYaw, 0f),
            turnSpeed * Time.deltaTime);
    }

    // ---------- helpers ----------

    /// <summary>
    /// Random point in a disc, snapped to the NavMesh. Prefers somewhere actually
    /// worth walking to — a point two steps away makes them look twitchy.
    /// </summary>
    private bool TrySamplePoint(Vector3 around, float radius, out Vector3 result)
    {
        Vector3 fallback = transform.position;
        bool haveFallback = false;

        for (int i = 0; i < 16; i++)
        {
            Vector2 disc = Random.insideUnitCircle * radius;
            Vector3 candidate = around + new Vector3(disc.x, 0f, disc.y);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleTolerance, NavMesh.AllAreas))
                continue;

            if ((hit.position - lastStop).sqrMagnitude >= minTravel * minTravel)
            {
                result = hit.position;
                return true;
            }

            if (!haveFallback)
            {
                fallback = hit.position;
                haveFallback = true;
            }
        }

        result = fallback;
        return haveFallback;
    }

    private void Stop()
    {
        IsStopped = true;
        IsScanning = false;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 centre = roomCentre != null ? roomCentre.position
                       : Application.isPlaying ? home : transform.position;
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.5f);
        Gizmos.DrawWireSphere(centre, wanderRadius);
    }
}
