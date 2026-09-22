using UnityEngine;

// Background student: makes a seated/writing animation look less robotic.
// - starts the loop at a random point and a slightly different speed, so the class isn't in sync
// - now and then glances up or sideways for a moment, then goes back to writing
// - strips colliders so students never block the invigilator's vision or pathfinding
// Put this on each student's root (the object that has, or contains, the Animator).
public class StudentAmbient : MonoBehaviour
{
    [Header("Variation")]
    [SerializeField] Vector2 speedRange = new Vector2(0.85f, 1.15f);

    [Header("Glances (head only, needs a Humanoid avatar)")]
    [SerializeField] bool glances = true;
    [SerializeField] Vector2 glanceEvery = new Vector2(8f, 20f);
    [SerializeField] Vector2 glanceLength = new Vector2(0.8f, 2f);
    [SerializeField] float maxGlanceYaw = 35f;
    [SerializeField] float glanceUpPitch = 20f;
    [SerializeField] float glanceSpeed = 4f;

    [Header("Safety")]
    [SerializeField] bool removeColliders = true;

    Animator animator;
    Transform head;
    float nextGlance, glanceTimer, weight;
    float glanceYaw, glancePitch;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();

        if (removeColliders)
            foreach (var c in GetComponentsInChildren<Collider>()) Destroy(c);

        if (CompareTag("Player"))
            Debug.LogWarning($"[Student] {name} is tagged Player - the invigilator will watch it instead of you. Set it to Untagged.", this);
    }

    void Start()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // cheaper when off-screen (WebGL)
            animator.speed = Random.Range(speedRange.x, speedRange.y);

            var state = animator.GetCurrentAnimatorStateInfo(0);
            animator.Play(state.fullPathHash, 0, Random.value);             // desync the loop
            if (animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        nextGlance = Time.time + Random.Range(glanceEvery.x, glanceEvery.y);
    }

    void LateUpdate()
    {
        if (!glances || head == null) return;

        if (glanceTimer <= 0f && Time.time >= nextGlance)
        {
            glanceTimer = Random.Range(glanceLength.x, glanceLength.y);
            glanceYaw = Random.Range(-maxGlanceYaw, maxGlanceYaw);
            glancePitch = -Random.Range(0f, glanceUpPitch);                   // look up from the paper
            nextGlance = Time.time + glanceTimer + Random.Range(glanceEvery.x, glanceEvery.y);
        }

        bool glancing = glanceTimer > 0f;
        if (glancing) glanceTimer -= Time.deltaTime;
        weight = Mathf.MoveTowards(weight, glancing ? 1f : 0f, Time.deltaTime * glanceSpeed);
        if (weight <= 0f) return;

        float w = Mathf.SmoothStep(0f, 1f, weight);
        head.rotation = Quaternion.AngleAxis(glanceYaw * w, transform.up)
                      * Quaternion.AngleAxis(glancePitch * w, transform.right)
                      * head.rotation;
    }
}
