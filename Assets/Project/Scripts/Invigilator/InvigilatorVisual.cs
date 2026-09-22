using UnityEngine;
using UnityEngine.AI;

// Walk + head-turn for the invigilator model.
// If the model has an Animator with a controller (e.g. a Mixamo walk), this drives its
// "Speed" parameter and only adds the head turn on top. Otherwise it animates the legs
// and arms procedurally, so it works with no animation clips at all.
// Put this on the character MODEL, which is a child of the Invigilator object.
// It only moves bones; the Invigilator's own scripts still own position and body rotation.
// Built for the Floreswa characters (Blender "metarig" bone names) - change names if you swap packs.
public class InvigilatorVisual : MonoBehaviour
{
    [Header("Animator mode (used when the model has an Animator with a controller)")]
    [SerializeField] string speedParam = "Speed";
    [SerializeField] string humanoidHeadFallback = "";   // leave empty: uses the Avatar's head bone

    [Header("Bone names (procedural mode)")]
    [SerializeField] string thighL = "thigh.L", thighR = "thigh.R";
    [SerializeField] string shinL = "shin.L", shinR = "shin.R";
    [SerializeField] string upperArmL = "upper_arm.L", upperArmR = "upper_arm.R";
    [SerializeField] string forearmL = "forearm.L", forearmR = "forearm.R";
    [SerializeField] string headBone = "spine.006";

    [Header("Rest pose: bring arms down from the T-pose (tune live in Play mode)")]
    [SerializeField] Vector3 armRestL = new Vector3(0f, 0f, 70f);
    [SerializeField] Vector3 armRestR = new Vector3(0f, 0f, -70f);
    [SerializeField] Vector3 elbowRest = new Vector3(10f, 0f, 0f);

    [Header("Walk")]
    [SerializeField] Vector3 swingAxis = Vector3.right;   // local axis legs/arms swing around
    [SerializeField] float fullWalkSpeed = 1.2f;         // agent speed that gives the full swing
    [SerializeField] float stepsPerMetre = 0.9f;         // full stride cycles per metre walked
    [SerializeField] float legSwing = 25f;
    [SerializeField] float kneeBend = 35f;               // flip the sign if knees bend backwards
    [SerializeField] float armSwing = 15f;
    [SerializeField] float bobHeight = 0.03f;

    [Header("Head: casual glances at whoever is nearest (everyone equal); stares at YOU once suspicious")]
    [Tooltip("Calm: glance at the nearest person (student or you) within this distance.")]
    [SerializeField] float lookWhenCloserThan = 2.5f;
    [SerializeField] float retargetInterval = 0.75f;
    [SerializeField, Range(0f, 1f)] float lookWhenSuspicionAbove = 0.4f;
    [SerializeField] float maxHeadYaw = 75f;
    [SerializeField] float maxHeadPitch = 30f;
    [SerializeField] float headTurnSpeed = 3f;

    NavMeshAgent agent;
    VisionCone vision;
    SuspicionMeter suspicion;
    Transform root;

    Transform tL, tR, sL, sR, aL, aR, fL, fR, head;
    Quaternion rtL, rtR, rsL, rsR, raL, raR, rfL, rfR, rHead;
    Vector3 baseLocalPos;
    Animator animator;
    bool useAnimator;
    int speedHash;
    float phase, blend, headWeight;

    public bool IsWatchingPlayer => headWeight > 0.5f && targetIsPlayer;

    Vector3 lookTarget;
    bool targetIsPlayer, hasTarget;
    float retargetTimer;

    void Awake()
    {
        agent = GetComponentInParent<NavMeshAgent>();
        root = agent != null ? agent.transform : transform.parent;
        suspicion = GetComponentInParent<SuspicionMeter>();
        vision = root != null ? root.GetComponentInChildren<VisionCone>() : null;

        // Use an Animator if one is set up; switch off any that have no controller.
        foreach (var anim in GetComponentsInChildren<Animator>())
        {
            if (anim.runtimeAnimatorController != null && animator == null) { animator = anim; continue; }
            anim.enabled = false;
        }
        useAnimator = animator != null;
        speedHash = Animator.StringToHash(speedParam);

        if (useAnimator)
        {
            animator.applyRootMotion = false;          // the NavMeshAgent moves him, not the clip
            head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) head = Find(string.IsNullOrEmpty(humanoidHeadFallback) ? headBone : humanoidHeadFallback);
            baseLocalPos = transform.localPosition;
            return;
        }

        tL = Find(thighL); tR = Find(thighR);
        sL = Find(shinL); sR = Find(shinR);
        aL = Find(upperArmL); aR = Find(upperArmR);
        fL = Find(forearmL); fR = Find(forearmR);
        head = Find(headBone);

        rtL = Rest(tL); rtR = Rest(tR); rsL = Rest(sL); rsR = Rest(sR);
        raL = Rest(aL); raR = Rest(aR); rfL = Rest(fL); rfR = Rest(fR); rHead = Rest(head);
        baseLocalPos = transform.localPosition;
    }

    Transform Find(string boneName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        Debug.LogWarning($"[InvigilatorVisual] Bone '{boneName}' not found on {name}.", this);
        return null;
    }

    static Quaternion Rest(Transform t) => t != null ? t.localRotation : Quaternion.identity;

    static void Pose(Transform t, Quaternion rest, Vector3 axis, float angle)
    {
        if (t != null) t.localRotation = rest * Quaternion.AngleAxis(angle, axis);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // ---- walk cycle driven by actual movement speed ----
        float speed = 0f;
        if (agent != null && agent.enabled)
        {
            Vector3 v = agent.velocity; v.y = 0f;
            speed = v.magnitude;
        }
        blend = Mathf.MoveTowards(blend, Mathf.Clamp01(speed / Mathf.Max(0.01f, fullWalkSpeed)), dt * 4f);

        if (useAnimator)
        {
            animator.SetFloat(speedHash, speed);
            ApplyHead(dt, false);                     // animation already posed the head this frame
            return;
        }

        phase = Mathf.Repeat(phase + speed * stepsPerMetre * Mathf.PI * 2f * dt, Mathf.PI * 2f);
        float s = Mathf.Sin(phase);

        Pose(tL, rtL, swingAxis,  s * legSwing * blend);
        Pose(tR, rtR, swingAxis, -s * legSwing * blend);
        Pose(sL, rsL, swingAxis, Mathf.Max(0f, -s) * kneeBend * blend);
        Pose(sR, rsR, swingAxis, Mathf.Max(0f,  s) * kneeBend * blend);

        if (aL != null) aL.localRotation = raL * Quaternion.Euler(armRestL) * Quaternion.AngleAxis(-s * armSwing * blend, swingAxis);
        if (aR != null) aR.localRotation = raR * Quaternion.Euler(armRestR) * Quaternion.AngleAxis( s * armSwing * blend, swingAxis);
        if (fL != null) fL.localRotation = rfL * Quaternion.Euler(elbowRest);
        if (fR != null) fR.localRotation = rfR * Quaternion.Euler(elbowRest);

        transform.localPosition = baseLocalPos + Vector3.up * (Mathf.Abs(s) * bobHeight * blend);

        ApplyHead(dt, true);
    }

    // Head turns toward the player when close or suspicious, on top of whatever pose it has.
    void ApplyHead(float dt, bool resetToRest)
    {
        if (head == null || root == null) return;
        if (resetToRest) head.localRotation = rHead;

        ChooseLookTarget(dt);
        headWeight = Mathf.MoveTowards(headWeight, hasTarget ? 1f : 0f, dt * headTurnSpeed);
        if (headWeight <= 0f) return;

        Vector3 local = root.InverseTransformDirection(lookTarget - head.position);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxHeadYaw, maxHeadYaw);
        float flat = new Vector2(local.x, local.z).magnitude;
        float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -maxHeadPitch, maxHeadPitch);

        float w = Mathf.SmoothStep(0f, 1f, headWeight);
        head.rotation = Quaternion.AngleAxis(yaw * w, root.up) * Quaternion.AngleAxis(pitch * w, root.right) * head.rotation;
    }

    // Suspicious -> always you. Calm -> the nearest person in range, and you are just one of them.
    void ChooseLookTarget(float dt)
    {
        bool playerKnown = vision != null && vision.HasPlayer && vision.PlayerHead != null;
        bool suspicious = playerKnown && suspicion != null && suspicion.Value01 >= lookWhenSuspicionAbove;

        if (suspicious)
        {
            lookTarget = vision.PlayerHead.position;
            targetIsPlayer = hasTarget = true;
            return;
        }

        retargetTimer -= dt;
        if (retargetTimer > 0f) return;
        retargetTimer = retargetInterval;

        hasTarget = false;
        targetIsPlayer = false;
        float best = lookWhenCloserThan;
        Vector3 me = root.position;

        foreach (var s in StudentAmbient.All)
        {
            float d = Flat(s.transform.position - me).magnitude;
            if (d < best) { best = d; lookTarget = s.LookPoint; hasTarget = true; targetIsPlayer = false; }
        }
        if (playerKnown)
        {
            float d = Flat(vision.PlayerRoot.position - me).magnitude;
            if (d < best) { lookTarget = vision.PlayerHead.position; hasTarget = true; targetIsPlayer = true; }
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
