using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Step 2: the invigilator walks to a random point on the NavMesh, pauses, and
/// goes again. Suspicion tiers and the vision cone get layered on top of this
/// later — for now it just needs to look like someone patrolling a room.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class InvigilatorController : MonoBehaviour
{
    [Header("Where they roam")]
    [Tooltip("Centre of the wander area. Leave empty to use wherever this starts.")]
    [SerializeField] private Transform roomCentre;
    [SerializeField] private float wanderRadius = 7f;
    [Tooltip("How far off a random point we'll accept a spot on the NavMesh.")]
    [SerializeField] private float sampleTolerance = 2f;

    [Header("Investigating")]
    [SerializeField] private SuspicionMeter suspicion;
    [SerializeField] private VisionCone vision;
    [Tooltip("Speed while locked onto the player. Faster than a patrol.")]
    [SerializeField] private float investigateSpeed = 1.9f;
    [Tooltip("How close they plant themselves next to the desk.")]
    [SerializeField] private float investigateStandoff = 1.6f;
    [SerializeField] private float investigateRepathInterval = 0.3f;
    [Tooltip("Degrees per second when turning to stare.")]
    [SerializeField] private float turnSpeed = 220f;

    [Header("Scanning")]
    [Tooltip("Chance that any given stop becomes a slow sweep of the room.")]
    [Range(0f, 1f)]
    [SerializeField] private float scanChance = 0.45f;
    [Tooltip("How long a scanning stop lasts, instead of the normal pause.")]
    [SerializeField] private Vector2 scanPause = new Vector2(3f, 5f);
    [Tooltip("Degrees either side of where they stopped.")]
    [SerializeField] private float scanSweep = 75f;
    [Tooltip("Degrees per second while sweeping.")]
    [SerializeField] private float scanSpeed = 40f;

    [Header("Pace")]
    [SerializeField] private float walkSpeed = 1.2f;
    [Tooltip("Seconds to stand still on arrival: random between x and y.")]
    [SerializeField] private Vector2 pauseRange = new Vector2(1f, 3f);

    public bool IsStopped { get; private set; }

    private NavMeshAgent agent;
    private Vector3 home;
    private float pauseTimer;
    private float repathTimer;
    private bool wasLocked;
    private bool arrivalHandled;
    private bool scanning;
    private float scanBaseYaw;
    private float scanPhase;

    public bool IsScanning => scanning;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (suspicion == null) suspicion = GetComponent<SuspicionMeter>();
        if (vision == null) vision = GetComponentInChildren<VisionCone>();
        home = roomCentre != null ? roomCentre.position : transform.position;
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

        // Seen with the phone out? Drop the patrol and go stand over them.
        if (suspicion != null && suspicion.IsLocked && vision != null && vision.HasPlayer)
        {
            Investigate();
            wasLocked = true;
            return;
        }

        if (wasLocked)
        {
            // Lock just expired — back to patrolling.
            wasLocked = false;
            agent.updateRotation = true;
            agent.speed = walkSpeed;
            PickDestination();
        }

        if (agent.pathPending) return;

        // Still walking?
        if (agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.35f)) return;

        if (!arrivalHandled)
        {
            arrivalHandled = true;
            BeginStop();
        }

        if (scanning) SweepLook();

        pauseTimer -= Time.deltaTime;
        if (pauseTimer <= 0f) PickDestination();
    }

    /// <summary>Some stops are a stretch of the legs. Some are a look around the room.</summary>
    private void BeginStop()
    {
        scanning = Random.value < scanChance;
        if (!scanning) return;

        pauseTimer = Random.Range(scanPause.x, scanPause.y);
        scanBaseYaw = transform.eulerAngles.y;
        scanPhase = Random.value * Mathf.PI * 2f;   // don't always sweep the same way first
        agent.updateRotation = false;
    }

    private void SweepLook()
    {
        scanPhase += scanSpeed * Mathf.Deg2Rad * Time.deltaTime;
        float offset = Mathf.Sin(scanPhase) * scanSweep;
        transform.rotation = Quaternion.Euler(0f, scanBaseYaw + offset, 0f);
    }

    /// <summary>Walk straight at the player and keep staring, however they squirm.</summary>
    private void Investigate()
    {
        Transform player = vision.PlayerRoot;
        agent.speed = investigateSpeed;
        agent.updateRotation = false;   // we do the turning, so they stare while walking

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

        Vector3 flat = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
        if (flat.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(flat), turnSpeed * Time.deltaTime);
        }
    }

    private void PickDestination()
    {
        scanning = false;
        arrivalHandled = false;
        agent.updateRotation = true;

        if (TrySamplePoint(home, wanderRadius, out Vector3 point))
            agent.SetDestination(point);

        pauseTimer = Random.Range(pauseRange.x, pauseRange.y);
    }

    /// <summary>Random point in a disc, snapped onto the NavMesh. False if nothing stuck.</summary>
    private bool TrySamplePoint(Vector3 around, float radius, out Vector3 result)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 disc = Random.insideUnitCircle * radius;
            Vector3 candidate = around + new Vector3(disc.x, 0f, disc.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleTolerance, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = transform.position;
        return false;
    }

    private void Stop()
    {
        IsStopped = true;
        if (agent != null) agent.updateRotation = true;
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
