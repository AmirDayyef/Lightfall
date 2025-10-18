using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Intro sequence:
/// 1) Force player into "down" pose in Animator
/// 2) Fade from black (CanvasGroup)
/// 3) After fade completes, wait delay, fire "get up"
/// 4) Enable listed objects right AFTER fade completes
/// 5) Disable ONLY ComboAttackControllerSimple and PlayerController3D during intro, then restore
/// </summary>
[DisallowMultipleComponent]
public class IntroPlayerGetUp : MonoBehaviour
{
    [Header("Player & Animator")]
    public Animator playerAnimator;
    [Tooltip("Animator state to force immediately at start (e.g., 'Down').")]
    public string downStateName = "Down";
    [Tooltip("Trigger sent to Animator when it's time to get up (leave empty to skip).")]
    public string getUpTrigger = "GetUp";
    [Tooltip("If you prefer a bool instead of a trigger, set this name and the values below.")]
    public string downBoolName = "";  // e.g., "IsDown"
    public bool useDownBool = false;  // true = use bool on start & clear it on get up

    [Header("Fade-In")]
    [Tooltip("CanvasGroup covering the screen (alpha=1 black ? 0 clear).")]
    public CanvasGroup fadeCanvas;
    [Tooltip("If true, we WAIT until fadeCanvas.alpha ~ 0 (fade done elsewhere). If false, we fade it here.")]
    public bool waitExternalFade = false;
    [Tooltip("Only used when we drive the fade here.")]
    public float fadeDuration = 1.0f;
    [Tooltip("Time scale used for fades/waits (Unscaled recommended for intro).")]
    public bool useUnscaledTime = true;

    [Header("Timing")]
    [Tooltip("Extra wait AFTER fade fully complete, BEFORE we trigger 'get up'.")]
    public float delayAfterFade = 0.75f;
    [Tooltip("Re-enable the disabled scripts this many seconds after we trigger 'get up'.")]
    public float reenableDelayAfterGetUp = 0.5f;

    [Header("Enable After Fade")]
    [Tooltip("These objects will be SetActive(true) right after the fade is done (before the get-up delay).")]
    public List<GameObject> enableAfterFade = new List<GameObject>();

    [Header("Run Settings")]
    public bool runOnStart = true;
    public bool runOnce = true;

    // ===== EXACTLY THESE TWO SCRIPTS WILL BE TEMPORARILY DISABLED =====
    [Header("Scripts to disable during intro")]
    public ComboAttackControllerSimple comboAttackController;
    public PlayerController3D playerController3D;

    // runtime
    bool _hasRun;
    bool _prevComboEnabled;
    bool _prevPC3DEnabled;

    void Reset()
    {
        // Try find common refs from this object downwards
        playerAnimator = GetComponentInChildren<Animator>(true);
        fadeCanvas = FindFirstObjectByType<CanvasGroup>();
        comboAttackController = GetComponentInChildren<ComboAttackControllerSimple>(true);
        playerController3D = GetComponentInChildren<PlayerController3D>(true);
    }

    void Start()
    {
        if (runOnStart) Begin();
    }

    /// <summary>Call this to start the intro sequence.</summary>
    public void Begin()
    {
        if (runOnce && _hasRun) return;
        _hasRun = true;
        StartCoroutine(RunIntro());
    }

    IEnumerator RunIntro()
    {
        // 0) Disable ONLY the requested scripts
        DisableTargetScripts();

        // 1) Force "down" pose
        if (playerAnimator)
        {
            if (useDownBool && !string.IsNullOrEmpty(downBoolName))
                playerAnimator.SetBool(downBoolName, true);

            if (!string.IsNullOrEmpty(downStateName))
                playerAnimator.Play(downStateName, 0, 0f); // hard set state
        }

        // 2) Fade-in handling
        if (fadeCanvas)
        {
            fadeCanvas.alpha = Mathf.Max(fadeCanvas.alpha, 1f);
            fadeCanvas.blocksRaycasts = true;
            fadeCanvas.interactable = false;

            if (waitExternalFade)
            {
                while (fadeCanvas.alpha > 0.01f) yield return null;
            }
            else
            {
                float t = 0f;
                float dur = Mathf.Max(0.0001f, fadeDuration);
                while (t < dur)
                {
                    t += GetDelta();
                    float u = Mathf.Clamp01(t / dur);
                    fadeCanvas.alpha = 1f - u;
                    yield return null;
                }
                fadeCanvas.alpha = 0f;
            }

            fadeCanvas.blocksRaycasts = false;
            fadeCanvas.interactable = false;
        }

        // 3) Enable objects RIGHT AFTER FADE
        if (enableAfterFade != null)
        {
            for (int i = 0; i < enableAfterFade.Count; i++)
                if (enableAfterFade[i]) enableAfterFade[i].SetActive(true);
        }

        // 4) Wait extra time after fade before getting up
        if (delayAfterFade > 0f) yield return WaitSeconds(delayAfterFade);

        // 5) Trigger "get up"
        if (playerAnimator)
        {
            if (useDownBool && !string.IsNullOrEmpty(downBoolName))
                playerAnimator.SetBool(downBoolName, false);

            if (!string.IsNullOrEmpty(getUpTrigger))
            {
                playerAnimator.ResetTrigger(getUpTrigger); // safety
                playerAnimator.SetTrigger(getUpTrigger);
            }
        }

        // 6) Re-enable the two scripts after a small delay (keeps the ending clean)
        if (reenableDelayAfterGetUp > 0f)
            yield return WaitSeconds(reenableDelayAfterGetUp);

        RestoreTargetScripts();
    }

    void DisableTargetScripts()
    {
        if (comboAttackController)
        {
            _prevComboEnabled = comboAttackController.enabled;
            comboAttackController.enabled = false;
        }
        if (playerController3D)
        {
            _prevPC3DEnabled = playerController3D.enabled;
            playerController3D.enabled = false;
        }
    }

    void RestoreTargetScripts()
    {
        if (comboAttackController)
            comboAttackController.enabled = _prevComboEnabled;

        if (playerController3D)
            playerController3D.enabled = _prevPC3DEnabled;
    }

    float GetDelta() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    IEnumerator WaitSeconds(float seconds)
    {
        if (seconds <= 0f) yield break;
        if (useUnscaledTime)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }
        else
        {
            yield return new WaitForSeconds(seconds);
        }
    }
}
