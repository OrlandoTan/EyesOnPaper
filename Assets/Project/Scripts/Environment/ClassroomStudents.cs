using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Fills the classroom with varied students: a random model per desk, some with glasses,
// some seats left empty. Works in the editor (right-click the component > Populate) so you
// can see and tweak the result, or can re-roll on every play.
//
// Setup, once:
//   1. Seat ONE student perfectly on ONE desk by hand (the way you just did).
//   2. Drag that student into "Example Student" and its desk into "Example Desk",
//      then right-click this component > "Capture Seat Offset".
//   3. Put a pair of glasses on that student's head bone by hand, drag them into
//      "Example Glasses", then right-click > "Capture Glasses Offset".
//   4. Right-click > "Populate". Don't like it? "Reroll".
public class ClassroomStudents : MonoBehaviour
{
    [Header("Where")]
    [Tooltip("Parent of all the desks (e.g. Room). Every child whose name starts with the prefix counts as a desk.")]
    [SerializeField] Transform desksRoot;
    [SerializeField] string deskNamePrefix = "Desk";
    [Tooltip("Desks closer than this to the Player are skipped, so nobody sits in your seat.")]
    [SerializeField] float skipNearPlayer = 1.2f;

    [Header("Who")]
    [Tooltip("Floreswa model prefabs. Each model's FBX must be set to Humanoid (with the facing fix).")]
    [SerializeField] GameObject[] studentModels;
    [SerializeField] RuntimeAnimatorController studentController;
    [SerializeField] GameObject[] glassesPrefabs;
    [SerializeField, Range(0f, 1f)] float glassesChance = 0.35f;
    [SerializeField, Range(0f, 1f)] float emptySeatChance = 0.2f;

    [Header("Randomness")]
    [SerializeField] int seed = 1;
    [SerializeField] bool rerollOnPlay = false;

    [Header("Offsets (filled by the Capture buttons)")]
    [SerializeField] Vector3 seatLocalPosition;
    [SerializeField] Vector3 seatLocalEuler;
    [SerializeField] float seatScale = 1f;
    [SerializeField] Vector3 glassesLocalPosition;
    [SerializeField] Vector3 glassesLocalEuler;
    [SerializeField] Vector3 glassesLocalScale = Vector3.one;

    [Header("Examples (only used by the Capture buttons)")]
    [SerializeField] Transform exampleStudent;
    [SerializeField] Transform exampleDesk;
    [SerializeField] Transform exampleGlasses;

    const string ContainerName = "Students (generated)";

    void Start()
    {
        if (!rerollOnPlay) return;
        seed = Random.Range(int.MinValue, int.MaxValue);
        Populate();
    }

    // ---------- capture ----------

    [ContextMenu("Capture Seat Offset")]
    void CaptureSeat()
    {
        if (exampleStudent == null || exampleDesk == null) { Debug.LogError("[Students] Set Example Student and Example Desk first."); return; }
        seatLocalPosition = exampleDesk.InverseTransformPoint(exampleStudent.position);
        seatLocalEuler = (Quaternion.Inverse(exampleDesk.rotation) * exampleStudent.rotation).eulerAngles;
        seatScale = exampleStudent.lossyScale.x;
        Dirty();
        Debug.Log($"[Students] Seat offset captured: pos {seatLocalPosition}, rot {seatLocalEuler}, scale {seatScale:0.###}");
    }

    [ContextMenu("Capture Glasses Offset")]
    void CaptureGlasses()
    {
        if (exampleGlasses == null) { Debug.LogError("[Students] Set Example Glasses first (glasses parented to a student's head bone)."); return; }
        glassesLocalPosition = exampleGlasses.localPosition;
        glassesLocalEuler = exampleGlasses.localEulerAngles;
        glassesLocalScale = exampleGlasses.localScale;
        Dirty();
        Debug.Log($"[Students] Glasses offset captured relative to '{exampleGlasses.parent?.name}'.");
    }

    // ---------- populate ----------

    [ContextMenu("Reroll")]
    void Reroll()
    {
        seed++;
        Populate();
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        Transform old = transform.Find(ContainerName);
        if (old == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) { Undo.DestroyObjectImmediate(old.gameObject); return; }
#endif
        Destroy(old.gameObject);
    }

    [ContextMenu("Populate")]
    public void Populate()
    {
        if (desksRoot == null || studentModels == null || studentModels.Length == 0)
        {
            Debug.LogError("[Students] Assign Desks Root and at least one Student Model.");
            return;
        }

        Clear();
        var container = new GameObject(ContainerName).transform;
        container.SetParent(transform, false);
#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RegisterCreatedObjectUndo(container.gameObject, "Populate students");
#endif

        var rng = new System.Random(seed);
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        int placed = 0, withGlasses = 0;

        foreach (Transform desk in FindDesks())
        {
            if (player != null && Vector3.Distance(Flat(desk.position), Flat(player.transform.position)) < skipNearPlayer) continue;
            if (rng.NextDouble() < emptySeatChance) continue;

            GameObject prefab = studentModels[rng.Next(studentModels.Length)];
            if (prefab == null) continue;

            GameObject s = Spawn(prefab, container);
            s.name = $"Student_{placed + 1:00} ({prefab.name})";
            s.tag = "Untagged";
            s.transform.SetPositionAndRotation(desk.TransformPoint(seatLocalPosition),
                                               desk.rotation * Quaternion.Euler(seatLocalEuler));
            s.transform.localScale = Vector3.one * seatScale;

            Animator anim = SetUpAnimator(s, prefab);
            if (s.GetComponent<StudentAmbient>() == null) s.AddComponent<StudentAmbient>();

            if (glassesPrefabs != null && glassesPrefabs.Length > 0 && rng.NextDouble() < glassesChance)
            {
                Transform head = FindHead(anim);
                GameObject gPrefab = glassesPrefabs[rng.Next(glassesPrefabs.Length)];
                if (head != null && gPrefab != null)
                {
                    GameObject g = Spawn(gPrefab, head);
                    g.transform.localPosition = glassesLocalPosition;
                    g.transform.localRotation = Quaternion.Euler(glassesLocalEuler);
                    g.transform.localScale = glassesLocalScale;
                    withGlasses++;
                }
            }
            placed++;
        }

        Dirty();
        Debug.Log($"[Students] Seated {placed} students ({withGlasses} with glasses), seed {seed}.");
    }

    // ---------- helpers ----------

    List<Transform> FindDesks()
    {
        var desks = new List<Transform>();
        foreach (Transform t in desksRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith(deskNamePrefix)) continue;
            // Skip a "Desk" that sits inside another desk (the Desk prefab has a child called Desk).
            if (t.parent != null && t.parent.name.StartsWith(deskNamePrefix)) continue;
            if (t.IsChildOf(transform)) continue;
            desks.Add(t);
        }
        return desks;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static GameObject Spawn(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Spawn student");
            return go;
        }
#endif
        return Instantiate(prefab, parent);
    }

    Animator SetUpAnimator(GameObject s, GameObject prefab)
    {
        Animator anim = s.GetComponentInChildren<Animator>();
        if (anim == null) anim = s.AddComponent<Animator>();
        if (studentController != null) anim.runtimeAnimatorController = studentController;
        anim.applyRootMotion = false;

#if UNITY_EDITOR
        // Always take the Humanoid avatar from this model's own FBX, so each variant animates correctly.
        Avatar found = FindAvatarFor(prefab != null ? prefab : s);
        if (found != null) anim.avatar = found;
#endif
        if (anim.avatar == null || !anim.avatar.isHuman)
            Debug.LogWarning($"[Students] {prefab.name} has no Humanoid avatar - set its FBX Rig to Humanoid or it won't play the writing animation.", s);
        return anim;
    }

#if UNITY_EDITOR
    static Avatar FindAvatarFor(GameObject model)
    {
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            string path = AssetDatabase.GetAssetPath(smr.sharedMesh);
            if (string.IsNullOrEmpty(path)) continue;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Avatar av && av.isHuman) return av;
        }
        return null;
    }

    // Fix students that are already placed (e.g. you switched their FBX to Humanoid after Populate).
    [ContextMenu("Refresh Avatars")]
    void RefreshAvatars()
    {
        Transform container = transform.Find(ContainerName);
        if (container == null) { Debug.LogWarning("[Students] Nothing generated yet - use Populate."); return; }

        int fixedCount = 0, missing = 0;
        foreach (Transform student in container)
        {
            Animator anim = student.GetComponentInChildren<Animator>();
            if (anim == null) continue;
            Avatar av = FindAvatarFor(student.gameObject);
            if (av != null)
            {
                Undo.RecordObject(anim, "Refresh avatar");
                anim.avatar = av;
                if (studentController != null) anim.runtimeAnimatorController = studentController;
                fixedCount++;
            }
            else
            {
                missing++;
                Debug.LogWarning($"[Students] {student.name}: its FBX has no Humanoid avatar yet.", student);
            }
        }
        Dirty();
        Debug.Log($"[Students] Avatars refreshed on {fixedCount} students ({missing} still missing).");
    }
#endif

    static Transform FindHead(Animator anim)
    {
        if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return null;

        Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (head != null) return head;

        // Edit mode fallback: look the bone name up in the avatar's mapping.
        foreach (HumanBone hb in anim.avatar.humanDescription.human)
        {
            if (hb.humanName != "Head") continue;
            foreach (Transform t in anim.GetComponentsInChildren<Transform>(true))
                if (t.name == hb.boneName) return t;
        }
        return null;
    }

    void Dirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }
}
