using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public enum Arrow { Up, Down, Left, Right }

// Stratagem-style arrow combo shown on the phone screen.
// Attach to the Phone object (same object as PhoneController).
public class ComboInput : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] PhoneController phone;       // auto-filled if on the same object
    [SerializeField] RectTransform arrowRow;      // PhoneCanvas/ArrowRow
    [SerializeField] Image arrowTemplate;         // ArrowRow/ArrowTemplate (disabled at runtime)

    [Header("Colours")]
    [SerializeField] Color pendingColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] Color doneColor    = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] Color errorColor   = new Color(1f, 0.25f, 0.25f, 1f);
    [SerializeField] float errorLockout = 0.25f;  // brief pause after a wrong arrow

    [Header("Testing (turn autoStart off once MessageQueue drives this)")]
    [SerializeField] bool autoStartOnShow = true;
    [SerializeField] int testLength = 6;
    [SerializeField] bool newSequenceOnHide = true;
    [SerializeField] bool logEvents = true;

    public event Action OnComboComplete;
    public event Action OnComboFailed;

    public bool IsActive => sequence.Count > 0 && !completed;
    public bool IsComplete => completed;

    readonly List<Arrow> sequence = new List<Arrow>();
    readonly List<Image> icons = new List<Image>();
    int progress;
    bool completed;
    float errorTimer;

    void Awake()
    {
        if (phone == null) phone = GetComponent<PhoneController>();
        if (arrowTemplate != null) arrowTemplate.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        GameEvents.OnPhoneShown  += HandleShown;
        GameEvents.OnPhoneHidden += HandleHidden;
    }

    void OnDisable()
    {
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
        Rebuild();
    }

    public void Clear()
    {
        sequence.Clear();
        progress = 0;
        completed = false;
        Rebuild();
    }

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
            if (errorTimer <= 0f) Refresh();
            return;
        }

        if (!IsActive || phone == null || !phone.IsReady) return;

        Arrow? pressed = ReadArrow();
        if (pressed == null) return;

        if (pressed.Value == sequence[progress])
        {
            progress++;
            Refresh();
            if (progress >= sequence.Count)
            {
                completed = true;
                if (logEvents) Debug.Log("[Combo] COMPLETE");
                OnComboComplete?.Invoke();
            }
        }
        else
        {
            progress = 0;
            errorTimer = errorLockout;
            foreach (var icon in icons) icon.color = errorColor;
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
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < icons.Count; i++)
            icons[i].color = i < progress ? doneColor : pendingColor;
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
