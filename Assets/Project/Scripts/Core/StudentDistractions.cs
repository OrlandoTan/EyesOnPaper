using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// Two ways to buy yourself a moment.
///
///   Q  Pencil drop  - signal a student to drop their pencil. The invigilator
///                     goes to help. Short, and it recharges.
///   F  Shout        - set someone off properly. They get escorted out, which
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

    [Header("Call")]
    [SerializeField] private Key callKey = Key.G;
    [SerializeField] private float callDuration = 12f;
    [Tooltip("Uses per exam.")]
    [SerializeField] private int callUses = 2;
    [Tooltip("The phone rings for this long before the invigilator reacts.")]
    [SerializeField] private float ringDelay = 1.5f;
    [Tooltip("You ring them on your own phone, so it has to be out.")]
    [SerializeField] private bool callNeedsPhoneOut = true;
    [Tooltip("Seconds before another call can be placed.")]
    [SerializeField] private float callCooldown = 45f;

    [Header("Dialling")]
    [Tooltip("The sequence to dial. U D L R - fixed, so it can be learned by heart.")]
    [SerializeField] private string dialPattern = "ULDR";
    [Tooltip("Give up if the whole sequence isn't entered in this long.")]
    [SerializeField] private float dialTimeout = 6f;
    [Tooltip("Colour of the dial panel. Keep it distinct from the phone's answer combo.")]
    [SerializeField] private Color dialAccent = new Color(0.35f, 0.78f, 1f);

    [Header("Shout")]
    [SerializeField] private Key shoutKey = Key.F;
    [SerializeField] private float shoutDuration = 16f;
    [Tooltip("Uses for the whole exam. One is the point.")]
    [SerializeField] private int shoutUses = 1;

    [Header("Choosing a student")]
    [Tooltip("Never pick someone this close to you - the idea is to send them away.")]
    [SerializeField] private float minDistanceFromPlayer = 5f;
    [Tooltip("Pick the student furthest from you rather than at random.")]
    [SerializeField] private bool preferFarthest = true;

    [Header("Audio")]
    [SerializeField] private AudioClip pencilClip;
    [SerializeField] private AudioClip callClip;
    [SerializeField] private AudioClip shoutClip;
    [SerializeField, Range(0f, 1f)] private float pencilVolume = 0.45f;
    [SerializeField, Range(0f, 1f)] private float callVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float shoutVolume = 0.9f;
    [Tooltip("Pitch the pencil up to make it sound like a small object.")]
    [SerializeField] private float pencilPitch = 1.45f;
    [SerializeField] private float audioMaxDistance = 28f;

    [Header("Refs")]
    [SerializeField] private InvigilatorController invigilator;
    [SerializeField] private VisionCone vision;
    [Tooltip("Optional. Found automatically - lets a shout actually walk the student out.")]
    [SerializeField] private ClassDeparture departure;
    [SerializeField] private SuspicionMeter suspicion;

    [Header("UI")]
    [SerializeField] private bool showPrompts = true;
    [Tooltip("On-screen state readout plus console logs. Off for builds.")]
    [SerializeField] private bool logEvents = false;

    public float PencilCooldownRemaining => Mathf.Max(0f, pencilReadyAt - Time.time);
    public bool PencilReady => PencilCooldownRemaining <= 0f;
    public int ShoutsLeft => shoutsLeft;

    private float pencilReadyAt;
    private int shoutsLeft;
    private int callsLeft;
    private float callReadyAt;
    private bool dialling;
    private int dialProgress;
    private float dialUntil;
    private ComboInput[] phoneCombos;
    private bool[] comboWasAccepting;
    private ComboInput dialOwner;
    private bool sawStudents;
    private int[] dialSequence = new int[0];
    private float hintUntil;
    private string hint = "";
    private AudioSource sfx;
    private Texture2D pixel;

    public int CallsLeft => callsLeft;
    public float CallCooldownRemaining => Mathf.Max(0f, callReadyAt - Time.time);
    public bool CallReady => callsLeft > 0 && CallCooldownRemaining <= 0f;

    private void Awake()
    {
        if (invigilator == null) invigilator = FindAnyObjectByType<InvigilatorController>();
        if (vision == null) vision = FindAnyObjectByType<VisionCone>();
        if (departure == null) departure = FindAnyObjectByType<ClassDeparture>();
        if (suspicion == null) suspicion = FindAnyObjectByType<SuspicionMeter>();

        shoutsLeft = shoutUses;
        callsLeft = callUses;
        dialSequence = ParsePattern(dialPattern);

        // One 3D source, moved to whoever is making the noise. Only ever one
        // distraction at a time, so one source is enough.
        var holder = new GameObject("DistractionAudio");
        holder.transform.SetParent(transform, false);
        sfx = holder.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 1f;
        sfx.rolloffMode = AudioRolloffMode.Linear;
        sfx.minDistance = 2f;
        sfx.maxDistance = audioMaxDistance;
        sfx.dopplerLevel = 0f;

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

        // Once the class exists, an empty room means there is genuinely nobody
        // left to drop a pencil, take a call, or kick off. No students at all
        // means a greybox test scene, where the fallback point is still useful.
        if (StudentAmbient.All.Count > 0) sawStudents = true;

        if (kb[pencilKey].wasPressedThisFrame && PencilReady)
        {
            if (!HaveTarget(out string why)) Reject(why);
            else if (Trigger(pencilDuration, false, pencilClip, pencilVolume, pencilPitch))
                pencilReadyAt = Time.time + pencilCooldown;
        }

        if (dialling)
        {
            UpdateDial(kb);
            return;                     // arrows belong to the dial right now
        }

        if (kb[callKey].wasPressedThisFrame)
        {
            if (callsLeft <= 0) Reject("no calls left");
            else if (CallCooldownRemaining > 0f) Reject($"line's busy - {CallCooldownRemaining:0}s");
            else if (callNeedsPhoneOut && (suspicion == null || !suspicion.PhoneIsOut))
                Reject("hold Space - you need your phone to call");
            else if (invigilator.IsDistracted) Reject("they're already busy");
            else if (dialSequence.Length == 0) Reject("dial pattern is not set");
            else if (!HaveTarget(out string why)) Reject(why);
            else
            {
                BeginDial();
                if (logEvents) Debug.Log($"[Skills] dialling started, pattern '{dialPattern}', " +
                                         $"{(phoneCombos == null ? 0 : phoneCombos.Length)} phone combo(s) muted");
            }
        }

        if (kb[shoutKey].wasPressedThisFrame && shoutsLeft > 0)
        {
            if (!HaveTarget(out string why)) Reject(why);
            else if (Trigger(shoutDuration, true, shoutClip, shoutVolume, 1f))
                shoutsLeft--;
        }
    }

    /// <summary>Is there anyone left worth signalling, and can we say why not?</summary>
    private bool HaveTarget(out string why)
    {
        why = null;
        if (!sawStudents) return true;              // greybox scene: fallback point is fine

        int total = CountStudents(out int farEnough);

        if (total == 0)     { why = "the room's empty - nobody left to signal"; return false; }
        if (farEnough == 0) { why = "everyone left is too close to you";        return false; }
        return true;
    }

    private int CountStudents(out int farEnough)
    {
        farEnough = 0;
        int total = 0;
        Vector3 player = PlayerPosition();

        foreach (StudentAmbient student in StudentAmbient.All)
        {
            if (student == null || !student.isActiveAndEnabled) continue;
            if (student.GetComponent<StudentLeaver>() is StudentLeaver leaver && leaver.IsLeaving) continue;

            total++;
            if (Vector3.Distance(student.transform.position, player) >= minDistanceFromPlayer) farEnough++;
        }

        return total;
    }

    private Vector3 PlayerPosition() =>
        vision != null && vision.HasPlayer ? vision.PlayerRoot.position : transform.position;

    // ---------- dialling ----------

    private void BeginDial()
    {
        dialling = true;
        dialProgress = 0;
        dialUntil = Time.time + dialTimeout;

        // The phone combos use the same arrow keys, so borrow them the way
        // JackMessenger borrows them from the Notes combo, and give them back after.
        phoneCombos = FindObjectsByType<ComboInput>(FindObjectsInactive.Include);
        comboWasAccepting = new bool[phoneCombos.Length];
        for (int i = 0; i < phoneCombos.Length; i++)
        {
            comboWasAccepting[i] = phoneCombos[i].AcceptInput;
            phoneCombos[i].AcceptInput = false;
        }

        // AcceptInput isn't enough on its own: SelfCheat.Retarget() sets it back to
        // true whenever the phone is shown or unpaused. ComboInput's static
        // inputOwner is the real lock, and nothing else touches it per-frame.
        if (phoneCombos.Length > 0)
        {
            dialOwner = phoneCombos[0];
            dialOwner.TakeInput();
        }
    }

    private void EndDial()
    {
        dialling = false;

        if (phoneCombos != null)
        {
            for (int i = 0; i < phoneCombos.Length; i++)
                if (phoneCombos[i] != null) phoneCombos[i].AcceptInput = comboWasAccepting[i];
        }

        // Releasing also starts ComboInput's 0.3s grace, so the last dial arrow
        // can't leak into the Notes combo.
        if (dialOwner != null) dialOwner.ReleaseInput();
        dialOwner = null;

        phoneCombos = null;
        comboWasAccepting = null;
    }

    private void UpdateDial(Keyboard kb)
    {
        // Putting the phone away hangs up, same as it resets the other combos.
        if (callNeedsPhoneOut && suspicion != null && !suspicion.PhoneIsOut)
        {
            EndDial();
            Hint("hung up");
            return;
        }

        if (Time.time > dialUntil || kb[callKey].wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
        {
            EndDial();
            Hint("call cancelled");
            return;
        }

        int arrow = ReadArrow(kb);
        if (arrow < 0) return;

        if (arrow == ArrowAt(dialProgress))
        {
            dialProgress++;
            if (dialProgress < dialSequence.Length) return;

            EndDial();
            if (TriggerCall())
            {
                callsLeft--;
                callReadyAt = Time.time + callCooldown;
            }
            return;
        }

        dialProgress = 0;                // wrong key: start the sequence again
        Hint("misdialled");
    }

    /// <summary>
    /// Turns the pattern string into arrows once, dropping anything that is not
    /// U/D/L/R. A stray character used to make the sequence impossible to finish,
    /// and an empty field indexed straight off the end of the string.
    /// </summary>
    private static int[] ParsePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return new int[0];

        var arrows = new System.Collections.Generic.List<int>(pattern.Length);
        foreach (char raw in pattern)
        {
            int a = char.ToUpperInvariant(raw) switch { 'U' => 0, 'D' => 1, 'L' => 2, 'R' => 3, _ => -1 };
            if (a >= 0) arrows.Add(a);
        }

        return arrows.ToArray();
    }

    private int ArrowAt(int index) =>
        index >= 0 && index < dialSequence.Length ? dialSequence[index] : -1;

    private static int ReadArrow(Keyboard kb)
    {
        if (kb.upArrowKey.wasPressedThisFrame    || kb.wKey.wasPressedThisFrame) return 0;
        if (kb.downArrowKey.wasPressedThisFrame  || kb.sKey.wasPressedThisFrame) return 1;
        if (kb.leftArrowKey.wasPressedThisFrame  || kb.aKey.wasPressedThisFrame) return 2;
        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) return 3;
        return -1;
    }

    private void Hint(string message)
    {
        hint = message;
        hintUntil = Time.time + 2f;
    }

    private void Reject(string why)
    {
        Hint(why);
        if (logEvents) Debug.Log($"[Skills] call refused: {why}  (phone out: " +
                                 $"{(suspicion != null && suspicion.PhoneIsOut)})");
    }

    /// <summary>
    /// The phone rings first, then they get dealt with. The gap is what makes it
    /// read as cause and effect rather than a button press.
    /// </summary>
    private bool TriggerCall()
    {
        if (invigilator.IsDistracted) return false;
        if (!TryPickStudent(out Vector3 point, out StudentAmbient student)) return false;

        Play(callClip, point, callVolume, 1f);
        StartCoroutine(CallRoutine(point, student));
        return true;
    }

    private System.Collections.IEnumerator CallRoutine(Vector3 point, StudentAmbient student)
    {
        yield return new WaitForSeconds(ringDelay);

        if (invigilator == null || invigilator.IsStopped) yield break;

        Transform escortTo = null;
        if (student != null && departure != null && departure.SendOut(student))
            escortTo = departure.ExitPoint;

        invigilator.Distract(point, callDuration, escortTo);
    }

    private bool Playable()
    {
        if (invigilator == null || invigilator.IsStopped) return false;
        return GameManager.Instance == null || GameManager.Instance.ExamRunning;
    }

    /// <summary>False if there was nowhere sensible to send them, so nothing is spent.</summary>
    private bool Trigger(float duration, bool escort, AudioClip clip, float volume, float pitch)
    {
        if (invigilator.IsDistracted) return false;
        if (!TryPickStudent(out Vector3 point, out StudentAmbient student)) return false;

        Play(clip, point, volume, pitch);

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

        Vector3 player = PlayerPosition();

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

        if (sawStudents)
        {
            point = Vector3.zero;
            return false;           // real classroom, and there's nobody to signal
        }

        return TryFallbackPoint(player, out point);
    }

    private void Play(AudioClip clip, Vector3 where, float volume, float pitch)
    {
        if (clip == null || sfx == null) return;

        sfx.transform.position = where + Vector3.up * 1f;
        sfx.pitch = pitch;
        sfx.PlayOneShot(clip, volume);
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

        bool anyone = HaveTarget(out string blocked);
        float y = Screen.height - 108f;

        if (!anyone)
        {
            var warn = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            warn.normal.textColor = new Color(1f, 0.6f, 0.5f);
            GUI.Label(new Rect(16f, y - 40f, 420f, 20f), blocked, warn);
        }
        Slot(new Rect(16f, y, 190f, 26f), $"[{pencilKey}] pencil drop",
             !anyone ? "-" : PencilReady ? "ready" : $"{PencilCooldownRemaining:0}s",
             anyone && PencilReady);

        bool phoneOut = suspicion != null && suspicion.PhoneIsOut;
        string callState = callsLeft <= 0 ? "spent"
                         : CallCooldownRemaining > 0f ? $"{CallCooldownRemaining:0}s"
                         : (!callNeedsPhoneOut || phoneOut) ? $"x{callsLeft}"
                         : "phone out";

        Slot(new Rect(16f, y + 30f, 190f, 26f), $"[{callKey}] call",
             !anyone ? "-" : callState,
             anyone && CallReady && (!callNeedsPhoneOut || phoneOut));

        if (dialling) DrawDial();

        if (logEvents)
        {
            var dbg = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            dbg.normal.textColor = new Color(0.6f, 0.9f, 1f);
            string state = GameManager.Instance != null ? GameManager.Instance.Current.ToString() : "no GM";
            GUI.Label(new Rect(16f, y - 22f, 520f, 20f),
                      $"dial:{(dialling ? "ON " + dialProgress + "/" + dialSequence.Length : "off")}  " +
                      $"phone:{(suspicion != null && suspicion.PhoneIsOut ? "T" : "F")}  " +
                      $"calls:{callsLeft}  cd:{CallCooldownRemaining:0}  " +
                      $"busy:{(invigilator != null && invigilator.IsDistracted ? "T" : "F")}  " +
                      $"state:{state}", dbg);
        }

        Slot(new Rect(16f, y + 60f, 190f, 26f), $"[{shoutKey}] shout",
             !anyone ? "-" : shoutsLeft > 0 ? "once only" : "spent",
             anyone && shoutsLeft > 0);

        if (Time.time < hintUntil)
        {
            var hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(0f, Screen.height * 0.72f, Screen.width, 24f), hint, hintStyle);
        }

        if (invigilator != null && invigilator.IsDistracted)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = new Color(0.5f, 0.95f, 0.55f);
            GUI.Label(new Rect(0f, Screen.height * 0.16f, Screen.width, 28f),
                      $"they're distracted - {invigilator.DistractionRemaining:0.0}s", style);
        }
    }

    private void DrawDial()
    {
        string[] glyphs = { "\u2191", "\u2193", "\u2190", "\u2192" };

        int count = Mathf.Max(1, dialSequence.Length);
        float cell = 56f;
        float pad = 18f;
        float width = Mathf.Max(320f, count * cell + pad * 2f);
        float height = 132f;

        // High on the screen and on its own dark panel, so it can never be
        // confused with the answer combo on the phone - that one is white on
        // white down by your hands.
        var panel = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.10f, width, height);

        Fill(new Rect(panel.x + 4f, panel.y + 5f, panel.width, panel.height), new Color(0f, 0f, 0f, 0.35f));
        Fill(panel, new Color(0.06f, 0.07f, 0.10f, 0.94f));
        Fill(new Rect(panel.x, panel.y, panel.width, 3f), dialAccent);

        var title = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = dialAccent;
        GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 20f), "CALLING A CLASSMATE", title);

        var arrow = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
        float startX = panel.center.x - (count - 1) * cell * 0.5f;
        float y = panel.y + 40f;

        for (int i = 0; i < dialSequence.Length; i++)
        {
            int a = ArrowAt(i);
            if (a < 0) continue;

            var slot = new Rect(startX + i * cell - cell * 0.42f, y, cell * 0.84f, 52f);

            bool done = i < dialProgress;
            bool next = i == dialProgress;

            Fill(slot, done ? new Color(0.18f, 0.45f, 0.22f, 0.9f)
                    : next ? new Color(dialAccent.r, dialAccent.g, dialAccent.b, 0.22f)
                           : new Color(1f, 1f, 1f, 0.06f));

            if (next) Fill(new Rect(slot.x, slot.yMax - 3f, slot.width, 3f), dialAccent);

            arrow.normal.textColor = done ? new Color(0.55f, 1f, 0.6f)
                                   : next ? Color.white
                                          : new Color(1f, 1f, 1f, 0.35f);
            GUI.Label(slot, glyphs[a], arrow);
        }

        var footer = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
        footer.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        GUI.Label(new Rect(panel.x, panel.yMax - 26f, panel.width, 18f),
                  $"your answer combo is paused  ·  {callKey} or Esc to hang up  ·  {Mathf.Max(0f, dialUntil - Time.time):0.0}s",
                  footer);
    }

    private void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, pixel);
        GUI.color = Color.white;
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
