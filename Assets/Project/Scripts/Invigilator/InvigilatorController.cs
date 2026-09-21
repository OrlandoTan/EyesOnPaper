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

    [Header("Pace")]
    [SerializeField] private float walkSpeed = 1.2f;
    [Tooltip("Seconds to stand still on arrival: random between x and y.")]
    [SerializeField] private Vector2 pauseRange = new Vector2(1f, 3f);

    public bool IsStopped { get; private set; }

    private NavMeshAgent agent;
    private Vector3 home;
    private float pauseTimer;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
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
        if (IsStopped || !agent.isOnNavMesh || agent.pathPending) return;

        // Still walking?
        if (agent.remainingDistance > Mathf.Max(agent.stoppingDistance, 0.35f)) return;

        pauseTimer -= Time.deltaTime;
        if (pauseTimer <= 0f) PickDestination();
    }

    private void PickDestination()
    {
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
