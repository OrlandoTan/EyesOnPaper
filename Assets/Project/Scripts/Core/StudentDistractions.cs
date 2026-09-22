using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Two ways to buy yourself a moment.
///
///   Q  Pencil drop  — signal a student to drop their pencil. The invigilator
///                     goes to help. Short, and it recharges.
///   F  Shout        — set someone off properly. They get escorted out, which
///                     takes much longer. Once per exam, and that's it.
///
/// The invigilator ignores suspicion entirely while distracted, so these are the
/// windows where a long arrow combo stops being suicide.
/// </summary>
public class StudentDistractions : MonoBehaviour
{
    [Header("Pencil drop")]
    [SerializeField] private Key pencilKey = Key.Q;
    [SerializeField] private float pencilDuration = 6f;
    [SerializeField] private float pencilCooldown = 30f;

    [Header("Shout")]
    [SerializeField] private Key shoutKey = Key.F;
    [SerializeField] private float shoutDuration = 16f;
    [Tooltip("Uses for the whole exam. One is the point.")]
    [SerializeField] private int shoutUses = 1;

    [Header("Choosing a student")]
    [Tooltip("Never pick someone this close to you — the idea is to send them away.")]
    [SerializeField] private float minDistanceFromPlayer = 5f;
    [Tooltip("Pick the student furthest from you rather than at random.")]
    [SerializeField] private bool preferFarthest = true;

    [Header("Refs")]
    [SerializeField] private InvigilatorController invigilator;
    [SerializeField] private VisionCone vision;
    [Tooltip("Optional. Found automatically — lets a shout actually walk the student out.")]
    [SerializeField] private ClassDeparture departure;

    [Header("UI")]
    [SerializeField] private bool showPrompts = true;

    public float PencilCooldownRemaining => Mathf.Max(0f, pencilReadyAt - Time.time);
    public bool PencilReady => PencilCooldownRemaining <= 0f;
    public int ShoutsLeft => shoutsLeft;

    private float pencilReadyAt;
    private int shoutsLeft;
    private Texture2D pixel;

    private void Awake()
    {
        if (invigilator == null) invigilator = FindAnyObjectByType<InvigilatorController>();
        if (vision == null) vision = FindAnyObjectByType<VisionCone>();
        if (departure == null) departure = FindAnyObjectByType<ClassDeparture>();

        shoutsLeft = shoutUses;

        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();
    }

    private void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }

    private void Update()
    {
        if (!Playable()) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb[pencilKey].wasPressedThisFrame && PencilReady)
        {
            if (Trigger(pencilDuration, false))
                pencilReadyAt = Time.time + pencilCooldown;
        }

        if (kb[shoutKey].wasPressedThisFrame && shoutsLeft > 0)
        {
            if (Trigger(shoutDuration, true))
                shoutsLeft--;
        }
    }

    private bool Playable()
    {
        if (invigilator == null || invigilator.IsStopped) return false;
        return GameManager.Instance == null || GameManager.Instance.ExamRunning;
    }

    /// <summary>False if there was nowhere sensible to send them, so nothing is spent.</summary>
    private bool Trigger(float duration, bool escort)
    {
        if (invigilator.IsDistracted) return false;
        if (!TryPickStudent(out Vector3 point, out StudentAmbient student)) return false;

        Transform escortTo = null;

        // A shout gets the student properly thrown out, reusing ClassDeparture's
        // own route (stand up, hand the paper in, out the door). The invigilator
        // only follows them to the door if that actually started.
        if (escort && student != null && departure != null && departure.SendOut(student))
            escortTo = departure.ExitPoint;

        invigilator.Distract(point, duration, escortTo);
        return true;
    }

    private bool TryPickStudent(out Vector3 point, out StudentAmbient student)
    {
        student = null;

        Vector3 player = vision != null && vision.HasPlayer
            ? vision.PlayerRoot.position
            : transform.position;

        Vector3 best = Vector3.zero;
        float bestScore = -1f;

        // Explicit markers win, so you can hand-pick who's available.
        DistractionTarget[] marked = FindObjectsByType<DistractionTarget>(FindObjectsInactive.Exclude);
        if (marked.Length > 0)
        {
            foreach (DistractionTarget marker in marked)
            {
                if (!marker.Available) continue;
                Consider(marker.transform.position, player, ref best, ref bestScore);
            }
        }
        else
        {
            // Otherwise use the class itself. ClassroomStudents generates these at
            // runtime, so there is nothing to place or tag by hand.
            foreach (StudentAmbient candidate in StudentAmbient.All)
            {
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                if (candidate.GetComponent<StudentLeaver>() is StudentLeaver leaver && leaver.IsLeaving) continue;

                float before = bestScore;
                Consider(candidate.transform.position, player, ref best, ref bestScore);
                if (bestScore > before) student = candidate;
            }
        }

        if (bestScore >= 0f)
        {
            point = best;
            return true;
        }

        return TryFallbackPoint(player, out point);
    }

    private void Consider(Vector3 candidate, Vector3 player, ref Vector3 best, ref float bestScore)
    {
        float distance = Vector3.Distance(candidate, player);
        if (distance < minDistanceFromPlayer) return;

        float score = preferFarthest ? distance : Random.value;
        if (score <= bestScore) return;

        bestScore = score;
        best = candidate;
    }

    /// <summary>
    /// No students in the room yet: send them to a spot on the NavMesh well away from the
    /// player, so the skills still work in a greybox room.
    /// </summary>
    private bool TryFallbackPoint(Vector3 player, out Vector3 point)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector2 disc = Random.insideUnitCircle.normalized * Random.Range(minDistanceFromPlayer, minDistanceFromPlayer + 6f);
            Vector3 candidate = player + new Vector3(disc.x, 0f, disc.y);

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas) &&
                Vector3.Distance(hit.position, player) >= minDistanceFromPlayer)
            {
                point = hit.position;
                return true;
            }
        }

        point = Vector3.zero;
        return false;
    }

    private void OnGUI()
    {
        if (!showPrompts) return;
        if (GameManager.Instance != null && !GameManager.Instance.ExamRunning) return;

        float y = Screen.height - 78f;
        Slot(new Rect(16f, y, 190f, 26f), $"[{pencilKey}] pencil drop",
             PencilReady ? "ready" : $"{PencilCooldownRemaining:0}s",
             PencilReady);

        Slot(new Rect(16f, y + 30f, 190f, 26f), $"[{shoutKey}] shout",
             shoutsLeft > 0 ? "once only" : "spent",
             shoutsLeft > 0);

        if (invigilator != null && invigilator.IsDistracted)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(0.5f, 0.95f, 0.55f);
            GUI.Label(new Rect(0f, Screen.height * 0.16f, Screen.width, 28f),
                      $"they're distracted — {invigilator.DistractionRemaining:0.0}s", style);
        }
    }

    private void Slot(Rect rect, string label, string state, bool ready)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(rect, pixel);
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        style.normal.textColor = ready ? Color.white : new Color(1f, 1f, 1f, 0.45f);

        GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f), $"{label}   {state}", style);
    }
}
