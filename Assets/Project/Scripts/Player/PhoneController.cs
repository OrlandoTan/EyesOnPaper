using UnityEngine;
using UnityEngine.InputSystem;

public class PhoneController : MonoBehaviour
{
    [Header("Poses (local to PlayerRig)")]
    [SerializeField] Vector3 hiddenPos = new Vector3(0f, 0.50f, 0.05f);   // in your pocket
    [SerializeField] Vector3 shownPos  = new Vector3(0f, 0.62f, 0.10f);   // must match PhoneFocus
    [SerializeField] Vector3 hiddenRot = new Vector3(90f, 0f, 0f);
    [SerializeField] Vector3 shownRot  = new Vector3(75f, 0f, 0f);
    [SerializeField] float raiseSpeed = 14f;

    [Header("Refs")]
    [SerializeField] SeatedLook look;   // drag Head here

    public bool IsOut { get; private set; }

    // True once the camera has finished tilting down - only then should combo input count
    public bool IsReady => IsOut && look != null && look.IsLookingAtPhone;

    void Start()
    {
        transform.localPosition = hiddenPos;
        transform.localRotation = Quaternion.Euler(hiddenRot);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        bool held = kb.spaceKey.isPressed;
        if (held && !IsOut)      { IsOut = true;  GameEvents.PhoneShown(); }
        else if (!held && IsOut) { IsOut = false; GameEvents.PhoneHidden(); }

        float t = 1f - Mathf.Exp(-raiseSpeed * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, IsOut ? shownPos : hiddenPos, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation,
                                   Quaternion.Euler(IsOut ? shownRot : hiddenRot), t);
    }
}
