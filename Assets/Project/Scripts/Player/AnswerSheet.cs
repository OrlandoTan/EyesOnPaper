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
    [SerializeField] bool logEvents = true;

    public event Action<int, int> OnMarked;      // (question, choice 0-3, or -1 = erased)
    public int[] Marks { get; private set; }     // -1 = blank
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
    Image reticle;
    bool phoneOut, locked;
    static Sprite ringSprite, discSprite;

    void Awake()
    {
        if (exam == null) { Debug.LogError("[AnswerSheet] No ExamData assigned."); enabled = false; return; }
        if (cam == null) cam = transform.root.GetComponentInChildren<Camera>();
        if (phone == null) phone = transform.root.GetComponentInChildren<PhoneController>();

        Marks = new int[exam.Count];
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
    void Lock()        { locked = true; SetHover(null); }

    void Update()
    {
        if (locked || phoneOut || cam == null) { SetHover(null); return; }

        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
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
        RefreshRow(q);
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

        if (reticle != null)
        {
            reticle.color = hovered != null ? hoverInk : new Color(1f, 1f, 1f, 0.7f);
            reticle.rectTransform.sizeDelta = Vector2.one * (hovered != null ? 12f : 7f);
        }
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
        for (int q = 0; q < exam.Count; q++)
        {
            float y = firstRowY - q * rowGap;
            MakeText(root, $"{q + 1}.", new Vector2(-1.5f * columnGap - 260f + xOffset, y), new Vector2(200f, bubbleSize), 72f, FontStyles.Bold);

            for (int c = 0; c < 4; c++)
            {
                float x = (c - 1.5f) * columnGap + xOffset;
                bubbles.Add(MakeBubble(root, q, c, new Vector2(x, y)));
            }
        }
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
        reticle.color = new Color(1f, 1f, 1f, 0.7f);
        reticle.raycastTarget = false;
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
