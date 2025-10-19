using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class IntroPlayerGetUp : MonoBehaviour
{
    public Animator playerAnimator;
    public string downStateName = "Down";
    public string getUpTrigger = "GetUp";
    public string downBoolName = "";
    public bool useDownBool = false;

    public CanvasGroup fadeCanvas;
    public bool waitExternalFade = false;
    public float fadeDuration = 1.0f;
    public bool useUnscaledTime = true;

    public float delayAfterFade = 0.75f;
    public float reenableDelayAfterGetUp = 0.5f;

    public List<GameObject> enableAfterFade = new List<GameObject>();

    public bool runOnStart = true;
    public bool runOnce = true;

    public ComboAttackControllerSimple comboAttackController;
    public PlayerController3D playerController3D;

    bool _hasRun;
    bool _prevComboEnabled;
    bool _prevPC3DEnabled;

    void Reset()
    {
        playerAnimator = GetComponentInChildren<Animator>(true);
        fadeCanvas = FindFirstObjectByType<CanvasGroup>();
        comboAttackController = GetComponentInChildren<ComboAttackControllerSimple>(true);
        playerController3D = GetComponentInChildren<PlayerController3D>(true);
    }

    void Start()
    {
        if (runOnStart) Begin();
    }

    public void Begin()
    {
        if (runOnce && _hasRun) return;
        _hasRun = true;
        StartCoroutine(RunIntro());
    }

    IEnumerator RunIntro()
    {
        DisableTargetScripts();

        if (playerAnimator)
        {
            if (useDownBool && !string.IsNullOrEmpty(downBoolName))
                playerAnimator.SetBool(downBoolName, true);

            if (!string.IsNullOrEmpty(downStateName))
                playerAnimator.Play(downStateName, 0, 0f);
        }

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

        if (enableAfterFade != null)
        {
            for (int i = 0; i < enableAfterFade.Count; i++)
                if (enableAfterFade[i]) enableAfterFade[i].SetActive(true);
        }

        if (delayAfterFade > 0f) yield return WaitSeconds(delayAfterFade);

        if (playerAnimator)
        {
            if (useDownBool && !string.IsNullOrEmpty(downBoolName))
                playerAnimator.SetBool(downBoolName, false);

            if (!string.IsNullOrEmpty(getUpTrigger))
            {
                playerAnimator.ResetTrigger(getUpTrigger);
                playerAnimator.SetTrigger(getUpTrigger);
            }
        }

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
