using UnityEngine;
using UnityEngine.UI;

// Heartbeat tied to the suspicion meter: silent when calm, then faster and louder as it rises.
// Also pulses a faint red vignette at the screen edges on each beat, so it still reads on
// laptop speakers or with the sound off.
// Put it anywhere (e.g. on PlayerRig). No setup needed: it finds the SuspicionMeter itself
// and generates its own sound if you don't assign one.
public class Heartbeat : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] SuspicionMeter suspicion;           // auto-found if empty

    [Header("When it starts")]
    [Tooltip("Suspicion (0-1) below which you hear nothing.")]
    [SerializeField, Range(0f, 1f)] float startAt = 0.15f;

    [Header("Response")]
    [Tooltip("How fast it speeds up when suspicion rises (per second). High = panic is instant.")]
    [SerializeField] float riseSpeed = 6f;
    [Tooltip("How fast it calms down again. Low = the adrenaline lingers.")]
    [SerializeField] float fallSpeed = 0.5f;

    [Header("Rate")]
    [SerializeField] float minBPM = 65f;                 // just above startAt
    [SerializeField] float maxBPM = 150f;                // at 100%
    [Tooltip(">1 keeps it calm longer, then races near the top.")]
    [SerializeField] float intensityCurve = 1.4f;

    [Header("Sound")]
    [SerializeField] AudioClip beatClip;                 // optional: a real "lub-dub" sample
    [SerializeField] float minVolume = 0.15f;
    [SerializeField] float maxVolume = 0.9f;
    [SerializeField] float maxPitchRise = 0.15f;         // slightly higher when panicking

    [Header("Screen pulse")]
    [SerializeField] bool vignette = true;
    [SerializeField] Color vignetteColor = new Color(0.55f, 0f, 0f, 1f);
    [SerializeField, Range(0f, 1f)] float maxVignetteAlpha = 0.45f;

    AudioSource source;
    Image vignetteImage;
    float smoothed, beatTimer, pulse;

    void Awake()
    {
        if (suspicion == null) suspicion = FindFirstObjectByType<SuspicionMeter>();

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;                        // inside your head, not in the room
        if (beatClip == null) beatClip = BuildLubDub();

        if (vignette) BuildVignette();
    }

    void OnDisable()
    {
        if (source != null) source.Stop();
        if (vignetteImage != null) vignetteImage.color = Clear();
    }

    void Update()
    {
        float target = suspicion != null ? suspicion.Value01 : 0f;
        // Fast attack, slow release: panic hits at once, calm comes back gradually.
        float speed = target > smoothed ? riseSpeed : fallSpeed;
        smoothed = Mathf.MoveTowards(smoothed, target, Time.deltaTime * speed);

        float intensity = Mathf.Pow(Mathf.InverseLerp(startAt, 1f, smoothed), intensityCurve);
        bool active = smoothed > startAt;

        if (active)
        {
            float bpm = Mathf.Lerp(minBPM, maxBPM, intensity);
            beatTimer -= Time.deltaTime;
            if (beatTimer <= 0f)
            {
                beatTimer = 60f / bpm;
                source.pitch = 1f + maxPitchRise * intensity;
                source.PlayOneShot(beatClip, Mathf.Lerp(minVolume, maxVolume, intensity));
                pulse = 1f;
            }
        }
        else beatTimer = 0f;

        // Vignette: flashes on the beat, decays between beats. Strength follows suspicion.
        pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 3f);
        if (vignetteImage != null)
        {
            float a = active ? maxVignetteAlpha * intensity * (0.35f + 0.65f * pulse) : 0f;
            var c = vignetteColor; c.a = a;
            vignetteImage.color = c;
        }
    }

    Color Clear() { var c = vignetteColor; c.a = 0f; return c; }

    // ---------- generated assets ----------

    // Two thumps ("lub" then softer "dub"). Harmonics up to ~240 Hz so it's audible on laptop speakers.
    static AudioClip BuildLubDub()
    {
        const int rate = 44100;
        const float length = 0.5f;
        int n = Mathf.CeilToInt(rate * length);
        var data = new float[n];

        void Thump(float start, float gain)
        {
            int s0 = Mathf.RoundToInt(start * rate);
            int len = Mathf.RoundToInt(0.14f * rate);
            for (int i = 0; i < len && s0 + i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 28f) * Mathf.Clamp01(t / 0.004f);
                float f = 58f - 18f * (t / 0.14f);                        // slight pitch drop
                float v = Mathf.Sin(2f * Mathf.PI * f * t)
                        + 0.55f * Mathf.Sin(2f * Mathf.PI * f * 2f * t)
                        + 0.30f * Mathf.Sin(2f * Mathf.PI * f * 4f * t);   // ~230 Hz: laptop-audible
                data[s0 + i] += v * env * gain;
            }
        }

        Thump(0.00f, 0.75f);   // lub
        Thump(0.20f, 0.50f);   // dub

        var clip = AudioClip.Create("Heartbeat", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void BuildVignette()
    {
        var canvasGo = new GameObject("HeartbeatVignette", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;                         // under the results screen (200)

        var imgGo = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)imgGo.transform;
        rt.SetParent(canvasGo.transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        vignetteImage = imgGo.GetComponent<Image>();
        vignetteImage.sprite = BuildVignetteSprite();
        vignetteImage.raycastTarget = false;
        vignetteImage.color = Clear();
    }

    static Sprite BuildVignetteSprite()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f;
            float dy = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;          // 0 centre, 1 corner
            float a = Mathf.Pow(Mathf.Clamp01((d - 0.45f) / 0.55f), 1.6f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
