using UnityEngine;

// Exam-hall atmosphere. Everything that comes from other people fades out as they leave,
// so once you're the last one in the room you get silence (plus the room tone and clock).
// Put this on an empty object in Main, e.g. "Ambience". Nothing else needs setting up:
// it creates its own AudioSources.
public class RoomAmbience : MonoBehaviour
{
    [Header("Always on")]
    [Tooltip("Air-con / building hum. 2D, quiet.")]
    [SerializeField] AudioClip roomTone;
    [SerializeField, Range(0f, 1f)] float roomToneVolume = 0.15f;

    [Tooltip("Wall clock. Positional - drag the clock object in.")]
    [SerializeField] AudioClip clockTick;
    [SerializeField] Transform clockPosition;
    [SerializeField, Range(0f, 1f)] float clockVolume = 0.2f;
    [SerializeField] float clockRange = 12f;

    [Header("The class (fades out as students leave)")]
    [Tooltip("Looping scribbling. Volume follows how many students are still seated.")]
    [SerializeField] AudioClip writingLoop;
    [SerializeField, Range(0f, 1f)] float writingVolumeFull = 0.28f;
    [SerializeField] Transform classCentre;         // optional: defaults to this object
    [SerializeField] float writingRange = 14f;
    [SerializeField] float writingFadeSpeed = 0.5f;

    [Header("One-shots from remaining students (never when the room is empty)")]
    [SerializeField] AudioClip[] coughs;
    [SerializeField] AudioClip[] paperShuffles;
    [SerializeField] Vector2 oneShotEvery = new Vector2(14f, 32f);
    [SerializeField, Range(0f, 1f)] float oneShotVolume = 0.5f;

    AudioSource tone, clock, writing;
    float nextOneShot;
    int startingStudents = -1;
    bool stopped;

    void OnEnable()
    {
        GameEvents.OnCaught    += StopPeople;
        GameEvents.OnExamEnded += StopPeople;
    }

    void OnDisable()
    {
        GameEvents.OnCaught    -= StopPeople;
        GameEvents.OnExamEnded -= StopPeople;
    }

    void Start()
    {
        tone = MakeSource("RoomTone", roomTone, roomToneVolume, spatial: false, position: transform.position, range: 0f);
        clock = MakeSource("ClockTick", clockTick, clockVolume, spatial: true,
                           position: clockPosition != null ? clockPosition.position : transform.position, range: clockRange);
        writing = MakeSource("ClassWriting", writingLoop, 0f, spatial: true,
                             position: classCentre != null ? classCentre.position : transform.position, range: writingRange);

        nextOneShot = Time.time + Random.Range(oneShotEvery.x, oneShotEvery.y);
    }

    void Update()
    {
        int seated = StudentAmbient.All.Count;
        if (startingStudents < 0 && seated > 0) startingStudents = seated;   // first frame the class exists

        // Scribbling follows how full the room is, and reaches silence when you're alone.
        if (writing != null)
        {
            float target = stopped || startingStudents <= 0 ? 0f
                         : writingVolumeFull * Mathf.Clamp01(seated / (float)startingStudents);
            writing.volume = Mathf.MoveTowards(writing.volume, target, Time.deltaTime * writingFadeSpeed);
        }

        if (stopped || seated <= 0) return;                                  // nobody left to cough

        if (Time.time >= nextOneShot)
        {
            nextOneShot = Time.time + Random.Range(oneShotEvery.x, oneShotEvery.y);
            PlayFromRandomStudent();
        }
    }

    void PlayFromRandomStudent()
    {
        var students = StudentAmbient.All;
        if (students.Count == 0) return;

        var who = students[Random.Range(0, students.Count)];
        if (who == null) return;

        AudioClip clip = PickClip();
        if (clip == null) return;

        AudioSource.PlayClipAtPoint(clip, who.LookPoint, oneShotVolume);
    }

    AudioClip PickClip()
    {
        int coughCount = coughs != null ? coughs.Length : 0;
        int shuffleCount = paperShuffles != null ? paperShuffles.Length : 0;
        int total = coughCount + shuffleCount;
        if (total == 0) return null;

        int i = Random.Range(0, total);
        return i < coughCount ? coughs[i] : paperShuffles[i - coughCount];
    }

    void StopPeople()
    {
        stopped = true;                                                      // exam over: the class goes quiet
    }

    AudioSource MakeSource(string name, AudioClip clip, float volume, bool spatial, Vector3 position, float range)
    {
        if (clip == null) return null;

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;

        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = volume;
        src.spatialBlend = spatial ? 1f : 0f;
        if (spatial)
        {
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = Mathf.Max(2f, range);
        }
        src.Play();
        return src;
    }
}
