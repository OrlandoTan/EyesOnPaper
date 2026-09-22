using System.Collections.Generic;
using UnityEngine;

// Students finish and walk out as the deadline approaches. Fewer students left = the calm
// invigilator's random "look at a seat" lands on you more often, until you're the last one.
// Put on any object in Main (e.g. the Students object). Needs an ExitPoint at the door.
public class ClassDeparture : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Optional: where they drop their paper (front desk). Leave empty to go straight out.")]
    [SerializeField] Transform handInPoint;
    [Tooltip("Just inside the door. They disappear on arrival.")]
    [SerializeField] Transform exitPoint;
    [Tooltip("Optional: something at the front of the room (e.g. the whiteboard). Used to tell which way each model faces. Defaults to the hand-in point, then world +Z.")]
    [SerializeField] Transform frontOfRoom;

    [Header("Animation")]
    [Tooltip("A controller with Idle + Walk and a float 'Speed' - the Invigilator controller works.")]
    [SerializeField] RuntimeAnimatorController walkController;
    [SerializeField] float walkSpeed = 1.1f;
    [SerializeField] float handInPause = 1.5f;

    [Header("Schedule (fraction of the exam elapsed)")]
    [SerializeField, Range(0f, 1f)] float firstLeavesAt = 0.5f;
    [Tooltip("Seconds before the end by which everyone who's going has left.")]
    [SerializeField] float allGoneWithSecondsLeft = 20f;
    [Tooltip("How many students are still sitting at the very end (0 = you're alone).")]
    [SerializeField] int stayUntilEnd = 0;
    [SerializeField, Range(0f, 0.9f)] float timingJitter = 0.35f;

    [Header("Fallback when there's no GameManager (e.g. Test_Player)")]
    [SerializeField] float examDurationIfNoManager = 300f;

    [SerializeField] bool logEvents = true;

    readonly List<StudentAmbient> queue = new List<StudentAmbient>();
    readonly List<float> leaveTimes = new List<float>();
    bool scheduled, stopped;
    float localTime;

    void OnEnable()
    {
        GameEvents.OnCaught    += Stop;
        GameEvents.OnExamEnded += Stop;
    }

    void OnDisable()
    {
        GameEvents.OnCaught    -= Stop;
        GameEvents.OnExamEnded -= Stop;
    }

    void Stop() => stopped = true;

    float Duration()
    {
        var gm = GameManager.Instance;
        return gm != null ? gm.TimeElapsed + gm.TimeRemaining : examDurationIfNoManager;
    }

    float Elapsed()
    {
        var gm = GameManager.Instance;
        return gm != null ? gm.TimeElapsed : localTime;
    }

    void Update()
    {
        localTime += Time.deltaTime;
        if (stopped) return;
        if (!scheduled) BuildSchedule();          // first frame: students have registered by now

        float now = Elapsed();
        while (queue.Count > 0 && leaveTimes.Count > 0 && now >= leaveTimes[0])
        {
            var s = queue[0];
            queue.RemoveAt(0);
            leaveTimes.RemoveAt(0);
            if (s == null || !s.isActiveAndEnabled) continue;
            SendOut(s);
        }
    }

    void BuildSchedule()
    {
        scheduled = true;
        if (exitPoint == null) { Debug.LogError("[Departure] Assign an Exit Point (an empty at the door)."); enabled = false; return; }

        queue.AddRange(StudentAmbient.All);
        for (int i = queue.Count - 1; i > 0; i--)             // random order
        {
            int j = Random.Range(0, i + 1);
            (queue[i], queue[j]) = (queue[j], queue[i]);
        }
        int leaving = Mathf.Max(0, queue.Count - stayUntilEnd);
        queue.RemoveRange(leaving, queue.Count - leaving);

        float duration = Duration();
        float start = duration * firstLeavesAt;
        float end = Mathf.Max(start, duration - allGoneWithSecondsLeft);
        float gap = leaving > 1 ? (end - start) / (leaving - 1) : 0f;

        for (int i = 0; i < leaving; i++)
        {
            float jitter = (i == 0 || i == leaving - 1) ? 0f : Random.Range(-timingJitter, timingJitter) * gap;
            leaveTimes.Add(start + gap * i + jitter);
        }
        leaveTimes.Sort();

        if (logEvents) Debug.Log($"[Departure] {leaving} of {StudentAmbient.All.Count} students will leave between {start:0}s and {end:0}s.");
    }

    // Public so a distraction skill can send one student out on demand.
    // Returns false when there's nowhere to send them, so the caller can decide
    // whether the skill was spent.
    public Transform ExitPoint => exitPoint;

    public bool SendOut(StudentAmbient s)
    {
        if (s == null || exitPoint == null) return false;

        var leaver = s.GetComponent<StudentLeaver>();
        if (leaver == null) leaver = s.gameObject.AddComponent<StudentLeaver>();

        var route = handInPoint != null ? new[] { handInPoint, exitPoint } : new[] { exitPoint };
        Transform front = frontOfRoom != null ? frontOfRoom : handInPoint;
        Vector3 towardFront = front != null ? front.position - s.transform.position : Vector3.forward;
        leaver.Begin(route, walkController, walkSpeed, handInPoint != null ? handInPause : 0f, towardFront);

        if (logEvents) Debug.Log($"[Departure] {s.name} is leaving ({StudentAmbient.All.Count} still sitting).");
        return true;
    }
}
