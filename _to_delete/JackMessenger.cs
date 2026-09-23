using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

// Jack doesn't help any more - he rings at the worst possible time.
// A call covers the phone screen, so you can't see the pattern you're halfway through,
// and the ringing is NOISE: the invigilator hears it if he's near.
//   Decline [E] : gone in a moment, your combo progress survives.
//   Ignore      : it sits over the screen until it rings out, and keeps making noise.
//   Hide phone  : the phone keeps ringing in your pocket.
// Attach to PhoneCanvas/JackPanel (with its CanvasGroup). No ComboInput needed any more.
public class JackMessenger : MonoBehaviour
{
    enum State { Idle, Ringing, Declined }

    [Header("Refs")]
    [SerializeField] CanvasGroup panel;          // auto: CanvasGroup on this object
    [SerializeField] TMP_Text headerText;        // JackPanel/JackHeader
    [SerializeField] TMP_Text revealText;        // JackPanel/JackReveal (used for the hint line)
    [SerializeField] AudioSource ringAudio;      // optional: leave empty for a generated ringtone
    [SerializeField] PhoneController phone;      // auto: found in parents

    [Header("Timing")]
    [SerializeField] float firstCallDelay = 25f;
    [SerializeField] Vector2 intervalRange = new Vector2(22f, 38f);
    [SerializeField] float ringDuration = 5f;
    [SerializeField] float declineFade = 0.35f;

    [Header("Noise")]
    [Tooltip("A ringing phone is loud. Each pulse spikes suspicion if the invigilator is near.")]
    [SerializeField] bool makeNoise = true;
    [SerializeField] float noisePulseEvery = 1.2f;
    [SerializeField, Range(0f, 1f)] float ringVolume = 0.75f;

    [Header("Input")]
    [SerializeField] Key declineKey = Key.E;

    [Header("Text")]
    [SerializeField] string senderName = "JACK";
    [SerializeField] bool logEvents = true;

    State state = State.Idle;
    float timer, nextCall, noiseTimer, fade;
    bool running = true;

    public bool IsRinging => state == State.Ringing;

    void Awake()
    {
        if (panel == null) panel = GetComponent<CanvasGroup>();
        if (phone == null) phone = GetComponentInParent<PhoneController>();

        if (ringAudio == null)
        {
            ringAudio = gameObject.AddComponent<AudioSource>();
            ringAudio.playOnAwake = false;
            ringAudio.loop = true;
            ringAudio.spatialBlend = 0f;
            ringAudio.clip = BuildRingtone();
        }
        ringAudio.volume = ringVolume;
    }

    void OnEnable()
    {
        GameEvents.OnCaught    += Stop;
        GameEvents.OnExamEnded += Stop;
    }

    void OnDisable()
    {
        GameEvents.OnCaught    -= Stop;
        GameEvents.OnExamEnded -= Stop;
        if (ringAudio != null) ringAudio.Stop();
    }

    void Start()
    {
        nextCall = Time.time + firstCallDelay;
        SetPanel(0f);
    }

    void Update()
    {
        switch (state)
        {
            case State.Idle:
                if (running && Time.time >= nextCall) Ring();
                break;

            case State.Ringing:
                timer -= Time.deltaTime;
                Noise();
                RefreshText();

                if (DeclinePressed()) { End("declined", false); break; }
                if (timer <= 0f) End("rang out", true);
                break;

            case State.Declined:
                fade -= Time.deltaTime / Mathf.Max(0.01f, declineFade);
                SetPanel(Mathf.Max(0f, fade));
                if (fade <= 0f) state = State.Idle;
                break;
        }
    }

    bool DeclinePressed()
    {
        var kb = Keyboard.current;
        return kb != null && kb[declineKey].wasPressedThisFrame;
    }

    // ---------- lifecycle ----------

    void Ring()
    {
        state = State.Ringing;
        timer = ringDuration;
        noiseTimer = 0f;
        fade = 1f;
        SetPanel(1f);
        if (ringAudio != null) ringAudio.Play();
        if (logEvents) Debug.Log($"[Jack] CALLING - {ringDuration}s of ringing");
    }

    void Noise()
    {
        if (!makeNoise) return;
        noiseTimer -= Time.deltaTime;
        if (noiseTimer > 0f) return;
        noiseTimer = noisePulseEvery;
        GameEvents.Buzz();                     // the invigilator can hear it
    }

    void End(string why, bool ignored)
    {
        if (ringAudio != null) ringAudio.Stop();
        state = State.Declined;
        fade = 1f;
        nextCall = Time.time + Random.Range(intervalRange.x, intervalRange.y);
        if (logEvents) Debug.Log($"[Jack] call {why}");
    }

    void Stop()
    {
        running = false;
        if (ringAudio != null) ringAudio.Stop();
        state = State.Idle;
        SetPanel(0f);
    }

    // ---------- display ----------

    void RefreshText()
    {
        if (headerText != null)
            headerText.text = $"<size=70%>incoming call</size>\n{senderName}";
        if (revealText != null)
            revealText.text = $"<size=55%>[{declineKey}] decline   ·   {Mathf.CeilToInt(timer)}s</size>";
    }

    void SetPanel(float alpha)
    {
        if (panel == null) return;
        panel.alpha = alpha;
        panel.blocksRaycasts = false;
        panel.interactable = false;
    }

    // A two-tone ringtone with a ring-ring-pause pattern, pitched to carry on laptop speakers.
    static AudioClip BuildRingtone()
    {
        const int rate = 44100;
        const float total = 2.6f;               // loops
        int n = Mathf.CeilToInt(rate * total);
        var data = new float[n];

        void Burst(float start, float length)
        {
            int s0 = Mathf.RoundToInt(start * rate);
            int len = Mathf.RoundToInt(length * rate);
            for (int i = 0; i < len && s0 + i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01((length - t) / 0.03f);
                float warble = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 20f * t);
                float v = Mathf.Sin(2f * Mathf.PI * 660f * t) * 0.6f
                        + Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.4f;
                data[s0 + i] += v * env * (0.6f + 0.4f * warble) * 0.5f;
            }
        }

        Burst(0.00f, 0.40f);
        Burst(0.55f, 0.40f);                    // ring-ring
        // then silence until the loop restarts

        var clip = AudioClip.Create("JackRing", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
