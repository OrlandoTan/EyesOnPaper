using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// The bubble answer sheet on your desk. Builds itself at runtime from ExamData.
// Look at a bubble (centre-screen dot) and left-click to mark it. Click again to erase.
// Put this on an empty GameObject lying flat on the desk: rotation (90, 0, 0).
public class AnswerSheet : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] ExamData exam;
    [SerializeField] Camera cam;                 // auto: camera inside PlayerRig
    [SerializeField] PhoneController phone;      // auto: phone inside PlayerRig

    [Header("Layout (canvas units: 10000 = 1 metre)")]
    [SerializeField] Vector2 sheetSize = new Vector2(2100f, 2970f);   // A4
    [SerializeField] float bubbleSize = 150f;
    [SerializeField] float columnGap = 260f;
    [SerializeField] float rowGap = 220f;
    [SerializeField] float headerGap = 90f;      // space between the NAME line and row 1
    [SerializeField] string title = "ANSWER SHEET";

    [Header("Colours")]
    [SerializeField] Color ink       = new Color(0.18f, 0.18f, 0.22f, 1f);
    [SerializeField] Color hoverInk  = new Color(0.15f, 0.45f, 1f, 1f);
    [SerializeField] Color pencil    = new Color(0.12f, 0.12f, 0.14f, 1f);

    [Header("Reticle")]
    [SerializeField] bool showReticle = true;
    [SerializeField] Color reticleColor = new Color(0f, 0f, 0f, 0.9f);
    [SerializeField] bool reticleOutline = true;   // faint light edge so it's still visible on dark surfaces
    [SerializeField] bool logEvents = true;

    [Header("Hand in")]
    [SerializeField] float handInHoldTime = 1f;  // hold left-click this long on HAND IN

    public event Action<int, int> OnMarked;      // (question, choice 0-3, or -1 = erased)
    public int[] Marks { get; private set; }     // -1 = blank
    public float[] MarkTimes { get; private set; }   // Time.time of the last mark per question
    public ExamData Exam => exam;
    public int HoveredRow => hovered != null ? hovered.q : -1;     // -1 = not aiming at a row
    public bool IsLocked => locked;

    // Pencil note in the margin next to a row (the off-phone cheat writes here).
    public void SetNote(int q, string text)
    {
        if (notes != null && q >= 0 && q < notes.Length && notes[q] != null) notes[q].text = text;
    }
    public int MarkedCount { get { int n = 0; foreach (int m in Marks) if (m >= 0) n++; return n; } }

    class Bubble
    {
        public int q, c;
        public RectTransform rt;
        public Image ring, fill;
        public TMP_Text letter;
    }

    readonly List<Bubble> bubbles = new List<Bubble>();
    Bubble hovered;
    TMP_Text[] notes;
    RectTransform handInBox, handInFill;
    TMP_Text handInLabel;
    Image handInBorder;
    bool handInHovered;
    float handInProgress;
    bool handedIn;
    Image reticle;
    bool phoneOut, locked;
    static Sprite ringSprite, discSprite;

    void Awake()
    {
        if (exam == null) { Debug.LogError("[AnswerSheet] No ExamData assigned."); enabled = false; return; }
        if (cam == null) cam = transform.root.GetComponentInChildren<Camera>();
        if (phone == null) phone = transform.root.GetComponentInChildren<PhoneController>();

        Marks = new int[exam.Count];
        MarkTimes = new float[exam.Count];
        for (int i = 0; i < Marks.Length; i++) Marks[i] = -1;

        // "Answered" now means "something is written on the sheet".
        // Your Notes cheat goes back to any question you left blank.
        CheatLog.IsAnswered = q => q >= 0 && q < Marks.Length && Marks[q] >= 0;

        if (ringSprite == null) ringSprite = MakeCircle(128, 0.14f, false);
        if (discSprite == null) discSprite = MakeCircle(128, 1f, true);

        Build();
        if (showReticle) BuildReticle();
    }

    void OnEnable()
    {
        GameEvents.OnPhoneShown  += PhoneShown;
        GameEvents.OnPhoneHidden += PhoneHidden;
        GameEvents.OnCaught      += Lock;
        GameEvents.OnExamEnded   += Lock;
    }

    void OnDisable()
    {
        GameEvents.OnPhoneShown  -= PhoneShown;
        GameEvents.OnPhoneHidden -= PhoneHidden;
        GameEvents.OnCaught      -= Lock;
        GameEvents.OnExamEnded   -= Lock;
    }

    void PhoneShown()  { phoneOut = true;  if (reticle) reticle.enabled = false; }
    void PhoneHidden() { phoneOut = false; if (reticle) reticle.enabled = true; }
    void Lock()        { locked = true; SetHover(null); SetHandInHover(false); if (reticle) reticle.enabled = false; }

    void Update()
    {
        if (locked || phoneOut || cam == null) { SetHover(null); SetHandInHover(false); return; }

        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        UpdateHandIn(centre);
        if (handInHovered) { SetHover(null); return; }
        Bubble hit = null;
        foreach (var b in bubbles)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(b.rt, centre, cam)) { hit = b; break; }
        }
        SetHover(hit);

        if (hit != null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            Toggle(hit.q, hit.c);
    }

    // ---------- Marking ----------

    public void Toggle(int q, int c)
    {
        Marks[q] = Marks[q] == c ? -1 : c;     // click the same bubble again to erase
        MarkTimes[q] = Time.time;
        RefreshRow(q);
        RefreshHandInLabel();
        if (logEvents) Debug.Log(Marks[q] < 0 ? $"[Sheet] Q{q + 1} erased" : $"[Sheet] Q{q + 1} = {ExamData.Letter(c)}");
        OnMarked?.Invoke(q, Marks[q]);
    }

    void RefreshRow(int q)
    {
        foreach (var b in bubbles)
        {
            if (b.q != q) continue;
            bool filled = Marks[q] == b.c;
            b.fill.enabled = filled;
            b.letter.enabled = !filled;
        }
    }

    void SetHover(Bubble b)
    {
        if (hovered == b) return;
        if (hovered != null) { hovered.ring.color = ink; hovered.letter.color = ink; }
        hovered = b;
        if (hovered != null) { hovered.ring.color = hoverInk; hovered.letter.color = hoverInk; }

        UpdateReticle();
    }

    void UpdateReticle()
    {
        if (reticle == null) return;
        bool any = hovered != null || handInHovered;
        reticle.color = any ? hoverInk : reticleColor;
        reticle.rectTransform.sizeDelta = Vector2.one * (any ? 12f : 7f);
    }

    // ---------- Hand in ----------

    void UpdateHandIn(Vector2 centre)
    {
        if (handInBox == null || handedIn) return;

        bool over = RectTransformUtility.RectangleContainsScreenPoint(handInBox, centre, cam);
        SetHandInHover(over);

        bool held = over && Mouse.current != null && Mouse.current.leftButton.isPressed;
        handInProgress = held ? handInProgress + Time.deltaTime / Mathf.Max(0.05f, handInHoldTime) : 0f;
        handInFill.sizeDelta = new Vector2(handInBox.sizeDelta.x * Mathf.Clamp01(handInProgress), handInBox.sizeDelta.y);

        if (handInProgress >= 1f) HandIn();
    }

    void HandIn()
    {
        handedIn = true;
        if (logEvents) Debug.Log($"[Sheet] HANDED IN with {MarkedCount}/{Marks.Length} marked");
        if (GameManager.Instance != null) GameManager.Instance.Submit();
        else GameEvents.ExamEnded();          // no GameManager in this scene (e.g. Test_Player)
    }

    void SetHandInHover(bool on)
    {
        if (handInHovered == on) return;
        handInHovered = on;
        if (!on) handInProgress = 0f;
        if (handInFill != null && !on) handInFill.sizeDelta = new Vector2(0f, handInBox.sizeDelta.y);
        if (handInBorder != null) handInBorder.color = on ? hoverInk : ink;
        if (handInLabel != null) handInLabel.color = on ? hoverInk : ink;
        UpdateReticle();
    }

    void RefreshHandInLabel()
    {
        if (handInLabel != null)
            handInLabel.text = $"HAND IN  <size=70%>({MarkedCount}/{Marks.Length} marked, hold click)</size>";
    }

    void BuildHandIn(RectTransform root, float y)
    {
        var size = new Vector2(1500f, 150f);

        var borderGo = new GameObject("HandIn", typeof(RectTransform), typeof(Image));
        handInBox = (RectTransform)borderGo.transform;
        handInBox.SetParent(root, false);
        handInBox.sizeDelta = size;
        handInBox.anchoredPosition = new Vector2(0f, y);
        handInBorder = borderGo.GetComponent<Image>();
        handInBorder.color = ink;

        var innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image));
        var inner = (RectTransform)innerGo.transform;
        inner.SetParent(handInBox, false);
        inner.sizeDelta = size - Vector2.one * 16f;
        innerGo.GetComponent<Image>().color = Color.white;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        handInFill = (RectTransform)fillGo.transform;
        handInFill.SetParent(handInBox, false);
        handInFill.anchorMin = handInFill.anchorMax = new Vector2(0f, 0.5f);
        handInFill.pivot = new Vector2(0f, 0.5f);
        handInFill.anchoredPosition = Vector2.zero;
        handInFill.sizeDelta = new Vector2(0f, size.y);
        fillGo.GetComponent<Image>().color = new Color(hoverInk.r, hoverInk.g, hoverInk.b, 0.35f);

        handInLabel = MakeText(handInBox, "", Vector2.zero, size, 64f, FontStyles.Bold);
        RefreshHandInLabel();
    }

    // ---------- Building ----------

    void Build()
    {
        var canvasGo = new GameObject("SheetCanvas", typeof(RectTransform), typeof(Canvas));
        var root = (RectTransform)canvasGo.transform;
        root.SetParent(transform, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * 0.0001f;
        root.sizeDelta = sheetSize;
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        // Header block, measured from the top edge of the sheet
        float top = sheetSize.y * 0.5f;
        float titleY = top - 200f;
        float nameY = titleY - 170f;
        const float nameHeight = 100f;

        MakeText(root, title, new Vector2(0f, titleY), new Vector2(sheetSize.x - 200f, 140f), 90f, FontStyles.Bold);
        MakeText(root, "NAME: ________________", new Vector2(0f, nameY), new Vector2(sheetSize.x - 200f, nameHeight), 56f, FontStyles.Normal);

        // Row 1 starts below the NAME line, never overlapping it
        float firstRowY = nameY - nameHeight * 0.5f - headerGap - bubbleSize * 0.5f;

        float xOffset = 150f;     // leave room on the left for question numbers
        notes = new TMP_Text[exam.Count];
        for (int q = 0; q < exam.Count; q++)
        {
            float y = firstRowY - q * rowGap;
            MakeText(root, $"{q + 1}.", new Vector2(-1.5f * columnGap - 260f + xOffset, y), new Vector2(200f, bubbleSize), 72f, FontStyles.Bold);

            for (int c = 0; c < 4; c++)
            {
                float x = (c - 1.5f) * columnGap + xOffset;
                bubbles.Add(MakeBubble(root, q, c, new Vector2(x, y)));
            }

            // Margin note (pencil), right of the D bubble
            var note = MakeText(root, "", new Vector2(2.5f * columnGap + xOffset + 30f, y), new Vector2(260f, bubbleSize), 80f, FontStyles.Italic);
            note.color = new Color(pencil.r, pencil.g, pencil.b, 0.75f);
            notes[q] = note;
        }

        float lastRowY = firstRowY - (exam.Count - 1) * rowGap;
        BuildHandIn(root, lastRowY - bubbleSize * 0.5f - 160f);
    }

    Bubble MakeBubble(RectTransform parent, int q, int c, Vector2 pos)
    {
        var go = new GameObject($"Q{q + 1}{ExamData.Letter(c)}", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = Vector2.one * bubbleSize;
        rt.anchoredPosition = pos;
        var ring = go.GetComponent<Image>();
        ring.sprite = ringSprite;
        ring.color = ink;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        var frt = (RectTransform)fillGo.transform;
        frt.SetParent(rt, false);
        frt.sizeDelta = Vector2.one * bubbleSize * 0.8f;
        var fill = fillGo.GetComponent<Image>();
        fill.sprite = discSprite;
        fill.color = pencil;
        fill.enabled = false;

        var letter = MakeText(rt, ExamData.Letter(c), Vector2.zero, Vector2.one * bubbleSize, 72f, FontStyles.Bold);

        return new Bubble { q = q, c = c, rt = rt, ring = ring, fill = fill, letter = letter };
    }

    TMP_Text MakeText(RectTransform parent, string text, Vector2 pos, Vector2 size, float fontSize, FontStyles style)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = ink;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }

    void BuildReticle()
    {
        var go = new GameObject("Reticle", typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var dot = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)dot.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.one * 7f;
        reticle = dot.GetComponent<Image>();
        reticle.sprite = discSprite;
        reticle.color = reticleColor;
        reticle.raycastTarget = false;
        if (reticleOutline)
        {
            var outline = dot.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.45f);
            outline.effectDistance = new Vector2(1f, -1f);
        }
    }

    // Anti-aliased circle / ring sprite, generated so there's no image to import.
    static Sprite MakeCircle(int size, float thickness, bool filled)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f - 1f;
        float inner = r * (1f - thickness);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
            float outerA = Mathf.Clamp01(r - d + 0.5f);
            float innerA = filled ? 1f : Mathf.Clamp01(d - inner + 0.5f);
            byte a = (byte)(255f * outerA * innerA);
            px[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
