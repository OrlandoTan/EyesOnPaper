using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

// Jack's texts: a notification banner at the top of the phone.
// Buzz -> "NEW MESSAGE" banner for a few seconds. Your Notes combo keeps working underneath.
// Press [E] (with the phone out) to focus the message: arrows now go to Jack's short combo
// and your Notes progress is paused, not lost. Finish in time -> answer flashes briefly.
// Ignore it, or run out of time -> it disappears.
// Attach to PhoneCanvas/JackPanel (which also has its own ComboInput + CanvasGroup).
public class JackMessenger : MonoBehaviour
{
    enum State { Idle, Banner, Open, Reveal, Expired }

    [Header("Refs")]
    [SerializeField] ExamData exam;
    [SerializeField] SelfCheat selfCheat;        // Phone
    [SerializeField] ComboInput jackCombo;       // auto-filled: ComboInput on this object
    [SerializeField] CanvasGroup panel;          // auto-filled: CanvasGroup on this object
    [SerializeField] TMP_Text headerText;        // JackPanel/JackHeader
    [SerializeField] TMP_Text revealText;        // JackPanel/JackReveal
    [SerializeField] AudioSource buzzAudio;      // optional: leave empty for a generated vibration sound
    [SerializeField, Range(0f, 1f)] float buzzVolume = 0.7f;
    [SerializeField] PhoneController phone;      // auto-filled from parents

    [Header("Input")]
    [SerializeField] Key openKey = Key.E;        // focus / unfocus the message

    [Header("Timing")]
    [SerializeField] float firstMessageDelay = 8f;
    [SerializeField] Vector2 intervalRange = new Vector2(15f, 25f);
    [SerializeField] float messageLifetime = 5f;     // banner + combo, total, from the buzz
    [SerializeField] float revealSingle = 1f;
    [SerializeField] float revealBundle = 1.5f;
    [SerializeField] float expiredShowTime = 0.8f;   // "EXPIRED" stays this long after time runs out
    [SerializeField] Color expiredColor = new Color(1f, 0.35f, 0.35f, 1f);

    [Header("Messages")]
    [SerializeField] int singleComboLength = 3;
    [SerializeField] int bundleComboLength = 3;
    [SerializeField] int bundleSize = 3;
    [SerializeField, Range(0f, 1f)] float bundleChance = 0.33f;
    [SerializeField] string senderName = "JACK";
    [SerializeField] bool logEvents = true;

    State state = State.Idle;
    float timer;
    float nextArrival;
    bool running = true;
    int batchCounter;
    readonly List<int> message = new List<int>();

    bool IsBundle => message.Count > 1;

    void Awake()
    {
        if (phone == null) phone = GetComponentInParent<PhoneController>();
        if (jackCombo == null) jackCombo = GetComponent<ComboInput>();
        if (panel == null) panel = GetComponent<CanvasGroup>();
        if (selfCheat == null) selfCheat = GetComponentInParent<SelfCheat>();

        // No sound assigned: make a placeholder phone vibration so the buzz is never silent.
        // The buzz raises suspicion, so the player must always hear it.
        if (buzzAudio == null)
        {
            buzzAudio = gameObject.AddComponent<AudioSource>();
            buzzAudio.playOnAwake = false;
            buzzAudio.spatialBlend = 0f;
            buzzAudio.clip = BuildVibration();
        }
        buzzAudio.volume = buzzVolume;
    }

    static AudioClip BuildVibration()
    {
        const int rate = 44100;
        const float pulse = 0.22f, gap = 0.12f;
        int total = Mathf.CeilToInt(rate * (pulse * 2f + gap));
        var data = new float[total];
        var rng = new System.Random(7);
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)rate;
            bool on = t < pulse || (t > pulse + gap && t < pulse * 2f + gap);
            if (!on) continue;
            float local = t < pulse ? t : t - pulse - gap;
            float env = Mathf.Clamp01(local / 0.01f) * Mathf.Clamp01((pulse - local) / 0.02f);
            float motor = Mathf.Sin(2f * Mathf.PI * 165f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 330f * t) * 0.2f;
            float rattle = ((float)rng.NextDouble() * 2f - 1f) * 0.15f;
            data[i] = (motor + rattle) * env * 0.8f;
        }
        var clip = AudioClip.Create("PhoneBuzz", total, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void OnEnable()
    {
        GameEvents.OnPhoneHidden += HandleHidden;
        GameEvents.OnCaught      += Stop;
        GameEvents.OnExamEnded   += Stop;
        if (jackCombo != null) jackCombo.OnComboComplete += HandleComplete;
    }

    void OnDisable()
    {
        GameEvents.OnPhoneHidden -= HandleHidden;
        GameEvents.OnCaught      -= Stop;
        GameEvents.OnExamEnded   -= Stop;
        if (jackCombo != null) jackCombo.OnComboComplete -= HandleComplete;
    }

    void Start()
    {
        if (exam == null) { Debug.LogError("[Jack] No ExamData assigned."); enabled = false; return; }
        if (jackCombo == null)
        {
            Debug.LogError("[Jack] JackPanel needs its own ComboInput component (Arrow Row = JackArrowRow). Jack disabled.");
            enabled = false;
            return;
        }
        nextArrival = Time.time + firstMessageDelay;
        jackCombo.AcceptInput = false;
        jackCombo.Clear();
        SetPanel(false);
    }

    void Update()
    {
        switch (state)
        {
            case State.Idle:
                if (running && Time.time >= nextArrival) Arrive();
                break;

            case State.Banner:
            case State.Open:
                timer -= Time.deltaTime;
                if (timer <= 0f) { Miss("timed out"); break; }

                if (OpenKeyPressed() && phone != null && phone.IsReady)
                {
                    if (state == State.Banner) Focus();
                    else Unfocus();          // changed your mind: back to Notes, banner stays
                }
                RefreshHeader();
                break;

            case State.Reveal:
                // Keeps counting even if you hide the phone. Look away = lose it.
                timer -= Time.deltaTime;
                if (timer <= 0f) Close();
                break;

            case State.Expired:
                timer -= Time.deltaTime;
                if (timer <= 0f) Close();
                break;
        }
    }

    bool OpenKeyPressed()
    {
        var kb = Keyboard.current;
        return kb != null && kb[openKey].wasPressedThisFrame;
    }

    // ---------- Lifecycle ----------

    void Arrive()
    {
        var pool = new List<int>();
        for (int q = 0; q < exam.Count; q++)
            if (!CheatLog.IsAnswered(q)) pool.Add(q);

        if (pool.Count == 0) { running = false; return; }   // nothing left to help with

        bool bundle = pool.Count >= 2 && Random.value < bundleChance;
        int n = bundle ? Mathf.Min(bundleSize, pool.Count) : 1;

        message.Clear();
        for (int i = 0; i < n; i++)
        {
            int pick = Random.Range(0, pool.Count);
            message.Add(pool[pick]);        // deliberately NOT sorted: harder to remember
            pool.RemoveAt(pick);
        }

        state = State.Banner;
        timer = messageLifetime;

        GameEvents.Buzz();
        if (buzzAudio != null) buzzAudio.Play();
        if (logEvents) Debug.Log($"[Jack] BUZZ - {(IsBundle ? $"bundle of {n}" : "single")}, {messageLifetime}s");

        jackCombo.AcceptInput = false;
        jackCombo.Clear();
        SetReveal("");
        RefreshHeader();
        SetPanel(true);
    }

    void Focus()
    {
        state = State.Open;
        if (selfCheat != null) selfCheat.SetPaused(true);
        jackCombo.AcceptInput = true;
        jackCombo.TakeInput();               // Notes combo ignores the keyboard now
        if (logEvents) Debug.Log("[Jack] opened - Notes paused");
        if (!jackCombo.IsActive) jackCombo.StartCombo(IsBundle ? bundleComboLength : singleComboLength);
        RefreshHeader();
    }

    void Unfocus()
    {
        state = State.Banner;
        jackCombo.AcceptInput = false;
        jackCombo.ReleaseInput();
        if (selfCheat != null) selfCheat.SetPaused(false);
        RefreshHeader();
    }

    void HandleComplete()
    {
        if (state != State.Open) return;

        int batch = ++batchCounter;
        var lines = new List<string>();
        foreach (int q in message)
        {
            int a = exam.questions[q].correctIndex;
            CheatLog.Reveal(q, a, CheatSource.Jack, batch);
            lines.Add($"Q{q + 1}: {ExamData.Letter(a)}");
        }
        if (logEvents) Debug.Log($"[Jack] REVEALED {string.Join("  ", lines)}");

        jackCombo.AcceptInput = false;
        jackCombo.ReleaseInput();
        jackCombo.Clear();
        if (selfCheat != null) selfCheat.SetPaused(false);   // Notes is usable again right away

        SetReveal(IsBundle ? string.Join("   ", lines) : lines[0]);
        if (headerText != null) headerText.text = senderName;
        timer = IsBundle ? revealBundle : revealSingle;
        state = State.Reveal;
    }

    void HandleHidden()
    {
        // Phone away: the message is still there (if time remains) but unfocused.
        if (state == State.Open)
        {
            state = State.Banner;
            jackCombo.AcceptInput = false;
            jackCombo.ReleaseInput();
            jackCombo.Clear();                       // start the combo again if you reopen
            if (selfCheat != null) selfCheat.SetPaused(false);
        }
    }

    void Miss(string reason)
    {
        CheatLog.MissJack(message);
        if (logEvents) Debug.Log($"[Jack] MISSED ({reason})");
        ShowExpired();
    }

    // Time ran out: hand the keyboard back straight away, but leave a brief "EXPIRED" banner
    // so the player understands why the message vanished.
    void ShowExpired()
    {
        bool wasFocused = state == State.Open;
        state = State.Expired;
        timer = expiredShowTime;

        jackCombo.AcceptInput = false;
        jackCombo.ReleaseInput();
        jackCombo.Clear();
        if (wasFocused && selfCheat != null) selfCheat.SetPaused(false);

        string hex = ColorUtility.ToHtmlStringRGB(expiredColor);
        if (headerText != null)
            headerText.text = $"<color=#{hex}>EXPIRED</color>\n<size=60%>{senderName}'s message is gone</size>";
        SetReveal("");
    }

    void Close()
    {
        bool wasFocused = state == State.Open;
        state = State.Idle;
        message.Clear();
        jackCombo.AcceptInput = false;
        jackCombo.ReleaseInput();
        jackCombo.Clear();
        SetReveal("");
        SetPanel(false);
        if (wasFocused && selfCheat != null) selfCheat.SetPaused(false);
        nextArrival = Time.time + Random.Range(intervalRange.x, intervalRange.y);
    }

    void Stop()
    {
        running = false;
        if (state != State.Idle) Close();
    }

    // ---------- Display ----------

    void RefreshHeader()
    {
        if (headerText == null) return;
        int secs = Mathf.CeilToInt(Mathf.Max(0f, timer));
        string label = IsBundle ? $"{message.Count} answers" : "1 answer";

        headerText.text = state == State.Open
            ? $"{senderName}  <size=70%>{label} · {secs}s</size>"
            : $"NEW MESSAGE · {senderName}\n<size=65%>[{openKey}] open · {secs}s</size>";
    }

    void SetPanel(bool on)
    {
        if (panel == null) return;
        panel.alpha = on ? 1f : 0f;
        panel.blocksRaycasts = false;
        panel.interactable = false;
    }

    void SetReveal(string msg)
    {
        if (revealText != null) revealText.text = msg;
    }
}
