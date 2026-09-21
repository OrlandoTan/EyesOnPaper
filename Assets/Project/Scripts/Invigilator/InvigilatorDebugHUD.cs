using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug only — the suspicion bar plus keys to fake the player half.
///
///   P      fake phone in / out   (fires PhoneShown / PhoneHidden)
///   B      fake buzz             (fires Buzz)
///   C      fake caught           (fires Caught)
///   [ / ]  nudge suspicion by 10
///
/// The real phone fires the same events, so once the player half is in the scene
/// untick Enable Debug Keys — otherwise P fights PhoneController over the state.
/// </summary>
public class InvigilatorDebugHUD : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SuspicionMeter suspicion;
    [SerializeField] private VisionCone vision;
    [SerializeField] private InvigilatorController invigilator;

    [Header("Toggles")]
    [SerializeField] private bool showBar = true;
    [SerializeField] private bool enableDebugKeys = true;

    private bool fakePhoneOut;
    private Texture2D pixel;

    private void Awake()
    {
        if (suspicion == null)   suspicion = GetComponent<SuspicionMeter>();
        if (vision == null)      vision = GetComponentInChildren<VisionCone>();
        if (invigilator == null) invigilator = GetComponent<InvigilatorController>();

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
        if (!enableDebugKeys) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.pKey.wasPressedThisFrame)
        {
            fakePhoneOut = !fakePhoneOut;
            if (fakePhoneOut) GameEvents.PhoneShown();
            else GameEvents.PhoneHidden();
        }

        if (kb.bKey.wasPressedThisFrame) GameEvents.Buzz();
        if (kb.cKey.wasPressedThisFrame) GameEvents.Caught();

        if (suspicion != null)
        {
            if (kb.leftBracketKey.wasPressedThisFrame)  suspicion.Add(-10f);
            if (kb.rightBracketKey.wasPressedThisFrame) suspicion.Add(10f);
        }
    }

    private void OnGUI()
    {
        if (!showBar || suspicion == null) return;

        const float width = 260f;
        const float height = 18f;
        var back = new Rect(12f, 12f, width, height);

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(back, pixel);

        GUI.color = suspicion.CurrentTier switch
        {
            SuspicionMeter.Tier.High   => new Color(1f, 0.25f, 0.2f, 0.9f),
            SuspicionMeter.Tier.Medium => new Color(1f, 0.75f, 0.2f, 0.9f),
            _                          => new Color(0.4f, 0.85f, 0.4f, 0.9f),
        };
        GUI.DrawTexture(new Rect(back.x, back.y, width * suspicion.Value01, height), pixel);
        GUI.color = Color.white;

        string seen = vision == null ? "no cone"
                    : !vision.HasPlayer ? "no player"
                    : vision.CanSeePlayer ? "SEES PLAYER"
                    : "clear";

        string rate = (suspicion.LastRatePerSecond >= 0f ? "+" : "") +
                      suspicion.LastRatePerSecond.ToString("0.0") + "%/s";
        string lockState = suspicion.IsLocked
            ? $"LOCKED ON  ({suspicion.LockTimeRemaining:0.0}s)"
            : "not locked";

        string lines =
            $"suspicion {suspicion.Value:0}%   ({suspicion.CurrentTier})   {rate}\n" +
            $"vision: {seen}\n" +
            $"{lockState}\n" +
            $"why: {suspicion.LastReason}\n" +
            $"phone out: {suspicion.PhoneIsOut}\n" +
            (invigilator != null && invigilator.IsStopped ? "invigilator: STOPPED\n" : "") +
            (enableDebugKeys ? "P phone   B buzz   C caught   [ ] nudge" : "");

        GUI.Label(new Rect(12f, back.yMax + 4f, 460f, 110f), lines);
    }
}
