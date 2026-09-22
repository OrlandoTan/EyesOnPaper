using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// Off-phone cheat: aim at a row on the answer sheet and type a pattern you memorised from the phone.
// A pencil note appears in the margin with the answer. Right pattern -> right letter.
// Wrong pattern -> a wrong letter (always the same one for the same wrong input). You can't tell which.
// The only pattern that works is the LAST one you fully saw on the phone for that question.
// While typing, a small panel at the bottom of the screen shows the arrows you've entered and
// how many are left - it never says whether they're right.
// Put this on the AnswerSheet object.
public class PaperCheat : MonoBehaviour
{
    [SerializeField] AnswerSheet sheet;          // auto: same object
    [SerializeField] SelfCheat notes;            // auto: found in scene
    [SerializeField] PhoneController phone;      // auto: found in scene
    [SerializeField] bool alsoAcceptWASD = true;
    [SerializeField] bool logEvents = true;

    [Header("Input display")]
    [SerializeField] float arrowSize = 44f;
    [SerializeField] float bottomOffset = 120f;
    [SerializeField] Color typedColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] Color slotColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] Color doneColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] Color resetColor = new Color(1f, 0.35f, 0.35f, 1f);
    [SerializeField] float messageTime = 0.8f;

    int row = -1;
    readonly List<Arrow> typed = new List<Arrow>();
    readonly Dictionary<int, string> written = new Dictionary<int, string>();

    // display
    RectTransform panel, arrowRow;
    TMP_Text label;
    readonly List<Image> slots = new List<Image>();
    static Sprite arrowSprite, dotSprite;
    float messageTimer;

    void Awake()
    {
        if (sheet == null) sheet = GetComponent<AnswerSheet>();
        if (notes == null) notes = FindFirstObjectByType<SelfCheat>();
        if (phone == null) phone = FindFirstObjectByType<PhoneController>();
        BuildDisplay();
    }

    void OnDisable()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    void Update()
    {
        if (messageTimer > 0f)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer <= 0f) RefreshDisplay();
        }

        if (sheet == null || sheet.Exam == null || sheet.IsLocked) { Hide(); return; }
        if (phone != null && phone.IsOut) { Cancel(false); Hide(); return; }   // phone out: arrows belong to the phone

        int hovered = sheet.HoveredRow;
        if (hovered != row)
        {
            Cancel(true);                                              // looked away mid-pattern: start over
            row = hovered;
            if (messageTimer <= 0f) RefreshDisplay();
        }
        if (row < 0) return;

        Arrow? a = ReadArrow();
        if (a == null) return;

        messageTimer = 0f;
        typed.Add(a.Value);
        int needed = sheet.Exam.ComboLength(row);
        if (typed.Count < needed)
        {
            sheet.SetNote(row, new string('·', typed.Count));
            RefreshDisplay();
            return;
        }

        RefreshDisplay();
        Resolve(row);
        ShowMessage($"Q{row + 1}  <size=80%>written</size>", doneColor, keepArrows: true);
        typed.Clear();
    }

    // ---------- logic ----------

    void Resolve(int q)
    {
        int correct = sheet.Exam.questions[q].correctIndex;
        bool right = notes != null && notes.TryGetLastSeen(q, out Arrow[] seen) && Matches(seen);

        int letter = right ? correct : WrongLetter(q, correct);
        written[q] = ExamData.Letter(letter) + "?";
        sheet.SetNote(q, written[q]);

        if (right) CheatLog.Reveal(q, correct, CheatSource.Paper);
        if (logEvents) Debug.Log($"[Paper] Q{q + 1}: typed {string.Join(" ", typed)} -> {ExamData.Letter(letter)} ({(right ? "correct pattern" : "WRONG pattern")})");
    }

    bool Matches(Arrow[] seen)
    {
        if (seen == null || seen.Length != typed.Count) return false;
        for (int i = 0; i < seen.Length; i++) if (seen[i] != typed[i]) return false;
        return true;
    }

    // Same wrong input on the same question always gives the same wrong letter.
    int WrongLetter(int q, int correct)
    {
        unchecked
        {
            int h = 17 + q * 31;
            foreach (var t in typed) h = h * 7 + (int)t + 1;
            int pick = (h % 3 + 3) % 3;
            int n = 0;
            for (int c = 0; c < 4; c++)
            {
                if (c == correct) continue;
                if (n == pick) return c;
                n++;
            }
        }
        return (correct + 1) % 4;
    }

    void Cancel(bool showReset)
    {
        if (typed.Count == 0) return;
        if (row >= 0) sheet.SetNote(row, written.TryGetValue(row, out var w) ? w : "");
        typed.Clear();
        if (showReset) ShowMessage("RESET", resetColor, keepArrows: false);
    }

    Arrow? ReadArrow()
    {
        var kb = Keyboard.current;
        if (kb == null) return null;
        if (kb.upArrowKey.wasPressedThisFrame    || (alsoAcceptWASD && kb.wKey.wasPressedThisFrame)) return Arrow.Up;
        if (kb.downArrowKey.wasPressedThisFrame  || (alsoAcceptWASD && kb.sKey.wasPressedThisFrame)) return Arrow.Down;
        if (kb.leftArrowKey.wasPressedThisFrame  || (alsoAcceptWASD && kb.aKey.wasPressedThisFrame)) return Arrow.Left;
        if (kb.rightArrowKey.wasPressedThisFrame || (alsoAcceptWASD && kb.dKey.wasPressedThisFrame)) return Arrow.Right;
        return null;
    }

    // ---------- display ----------

    void Hide()
    {
        if (panel != null && messageTimer <= 0f) panel.gameObject.SetActive(false);
    }

    void ShowMessage(string text, Color c, bool keepArrows)
    {
        panel.gameObject.SetActive(true);
        label.text = text;
        label.color = c;
        foreach (var s in slots)
        {
            if (!keepArrows) s.gameObject.SetActive(false);
            else if (s.sprite == arrowSprite) s.color = c;
        }
        messageTimer = messageTime;
    }

    // Shows: "Q3  2/6"  then the arrows typed so far and faint dots for the ones still to go.
    void RefreshDisplay()
    {
        if (panel == null) return;
        if (row < 0) { panel.gameObject.SetActive(false); return; }

        int needed = sheet.Exam.ComboLength(row);
        panel.gameObject.SetActive(true);
        label.color = typedColor;
        label.text = $"Q{row + 1}  <size=80%>{typed.Count}/{needed}</size>";

        while (slots.Count < needed) slots.Add(MakeSlot());
        float step = arrowSize + 8f;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            bool used = i < needed;
            s.gameObject.SetActive(used);
            if (!used) continue;

            s.rectTransform.anchoredPosition = new Vector2((i - (needed - 1) * 0.5f) * step, 0f);
            if (i < typed.Count)
            {
                s.sprite = arrowSprite;
                s.color = typedColor;
                s.rectTransform.sizeDelta = Vector2.one * arrowSize;
                s.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Angle(typed[i]));
            }
            else
            {
                s.sprite = dotSprite;
                s.color = slotColor;
                s.rectTransform.sizeDelta = Vector2.one * arrowSize * 0.3f;
                s.rectTransform.localRotation = Quaternion.identity;
            }
        }
    }

    Image MakeSlot()
    {
        var go = new GameObject("Slot", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(arrowRow, false);
        var img = go.GetComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    void BuildDisplay()
    {
        if (arrowSprite == null) arrowSprite = MakeArrowSprite();
        if (dotSprite == null) dotSprite = MakeDotSprite();

        var canvasGo = new GameObject("PaperInputDisplay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        panel.SetParent(canvasGo.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, bottomOffset);
        panel.sizeDelta = new Vector2(620f, 120f);
        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);
        bg.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        var lrt = (RectTransform)labelGo.transform;
        lrt.SetParent(panel, false);
        lrt.anchoredPosition = new Vector2(0f, 32f);
        lrt.sizeDelta = new Vector2(600f, 40f);
        label = labelGo.AddComponent<TextMeshProUGUI>();
        if (label.font == null) label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 30f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        arrowRow = new GameObject("Arrows", typeof(RectTransform)).GetComponent<RectTransform>();
        arrowRow.SetParent(panel, false);
        arrowRow.anchoredPosition = new Vector2(0f, -18f);

        panel.gameObject.SetActive(false);
    }

    static float Angle(Arrow a) => a switch
    {
        Arrow.Up => 0f, Arrow.Left => 90f, Arrow.Down => 180f, Arrow.Right => -90f, _ => 0f
    };

    static Sprite MakeArrowSprite()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            // Unity textures are bottom-up: arrow points to +y (up)
            float fx = x + 0.5f, fy = y + 0.5f;
            bool head = fy >= 30f && fy <= 58f && Mathf.Abs(fx - 32f) <= (58f - fy) * 27f / 28f;
            bool shaft = fy >= 6f && fy < 31f && fx >= 22f && fx <= 42f;
            px[y * n + x] = new Color32(255, 255, 255, (byte)(head || shaft ? 255 : 0));
        }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeDotSprite()
    {
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
            px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(n / 2f - 1f - d + 0.5f)));
        }
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }
}
