using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Footsteps, so the player can hear where the invigilator is without turning
/// round to look - which now costs them (see SuspicionMeter's over-shoulder rule).
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
    [Tooltip("How sharply volume drops with distance. 1 is linear; higher makes closeness count for much more.")]
    [Range(1f, 5f)]
    [SerializeField] private float falloffSharpness = 2.5f;

    [Header("Muffling")]
    [Tooltip("Distant steps lose their high end, the way they do through air and furniture.")]
    [SerializeField] private bool muffleWithDistance = true;
    [Tooltip("Cutoff in Hz right next to you - effectively no filtering.")]
    [SerializeField] private float nearCutoff = 22000f;
    [Tooltip("Cutoff in Hz at maximum range. Low values sound far away.")]
    [SerializeField] private float farCutoff = 750f;

    private NavMeshAgent agent;
    private AudioSource source;
    private AudioClip generated;
    private AudioLowPassFilter lowPass;
    private Transform listener;
    private float distanceSinceStep;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        source = GetComponent<AudioSource>();

        // Positional audio is the entire point - the player has to be able to
        // tell that someone is behind them, not merely that someone exists.
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;

        // Linear rolloff keeps a walker at the far wall over half as loud as one
        // at your shoulder, which tells the player almost nothing. A curved
        // falloff makes the last few metres count for what they should.
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, BuildFalloffCurve());

        if (muffleWithDistance)
        {
            lowPass = GetComponent<AudioLowPassFilter>();
            if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.cutoffFrequency = nearCutoff;
        }

        AudioListener found = FindAnyObjectByType<AudioListener>();
        if (found != null) listener = found.transform;

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

    /// <summary>
    /// Steps from across the room arrive muffled; steps at your shoulder are
    /// sharp. Cheaper than reverb and it reads instantly.
    /// </summary>
    private void UpdateMuffle()
    {
        if (lowPass == null || listener == null) return;

        float distance = Vector3.Distance(transform.position, listener.position);
        float t = Mathf.Clamp01(Mathf.InverseLerp(minDistance, maxDistance, distance));
        lowPass.cutoffFrequency = Mathf.Lerp(nearCutoff, farCutoff, t);
    }

    /// <summary>Normalised over 0..maxDistance. (1 - t) raised to the sharpness.</summary>
    private AnimationCurve BuildFalloffCurve()
    {
        const int points = 17;
        var keys = new Keyframe[points];

        for (int i = 0; i < points; i++)
        {
            float t = i / (float)(points - 1);
            keys[i] = new Keyframe(t, Mathf.Pow(1f - t, falloffSharpness));
        }

        var curve = new AnimationCurve(keys);
        for (int i = 0; i < points; i++) curve.SmoothTangents(i, 0f);
        return curve;
    }

    private void PlayStep()
    {
        AudioClip clip = steps != null && steps.Length > 0
            ? steps[Random.Range(0, steps.Length)]
            : generated;

        if (clip == null) return;

        UpdateMuffle();
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
    /// fast decay. Not good audio - just honest placeholder audio, so the fairness
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
