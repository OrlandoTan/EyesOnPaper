using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A black rectangle that fades in and out over everything else, and can carry
/// you into another scene without the hard cut.
///
/// Drawn in OnGUI at a very low depth so it sits on top of the placeholder UI.
/// Uses unscaled time, so it still works while the game is paused.
/// </summary>
public class ScreenFade : MonoBehaviour
{
    [SerializeField] private Color fadeColour = Color.black;
    [Tooltip("Fade up from black when this scene opens.")]
    [SerializeField] private bool fadeInOnStart = true;
    [SerializeField] private float fadeInDuration = 0.6f;
    [SerializeField] private float fadeOutDuration = 0.5f;

    public bool IsFading { get; private set; }

    private float alpha;
    private Texture2D pixel;

    private void Awake()
    {
        pixel = new Texture2D(1, 1);
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply();

        alpha = fadeInOnStart ? 1f : 0f;
    }

    private void OnDestroy()
    {
        if (pixel != null) Destroy(pixel);
    }

    private void Start()
    {
        if (fadeInOnStart) StartCoroutine(FadeTo(0f, fadeInDuration, null));
    }

    /// <summary>Fade down to black, then load the named scene.</summary>
    public void FadeOutAndLoad(string sceneName)
    {
        if (IsFading) return;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[ScreenFade] Scene '{sceneName}' isn't in Build Settings. " +
                           "File > Build Profiles > Scene List, then add it.", this);
            return;
        }

        StartCoroutine(FadeTo(1f, fadeOutDuration, () => SceneManager.LoadScene(sceneName)));
    }

    public void FadeOut(Action onComplete = null)
    {
        if (!IsFading) StartCoroutine(FadeTo(1f, fadeOutDuration, onComplete));
    }

    private IEnumerator FadeTo(float target, float duration, Action onComplete)
    {
        IsFading = true;

        float from = alpha;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;          // works while paused
            alpha = Mathf.Lerp(from, target, elapsed / duration);
            yield return null;
        }

        alpha = target;
        IsFading = false;
        onComplete?.Invoke();
    }

    private void OnGUI()
    {
        if (alpha <= 0.001f) return;

        GUI.depth = -1000;                              // in front of everything
        GUI.color = new Color(fadeColour.r, fadeColour.g, fadeColour.b, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), pixel);
        GUI.color = Color.white;
    }
}
