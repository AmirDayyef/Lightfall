using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class DialogueBubbleTesterPerLine : MonoBehaviour
{
    public enum StartMode
    {
        OnEnable,
        OnTriggerEnter3D,
        OnTriggerEnter2D,
        OnCollisionEnter2D,
        Manual
    }

    [System.Serializable]
    public struct Step
    {
        [Tooltip("Line content. Use line.lockMovement to freeze DURING this line.")]
        public DialogueBubble.Line line;

        [Header("Per-line timing")]
        [Min(0)] public float preDelay;
        [Min(0)] public float postDelay;

        [Header("Per-line movement lock (outside the line)")]
        public bool lockDuringPre;
        public bool lockDuringPost;

        [Header("Advance mode for THIS line")]
        [Tooltip("If true, this line auto-advances after autoAdvanceSeconds. Otherwise waits for advance key.")]
        public bool autoAdvance;
        [Min(0)] public float autoAdvanceSeconds;
    }

    [Header("Refs")]
    public DialogueBubble bubble;
    public Transform followTarget;
    public PlayerController3D player;

    [Header("Start Mode")]
    public StartMode startMode = StartMode.OnTriggerEnter2D;
    [Tooltip("Tag required on the OTHER collider (player). Leave empty to accept any.")]
    public string triggerTag = "Player";
    public bool oneShot = true;
    public float retriggerCooldown = 1.0f;

    [Header("Steps (fully per-line control)")]
    public Step[] steps;

    [Header("Global bubble defaults")]
    public float defaultAutoAdvanceTime = 0f; 
    public KeyCode defaultAdvanceKey = KeyCode.Space;

    bool _running;
    float _lastRun = -999f;
    bool _prevEnabled = true;
    int _lockDepth = 0;

    void Awake()
    {
        if (!bubble) bubble = GetComponentInChildren<DialogueBubble>(true);
        if (!player) player = FindFirstObjectByType<PlayerController3D>();
        if (bubble && followTarget) bubble.followTarget = followTarget;

        if (bubble)
        {
            bubble.autoAdvanceTime = defaultAutoAdvanceTime;
            bubble.advanceKey = defaultAdvanceKey;
        }
    }

    void OnEnable()
    {
        if (startMode == StartMode.OnEnable) TryStart();
    }

    void OnTriggerEnter(Collider other)
    {
        if (startMode != StartMode.OnTriggerEnter3D) return;
        if (!string.IsNullOrEmpty(triggerTag) && !other.CompareTag(triggerTag)) return;
        TryStart();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (startMode != StartMode.OnTriggerEnter2D) return;
        if (!string.IsNullOrEmpty(triggerTag) && !other.CompareTag(triggerTag)) return;
        TryStart();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        if (startMode != StartMode.OnCollisionEnter2D) return;
        if (!string.IsNullOrEmpty(triggerTag) && !collision.collider.CompareTag(triggerTag)) return;
        TryStart();
    }

    public void TryStart()
    {
        if (_running) return;
        if (oneShot && _lastRun > -900f) return;
        if (Time.time - _lastRun < retriggerCooldown) return;
        if (!bubble || steps == null || steps.Length == 0) return;

        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        _running = true;

        var single = new DialogueBubble.Line[1];
        float savedAuto = bubble.autoAdvanceTime;

        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];

            if (s.preDelay > 0f)
            {
                if (s.lockDuringPre) PushLock();
                yield return WaitUnscaled(s.preDelay);
                if (s.lockDuringPre) PopLock();
            }

            bubble.autoAdvanceTime = s.autoAdvance ? Mathf.Max(0f, s.autoAdvanceSeconds) : 0f;
            single[0] = s.line;
            yield return bubble.ShowLinesAndWait(single);
            bubble.autoAdvanceTime = savedAuto;

            if (s.postDelay > 0f)
            {
                if (s.lockDuringPost) PushLock();
                yield return WaitUnscaled(s.postDelay);
                if (s.lockDuringPost) PopLock();
            }
        }

        _lastRun = Time.time;
        _running = false;
    }

    IEnumerator WaitUnscaled(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    void PushLock()
    {
        if (!player) return;
        if (_lockDepth == 0) _prevEnabled = player.enabled;
        _lockDepth++;
        player.enabled = false;
    }

    void PopLock()
    {
        if (!player) return;
        if (_lockDepth <= 0) return;
        _lockDepth--;
        if (_lockDepth == 0) player.enabled = _prevEnabled;
    }
}
