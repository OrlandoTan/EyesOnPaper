using UnityEngine;

/// <summary>
/// When the run ends - caught or time up - switch the player off.
///
/// Without this the exam is over but Space still raises the phone and the arrow
/// keys still run combos behind the end screen.
///
/// Drop this on the GameManager and drag the player-side components you want
/// frozen into the list (PhoneController, ComboInput, SeatedLook). Disabling is
/// enough: ComboInput's OnDisable releases its input and unsubscribes on its own.
///
/// This is deliberately a scene-wired list rather than an edit to the player
/// scripts, so it can't conflict with the other half of the project.
/// </summary>
public class ExamLockout : MonoBehaviour
{
    [Tooltip("Find the player by tag and switch off every script under them. No wiring needed.")]
    [SerializeField] private bool freezeWholePlayer = true;
    [SerializeField] private string playerTag = "Player";

    [Tooltip("Optional. Only needed for objects outside the player hierarchy.")]
    [SerializeField] private GameObject[] freezeScriptsOn;

    [Tooltip("Optional, for picking off one script when a whole object is too blunt.")]
    [SerializeField] private MonoBehaviour[] disableOnEnd;

    [Tooltip("Objects hidden when the exam ends - the phone model, combo UI, and so on.")]
    [SerializeField] private GameObject[] hideOnEnd;

    [Tooltip("Give the mouse back so the end screen can be clicked.")]
    [SerializeField] private bool releaseCursor = true;

    private bool lockedOut;

    private void OnEnable()
    {
        GameEvents.OnCaught    += LockOut;
        GameEvents.OnExamEnded += LockOut;
    }

    private void OnDisable()
    {
        GameEvents.OnCaught    -= LockOut;
        GameEvents.OnExamEnded -= LockOut;
    }

    /// <summary>
    /// Switch off gameplay scripts only. Engine components - uGUI graphics, URP's
    /// camera data, TextMeshPro - are MonoBehaviours too, and disabling those
    /// blanks the screen instead of stopping the game. Our own scripts are the
    /// ones with no namespace, which is a blunt rule but an accurate one here.
    /// </summary>
    private void Freeze(MonoBehaviour behaviour)
    {
        if (behaviour == null || behaviour == this) return;
        if (behaviour.GetType().Namespace != null) return;

        behaviour.enabled = false;
    }

    private void LockOut()
    {
        if (lockedOut) return;
        lockedOut = true;

        if (freezeWholePlayer)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
            {
                foreach (MonoBehaviour behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
                    Freeze(behaviour);
            }
            else
            {
                Debug.LogWarning($"[ExamLockout] Nothing tagged '{playerTag}' to freeze.", this);
            }
        }

        foreach (GameObject target in freezeScriptsOn)
        {
            if (target == null) continue;

            foreach (MonoBehaviour behaviour in target.GetComponents<MonoBehaviour>())
                Freeze(behaviour);
        }

        foreach (MonoBehaviour behaviour in disableOnEnd)
        {
            if (behaviour != null) behaviour.enabled = false;
        }

        foreach (GameObject go in hideOnEnd)
        {
            if (go != null) go.SetActive(false);
        }

        if (releaseCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
