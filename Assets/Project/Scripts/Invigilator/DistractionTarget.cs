using UnityEngine;

/// <summary>
/// Put this on an NPC student. The distraction skills pick one of these to be
/// the one who drops a pencil or kicks off.
///
/// If there are none in the scene, the skills fall back to a point on the
/// NavMesh away from the player, so everything still works in a greybox room.
/// </summary>
public class DistractionTarget : MonoBehaviour
{
    [Tooltip("Off for a student who shouldn't be picked - the player's neighbour, say.")]
    [SerializeField] private bool available = true;

    public bool Available => available && isActiveAndEnabled;
}
