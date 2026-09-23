using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using TMPro;

// Browsers refuse both pointer lock and audio until the player has interacted with
// the page. SeatedLook already defers the lock until the first click; this puts a
// visible gate in front of it so the player knows to click, and holds the exam
// clock at zero until they do. Web builds only - it never appears elsewhere.
public class ClickToStart : MonoBehaviour
{
    static ClickToStart instance;
    GameObject overlay;
    bool released;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (s, m) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (instance != null) return;
        if (FindFirstObjectByType<SeatedLook>() == null) return;   // not the exam scene
        instance = new GameObject("ClickToStart").AddComponent<ClickToStart>();
#endif
    }

    void Start()
    {
        Time.timeScale = 0f;
        BuildOverlay();
    }

    void Update()
    {
        if (released) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        released = true;
        Time.timeScale = 1f;
        if (overlay != null) Destroy(overlay);
        Destroy(gameObject);
        instance = null;
    }

    void OnDestroy()
    {
        if (!released) Time.timeScale = 1f;   // never leave the game frozen
    }

    void BuildOverlay()
    {
        overlay = new GameObject("ClickToStartCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        var scaler = overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var dimGo = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        var dim = (RectTransform)dimGo.transform;
        dim.SetParent(overlay.transform, false);
        dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one;
        dim.offsetMin = Vector2.zero; dim.offsetMax = Vector2.zero;
        dimGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        MakeLine("CLICK TO BEGIN", 0f, 90f, new Color(0.97f, 0.76f, 0.29f));
        MakeLine("Your browser needs one click before it will give the game your mouse.",
                 -70f, 38f, new Color(0.85f, 0.85f, 0.85f));
        MakeLine("Mouse to look  ·  Hold Space for your phone  ·  Arrow keys to enter a sequence",
                 -140f, 32f, new Color(0.6f, 0.6f, 0.6f));
    }

    void MakeLine(string text, float y, float size, Color colour)
    {
        var go = new GameObject("Line", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(overlay.transform, false);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(1600f, size * 2.2f);

        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = colour;
        t.alignment = TextAlignmentOptions.Center;
    }
}
