using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class OutroSequence : MonoBehaviour
{
    public string mainMenuSceneName = "MainMenu";
    public CanvasGroup fader;
    public bool blockRaycastsDuringFade = true;
    public Image fullRedImage;
    public Image imageA;
    public Image imageB;

    public float fadeToRedTime = 0.6f; // unused when red starts full
    public float holdRedTime = 0.35f;
    public float fadeOutFromRedTime = 0.6f;
    public float fadeInA = 0.7f;
    public float holdA = 1.2f;
    public float fadeOutA = 0.7f;
    public float fadeInB = 0.7f;
    public float holdB = 1.2f;
    public float fadeOutB = 0.7f;

    public bool allowSkip = true;
    public bool skipNow = false;

    bool _running;

    void Awake()
    {
        if (fader != null)
        {
            fader.alpha = 0f;
            fader.blocksRaycasts = false;
            fader.interactable = false;
        }
        if (fullRedImage) SetImageAlpha(fullRedImage, 1f);
        if (imageA) SetImageAlpha(imageA, 0f);
        if (imageB) SetImageAlpha(imageB, 0f);
    }

    void OnEnable()
    {
        if (!_running) StartCoroutine(RunOutro());
    }

    IEnumerator RunOutro()
    {
        _running = true;
        if (skipNow)
        {
            LoadMenu();
            yield break;
        }

        if (fader && blockRaycastsDuringFade)
        {
            fader.blocksRaycasts = true;
            fader.interactable = true;
        }

        if (fullRedImage)
        {
            yield return Hold(holdRedTime);
            yield return FadeImage(fullRedImage, 1f, 0f, fadeOutFromRedTime);
        }

        if (imageA)
        {
            yield return FadeImage(imageA, 0f, 1f, fadeInA);
            yield return Hold(holdA);
            yield return FadeImage(imageA, 1f, 0f, fadeOutA);
        }

        if (imageB)
        {
            yield return FadeImage(imageB, 0f, 1f, fadeInB);
            yield return Hold(holdB);
            yield return FadeImage(imageB, 1f, 0f, fadeOutB);
        }

        if (fader)
        {
            yield return FadeCanvas(fader, fader.alpha, 1f, 0.5f);
        }

        LoadMenu();
    }

    void LoadMenu()
    {
        if (fader)
        {
            fader.blocksRaycasts = true;
            fader.interactable = true;
            fader.alpha = 1f;
        }
        SceneManager.LoadScene(mainMenuSceneName);
    }

    IEnumerator Hold(float t)
    {
        float timer = 0f;
        while (timer < t)
        {
            if (allowSkip && Input.anyKeyDown) { LoadMenu(); yield break; }
            timer += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator FadeImage(Image img, float from, float to, float time)
    {
        if (!img || time <= 0f)
        {
            if (img) SetImageAlpha(img, to);
            yield break;
        }

        if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
        float t = 0f;
        SetImageAlpha(img, from);

        while (t < time)
        {
            if (allowSkip && Input.anyKeyDown) { LoadMenu(); yield break; }
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, t / time);
            SetImageAlpha(img, a);
            yield return null;
        }
        SetImageAlpha(img, to);
    }

    IEnumerator FadeCanvas(CanvasGroup cg, float from, float to, float time)
    {
        if (!cg || time <= 0f)
        {
            if (cg) cg.alpha = to;
            yield break;
        }

        float t = 0f;
        cg.alpha = from;

        while (t < time)
        {
            if (allowSkip && Input.anyKeyDown) { LoadMenu(); yield break; }
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, t / time);
            yield return null;
        }
        cg.alpha = to;
    }

    void SetImageAlpha(Image img, float a)
    {
        var c = img.color;
        c.a = a;
        img.color = c;
    }

    public void StartOutroNow()
    {
        if (!_running) StartCoroutine(RunOutro());
    }

    public void SkipToMenu()
    {
        LoadMenu();
    }
}
