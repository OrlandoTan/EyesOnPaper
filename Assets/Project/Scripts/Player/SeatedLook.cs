using UnityEngine;
using UnityEngine.InputSystem;

public class SeatedLook : MonoBehaviour
{
    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float yawLimit = 70f;     // left/right
    [SerializeField] float pitchUp = 30f;      // how far you can look up
    [SerializeField] float pitchDown = 40f;    // how far you can look down (desk/phone)
    [SerializeField] float startPitch = 20f;   // start looking down at the paper

    float yaw, pitch;

    void OnEnable()
    {
        pitch = startPitch;
        yaw = 0f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue() * sensitivity;
        yaw = Mathf.Clamp(yaw + delta.x, -yawLimit, yawLimit);
        pitch = Mathf.Clamp(pitch - delta.y, -pitchUp, pitchDown);

        transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
    }
}
