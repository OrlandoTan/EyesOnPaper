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
    public enum State { Title, HowTo, Exam, Caught, TimeUp, Submitted }

    public static GameManager Instance { get; private set; }

    /// <summary>Set by the player half's ResultsScreen so the placeholder end box steps aside. The clock still draws.</summary>
    public static bool ExternalEndScreen;

    [Header("Front end")]
    [Tooltip("Off to drop straight into the exam while testing.")]
    [SerializeField] private bool startAtTitle = true;
    [SerializeField] private string gameTitle = "EYES ON PAPER";
    [SerializeField] private string tagline = "You cheated on every question. You still failed.";
    [TextArea(4, 10)]
    [SerializeField] private string howToText =
        "Mouse           look around\n" +
        "Hold Space      take out your phone\n" +
        "Arrow keys      enter the arrow combo\n" +
        "Click / 1-4     mark an answer\n" +
        "\n" +
        "Jack texts you answers. Each one shows for three seconds, then it's gone for good.\n" +
        "The invigilator is watching. Don't get caught looking.";

    [Header("Exam")]
    [Tooltip("Seconds. SCOPE says 5:00.")]
    [SerializeField] private float examDuration = 160f;

    [Header("Placeholder UI")]
    [SerializeField] private bool showPlaceholderUI = true;
    [SerializeField] private Key restartKey = Key.R;
    [Tooltip("Seconds under which the clock turns red.")]
    [SerializeField] private float lowTimeWarning = 30f;

    public State Current { get; private set; } = State.Title;
    public float TimeRemaining { get; private set; }
    public bool ExamRunning => Current == State.Exam;
    /// <summary>How long the player actually lasted. The results screen will want this.</summary>
    public float TimeElapsed => examDuration - TimeRemaining;

    private void Awake()
    {
        Instance = this;
        TimeRemaining = examDuration;

        if (startAtTitle)
        {
            Current = State.Title;
            Time.timeScale = 0f;    // holds the invigilator, the phone and Jack all still
        }
        else
        {
            BeginExam();
        }
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
        if (Current == State.Title)
        {
            if (Dismissed()) Current = State.HowTo;
            return;
        }

        if (Current == State.HowTo)
        {
            if (Dismissed()) BeginExam();
            return;
        }

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

    /// <summary>Player handed the paper in early. Stops the clock; being caught afterwards no longer counts.</summary>
    public void Submit()
    {
        if (Current != State.Exam) return;
        Current = State.Submitted;
        GameEvents.ExamEnded();
    }

    /// <summary>Any key or click. Deliberately forgiving - nobody should hunt for the button.</summary>
    private bool Dismissed()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
        return Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
    }

    private void BeginExam()
    {
        Current = State.Exam;
        Time.timeScale = 1f;
    }

    private void HandleCaught()
    {
        // Caught after the bell doesn't count - you already handed the paper in.
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

        if (Current == State.Title) { DrawTitle(); return; }
        if (Current == State.HowTo) { DrawHowTo(); return; }

        DrawClock();
        if (Current != State.Exam && !ExternalEndScreen) DrawEndScreen();
    }

    private void DrawTitle()
    {
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);

        float y = Screen.height * 0.32f;
        GUI.Label(new Rect(0f, y, Screen.width, 70f), gameTitle, Centred(52));
        GUI.Label(new Rect(0f, y + 78f, Screen.width, 28f), tagline, Centred(18));
        GUI.Label(new Rect(0f, y + 150f, Screen.width, 24f), "press any key", Centred(15));
    }

    private void DrawHowTo()
    {
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);

        var body = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
        };

        float width = Mathf.Min(560f, Screen.width - 80f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height * 0.22f;

        GUI.Label(new Rect(0f, y, Screen.width, 40f), "HOW TO PLAY", Centred(30));
        GUI.Label(new Rect(x, y + 60f, width, 260f), howToText, body);
        GUI.Label(new Rect(0f, Screen.height * 0.82f, Screen.width, 24f), "press any key to start", Centred(15));
    }

    private static GUIStyle Centred(int size) => new GUIStyle(GUI.skin.label)
    {
        fontSize = size,
        alignment = TextAnchor.MiddleCenter,
    };

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
                  $"placeholder screen - press {restartKey} to restart", hint);
    }
}
