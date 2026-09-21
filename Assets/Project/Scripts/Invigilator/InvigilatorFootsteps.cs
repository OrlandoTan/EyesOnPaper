using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Footsteps, so the player can hear where the invigilator is without turning
/// round to look — which now costs them (see SuspicionMeter's over-shoulder rule).
///
/// SCOPE §3.5's fairness rule: "footsteps are always audible". This is that rule.
///
/// Steps are driven by distance travelled rather than a timer, so they speed up
/// naturally when the invigilator breaks into an investigating walk. With no clips
/// assigned it synthesises a placeholder thud, so it works before any audio exists.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AudioSource))]
public class InvigilatorFootsteps : MonoBehaviour
{
    [Header("Clips")]
    [Tooltip("Leave empty to use a generated placeholder step.")]
    [SerializeField] private AudioClip[] steps;

    [Header("Stride")]
    [Tooltip("Metres walked per footstep.")]
    [SerializeField] private float strideLength = 0.8f;
    [SerializeField] private float volume = 0.55f;
    [SerializeField] private Vector2 pitchRange = new Vector2(0.92f, 1.08f);

    [Header("Audible range")]
    [Tooltip("Full volume within this distance.")]
    [SerializeField] private float minDistance = 1.5f;
    [Tooltip("Silent beyond this. Should comfortably cover the room.")]
    [SerializeField] private float maxDistance = 22f;

    private NavMeshAgent agent;
    private AudioSource source;
    private AudioClip generated;
    private float distanceSinceStep;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        source = GetComponent<AudioSource>();

        // Positional audio is the entire point — the player has to be able to
        // tell that someone is behind them, not merely that someone exists.
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;

        if (steps == null || steps.Length == 0)
            generated = BuildPlaceholderStep();
    }

    private void OnEnable()
    {
        GameEvents.OnCaught    += Silence;
        GameEvents.OnExamEnded += Silence;
    }

    private void OnDisable()
    {
        GameEvents.OnCaught    -= Silence;
        GameEvents.OnExamEnded -= Silence;
    }

    private void OnDestroy()
    {
        if (generated != null) Destroy(generated);
    }

    private void Update()
    {
        float travelled = agent.velocity.magnitude * Time.deltaTime;
        if (travelled <= 0.0001f) return;   // standing still is silent, and that's worse

        distanceSinceStep += travelled;
        if (distanceSinceStep < strideLength) return;

        distanceSinceStep = 0f;
        PlayStep();
    }

    private void PlayStep()
    {
        AudioClip clip = steps != null && steps.Length > 0
            ? steps[Random.Range(0, steps.Length)]
            : generated;

        if (clip == null) return;

        source.pitch = Random.Range(pitchRange.x, pitchRange.y);
        source.PlayOneShot(clip, volume);
    }

    private void Silence()
    {
        enabled = false;
        source.Stop();
    }

    /// <summary>
    /// A short thud: a low sine for the heel, a little noise for the scuff, and a
    /// fast decay. Not good audio — just honest placeholder audio, so the fairness
    /// rule holds before anyone has time to go sound hunting.
    /// </summary>
    private static AudioClip BuildPlaceholderStep()
    {
        const int sampleRate = 44100;
        const float duration = 0.13f;

        int length = Mathf.RoundToInt(sampleRate * duration);
        float[] data = new float[length];

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)length;
            float envelope = Mathf.Exp(-t * 17f);
            float heel = Mathf.Sin(2f * Mathf.PI * 95f * i / sampleRate);
            float scuff = Random.value * 2f - 1f;

            data[i] = (heel * 0.75f + scuff * 0.25f) * envelope * 0.8f;
        }

        AudioClip clip = AudioClip.Create("FootstepPlaceholder", length, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
