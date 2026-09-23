using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The front end: a drawn classroom, the title on the board, and real buttons.
///
/// Everything here is generated in code - flat shapes and rounded rectangles -
/// so it needs no art. Turn off Draw Backdrop once there's a real classroom
/// behind the camera and the buttons will sit straight on top of it.
/// </summary>
public class TitleMenu : MonoBehaviour
{
    private enum Page { Title, HowTo, Leaving }

    [Header("Text")]
    [SerializeField] private string gameTitle = "EYES ON PAPER";
    [SerializeField] private string tagline = "You cheated on every question. You still failed.";
    [Tooltip("One per line, written as  Key|What it does")]
    [SerializeField] private string[] controls =
    {
        "Mouse|look around",
        "Hold Space|take out your phone",
        "Arrow keys|enter the arrow combo",
        "Right mouse|lean in over your paper",
        "Click or 1-4|mark an answer",
        "Hold left click|hand in your paper",
        "Q|make a classmate drop a pencil",
        "G|call a classmate, phone must be out",
        "F|make a classmate shout, once per exam",
    };

    [TextArea(3, 8)]
    [SerializeField] private string rules =
        "Your phone is under the desk. Hold Space to look at it and enter the arrow combo shown.\n" +
        "It gives you one answer, once. Remember it while you look up and mark the sheet.\n" +
        "The invigilator is watching. Listen for footsteps - silence means he has stopped.\n" +
        "Q, G and F pull him away from you. Use them to buy a clean window.";

    [Header("Flow")]
    [Tooltip("Scene to load. Must be in Build Settings.")]
    [SerializeField] private string examScene = "Main";
    [SerializeField] private ScreenFade fade;

    [Header("Look")]
    [Tooltip("Off when there's a real 3D classroom behind the camera.")]
    [SerializeField] private bool drawBackdrop = true;
    [SerializeField] private Color wall = new Color(0.96f, 0.90f, 0.78f);
    [SerializeField] private Color board = new Color(0.16f, 0.29f, 0.24f);
    [SerializeField] private Color frame = new Color(0.66f, 0.44f, 0.25f);
    [SerializeField] private Color desk = new Color(0.79f, 0.55f, 0.30f);
    [SerializeField] private Color chalk = new Color(0.94f, 0.95f, 0.90f);
    [SerializeField] private Color buttonColour = new Color(0.97f, 0.76f, 0.29f);
    [SerializeField] private Color buttonInk = new Color(0.20f, 0.16f, 0.10f);

    private Page page = Page.Title;
    private Texture2D pixel;
    private Texture2D rounded;
    private GUIStyle roundedStyle;

    private void Awake()
    {
        if (fade == null) fade = GetComponent<ScreenFade>();

        pixel = MakePixel();
        rounded = MakeRoundedRect(64, 16);

        // A restart may have left these however the exam wanted them.
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
        if (rounded != null) Destroy(rounded);
    }

    private void Update()
    {
        // Keyboard shortcut for people who never touch the mouse.
        if (page == Page.HowTo && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
            page = Page.Title;
    }

    private void StartGame()
    {
        page = Page.Leaving;

        if (fade != null) fade.FadeOutAndLoad(examScene);
        else SceneManager.LoadScene(examScene);
    }

    // ---------- drawing ----------

    private void OnGUI()
    {
        float s = Screen.height / 720f;          // everything scales off a 720p reference

        if (drawBackdrop) DrawClassroom(s);

        if (page == Page.HowTo) DrawHowTo(s);
        else DrawTitle(s);
    }

    private void DrawClassroom(float s)
    {
        // Wall
        Fill(new Rect(0f, 0f, Screen.width, Screen.height), wall);

        // Blackboard, with a wooden frame and a chalk tray
        float boardW = Mathf.Min(Screen.width * 0.72f, 820f * s);
        float boardH = boardW * 0.42f;
        float boardX = (Screen.width - boardW) * 0.5f;
        float boardY = Screen.height * 0.12f;
        float lip = 10f * s;

        Fill(new Rect(boardX - lip, boardY - lip, boardW + lip * 2f, boardH + lip * 2f), frame);
        Fill(new Rect(boardX, boardY, boardW, boardH), board);
        Fill(new Rect(boardX - lip, boardY + boardH + lip, boardW + lip * 2f, 8f * s), frame);

        // Chalk scribbles in the corner, so the board isn't a flat rectangle
        var faint = new Color(chalk.r, chalk.g, chalk.b, 0.22f);
        for (int i = 0; i < 3; i++)
            Fill(new Rect(boardX + 26f * s, boardY + (24f + i * 16f) * s, (90f + i * 38f) * s, 3f * s), faint);

        // A row of desks along the bottom
        float deskTop = Screen.height * 0.78f;
        float deskW = 150f * s;
        float gap = 42f * s;
        float total = deskW * 4f + gap * 3f;
        float x = (Screen.width - total) * 0.5f;

        var legs = new Color(desk.r * 0.72f, desk.g * 0.72f, desk.b * 0.72f);
        for (int i = 0; i < 4; i++)
        {
            float dx = x + i * (deskW + gap);
            Fill(new Rect(dx, deskTop, deskW, 16f * s), desk);
            Fill(new Rect(dx + 12f * s, deskTop + 16f * s, 10f * s, 70f * s), legs);
            Fill(new Rect(dx + deskW - 22f * s, deskTop + 16f * s, 10f * s, 70f * s), legs);
        }

        // Floor
        Fill(new Rect(0f, deskTop + 86f * s, Screen.width, Screen.height), new Color(0.80f, 0.72f, 0.60f));
    }

    private void DrawTitle(float s)
    {
        float boardTop = Screen.height * 0.12f;

        GUI.Label(new Rect(0f, boardTop + 46f * s, Screen.width, 90f * s),
                  gameTitle, Centred(Mathf.RoundToInt(62f * s), chalk));
        GUI.Label(new Rect(0f, boardTop + 132f * s, Screen.width, 34f * s),
                  tagline, Centred(Mathf.RoundToInt(19f * s), new Color(chalk.r, chalk.g, chalk.b, 0.75f)));

        if (page == Page.Leaving) return;

        float w = 280f * s;
        float h = 68f * s;
        float cx = (Screen.width - w) * 0.5f;
        float y = Screen.height * 0.55f;

        if (Button(new Rect(cx, y, w, h), "START", s, true)) StartGame();
        if (Button(new Rect(cx, y + h + 26f * s, w, h * 0.80f), "how to play", s, false)) page = Page.HowTo;
    }

    private void DrawHowTo(float s)
    {
        string[] ruleLines = rules.Split('\n');

        float bh = 62f * s;
        float buttonGap = 28f * s;
        float margin = 20f;

        // Measure at this scale, then shrink if the card plus its buttons would
        // not fit. Everything scales linearly, so one pass is exact enough.
        float height = MeasureCard(s, ruleLines, out float[] ruleHeights, out GUIStyle ruleStyle, out float width);
        float block = height + buttonGap + bh + margin * 2f;

        if (block > Screen.height)
        {
            s *= (Screen.height - margin * 2f) / (block - margin * 2f);
            bh = 62f * s;
            buttonGap = 28f * s;
            height = MeasureCard(s, ruleLines, out ruleHeights, out ruleStyle, out width);
            block = height + buttonGap + bh + margin * 2f;
        }

        float top = Mathf.Max(margin, (Screen.height - block) * 0.5f + margin);
        var card = new Rect((Screen.width - width) * 0.5f, top, width, height);

        float pad = 44f * s;
        float rowH = 42f * s;

        Fill(new Rect(card.x + 6f * s, card.y + 9f * s, card.width, card.height), new Color(0f, 0f, 0f, 0.16f), true);
        Fill(card, new Color(0.99f, 0.98f, 0.94f), true);

        GUI.Label(new Rect(card.x, card.y + 34f * s, card.width, 44f * s),
                  "HOW TO PLAY", Centred(Mathf.RoundToInt(31f * s), buttonInk));

        var keyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(19f * s),
            alignment = TextAnchor.MiddleRight,
        };
        keyStyle.normal.textColor = new Color(0.24f, 0.20f, 0.14f);

        var actionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(19f * s),
            alignment = TextAnchor.MiddleLeft,
        };
        actionStyle.normal.textColor = new Color(0.38f, 0.35f, 0.30f);

        float split = card.x + width * 0.38f;
        float y = card.y + 108f * s;

        foreach (string row in controls)
        {
            int bar = row.IndexOf('|');
            string key = bar >= 0 ? row.Substring(0, bar) : row;
            string action = bar >= 0 ? row.Substring(bar + 1) : "";

            GUI.Label(new Rect(card.x + pad, y, split - card.x - pad - 18f * s, rowH), key, keyStyle);
            GUI.Label(new Rect(split + 22f * s, y, card.xMax - split - pad - 22f * s, rowH), action, actionStyle);
            y += rowH;
        }

        y += 22f * s;
        Fill(new Rect(card.x + pad, y, card.width - pad * 2f, 1f), new Color(0f, 0f, 0f, 0.13f));
        y += 24f * s;

        for (int i = 0; i < ruleLines.Length; i++)
        {
            GUI.Label(new Rect(card.x + pad, y, width - pad * 2f, ruleHeights[i]), ruleLines[i], ruleStyle);
            y += ruleHeights[i] + 14f * s;
        }

        float bw = 230f * s;
        float by = card.yMax + buttonGap;

        if (Button(new Rect(Screen.width * 0.5f - bw - 14f * s, by, bw, bh), "back", s, false))
            page = Page.Title;

        if (Button(new Rect(Screen.width * 0.5f + 14f * s, by, bw, bh), "START", s, true))
            StartGame();
    }

    /// <summary>Works out how tall the card needs to be, measuring wrapped text properly.</summary>
    private float MeasureCard(float s, string[] ruleLines, out float[] ruleHeights,
                              out GUIStyle ruleStyle, out float width)
    {
        float pad = 44f * s;
        width = Mathf.Min(820f * s, Screen.width - 80f * s);
        float textWidth = width - pad * 2f;

        ruleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(17f * s),
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
        };
        ruleStyle.normal.textColor = new Color(0.34f, 0.31f, 0.26f);

        ruleHeights = new float[ruleLines.Length];
        float rulesHeight = 0f;
        for (int i = 0; i < ruleLines.Length; i++)
        {
            ruleHeights[i] = ruleStyle.CalcHeight(new GUIContent(ruleLines[i]), textWidth);
            rulesHeight += ruleHeights[i] + 14f * s;
        }

        return 108f * s + controls.Length * 42f * s + 46f * s + rulesHeight + pad;
    }

    /// <summary>Rounded button. Returns true on click.</summary>
    private bool Button(Rect rect, string label, float s, bool primary)
    {
        bool hover = rect.Contains(Event.current.mousePosition);
        // UnityEngine.Input throws in this project - active input handling is
        // the Input System package only.
        bool held = hover && Mouse.current != null && Mouse.current.leftButton.isPressed;

        Color face = primary ? buttonColour : new Color(1f, 1f, 1f, 0.85f);
        if (hover) face = new Color(face.r * 1.06f, face.g * 1.06f, face.b * 1.06f, face.a);
        if (held) face = new Color(face.r * 0.88f, face.g * 0.88f, face.b * 0.88f, face.a);

        float lift = held ? 2f * s : 0f;

        // Drop shadow, which is what makes flat shapes read as a button
        Fill(new Rect(rect.x, rect.y + 5f * s, rect.width, rect.height), new Color(0f, 0f, 0f, 0.20f), true);
        Fill(new Rect(rect.x, rect.y + lift, rect.width, rect.height - lift), face, true);

        var style = Centred(Mathf.RoundToInt((primary ? 26f : 19f) * s), buttonInk);
        GUI.Label(new Rect(rect.x, rect.y + lift, rect.width, rect.height - lift), label, style);

        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    // ---------- helpers ----------

    private void Fill(Rect r, Color c, bool round = false)
    {
        GUI.color = c;

        if (round)
        {
            // 9-sliced, so the corner radius stays the size it was drawn at.
            // Stretching the whole texture turns a 16px corner into a 300px
            // blur once the card is big.
            if (roundedStyle == null)
            {
                roundedStyle = new GUIStyle();
                roundedStyle.normal.background = rounded;
                roundedStyle.border = new RectOffset(18, 18, 18, 18);
            }

            GUI.Box(r, GUIContent.none, roundedStyle);
        }
        else
        {
            GUI.DrawTexture(r, pixel, ScaleMode.StretchToFill, true);
        }

        GUI.color = Color.white;
    }

    private static GUIStyle Centred(int size, Color colour)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(8, size),
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = colour;
        return style;
    }

    private static Texture2D MakePixel()
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        return t;
    }

    /// <summary>White rounded rectangle, transparent outside. Tinted via GUI.color.</summary>
    private static Texture2D MakeRoundedRect(int size, int radius)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
            float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
            float d = Mathf.Sqrt(dx * dx + dy * dy);

            float a = Mathf.Clamp01(radius - d + 0.5f);      // soft edge
            if (dx == 0f && dy == 0f) a = 1f;

            t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }

        t.Apply();
        return t;
    }
}
