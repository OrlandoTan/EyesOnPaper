using UnityEngine;
using UnityEngine.AI;

// Walks one student out of the exam: stand up -> (hand in paper at the front) -> out the door.
// Added at runtime by ClassDeparture. Reuses the invigilator's Idle/Walk controller, so no new
// animation setup is needed. Handles models that face backwards (the Blender facing issue).
public class StudentLeaver : MonoBehaviour
{
    public bool IsLeaving { get; private set; }

    NavMeshAgent agent;
    Animator anim;
    Transform[] route;
    int next;
    float pauseTimer, handInPause;
    float yawOffset;                 // 0 or 180: how the model's face relates to its root's forward
    int speedHash = Animator.StringToHash("Speed");

    public void Begin(Transform[] waypoints, RuntimeAnimatorController walkController, float speed,
                      float pauseAtHandIn, Vector3 towardFront)
    {
        if (IsLeaving) return;
        IsLeaving = true;
        route = waypoints;
        handInPause = pauseAtHandIn;

        // Seated students face the front. If the root points the other way, the model is backwards.
        Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Vector3 t = Vector3.ProjectOnPlane(towardFront, Vector3.up);
        yawOffset = (t.sqrMagnitude > 0.01f && Vector3.Dot(f, t) < 0f) ? 180f : 0f;

        // No longer part of the class: the invigilator stops picking this seat, glances stop.
        var ambient = GetComponent<StudentAmbient>();
        if (ambient != null) ambient.enabled = false;

        anim = GetComponentInChildren<Animator>();
        if (anim != null)
        {
            if (walkController != null) anim.runtimeAnimatorController = walkController;
            anim.applyRootMotion = false;
            anim.speed = 1f;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"[Departure] {name} isn't near the NavMesh - removed instead of walking.", this);
            gameObject.SetActive(false);
            return;
        }

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = 0.25f;
        agent.height = 1.7f;
        agent.baseOffset = 0f;
        agent.speed = speed;
        agent.acceleration = 6f;
        agent.angularSpeed = 0f;
        agent.updateRotation = false;                    // we turn the body ourselves (facing fix)
        agent.stoppingDistance = 0.2f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.avoidancePriority = 70;                    // the invigilator (50) has right of way
        agent.Warp(hit.position);

        next = 0;
        GoNext();
    }

    void GoNext()
    {
        while (next < route.Length && route[next] == null) next++;
        if (next >= route.Length) { Finish(); return; }
        agent.SetDestination(route[next].position);
    }

    void Update()
    {
        if (!IsLeaving || agent == null || !agent.isOnNavMesh) return;

        Vector3 v = agent.velocity; v.y = 0f;
        if (anim != null) anim.SetFloat(speedHash, v.magnitude);

        if (v.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(v) * Quaternion.Euler(0f, -yawOffset, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 360f * Time.deltaTime);
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.deltaTime;
            if (pauseTimer <= 0f) { next++; GoNext(); }
            return;
        }

        if (agent.pathPending || agent.remainingDistance > 0.35f) return;

        bool lastStop = next >= route.Length - 1;
        if (lastStop) { Finish(); return; }

        // Any stop before the door is the hand-in desk: stand there briefly.
        if (handInPause > 0f) pauseTimer = handInPause;
        else { next++; GoNext(); }
    }

    void Finish()
    {
        IsLeaving = false;
        gameObject.SetActive(false);      // out the door
    }
}
