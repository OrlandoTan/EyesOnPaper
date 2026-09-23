using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Moves the invigilator, and - importantly - is the *only* thing that rotates them.
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

    [Header("Distraction")]
    [Tooltip("Speed while crossing the room to deal with another student.")]
    [SerializeField] private float distractedSpeed = 2.1f;
    [Tooltip("How close they stand to the student they're dealing with.")]
    [SerializeField] private float distractedStandoff = 1.2f;
    [Tooltip("Optional. Where a shouting student gets escorted to.")]
    [SerializeField] private Transform exitPoint;

    [Header("Scanning")]
    [Range(0f, 1f)]
    [SerializeField] private float scanChance = 0.45f;
    [SerializeField] private Vector2 scanPause = new Vector2(3f, 5f);
    [SerializeField] private float scanSweep = 70f;
    [SerializeField] private float scanSpeed = 40f;
    [Tooltip("LOW suspicion: chance a stop faces a random seat (any student, you included, equal odds) rather than the middle of the room.")]
    [Range(0f, 1f)]
    [SerializeField] private float lookAtPlayerChance = 0.4f;

    [Header("Suspicion tiers: how much they hover around the player")]
    [Tooltip("Medium tier: chance each new stop is near the player's desk.")]
    [Range(0f, 1f)] [SerializeField] private float mediumNearPlayerChance = 0.6f;
    [Tooltip("High tier: chance each new stop is near the player's desk.")]
    [Range(0f, 1f)] [SerializeField] private float highNearPlayerChance = 1f;
    [Tooltip("How far from the player those stops are (min, max metres).")]
    [SerializeField] private Vector2 nearPlayerDistance = new Vector2(1.4f, 2.8f);
    [Tooltip("High tier: chance a stop is BEHIND the player, where they can't see you.")]
    [Range(0f, 1f)] [SerializeField] private float highBehindChance = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float mediumLookAtPlayerChance = 0.75f;
    [Range(0f, 1f)] [SerializeField] private float highLookAtPlayerChance = 1f;
    [Tooltip("High tier: they linger longer at each stop.")]
    [SerializeField] private Vector2 highPauseRange = new Vector2(3f, 6f);

    [Header("Look-arounds: footsteps stop = they're watching")]
    [Tooltip("Every stop is a look-around, so silence always means 'check before you cheat'.")]
    [SerializeField] private bool everyStopIsLookAround = true;
    [Tooltip("Scan length per tier (seconds).")]
    [SerializeField] private Vector2 lowScanLength = new Vector2(2f, 3f);
    [SerializeField] private Vector2 mediumScanLength = new Vector2(2.5f, 4f);
    [SerializeField] private Vector2 highScanLength = new Vector2(3f, 5f);
    [Tooltip("High tier: shrink the sweep so they keep their eyes mostly on the player.")]
    [Range(0f, 1f)] [SerializeField] private float highSweepScale = 0.4f;

    [Header("Mid-walk stops: freeze halfway down an aisle")]
    [SerializeField] private bool midWalkStops = true;
    [Tooltip("Seconds of walking between sudden stops, per tier.")]
    [SerializeField] private Vector2 lowMidStopEvery = new Vector2(8f, 14f);
    [SerializeField] private Vector2 mediumMidStopEvery = new Vector2(6f, 10f);
    [SerializeField] private Vector2 highMidStopEvery = new Vector2(4f, 7f);

    [Header("Turning")]
    [Tooltip("Degrees per second. Everything turns at this rate - nothing ever snaps.")]
    [SerializeField] private float turnSpeed = 160f;
    [Tooltip("Below this speed they're treated as standing still.")]
    [SerializeField] private float movingThreshold = 0.15f;

    public bool IsStopped { get; private set; }
    public bool IsScanning { get; private set; }
    /// <summary>Dealing with someone else entirely. They are not looking for you.</summary>
    public bool IsDistracted => distractionTimer > 0f;
    /// <summary>What they're dealing with. The head-look aims here while distracted.</summary>
    public Vector3 DistractionPoint => distractionPoint;
    public float DistractionRemaining => Mathf.Max(0f, distractionTimer);

    private NavMeshAgent agent;
    private Vector3 home;
    private Vector3 lastStop;

    private float pauseTimer;
    private float repathTimer;
    private bool wasLocked;
    private bool arrivalHandled;

    private float distractionTimer;
    private Vector3 distractionPoint;
    private Transform escortTarget;
    private bool reachedStudent;

    private float restingYaw;     // heading a stop settles on
    private float scanPhase;
    private float scanDirection = 1f;
    private SuspicionMeter.Tier lastTier = SuspicionMeter.Tier.Low;

    private float midStopCountdown;     // walking time left before the next sudden stop
    private float midStopTimer;         // > 0 while frozen mid-walk
    public bool IsMidWalkStop => midStopTimer > 0f;

    private SuspicionMeter.Tier CurrentTier =>
        suspicion != null ? suspicion.CurrentTier : SuspicionMeter.Tier.Low;

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
        if (suspicion != null) suspicion.OnTierChanged += HandleTierChanged;
    }

    private void OnDisable()
    {
        GameEvents.OnCaught    -= Stop;
        GameEvents.OnExamEnded -= Stop;
        if (suspicion != null) suspicion.OnTierChanged -= HandleTierChanged;
    }

    // Suspicion went UP a tier: drop the current stroll and head over now.
    private void HandleTierChanged(SuspicionMeter.Tier tier)
    {
        bool rising = tier > lastTier;
        lastTier = tier;
        if (!rising || IsStopped || wasLocked || agent == null || !agent.isOnNavMesh) return;
        EndMidWalkStop();
        PickDestination();
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
        ResetMidStopCountdown();
        PickDestination();
    }

    private void Update()
    {
        if (IsStopped || !agent.isOnNavMesh) return;

        // Someone else is causing a scene. That outranks everything, including
        // being suspicious of you - which is the entire point of the skills.
        if (IsDistracted)
        {
            HandleDistraction();
            return;
        }

        bool locked = suspicion != null && suspicion.IsLocked && vision != null && vision.HasPlayer;
        if (locked)
        {
            EndMidWalkStop();
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
        // Frozen mid-walk: stand still, look around, then carry on to the same spot.
        if (midStopTimer > 0f)
        {
            midStopTimer -= Time.deltaTime;
            FaceRest();
            if (midStopTimer <= 0f) EndMidWalkStop();
            return;
        }

        if (agent.pathPending || !Arrived())
        {
            IsScanning = false;
            FaceTravel();

            if (midWalkStops && !agent.pathPending && agent.velocity.magnitude > movingThreshold)
            {
                midStopCountdown -= Time.deltaTime;
                if (midStopCountdown <= 0f) BeginMidWalkStop();
            }
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
        IsScanning = everyStopIsLookAround || Random.value < scanChance;

        // Face something worth facing. Standing nose-to-the-wall is what a
        // random heading gives you, and it reads as a broken robot.
        Vector3 lookAt = ChooseLookTarget();
        Vector3 flat = Vector3.ProjectOnPlane(lookAt - transform.position, Vector3.up);
        restingYaw = flat.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(flat).eulerAngles.y
            : transform.eulerAngles.y;

        if (!IsScanning) return;

        Vector2 len = everyStopIsLookAround ? ScanLength() : scanPause;
        pauseTimer = Random.Range(len.x, len.y);
        scanPhase = 0f;                                        // starts centred: no snap
        scanDirection = Random.value < 0.5f ? -1f : 1f;        // vary which way they look first
    }

    // ---------- mid-walk stops ----------

    private Vector2 ScanLength() =>
        CurrentTier == SuspicionMeter.Tier.High   ? highScanLength :
        CurrentTier == SuspicionMeter.Tier.Medium ? mediumScanLength : lowScanLength;

    private void ResetMidStopCountdown()
    {
        Vector2 every = CurrentTier == SuspicionMeter.Tier.High   ? highMidStopEvery
                      : CurrentTier == SuspicionMeter.Tier.Medium ? mediumMidStopEvery
                      : lowMidStopEvery;
        midStopCountdown = Random.Range(every.x, every.y);
    }

    private void BeginMidWalkStop()
    {
        agent.isStopped = true;                 // keeps the path; footsteps fall silent
        IsScanning = true;

        Vector3 lookAt = ChooseLookTarget();
        Vector3 flat = Vector3.ProjectOnPlane(lookAt - transform.position, Vector3.up);
        restingYaw = flat.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(flat).eulerAngles.y
            : transform.eulerAngles.y;

        Vector2 len = ScanLength();
        midStopTimer = Random.Range(len.x, len.y);
        scanPhase = 0f;
        scanDirection = Random.value < 0.5f ? -1f : 1f;
    }

    private void EndMidWalkStop()
    {
        midStopTimer = 0f;
        IsScanning = false;
        if (agent != null && agent.isOnNavMesh && !IsStopped) agent.isStopped = false;
        ResetMidStopCountdown();
    }

    private Vector3 ChooseLookTarget()
    {
        bool hasPlayer = vision != null && vision.HasPlayer;
        SuspicionMeter.Tier tier = CurrentTier;

        // Suspicious: they single you out.
        if (hasPlayer && tier == SuspicionMeter.Tier.High && Random.value < highLookAtPlayerChance)
            return vision.PlayerRoot.position;
        if (hasPlayer && tier == SuspicionMeter.Tier.Medium && Random.value < mediumLookAtPlayerChance)
            return vision.PlayerRoot.position;

        // Calm: everyone is treated the same. Pick any seat - you are just one of the class.
        if (tier == SuspicionMeter.Tier.Low && Random.value < lookAtPlayerChance)
        {
            int count = StudentAmbient.All.Count + (hasPlayer ? 1 : 0);
            if (count > 0)
            {
                int pick = Random.Range(0, count);
                if (pick < StudentAmbient.All.Count) return StudentAmbient.All[pick].transform.position;
                return vision.PlayerRoot.position;
            }
        }

        // Middle of the room means most of the class, and never a wall.
        if ((home - transform.position).sqrMagnitude > 1f) return home;

        // Standing on the centre already - pick a heading at random instead.
        return transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
    }

    private void PickDestination()
    {
        IsScanning = false;
        arrivalHandled = false;
        agent.speed = walkSpeed;

        SuspicionMeter.Tier tier = CurrentTier;
        float nearChance = tier == SuspicionMeter.Tier.High   ? highNearPlayerChance
                         : tier == SuspicionMeter.Tier.Medium ? mediumNearPlayerChance
                         : 0f;

        bool goNear = vision != null && vision.HasPlayer && Random.value < nearChance;
        if (goNear && TrySampleNearPlayer(tier, out Vector3 near))
            agent.SetDestination(near);
        else if (TrySamplePoint(home, wanderRadius, out Vector3 point))
            agent.SetDestination(point);

        Vector2 pause = tier == SuspicionMeter.Tier.High ? highPauseRange : pauseRange;
        pauseTimer = Random.Range(pause.x, pause.y);
    }

    /// <summary>A spot a step or two from the player's desk. At High, usually behind them.</summary>
    private bool TrySampleNearPlayer(SuspicionMeter.Tier tier, out Vector3 result)
    {
        Transform player = vision.PlayerRoot;
        bool behind = tier == SuspicionMeter.Tier.High && Random.value < highBehindChance;

        for (int i = 0; i < 16; i++)
        {
            Vector3 dir = behind
                ? Quaternion.Euler(0f, Random.Range(-60f, 60f), 0f) * -player.forward
                : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            dir.y = 0f;

            Vector3 candidate = player.position + dir.normalized * Random.Range(nearPlayerDistance.x, nearPlayerDistance.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = transform.position;
        return false;
    }

    // ---------- distraction ----------

    /// <summary>
    /// Pulled away to deal with another student. Walks over, deals with it, and
    /// goes back to patrolling. An escort walks them out of the room first.
    /// </summary>
    public void Distract(Vector3 point, float duration, Transform escortTo = null)
    {
        if (IsStopped) return;

        distractionPoint = point;
        distractionTimer = duration;
        escortTarget = escortTo != null ? escortTo : exitPoint;
        reachedStudent = false;

        agent.speed = distractedSpeed;
        wasLocked = false;

        // Whatever they were suspicious about, it's someone else's problem now:
        // the bar dumps to zero and stops climbing until they're done with them.
        if (suspicion != null) suspicion.ResetToZero();

        if (agent.isOnNavMesh && TrySamplePoint(point, distractedStandoff, out Vector3 spot))
            agent.SetDestination(spot);
    }

    private void HandleDistraction()
    {
        distractionTimer -= Time.deltaTime;
        IsScanning = false;

        // Held at zero for the whole distraction, so nothing can re-arm behind
        // their back and pounce the moment they turn round.
        if (suspicion != null) suspicion.ClearLock();

        if (distractionTimer <= 0f)
        {
            // Back to work.
            agent.speed = walkSpeed;
            PickDestination();
            return;
        }

        bool arrived = !agent.pathPending && Arrived();

        if (arrived && !reachedStudent)
        {
            reachedStudent = true;

            // Escorting: follow them to the door. Otherwise stay and help.
            if (escortTarget != null &&
                TrySamplePoint(escortTarget.position, 1.5f, out Vector3 door))
            {
                agent.SetDestination(door);
                distractionPoint = escortTarget.position;   // look where they're herding them
            }
        }

        if (agent.velocity.sqrMagnitude > movingThreshold * movingThreshold) FaceTravel();
        else FaceTowards(distractionPoint);
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
            float sweep = CurrentTier == SuspicionMeter.Tier.High ? scanSweep * highSweepScale : scanSweep;
            yaw += Mathf.Sin(scanPhase) * sweep;
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
    /// worth walking to - a point two steps away makes them look twitchy.
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
        distractionTimer = 0f;

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
