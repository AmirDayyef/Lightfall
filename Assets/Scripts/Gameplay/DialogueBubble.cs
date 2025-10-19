using UnityEngine;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class DialogueBubble : MonoBehaviour
{
    public float autoAdvanceTime = 0f;

    public Transform followTarget;
    public SpriteRenderer frontCapsule;
    public SpriteRenderer backCapsule;
    public SpriteRenderer bg;
    public TextMeshPro textTMP;
    public Transform contentRoot;

    public Vector2 padding = new Vector2(0.24f, 0.16f);
    public float lineHeight = 0.26f;
    public Vector3 offset = new Vector3(0f, 1.4f, 0f);
    public float maxWidth = 6f;

    public bool faceCamera = true;
    public bool clampToTarget = true;
    public float followLerp = 10f;
    public float appearScale = 0.85f;
    public float fadeIn = 0.12f;
    public float fadeOut = 0.10f;

    public float defaultCPS = 40f;
    public float fastForwardMult = 3.5f;
    public KeyCode advanceKey = KeyCode.Space;

    public Color frontColor = Color.black;
    public Color backColor = new Color(1f, 0.9f, 0.1f, 1f);
    public Color textColor = Color.white;
    [Range(1f, 1.5f)] public float backScaleMult = 1.08f;
    public float backZOffset = 0.005f;

    [System.Serializable]
    public struct Line
    {
        public string text;
        public float cps;
        public AudioClip voiceClip;
        [Range(0.01f, 0.25f)] public float voiceEvery;
        public bool lockMovement;
    }

    public AudioSource sfxSource;
    [Range(0.01f, 0.25f)] public float defaultVoiceInterval = 0.06f;

    public PlayerController3D playerController;
    public bool lockControlsForWholeDialogue = false;
    public bool alwaysEnableControllerAtEnd = false;

    public Behaviour[] extraControllersToDisable;

    public Behaviour comboAttackController;
    public bool autoFindComboOnPlayer = true;
    public string comboTypeName = "ComboAttackController";
    public bool watchdogComboWhileLocked = true;

    Camera _cam;
    MaterialPropertyBlock _mpbFront, _mpbBack;
    Coroutine _seqCo;
    bool _isShowing, _requestSkip, _fast;
    int _targetVisible;
    readonly Queue<Line> _queue = new Queue<Line>();
    float _nextBleep;

    bool _playerPrevEnabled = true;
    int _lockDepth = 0;

    readonly Dictionary<Behaviour, bool> _extraPrevEnabled = new Dictionary<Behaviour, bool>();
    bool _comboPrevEnabled = true;
    bool _hasComboPrev = false;

    Action _onSeqDone;
    bool _seqDoneFlag;

    Vector2 _frontBaseWorldSize, _backBaseWorldSize;
    Vector3 _frontBaseLocalScale, _backBaseLocalScale;
    const float EPS = 1e-4f;
    public Vector2 scaleClamp = new Vector2(0.05f, 20f);

    void Awake()
    {
        if (!contentRoot) contentRoot = transform;
        if (!frontCapsule && bg) frontCapsule = bg;
        if (!frontCapsule) frontCapsule = GetComponentInChildren<SpriteRenderer>(true);
        if (!textTMP) textTMP = GetComponentInChildren<TextMeshPro>(true);
        if (!sfxSource) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;

        if (!playerController)
        {
#if UNITY_2023_1_OR_NEWER
            playerController = FindFirstObjectByType<PlayerController3D>();
#else
            playerController = FindObjectOfType<PlayerController3D>();
#endif
        }

        if (!comboAttackController && autoFindComboOnPlayer && playerController)
            comboAttackController = FindByTypeNameUnder(comboTypeName, playerController.gameObject);

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

        if (_lockDepth > 0 && watchdogComboWhileLocked && comboAttackController)
            if (comboAttackController.enabled) comboAttackController.enabled = false;
    }

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
        ForceUnlockIfNeeded();
    }

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
        if (lockControlsForWholeDialogue) PopMovementLock();
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
            frontCapsule.transform.localScale = new Vector3(_frontBaseLocalScale.x * sx, _frontBaseLocalScale.y * sy, _frontBaseLocalScale.z);
        }

        if (backCapsule && backCapsule.sprite && _backBaseWorldSize.x > EPS && _backBaseWorldSize.y > EPS)
        {
            float sx = Mathf.Clamp((bubbleWorld.x * backScaleMult) / _backBaseWorldSize.x, scaleClamp.x, scaleClamp.y);
            float sy = Mathf.Clamp((bubbleWorld.y * backScaleMult) / _backBaseWorldSize.y, scaleClamp.x, scaleClamp.y);
            backCapsule.transform.localScale = new Vector3(_backBaseLocalScale.x * sx, _backBaseLocalScale.y * sy, _backBaseLocalScale.z);
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

    void PushMovementLock()
    {
        if (playerController)
        {
            if (_lockDepth == 0) _playerPrevEnabled = playerController.enabled;
            playerController.enabled = false;
        }

        if (_lockDepth == 0 && extraControllersToDisable != null)
        {
            _extraPrevEnabled.Clear();
            foreach (var b in extraControllersToDisable)
            {
                if (!b) continue;
                _extraPrevEnabled[b] = b.enabled;
                b.enabled = false;
            }
        }

        if (_lockDepth == 0 && comboAttackController == null && autoFindComboOnPlayer && playerController)
            comboAttackController = FindByTypeNameUnder(comboTypeName, playerController.gameObject);

        if (_lockDepth == 0 && comboAttackController)
        {
            _hasComboPrev = true;
            _comboPrevEnabled = comboAttackController.enabled;
            comboAttackController.enabled = false;
            var mb = comboAttackController as MonoBehaviour;
            if (mb) mb.StopAllCoroutines();
        }

        _lockDepth++;
    }

    void PopMovementLock()
    {
        if (_lockDepth <= 0) return;
        _lockDepth--;

        if (_lockDepth == 0)
        {
            if (playerController)
                playerController.enabled = alwaysEnableControllerAtEnd ? true : _playerPrevEnabled;

            if (extraControllersToDisable != null)
            {
                foreach (var kv in _extraPrevEnabled)
                {
                    var b = kv.Key;
                    if (!b) continue;
                    b.enabled = alwaysEnableControllerAtEnd ? true : kv.Value;
                }
                _extraPrevEnabled.Clear();
            }

            if (comboAttackController && _hasComboPrev)
                comboAttackController.enabled = alwaysEnableControllerAtEnd ? true : _comboPrevEnabled;

            _hasComboPrev = false;
        }
    }

    void ForceUnlockIfNeeded()
    {
        while (_lockDepth > 0) _lockDepth--;

        if (playerController)
            playerController.enabled = alwaysEnableControllerAtEnd ? true : _playerPrevEnabled;

        if (extraControllersToDisable != null)
        {
            foreach (var kv in _extraPrevEnabled)
            {
                var b = kv.Key;
                if (!b) continue;
                b.enabled = alwaysEnableControllerAtEnd ? true : kv.Value;
            }
            _extraPrevEnabled.Clear();
        }

        if (comboAttackController && _hasComboPrev)
            comboAttackController.enabled = alwaysEnableControllerAtEnd ? true : _comboPrevEnabled;

        _hasComboPrev = false;
    }

    void ClearText()
    {
        if (textTMP)
        {
            textTMP.text = "";
            textTMP.maxVisibleCharacters = 0;
        }
    }

    Behaviour FindByTypeNameUnder(string typeName, GameObject root)
    {
        if (!root || string.IsNullOrEmpty(typeName)) return null;
        var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < mbs.Length; i++)
        {
            var mb = mbs[i];
            if (!mb) continue;
            if (mb.GetType().Name == typeName)
                return (Behaviour)mb;
        }
        return null;
    }
}
