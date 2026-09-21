using System.Text;
using UnityEngine;
using TMPro;

// The printed question paper on your desk. Builds its text at runtime from ExamData.
// Put this on an empty GameObject lying flat on the desk: rotation (90, 0, 0).
public class QuestionPaper : MonoBehaviour
{
    [SerializeField] ExamData exam;
    [SerializeField] Vector2 sheetSize = new Vector2(2100f, 2970f);   // A4, 10000 units = 1 m
    [SerializeField] float margin = 140f;
    [SerializeField] Vector2 fontSizeRange = new Vector2(30f, 54f);   // auto-fits between these
    [SerializeField] Color ink = new Color(0.12f, 0.12f, 0.15f, 1f);

    void Awake()
    {
        if (exam == null) { Debug.LogError("[QuestionPaper] No ExamData assigned."); enabled = false; return; }

        var canvasGo = new GameObject("PaperCanvas", typeof(RectTransform), typeof(Canvas));
        var root = (RectTransform)canvasGo.transform;
        root.SetParent(transform, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one * 0.0001f;
        root.sizeDelta = sheetSize;
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var textGo = new GameObject("Questions", typeof(RectTransform));
        var rt = (RectTransform)textGo.transform;
        rt.SetParent(root, false);
        rt.sizeDelta = sheetSize - Vector2.one * margin * 2f;

        var t = textGo.AddComponent<TextMeshProUGUI>();
        if (t.font == null) t.font = TMP_Settings.defaultFontAsset;
        t.color = ink;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.enableAutoSizing = true;
        t.fontSizeMin = fontSizeRange.x;
        t.fontSizeMax = fontSizeRange.y;
        t.raycastTarget = false;
        t.text = BuildText();
    }

    string BuildText()
    {
        var sb = new StringBuilder();
        sb.Append($"<align=center><b>{exam.examTitle}</b>\n");
        sb.Append("<size=70%>Time allowed: 5 minutes. Mark all answers on the answer sheet.</size></align>\n\n");

        for (int i = 0; i < exam.Count; i++)
        {
            var q = exam.questions[i];
            sb.Append($"<b>{i + 1}.</b> {q.text}\n");
            sb.Append("<indent=6%><size=85%>");
            for (int c = 0; c < 4; c++)
                sb.Append($"{ExamData.Letter(c)}) {q.options[c]}      ");
            sb.Append("</size></indent>\n\n");
        }
        return sb.ToString();
    }
}
