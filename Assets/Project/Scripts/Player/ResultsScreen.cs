using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// End-of-exam results: grade, score, win/fail, and a row-by-row reveal of
// the human error behind every wrong answer.
// Creates itself automatically in any scene that has an AnswerSheet - no setup needed.
// Lives outside PlayerRig on purpose, so ExamLockout doesn't switch it off.
public class ResultsScreen : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] float delayBeforeShow = 0.8f;
    [SerializeField] float rowInterval = 0.35f;

    [Header("Scoring")]
    [SerializeField] int pointsPerCorrect = 100;
    [SerializeField] int pointsPerSecondLeft = 2;     // only when handed in early
    [SerializeField, Range(0f, 1f)] float passMark = 0.5f;

    static readonly Color Paper = new Color(0.96f, 0.95f, 0.91f, 1f);
    static readonly Color Ink   = new Color(0.15f, 0.15f, 0.18f, 1f);
    const string Red = "#C0392B", Green = "#2E8B57", Grey = "#7A7A7A";

    AnswerSheet sheet;
    ExamData exam;
    bool shown;
    TMP_Text headerText, rowsText, summaryText;
    GameObject overlay;

    // ---------- Auto-create ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
        if (FindFirstObjectByType<ResultsScreen>() != null) return;
        if (FindFirstObjectByType<AnswerSheet>() == null) return;
        new GameObject("ResultsScreen").AddComponent<ResultsScreen>();
    }

    void OnEnable()
    {
        GameManager.ExternalEndScreen = true;
        GameEvents.OnExamEnded += HandleEnd;
        GameEvents.OnCaught    += HandleEnd;
    }

    void OnDisable()
    {
        GameManager.ExternalEndScreen = false;
        GameEvents.OnExamEnded -= HandleEnd;
        GameEvents.OnCaught    -= HandleEnd;
    }

    void HandleEnd()
    {
        if (shown) return;      // first ending wins (caught after handing in doesn't count)
        shown = true;
        StartCoroutine(Show());
    }

    // ---------- Scoring ----------

    enum Ending { HandedIn, PensDown, Caught }

    Ending GetEnding()
    {
        var gm = GameManager.Instance;
        if (gm == null) return Ending.HandedIn;
        switch (gm.Current)
        {
            case GameManager.State.Caught: return Ending.Caught;
            case GameManager.State.TimeUp: return Ending.PensDown;
            default: return Ending.HandedIn;
        }
    }

    // Victorian university grading (Monash, RMIT, Deakin, La Trobe, Swinburne)
    static string Grade(float pct) =>
        pct >= 0.8f ? "HD" : pct >= 0.7f ? "D" : pct >= 0.6f ? "C" : pct >= 0.5f ? "P" : "N";

    static string GradeName(string g) => g switch
    {
        "HD" => "High Distinction",
        "D"  => "Distinction",
        "C"  => "Credit",
        "P"  => "Pass",
        _    => "Fail",
    };

    // One line per question: what you wrote, and whether it was right.
    string Row(int q, out bool correct)
    {
        int written = sheet.Marks[q];
        correct = written == exam.questions[q].correctIndex;

        string wrote = written < 0 ? "-" : ExamData.Letter(written);
        string verdict = correct ? $"<color={Green}>RIGHT</color>" : $"<color={Red}>WRONG</color>";
        return $"<b>Q{q + 1}</b><pos=30%>{wrote}<pos=55%>{verdict}";
    }

    // ---------- Display ----------

    IEnumerator Show()
    {
        yield return new WaitForSecondsRealtime(delayBeforeShow);

        sheet = FindFirstObjectByType<AnswerSheet>();
        exam = sheet != null ? sheet.Exam : null;
        if (sheet == null || exam == null) { Debug.LogError("[Results] No AnswerSheet/ExamData found."); yield break; }

        Ending ending = GetEnding();
        var gm = GameManager.Instance;
        float timeLeft = gm != null ? gm.TimeRemaining : 0f;
        float timeUsed = gm != null ? gm.TimeElapsed : Time.timeSinceLevelLoad;

        BuildUI();

        headerText.text = ending switch
        {
            Ending.Caught   => $"<color={Red}>CAUGHT</color>\n<size=45%>Paper confiscated - academic misconduct.</size>",
            Ending.PensDown => "PENS DOWN\n<size=45%>Time's up. Papers collected.</size>",
            _               => "HANDED IN\n<size=45%>You submitted your paper.</size>",
        };

        // Reveal the sheet row by row
        var rows = new StringBuilder();
        int correctCount = 0;
        for (int q = 0; q < exam.Count; q++)
        {
            rows.Append(Row(q, out bool ok)).Append('\n');
            if (ok) correctCount++;
            rowsText.text = rows.ToString();
            yield return new WaitForSecondsRealtime(rowInterval);
        }

        // Summary
        var cheated = new HashSet<int>();
        foreach (var e in CheatLog.Entries) cheated.Add(e.question);

        float pct = exam.Count > 0 ? (float)correctCount / exam.Count : 0f;
        bool caught = ending == Ending.Caught;
        bool win = !caught && pct >= passMark;
        int bonus = ending == Ending.HandedIn ? Mathf.FloorToInt(timeLeft) * pointsPerSecondLeft : 0;
        int score = caught ? 0 : correctCount * pointsPerCorrect + bonus;
        string grade = caught ? "N" : Grade(pct);

        var sb = new StringBuilder();
        sb.Append($"<size=120%>You cheated on <b>{cheated.Count}/{exam.Count}</b> questions. ");
        sb.Append(caught ? $"You would have scored <b>{correctCount}/{exam.Count}</b>.</size>\n"
                         : $"You scored <b>{correctCount}/{exam.Count}</b>.</size>\n");
        sb.Append($"<color={Grey}>Time {Mathf.FloorToInt(timeUsed / 60f)}:{Mathf.FloorToInt(timeUsed % 60f):00}   ·   ");
        sb.Append($"Jack messages missed: {CheatLog.MissedMessages.Count}");
        if (bonus > 0) sb.Append($"   ·   Early hand-in bonus +{bonus}");
        sb.Append("</color>\n\n");
        string gradeDetail = caught ? "Fail - misconduct" : $"{GradeName(grade)}, {Mathf.RoundToInt(pct * 100f)}%";
        sb.Append($"<size=160%><b>{grade}  <size=60%>({gradeDetail})</size>   ·   SCORE {score}</b></size>\n");
        sb.Append(win ? $"<size=130%><color={Green}><b>PASSED</b></color></size>" : $"<size=130%><color={Red}><b>FAILED</b></color></size>");
        sb.Append($"\n\n<size=80%><color={Grey}>Press R to retake the exam</color></size>");
        summaryText.text = sb.ToString();

        Debug.Log($"[Results] {ending}  {correctCount}/{exam.Count}  grade {grade}  score {score}  {(win ? "WIN" : "FAIL")}");
    }

    void BuildUI()
    {
        overlay = new GameObject("ResultsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        overlay.transform.SetParent(transform, false);
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dim = MakeImage(overlay.transform, "Dim", new Color(0f, 0f, 0f, 0.8f));
        Stretch(dim);

        var paper = MakeImage(overlay.transform, "Paper", Paper);
        paper.sizeDelta = new Vector2(1250f, 1000f);

        headerText  = MakeText(paper, new Vector2(0f, 400f),  new Vector2(1150f, 160f), 64f, TextAlignmentOptions.Center);
        rowsText    = MakeText(paper, new Vector2(0f, 60f),   new Vector2(460f, 520f), 32f, TextAlignmentOptions.TopLeft);
        summaryText = MakeText(paper, new Vector2(0f, -360f), new Vector2(1150f, 260f), 26f, TextAlignmentOptions.Center);
        rowsText.lineSpacing = 12f;
    }

    static RectTransform MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static TMP_Text MakeText(RectTransform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var t = go.AddComponent<TextMeshProUGUI>();
        if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        t.fontSize = fontSize;
        t.color = Ink;
        t.alignment = align;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }
}
