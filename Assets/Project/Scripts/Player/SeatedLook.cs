using UnityEngine;
using UnityEngine.InputSystem;

public class SeatedLook : MonoBehaviour
{
    [Header("Free look")]
    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float yawLimit = 70f;
    [SerializeField] float pitchUp = 30f;
    [SerializeField] float pitchDown = 40f;     // can't see under the desk normally
    [SerializeField] float startPitch = 20f;

    [Header("Phone look")]
    [SerializeField] float phonePitch = 80f;    // looking down into your lap
    [SerializeField] float transitionTime = 0.25f;

    float yaw, pitch;
    float blend;          // 0 = free look, 1 = locked on phone
    bool phoneMode;

    public bool IsLookingAtPhone => blend >= 0.99f;

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
        // Mouse-look only when not on the phone
        if (!phoneMode && Mouse.current != null)
        {
            Vector2 d = Mouse.current.delta.ReadValue() * sensitivity;
            yaw = Mathf.Clamp(yaw + d.x, -yawLimit, yawLimit);
            pitch = Mathf.Clamp(pitch - d.y, -pitchUp, pitchDown);
        }

        blend = Mathf.MoveTowards(blend, phoneMode ? 1f : 0f, Time.deltaTime / transitionTime);
        float eased = Mathf.SmoothStep(0f, 1f, blend);

        Quaternion freeLook = Quaternion.Euler(pitch, yaw, 0f);
        Quaternion phoneLook = Quaternion.Euler(phonePitch, 0f, 0f);
        transform.localRotation = Quaternion.Slerp(freeLook, phoneLook, eased);
    }
}