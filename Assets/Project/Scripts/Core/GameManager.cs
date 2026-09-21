using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns the exam clock and the end of the run.
///
///   time runs out  -> fires GameEvents.ExamEnded()  (the player half shows results)
///   caught         -> listens for GameEvents.Caught(), stops the clock
///
/// The OnGUI screens are placeholders so the loop can be played end to end before
/// any real UI exists. Replace them, don't build on them.
/// </summary>
public class GameManager : MonoBehaviour
{
    public enum State { Exam, Caught, TimeUp }

    public static GameManager Instance { get; private set; }

    [Header("Exam")]
    [Tooltip("Seconds. SCOPE says 5:00.")]
    [SerializeField] private float examDuration = 300f;

    [Header("Placeholder UI")]
    [SerializeField] private bool showPlaceholderUI = true;
    [SerializeField] private Key restartKey = Key.R;
    [Tooltip("Seconds under which the clock turns red.")]
    [SerializeField] private float lowTimeWarning = 30f;

    public State Current { get; private set; } = State.Exam;
    public float TimeRemaining { get; private set; }
    public bool ExamRunning => Current == State.Exam;
    /// <summary>How long the player actually lasted. The results screen will want this.</summary>
    public float TimeElapsed => examDuration - TimeRemaining;

    private void Awake()
    {
        Instance = this;
        TimeRemaining = examDuration;
    }

    private void OnEnable()
    {
        GameEvents.OnCaught += HandleCaught;
    }

    private void OnDisable()
    {
        GameEvents.OnCaught -= HandleCaught;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Current == State.Exam)
        {
            TimeRemaining -= Time.deltaTime;
            if (TimeRemaining > 0f) return;

            TimeRemaining = 0f;
            Current = State.TimeUp;
            GameEvents.ExamEnded();
            return;
        }

        if (Keyboard.current != null && Keyboard.current[restartKey].wasPressedThisFrame)
            Restart();
    }

    private void HandleCaught()
    {
        // Caught after the bell doesn't count — you already handed the paper in.
        if (Current != State.Exam) return;
        Current = State.Caught;
    }

    public void Restart()
    {
        Time.timeScale = 1f;

        int index = SceneManager.GetActiveScene().buildIndex;
        if (index < 0)
        {
            Debug.LogError("[GameManager] This scene isn't in Build Settings, so it can't be reloaded. " +
                           "File > Build Settings (or Build Profiles) > Add Open Scenes, then try again.", this);
            return;
        }

        SceneManager.LoadScene(index);
    }

    private void OnGUI()
    {
        if (!showPlaceholderUI) return;

        DrawClock();
        if (Current != State.Exam) DrawEndScreen();
    }

    private void DrawClock()
    {
        int minutes = Mathf.FloorToInt(TimeRemaining / 60f);
        int seconds = Mathf.FloorToInt(TimeRemaining % 60f);

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.UpperCenter,
        };
        style.normal.textColor = TimeRemaining <= lowTimeWarning && Current == State.Exam
            ? new Color(1f, 0.35f, 0.3f)
            : Color.white;

        GUI.Label(new Rect(Screen.width * 0.5f - 100f, 10f, 200f, 36f), $"{minutes}:{seconds:00}", style);
    }

    private void DrawEndScreen()
    {
        var box = new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.5f - 90f, 500f, 180f);
        GUI.Box(box, GUIContent.none);

        var banner = new GUIStyle(GUI.skin.label)
        {
            fontSize = 40,
            alignment = TextAnchor.MiddleCenter,
        };
        banner.normal.textColor = Current == State.Caught ? new Color(1f, 0.35f, 0.3f) : Color.white;

        var hint = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleCenter,
        };

        GUI.Label(new Rect(box.x, box.y + 28f, box.width, 56f),
                  Current == State.Caught ? "CAUGHT" : "PENS DOWN", banner);

        string detail = Current == State.Caught
            ? $"you lasted {Mathf.FloorToInt(TimeElapsed / 60f)}:{Mathf.FloorToInt(TimeElapsed % 60f):00}"
            : "the exam is over";

        GUI.Label(new Rect(box.x, box.y + 92f, box.width, 24f), detail, hint);
        GUI.Label(new Rect(box.x, box.y + 126f, box.width, 24f),
                  $"placeholder screen — press {restartKey} to restart", hint);
    }
}
