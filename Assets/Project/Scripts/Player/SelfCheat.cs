using UnityEngine;
using TMPro;

// Your own cheat ("Notes" app). Always targets the next unanswered question, in order.
// Long combo (by difficulty). On success: shows the answer until you hide the phone.
// Next time you pull the phone out, it moves on to the next unanswered question.
// Attach to the Phone object (same object as PhoneController + the Notes ComboInput).
public class SelfCheat : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] ExamData exam;
    [SerializeField] ComboInput combo;           // auto-filled: ComboInput on this object
    [SerializeField] PhoneController phone;      // auto-filled
    [SerializeField] TMP_Text titleText;         // PhoneCanvas/HeaderText  ("NOTES  Q3")
    [SerializeField] TMP_Text answerText;        // PhoneCanvas/RevealText  ("Q3: C")

    [Header("Flow")]
    [SerializeField] bool autoAdvance = false;        // false: answer stays until you hide the phone
    [SerializeField] float answerHoldTime = 1.5f;     // only used when autoAdvance is on
    [SerializeField] string appName = "NOTES";
    [SerializeField] bool logEvents = true;

    int target = -1;
    bool showingAnswer;
    float holdTimer;
    bool paused;

    public int CurrentTarget => target;

    void Awake()
    {
        if (phone == null) phone = GetComponentInParent<PhoneController>();
        if (combo == null) combo = GetComponent<ComboInput>();
    }

    void OnEnable()
    {
        GameEvents.OnPhoneShown  += HandleShown;
        GameEvents.OnPhoneHidden += HandleHidden;
        if (combo != null) combo.OnComboComplete += HandleComplete;
    }

    void OnDisable()
    {
        GameEvents.OnPhoneShown  -= HandleShown;
        GameEvents.OnPhoneHidden -= HandleHidden;
        if (combo != null) combo.OnComboComplete -= HandleComplete;
    }

    void Start()
    {
        if (exam == null) { Debug.LogError("[SelfCheat] No ExamData assigned."); enabled = false; return; }
        SetTexts("", "");
    }

    void Update()
    {
        if (!showingAnswer || !autoAdvance) return;

        holdTimer -= Time.deltaTime;
        if (holdTimer <= 0f)
        {
            showingAnswer = false;
            if (answerText != null) answerText.text = "";
            if (phone != null && phone.IsOut) Retarget();   // move on to the next question
        }
    }

    // JackMessenger pauses your combo while you're reading his message. Progress is kept.
    public void SetPaused(bool p)
    {
        paused = p;
        if (combo != null) combo.AcceptInput = !p;

        // Resuming: if Jack just gave you the question you were working on, move on.
        if (!p && phone != null && phone.IsOut && !showingAnswer && (target < 0 || CheatLog.IsAnswered(target)))
            Retarget();
    }

    void HandleShown()
    {
        if (showingAnswer) return;
        Retarget();
    }

    void HandleHidden()
    {
        // Looked away: whatever answer was on screen is gone.
        showingAnswer = false;
        if (answerText != null) answerText.text = "";
    }

    void Retarget()
    {
        target = CheatLog.NextUnanswered(exam.Count);
        if (target < 0)
        {
            combo.Clear();
            SetTexts($"{appName}\n<size=60%>All answers found</size>", "");
            return;
        }

        combo.StartCombo(exam.ComboLength(target));
        combo.AcceptInput = !paused;
        SetTexts($"{appName}  <size=70%>Q{target + 1}</size>", "");
    }

    void HandleComplete()
    {
        if (target < 0) return;

        int answer = exam.questions[target].correctIndex;
        CheatLog.Reveal(target, answer, CheatSource.Self);
        if (logEvents) Debug.Log($"[SelfCheat] Q{target + 1}: {ExamData.Letter(answer)}");

        combo.Clear();                 // clears arrows and status text: only the answer shows
        showingAnswer = true;
        holdTimer = answerHoldTime;
        SetTexts($"{appName}  <size=70%>Q{target + 1}</size>", $"Q{target + 1}: {ExamData.Letter(answer)}");
    }

    void SetTexts(string title, string answer)
    {
        if (titleText != null) titleText.text = title;
        if (answerText != null) answerText.text = answer;
    }
}
