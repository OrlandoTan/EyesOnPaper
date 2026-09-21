using UnityEngine;
using UnityEngine.InputSystem;

public class SeatedLook : MonoBehaviour
{
    [Header("Free look")]
    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float yawLimit = 150f;
    [SerializeField] float pitchUp = 30f;
    [SerializeField] float pitchDown = 65f;     // enough to read the papers, not to see the phone
    [SerializeField] float startPitch = 20f;

    [Header("Lean in (hold right mouse)")]
    [SerializeField] float leanFOV = 38f;       // zoomed view for reading the papers
    [SerializeField] float leanSpeed = 8f;

    [Header("Phone look")]
    [SerializeField] Transform phoneFocus;      // drag PhoneFocus here
    [SerializeField] float phoneFOV = 22f;      // zoomed-in field of view
    [SerializeField] float transitionTime = 0.25f;

    Camera cam;
    float freeFOV;
    float yaw, pitch;
    float blend;          // 0 = free look, 1 = locked on phone
    float leanBlend;      // 0 = normal, 1 = leaning in
    bool phoneMode;

    public bool IsLookingAtPhone => blend >= 0.99f;

    void Awake()
    {
        cam = GetComponentInChildren<Camera>();
        freeFOV = cam.fieldOfView;
    }

    void OnEnable()
    {
        pitch = startPitch;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        GameEvents.OnPhoneShown += EnterPhone;
        GameEvents.OnPhoneHidden += ExitPhone;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GameEvents.OnPhoneShown -= EnterPhone;
        GameEvents.OnPhoneHidden -= ExitPhone;
    }

    void EnterPhone() => phoneMode = true;
    void ExitPhone() => phoneMode = false;

    void Update()
    {
        bool leanHeld = !phoneMode && Mouse.current != null && Mouse.current.rightButton.isPressed;
        leanBlend = Mathf.MoveTowards(leanBlend, leanHeld ? 1f : 0f, Time.deltaTime * leanSpeed);
        float leanEased = Mathf.SmoothStep(0f, 1f, leanBlend);

        if (!phoneMode && Mouse.current != null)
        {
            // Slower look while zoomed so the reticle is easy to aim
            float zoomScale = Mathf.Lerp(1f, leanFOV / Mathf.Max(1f, freeFOV), leanEased);
            Vector2 d = Mouse.current.delta.ReadValue() * sensitivity * zoomScale;
            yaw = Mathf.Clamp(yaw + d.x, -yawLimit, yawLimit);
            pitch = Mathf.Clamp(pitch - d.y, -pitchUp, pitchDown);
        }

        blend = Mathf.MoveTowards(blend, phoneMode ? 1f : 0f, Time.deltaTime / transitionTime);
        float eased = Mathf.SmoothStep(0f, 1f, blend);

        Quaternion freeLook = Quaternion.Euler(pitch, yaw, 0f);
        Quaternion phoneLook = PhoneLookRotation();

        transform.localRotation = Quaternion.Slerp(freeLook, phoneLook, eased);
        float baseFOV = Mathf.Lerp(freeFOV, leanFOV, leanEased);
        cam.fieldOfView = Mathf.Lerp(baseFOV, phoneFOV, eased);
    }

    // Rotation (local to PlayerRig) that points Head straight at the phone
    Quaternion PhoneLookRotation()
    {
        if (phoneFocus == null) return Quaternion.Euler(80f, 0f, 0f);
        Vector3 worldDir = phoneFocus.position - transform.position;
        Vector3 localDir = transform.parent.InverseTransformDirection(worldDir);
        return Quaternion.LookRotation(localDir, Vector3.up);
    }
}