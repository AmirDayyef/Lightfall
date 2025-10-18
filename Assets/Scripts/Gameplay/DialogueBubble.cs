// DialogueBubble.cs  (uses PlayerController3D)
using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class DialogueBubble : MonoBehaviour
{
    [Header("Advance Settings")]
    public float autoAdvanceTime = 0f; // 0 = wait for input, >0 = seconds to wait before auto-advance

    [Header("Refs")]
    public Transform followTarget;
    public SpriteRenderer frontCapsule;         // black
    public SpriteRenderer backCapsule;          // yellow behind
    public SpriteRenderer bg;                   // legacy single bg -> used as front if frontCapsule not assigned
    public TextMeshPro textTMP;                 // WORLD-space TMP (not UGUI)
    public Transform contentRoot;

    [Header("Layout")]
    public Vector2 padding = new Vector2(0.24f, 0.16f); // WORLD units
    public float lineHeight = 0.26f;
    public Vector3 offset = new Vector3(0f, 1.4f, 0f);
    public float maxWidth = 6f;                         // WORLD wrap width

    [Header("Behaviour")]
    public bool faceCamera = true;
    public bool clampToTarget = true;
    public float followLerp = 10f;
    public float appearScale = 0.85f;
    public float fadeIn = 0.12f;
    public float fadeOut = 0.10f;

    [Header("Typewriter")]
    public float defaultCPS = 40f;
    public float fastForwardMult = 3.5f;
    public KeyCode advanceKey = KeyCode.Space;

    [Header("Rendering")]
    public Color frontColor = Color.black;
    public Color backColor = new Color(1f, 0.9f, 0.1f, 1f);
    public Color textColor = Color.white;
    [Range(1f, 1.5f)] public float backScaleMult = 1.08f;
    public float backZOffset = 0.005f;

    [System.Serializable]
    public struct Line
    {
        [TextArea(1, 4)] public string text;
        public float cps;
        public AudioClip voiceClip;
        [Range(0.01f, 0.25f)] public float voiceEvery;

        [Header("Control")]
        public bool lockMovement; // per-line lock
    }

    [Header("Audio")]
    public AudioSource sfxSource;
    [Range(0.01f, 0.25f)] public float defaultVoiceInterval = 0.06f;

    [Header("Player Movement Locking")]
    public PlayerController3D playerController;   // << changed
    [Tooltip("If true, lock controls for the entire dialogue (from show to hide) in addition to any per-line locks.")]
    public bool lockControlsForWholeDialogue = false;
    [Tooltip("If true, force-enable PlayerController3D at the end of the dialogue (ignores whatever state it had before).")]
    public bool alwaysEnableControllerAtEnd = false;

    // runtime
    Camera _cam;
    MaterialPropertyBlock _mpbFront, _mpbBack;
    Coroutine _seqCo;
    bool _isShowing, _requestSkip, _fast;
    int _targetVisible;
    readonly Queue<Line> _queue = new Queue<Line>();
    float _nextBleep;

    // movement-lock bookkeeping (supports nesting)
    bool _playerPrevEnabled = true;
    int _lockDepth = 0;

    // blocking helpers
    Action _onSeqDone;
    bool _seqDoneFlag;

    // unit-correct sizing caches
    Vector2 _frontBaseWorldSize, _backBaseWorldSize;
    Vector3 _frontBaseLocalScale, _backBaseLocalScale;
    const float EPS = 1e-4f;
    public Vector2 scaleClamp = new Vector2(0.05f, 20f);

    void Awake()
    {
        if (!contentRoot) contentRoot = transform;

        if (!frontCapsule && bg) frontCapsule = bg; // legacy

        if (!frontCapsule) frontCapsule = GetComponentInChildren<SpriteRenderer>(true);
        if (!textTMP) textTMP = GetComponentInChildren<TextMeshPro>(true);
        if (!sfxSource) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 1f;
        sfxSource.minDistance = 2f; sfxSource.maxDistance = 12f;

        if (!playerController)
            playerController = FindFirstObjectByType<PlayerController3D>(); // << changed

        _cam = Camera.main;
        _mpbFront = new MaterialPropertyBlock();
        _mpbBack = new MaterialPropertyBlock();

        if (textTMP)
        {
            textTMP.textWrappingMode = TextWrappingModes.Normal;
            textTMP.overflowMode = TextOverflowModes.Overflow;
            textTMP.autoSizeTextContainer = false;
            textTMP.text = "";
            textTMP.maxVisibleCharacters = 0;
        }

        if (backCapsule)
        {
            var lp = backCapsule.transform.localPosition;
            backCapsule.transform.localPosition = new Vector3(lp.x, lp.y, lp.z + Mathf.Abs(backZOffset));
        }

        if (frontCapsule)
        {
            _frontBaseLocalScale = frontCapsule.transform.localScale;
            _frontBaseWorldSize = frontCapsule.bounds.size;
        }
        if (backCapsule)
        {
            _backBaseLocalScale = backCapsule.transform.localScale;
            _backBaseWorldSize = backCapsule.bounds.size;
        }

        SetAlpha(0f);
        contentRoot.localScale = Vector3.one * appearScale;
    }

    void OnDisable() { ForceUnlockIfNeeded(); }
    void OnDestroy() { ForceUnlockIfNeeded(); }

    void Update()
    {
        _fast = Input.GetKey(advanceKey);
        if (Input.GetKeyDown(advanceKey)) _requestSkip = true;

        if (followTarget)
        {
            Vector3 targetPos = followTarget.position + offset;
            if (clampToTarget)
                transform.position = Vector3.Lerp(transform.position, targetPos, 1f - Mathf.Exp(-followLerp * Time.deltaTime));
            else
                transform.position = targetPos;
        }

        if (faceCamera && _cam)
        {
            Vector3 fwd = _cam.transform.forward;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
    }

    // ===== Public API =====

    public void ShowLines(IEnumerable<Line> lines)
    {
        _onSeqDone = null;
        StartLinesInternal(lines);
    }

    public IEnumerator ShowLinesAndWait(IEnumerable<Line> lines)
    {
        _seqDoneFlag = false;
        _onSeqDone = () => _seqDoneFlag = true;
        StartLinesInternal(lines);
        while (!_seqDoneFlag) yield return null;
    }

    public IEnumerator ShowLineAndWait(Line line)
    {
        _seqDoneFlag = false;
        _onSeqDone = () => _seqDoneFlag = true;
        StartLinesInternal(new[] { line });
        while (!_seqDoneFlag) yield return null;
    }

    public void HideImmediate()
    {
        if (_seqCo != null) StopCoroutine(_seqCo);
        _seqCo = null;

        ClearText();
        SetAlpha(0f);
        contentRoot.localScale = Vector3.one * appearScale;
        _isShowing = false;
        _queue.Clear();

        // release any locks and (optionally) force-enable
        ForceUnlockIfNeeded();
    }

    // ===== Internals =====

    void StartLinesInternal(IEnumerable<Line> lines)
    {
        ClearText();

        _queue.Clear();
        foreach (var l in lines) _queue.Enqueue(l);
        if (_seqCo != null) StopCoroutine(_seqCo);
        _seqCo = StartCoroutine(RunSequence());
    }

    IEnumerator RunSequence()
    {
        _requestSkip = false;

        // sequence-wide lock (optional)
        if (lockControlsForWholeDialogue) PushMovementLock();

        yield return ShowAnim();

        while (_queue.Count > 0)
        {
            var l = _queue.Dequeue();

            if (l.lockMovement) PushMovementLock();
            yield return PlayLine(l);
            yield return WaitAdvance();
            if (l.lockMovement) PopMovementLock();
        }

        ClearText();
        yield return HideAnim();
        _seqCo = null;

        // sequence lock release
        if (lockControlsForWholeDialogue) PopMovementLock();

        // safety: release any leftover locks and optionally force enable
        ForceUnlockIfNeeded();

        _onSeqDone?.Invoke();
        _onSeqDone = null;
    }

    IEnumerator PlayLine(Line l)
    {
        if (!textTMP) yield break;

        textTMP.maxVisibleCharacters = 0;
        textTMP.text = l.text;

        textTMP.ForceMeshUpdate();
        FitBackgroundToText();

        int charCount = textTMP.textInfo.characterCount;
        _targetVisible = charCount;
        _nextBleep = 0f;

        float cps = l.cps > 0f ? l.cps : defaultCPS;
        float voiceEvery = l.voiceEvery > 0f ? l.voiceEvery : defaultVoiceInterval;
        float visible = 0f, t = 0f;

        while (visible < charCount)
        {
            float speed = _fast ? cps * fastForwardMult : cps;
            float secPerChar = 1f / Mathf.Max(1f, speed);

            t += Time.unscaledDeltaTime;
            while (t >= secPerChar && visible < charCount)
            {
                t -= secPerChar;
                visible += 1f;
                int vis = Mathf.Min((int)visible, charCount);
                textTMP.maxVisibleCharacters = vis;

                if (l.voiceClip && Time.unscaledTime >= _nextBleep)
                {
                    sfxSource.PlayOneShot(l.voiceClip, 1f);
                    _nextBleep = Time.unscaledTime + voiceEvery;
                }
            }

            if (_requestSkip) break;
            yield return null;
        }

        textTMP.maxVisibleCharacters = charCount;
        _requestSkip = false;
    }

    IEnumerator ShowAnim()
    {
        if (_isShowing) yield break;
        _isShowing = true;

        float t = 0f;
        float a0 = 0f, a1 = 1f;
        Vector3 s0 = Vector3.one * appearScale, s1 = Vector3.one;

        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeIn));
            SetAlpha(Mathf.Lerp(a0, a1, u));
            contentRoot.localScale = Vector3.Lerp(s0, s1, EaseOutBack(u));
            yield return null;
        }
        SetAlpha(1f); contentRoot.localScale = s1;
    }

    IEnumerator HideAnim()
    {
        float t = 0f;
        float a0 = 1f, a1 = 0f;
        Vector3 s0 = Vector3.one, s1 = Vector3.one * appearScale;

        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeOut));
            SetAlpha(Mathf.Lerp(a0, a1, u));
            contentRoot.localScale = Vector3.Lerp(s0, s1, u);
            yield return null;
        }
        SetAlpha(0f); contentRoot.localScale = s1;
        _isShowing = false;
    }

    IEnumerator WaitAdvance()
    {
        _requestSkip = false;
        yield return null;

        if (autoAdvanceTime > 0f)
        {
            float timer = 0f;
            while (!_requestSkip && timer < autoAdvanceTime)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }
            _requestSkip = false;
        }
        else
        {
            while (!_requestSkip) yield return null;
            _requestSkip = false;
        }
    }

    void SetAlpha(float a)
    {
        if (frontCapsule)
        {
            if (_mpbFront == null) _mpbFront = new MaterialPropertyBlock();
            _mpbFront.SetColor("_Color", new Color(frontColor.r, frontColor.g, frontColor.b, a));
            frontCapsule.SetPropertyBlock(_mpbFront);
        }
        if (backCapsule)
        {
            if (_mpbBack == null) _mpbBack = new MaterialPropertyBlock();
            _mpbBack.SetColor("_Color", new Color(backColor.r, backColor.g, backColor.b, a));
            backCapsule.SetPropertyBlock(_mpbBack);
        }
        if (textTMP)
        {
            var c = textColor; c.a = a;
            textTMP.color = c;
        }
    }

    void FitBackgroundToText()
    {
        if (!textTMP) return;

        textTMP.textWrappingMode = TextWrappingModes.Normal;
        textTMP.overflowMode = TextOverflowModes.Overflow;

        var lossy = textTMP.transform.lossyScale;
        float localMaxW = maxWidth / Mathf.Max(EPS, lossy.x);

        Vector2 prefLocal = textTMP.GetPreferredValues(textTMP.text, localMaxW, 0f);
        float prefWorldX = prefLocal.x * lossy.x;
        float prefWorldY = prefLocal.y * lossy.y;

        Vector2 bubbleWorld = new Vector2(
            Mathf.Min(prefWorldX, maxWidth) + padding.x * 2f,
            Mathf.Max(prefWorldY, lineHeight) + padding.y * 2f
        );

        if (frontCapsule && frontCapsule.sprite && _frontBaseWorldSize.x > EPS && _frontBaseWorldSize.y > EPS)
        {
            float sx = Mathf.Clamp(bubbleWorld.x / _frontBaseWorldSize.x, scaleClamp.x, scaleClamp.y);
            float sy = Mathf.Clamp(bubbleWorld.y / _frontBaseWorldSize.y, scaleClamp.x, scaleClamp.y);
            frontCapsule.transform.localScale = new Vector3(_frontBaseLocalScale.x * sx,
                                                            _frontBaseLocalScale.y * sy,
                                                            _frontBaseLocalScale.z);
        }

        if (backCapsule && backCapsule.sprite && _backBaseWorldSize.x > EPS && _backBaseWorldSize.y > EPS)
        {
            float sx = Mathf.Clamp((bubbleWorld.x * backScaleMult) / _backBaseWorldSize.x, scaleClamp.x, scaleClamp.y);
            float sy = Mathf.Clamp((bubbleWorld.y * backScaleMult) / _backBaseWorldSize.y, scaleClamp.x, scaleClamp.y);
            backCapsule.transform.localScale = new Vector3(_backBaseLocalScale.x * sx,
                                                           _backBaseLocalScale.y * sy,
                                                           _backBaseLocalScale.z);

            var lp = backCapsule.transform.localPosition;
            backCapsule.transform.localPosition = new Vector3(lp.x, lp.y, Mathf.Abs(backZOffset));
        }

        textTMP.transform.localPosition = Vector3.zero;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1 + c3 * Mathf.Pow(x - 1, 3) + c1 * Mathf.Pow(x - 1, 2);
    }

    // ===== Lock helpers & fail-safe =====
    void PushMovementLock()
    {
        if (!playerController) return;
        if (_lockDepth == 0) _playerPrevEnabled = playerController.enabled;
        _lockDepth++;
        playerController.enabled = false;
    }

    void PopMovementLock()
    {
        if (!playerController) return;
        if (_lockDepth <= 0) return;
        _lockDepth--;
        if (_lockDepth == 0)
        {
            // restore to previous state OR force enable if requested
            playerController.enabled = alwaysEnableControllerAtEnd ? true : _playerPrevEnabled;
        }
    }

    void ForceUnlockIfNeeded()
    {
        if (!playerController) return;

        while (_lockDepth > 0) _lockDepth--;

        playerController.enabled = alwaysEnableControllerAtEnd ? true : _playerPrevEnabled;
    }

    void ClearText()
    {
        if (textTMP)
        {
            textTMP.text = "";
            textTMP.maxVisibleCharacters = 0;
        }
    }
}
