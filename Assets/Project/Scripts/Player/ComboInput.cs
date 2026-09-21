using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public enum Arrow { Up, Down, Left, Right }

// Stratagem-style arrow combo shown on the phone screen.
// Attach to the Phone object (same object as PhoneController).
public class ComboInput : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] PhoneController phone;       // auto-filled if on the same object
    [SerializeField] RectTransform arrowRow;      // PhoneCanvas/ArrowRow
    [SerializeField] Image arrowTemplate;         // ArrowRow/ArrowTemplate (disabled at runtime)
    [SerializeField] TMP_Text statusText;         // PhoneCanvas/StatusText

    [Header("Layout (used when ArrowRow has NO layout group component)")]
    [SerializeField] int maxPerRow = 5;
    [SerializeField] float arrowSpacing = 8f;
    [SerializeField] float rowSpacing = 12f;

    [Header("Colours")]
    [SerializeField] Color pendingColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] Color doneColor    = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] Color errorColor   = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] float errorLockout = 1.0f;   // penalty pause after a wrong arrow (phone stays out = risk)

    [Header("Status messages")]
    [SerializeField] string idleMessage     = "ENTER CODE";
    [SerializeField] string errorMessage    = "INCORRECT CODE";
    [SerializeField] string completeMessage = "UNLOCKED";
    [SerializeField] Color  idleTextColor   = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] bool   showCountdown   = true;   // "INCORRECT CODE\nTRY AGAIN IN 0.8s"

    [Header("Error shake")]
    [SerializeField] float shakeAmount = 18f;     // canvas units
    [SerializeField] float shakeTime   = 0.3f;

    [Header("Testing (turn autoStart off once MessageQueue drives this)")]
    [SerializeField] bool autoStartOnShow = false;
    [SerializeField] int testLength = 6;
    [SerializeField] bool newSequenceOnHide = true;
    [SerializeField] bool logEvents = true;

    public event Action OnComboComplete;
    public event Action OnComboFailed;

    public bool IsActive => sequence.Count > 0 && !completed;

    // When false this combo ignores the keyboard (e.g. your own combo while Jack's message is open).
    public bool AcceptInput { get; set; } = true;

    // Only one combo may read the arrow keys at a time. When a combo takes input
    // (Jack's message), every other combo ignores the keyboard until it's released.
    static ComboInput inputOwner;
    static float inputBlockedUntil;          // brief grace after a hand-back, so in-flight keys don't leak
    const float ReleaseGrace = 0.3f;

    public void TakeInput() => inputOwner = this;

    public void ReleaseInput()
    {
        if (inputOwner != this) return;
        inputOwner = null;
        inputBlockedUntil = Time.unscaledTime + ReleaseGrace;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOwner() { inputOwner = null; inputBlockedUntil = 0f; }
    public bool IsComplete => completed;

    readonly List<Arrow> sequence = new List<Arrow>();
    readonly List<Image> icons = new List<Image>();
    int progress;
    bool completed;
    float errorTimer;
    Vector2 rowBasePos;

    void Awake()
    {
        if (phone == null) phone = GetComponentInParent<PhoneController>();
        if (arrowTemplate != null) arrowTemplate.gameObject.SetActive(false);
        if (arrowRow != null) rowBasePos = arrowRow.anchoredPosition;
        SetStatus("", idleTextColor);
    }

    void OnEnable()
    {
        GameEvents.OnPhoneShown  += HandleShown;
        GameEvents.OnPhoneHidden += HandleHidden;
    }

    void OnDisable()
    {
        ReleaseInput();
        GameEvents.OnPhoneShown  -= HandleShown;
        GameEvents.OnPhoneHidden -= HandleHidden;
    }

    // ---------- Public API (MessageQueue will call these) ----------

    public void StartCombo(int length)
    {
        sequence.Clear();
        for (int i = 0; i < length; i++)
            sequence.Add((Arrow)UnityEngine.Random.Range(0, 4));
        progress = 0;
        completed = false;
        errorTimer = 0f;
        ResetShake();
        Rebuild();
        SetStatus(idleMessage, idleTextColor);
    }

    public void Clear()
    {
        sequence.Clear();
        progress = 0;
        completed = false;
        errorTimer = 0f;
        ResetShake();
        Rebuild();
        SetStatus("", idleTextColor);
    }

    // Show a message in this combo's status line (e.g. "COMPLETE").
    public void ShowMessage(string msg, bool success = true) =>
        SetStatus(msg, success ? doneColor : errorColor);

    // ---------- Phone events ----------

    void HandleShown()
    {
        if (autoStartOnShow && (sequence.Count == 0 || completed))
            StartCombo(testLength);
    }

    void HandleHidden()
    {
        if (completed || sequence.Count == 0) return;

        if (newSequenceOnHide) StartCombo(sequence.Count);   // hiding = start over with a new pattern
        else { progress = 0; Refresh(); }
    }

    // ---------- Input ----------

    void Update()
    {
        if (errorTimer > 0f)
        {
            errorTimer -= Time.deltaTime;
            UpdateShake();
            if (showCountdown && errorTimer > 0f)
                SetStatus($"{errorMessage}\n<size=70%>TRY AGAIN IN {errorTimer:0.0}s</size>", errorColor);

            if (errorTimer <= 0f)
            {
                ResetShake();
                Refresh();
                SetStatus(idleMessage, idleTextColor);
            }
            return;   // input ignored during lockout
        }

        if (inputOwner != null && inputOwner != this) return;   // another combo has the keyboard
        if (inputOwner == null && Time.unscaledTime < inputBlockedUntil) return;   // just handed back
        if (!AcceptInput || !IsActive || phone == null || !phone.IsReady) return;

        Arrow? pressed = ReadArrow();
        if (pressed == null) return;

        if (pressed.Value == sequence[progress])
        {
            progress++;
            Refresh();
            if (progress >= sequence.Count)
            {
                completed = true;
                SetStatus(completeMessage, doneColor);
                if (logEvents) Debug.Log("[Combo] COMPLETE");
                OnComboComplete?.Invoke();
            }
        }
        else
        {
            progress = 0;
            errorTimer = errorLockout;
            foreach (var icon in icons) icon.color = errorColor;
            SetStatus(errorMessage, errorColor);
            if (logEvents) Debug.Log("[Combo] WRONG - reset");
            OnComboFailed?.Invoke();
        }
    }

    static Arrow? ReadArrow()
    {
        var kb = Keyboard.current;
        if (kb == null) return null;
        if (kb.upArrowKey.wasPressedThisFrame    || kb.wKey.wasPressedThisFrame) return Arrow.Up;
        if (kb.downArrowKey.wasPressedThisFrame  || kb.sKey.wasPressedThisFrame) return Arrow.Down;
        if (kb.leftArrowKey.wasPressedThisFrame  || kb.aKey.wasPressedThisFrame) return Arrow.Left;
        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) return Arrow.Right;
        return null;
    }

    // ---------- Display ----------

    void Rebuild()
    {
        foreach (var icon in icons) if (icon != null) Destroy(icon.gameObject);
        icons.Clear();
        if (arrowRow == null || arrowTemplate == null) return;

        foreach (var a in sequence)
        {
            Image img = Instantiate(arrowTemplate, arrowRow);
            img.gameObject.SetActive(true);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Angle(a));
            icons.Add(img);
        }

        // No layout group on ArrowRow -> place arrows ourselves, every row centred.
        if (arrowRow.GetComponent<LayoutGroup>() == null) LayoutCentredRows();
        Refresh();
    }

    void LayoutCentredRows()
    {
        int n = icons.Count;
        if (n == 0) return;

        int perRow = Mathf.Max(1, maxPerRow);
        int rows = (n + perRow - 1) / perRow;
        Vector2 size = arrowTemplate.rectTransform.sizeDelta;
        float stepX = size.x + arrowSpacing;
        float stepY = size.y + rowSpacing;

        for (int i = 0; i < n; i++)
        {
            int r = i / perRow;
            int c = i % perRow;
            int inThisRow = Mathf.Min(perRow, n - r * perRow);

            float x = (c - (inThisRow - 1) * 0.5f) * stepX;
            float y = ((rows - 1) * 0.5f - r) * stepY;

            RectTransform rt = icons[i].rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(x, y);
        }
    }

    void Refresh()
    {
        for (int i = 0; i < icons.Count; i++)
            icons[i].color = i < progress ? doneColor : pendingColor;
    }

    void SetStatus(string msg, Color c)
    {
        if (statusText == null) return;
        statusText.text = msg;
        statusText.color = c;
    }

    void UpdateShake()
    {
        if (arrowRow == null) return;
        float elapsed = errorLockout - errorTimer;
        if (elapsed > shakeTime) { ResetShake(); return; }
        float fade = 1f - elapsed / shakeTime;
        float x = Mathf.Sin(elapsed * 60f) * shakeAmount * fade;
        arrowRow.anchoredPosition = rowBasePos + new Vector2(x, 0f);
    }

    void ResetShake()
    {
        if (arrowRow != null) arrowRow.anchoredPosition = rowBasePos;
    }

    static float Angle(Arrow a) => a switch
    {
        Arrow.Up    => 0f,
        Arrow.Left  => 90f,
        Arrow.Down  => 180f,
        Arrow.Right => -90f,
        _ => 0f
    };
}
