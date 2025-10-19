using UnityEngine;
using System.Collections;

[DisallowMultipleComponent]
public class GhostStalker3D : MonoBehaviour
{
    public Animator animator;
    public string runTrigger = "Run";
    public string jumpTrigger = "Jump";

    public string playerSuccessTrigger = "Light";

    public string playerTag = "Player";
    public Transform modelRoot;
    public Transform physicsRoot;
    public Rigidbody ghostRB;
    public Transform respawnAnchor;

    public float frontDistance = 8f;
    public float behindDistance = 6f;
    public Vector2 lateralOffsetRange = new Vector2(-2f, 2f);
    public float spawnYOffset = 0f;
    [Range(0, 1)] public float frontChance = 0.7f;

    public float runSpeed = 9f;
    public float faceTurnSpeedDeg = 720f;
    public float lungeTriggerDistance = 3.0f;

    public float slowmoWindow = 0.85f;
    [Range(0.05f, 1f)] public float slowmoTimeScale = 0.25f;
    public KeyCode executionKey = KeyCode.E;
    public float lungeDistance = 2.5f;
    public float lungeUp = 0.6f;

    public float possessDPS = 6f;
    public KeyCode mashKey = KeyCode.E;
    public float mashRequired = 12f;
    public float mashPerPress = 1f;
    public float mashDecayPerSecond = 1.5f;
    public bool freezePlayerRigidbody = true;

    public bool autoActivateOnStart = true;
    public float longRespawnCooldown = 6f;
    public float shortRespawnCooldown = 2.5f;

    public bool fadeOnSpawn = true;
    public float fadeTime = 0.25f;
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    public bool logs = false;

    public Transform ePrompt;
    public float promptPulseHz = 2.2f;
    public float promptPulseAmt = 0.15f;
    public float promptHitPop = 0.35f;
    public float promptAnimLerp = 12f;

    enum State { Spawning, Chasing, Lunge, Possessing, Cooldown }
    State _state;

    Transform _player;
    PlayerController3D _playerCtrl;
    ComboAttackControllerSimple _playerCombo;
    Rigidbody _playerRB;
    Animator _playerAnimator;

    Renderer[] _renderers;
    Color[][] _baseColors;
    Collider[] _colliders;

    float _origTimeScale = 1f;
    bool _inSlowmo;

    Vector3 _lungeStart, _lungeTarget;

    Vector3 _promptBaseScale = Vector3.one;
    Vector3 _promptScale;
    float _promptExtraPulse;

    void Awake()
    {
        if (!modelRoot) modelRoot = transform;
        if (!physicsRoot) physicsRoot = transform;
        if (!ghostRB) ghostRB = physicsRoot.GetComponent<Rigidbody>();

        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo)
        {
            _player = pgo.transform;
            _playerCtrl = pgo.GetComponent<PlayerController3D>() ?? pgo.GetComponentInChildren<PlayerController3D>(true);
            _playerCombo = pgo.GetComponent<ComboAttackControllerSimple>() ?? pgo.GetComponentInChildren<ComboAttackControllerSimple>(true);
            _playerRB = pgo.GetComponent<Rigidbody>() ?? pgo.GetComponentInChildren<Rigidbody>(true);
            _playerAnimator = pgo.GetComponentInChildren<Animator>(true);
        }

        _colliders = physicsRoot.GetComponentsInChildren<Collider>(true);
        CacheRenderers();
        SetAlpha(0f);
        SetColliders(false);

        if (ePrompt)
        {
            _promptBaseScale = ePrompt.localScale;
            _promptScale = _promptBaseScale;
            ePrompt.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        if (autoActivateOnStart) StartCoroutine(MainLoop());
    }

    void OnDisable()
    {
        UnlockPlayer();
        RestoreTimescale();
        if (ghostRB && !ghostRB.isKinematic)
#if UNITY_6000_0_OR_NEWER
            ghostRB.linearVelocity = Vector3.zero;
#else
            ghostRB.velocity = Vector3.zero;
#endif
        if (ePrompt) ePrompt.gameObject.SetActive(false);
    }

    IEnumerator MainLoop()
    {
        if (!_player) yield break;

        while (enabled && gameObject.activeInHierarchy)
        {
            _state = State.Spawning;
            PlaceAtRespawnPoint();
            SetColliders(false);
            if (fadeOnSpawn) yield return Fade(0f, 1f, fadeTime); else SetAlpha(1f);

            _state = State.Chasing;
            SetColliders(true);
            Fire(runTrigger);

            while (_state == State.Chasing && _player)
            {
                Vector3 to = _player.position - physicsRoot.position;
                Vector3 dirXZ = new Vector3(to.x, 0f, to.z);
                float dist = dirXZ.magnitude;

                if (dist > 1e-4f)
                {
                    Vector3 fwd = dirXZ / Mathf.Max(1e-5f, dist);
                    var look = Quaternion.LookRotation(fwd, Vector3.up);
                    modelRoot.rotation = Quaternion.RotateTowards(modelRoot.rotation, look, faceTurnSpeedDeg * Time.deltaTime);
                    MoveGhost(fwd * runSpeed);
                }

                if (dist <= lungeTriggerDistance)
                {
                    yield return LungeSlowmo();
                    break;
                }

                yield return null;
            }

            if (_state == State.Possessing)
            {
                SetColliders(false);
                yield return PossessionLoop();
                yield return Cooldown(shortRespawnCooldown);
            }
            else if (_state == State.Cooldown)
            {
                SetColliders(false);
                yield return Cooldown(longRespawnCooldown);
            }
        }
    }

    void MoveGhost(Vector3 planarVelocity)
    {
        if (!ghostRB)
        {
            physicsRoot.position += planarVelocity * Time.deltaTime;
            return;
        }

        if (ghostRB.isKinematic)
        {
            ghostRB.MovePosition(physicsRoot.position + planarVelocity * Time.deltaTime);
        }
        else
        {
#if UNITY_6000_0_OR_NEWER
            var v = ghostRB.linearVelocity;
            v.x = planarVelocity.x; v.z = planarVelocity.z;
            ghostRB.linearVelocity = v;
#else
            var v = ghostRB.velocity;
            v.x = planarVelocity.x; v.z = planarVelocity.z;
            ghostRB.velocity = v;
#endif
        }
    }

    IEnumerator LungeSlowmo()
    {
        _state = State.Lunge;
        Fire(jumpTrigger);
        SetColliders(true);

        _lungeStart = physicsRoot.position;
        Vector3 to = (_player ? _player.position - _lungeStart : modelRoot.forward * 2f);
        Vector3 dirXZ = new Vector3(to.x, 0f, to.z).normalized;
        _lungeTarget = _lungeStart + dirXZ * lungeDistance + Vector3.up * lungeUp;

        ApplyTimescale(slowmoTimeScale);

        float t = 0f, dur = Mathf.Max(0.1f, slowmoWindow);
        bool executed = false;
        Vector3 apex = Vector3.Lerp(_lungeStart, _lungeTarget, 0.5f) + Vector3.up * 0.25f;

        SetAlpha(1f);
        PromptShow(true);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / dur);

            Vector3 p1 = Vector3.Lerp(_lungeStart, apex, u);
            Vector3 p2 = Vector3.Lerp(apex, _lungeTarget, u);
            Vector3 pos = Vector3.Lerp(p1, p2, u);

            SetPhysicsPositionImmediate(pos);
            SetAlpha(1f - u);

            PromptPulse();

            if (Input.GetKeyDown(executionKey))
            {
                executed = true;
                FirePlayerSuccessTrigger();
                break;
            }
            yield return null;
        }

        SetAlpha(0f);
        RestoreTimescale();
        PromptShow(false);

        _state = executed ? State.Cooldown : State.Possessing;
        if (logs) Debug.Log(executed ? "[Ghost] Executed" : "[Ghost] Missed -> possession");
    }

    IEnumerator PossessionLoop()
    {
        if (logs) Debug.Log("[Ghost] Possession start");
        LockPlayer();

        float mash = 0f;
        Vector3 offset = new Vector3(0f, 1.2f, 0.4f);
        RigidbodyConstraints prevRB = RigidbodyConstraints.None;

        if (freezePlayerRigidbody && _playerRB)
        {
            prevRB = _playerRB.constraints;
            _playerRB.constraints = RigidbodyConstraints.FreezeAll;
#if UNITY_6000_0_OR_NEWER
            _playerRB.linearVelocity = Vector3.zero; _playerRB.angularVelocity = Vector3.zero;
#else
            _playerRB.velocity = Vector3.zero; _playerRB.angularVelocity = Vector3.zero;
#endif
        }

        SetAlpha(0f);
        PromptShow(true);

        while (_state == State.Possessing && _player)
        {
            SetPhysicsPositionImmediate(_player.position + offset);
            modelRoot.LookAt(_player.position + Vector3.up * 1.2f, Vector3.up);

            if (_playerCtrl && possessDPS > 0f) _playerCtrl.ApplyDamage(possessDPS * Time.deltaTime);

            mash = Mathf.Max(0f, mash - mashDecayPerSecond * Time.deltaTime);

            bool pressed = Input.GetKeyDown(mashKey);
            if (pressed) mash += mashPerPress;

            float progress = Mathf.Clamp01(mash / Mathf.Max(0.0001f, mashRequired));
            PromptPulse(pressed, progress);

            if (mash >= mashRequired)
            {
                FirePlayerSuccessTrigger();
                break;
            }

            yield return null;
        }

        PromptShow(false);

        if (freezePlayerRigidbody && _playerRB) _playerRB.constraints = prevRB;
        UnlockPlayer();

        if (logs) Debug.Log("[Ghost] Possession end");
    }

    IEnumerator Cooldown(float seconds)
    {
        _state = State.Cooldown;
        float t = 0f;
        if (ghostRB && !ghostRB.isKinematic)
#if UNITY_6000_0_OR_NEWER
            ghostRB.linearVelocity = Vector3.zero;
#else
            ghostRB.velocity = Vector3.zero;
#endif
        while (t < seconds) { t += Time.deltaTime; yield return null; }
    }

    void FirePlayerSuccessTrigger()
    {
        if (_playerAnimator && !string.IsNullOrEmpty(playerSuccessTrigger))
        {
            _playerAnimator.ResetTrigger(playerSuccessTrigger);
            _playerAnimator.SetTrigger(playerSuccessTrigger);
        }
    }

    void LockPlayer()
    {
        if (_playerCtrl) _playerCtrl.SetMovementLocked(true);
        if (_playerCombo) _playerCombo.SetFrozen(true);
    }

    void UnlockPlayer()
    {
        if (_playerCtrl) _playerCtrl.SetMovementLocked(false);
        if (_playerCombo) _playerCombo.SetFrozen(false);
    }

    void PlaceAtRespawnPoint()
    {
        Vector3 spawn;
        if (respawnAnchor)
        {
            spawn = respawnAnchor.position;
        }
        else
        {
            bool front = (Random.value < frontChance);
            float dist = front ? frontDistance : behindDistance;
            Vector3 basis = front && _player ? _player.forward : (_player ? -_player.forward : transform.forward);
            Vector3 side = Vector3.Cross(Vector3.up, basis).normalized;
            float lateral = Random.Range(lateralOffsetRange.x, lateralOffsetRange.y);
            spawn = (_player ? _player.position : transform.position) + basis * dist + side * lateral + Vector3.up * spawnYOffset;
        }

        SetPhysicsPositionImmediate(spawn);

        if (_player)
        {
            Vector3 lookDir = _player.position - physicsRoot.position; lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 1e-4f)
                modelRoot.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        }
    }

    void Fire(string trigger)
    {
        if (!animator || string.IsNullOrEmpty(trigger)) return;
        animator.ResetTrigger(trigger);
        animator.SetTrigger(trigger);
    }

    void SetPhysicsPositionImmediate(Vector3 worldPos)
    {
        if (ghostRB && ghostRB.isKinematic) ghostRB.position = worldPos;
        else physicsRoot.position = worldPos;
    }

    void SetColliders(bool on)
    {
        if (_colliders == null) return;
        for (int i = 0; i < _colliders.Length; i++)
            if (_colliders[i]) _colliders[i].enabled = on;
    }

    void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_renderers == null || _renderers.Length == 0) { _baseColors = null; return; }

        _baseColors = new Color[_renderers.Length][];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i]; if (!r) continue;
            var mats = r.materials;
            _baseColors[i] = new Color[mats.Length];
            for (int j = 0; j < mats.Length; j++)
            {
                var m = mats[j];
                _baseColors[i][j] = (m && m.HasProperty("_Color")) ? m.color : Color.white;
            }
        }
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        if (_renderers == null || _renderers.Length == 0 || duration <= 0f)
        { SetAlpha(to); yield break; }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float a = Mathf.Lerp(from, to, (fadeCurve != null && fadeCurve.length > 0) ? fadeCurve.Evaluate(u) : u);
            SetAlpha(a);
            yield return null;
        }
        SetAlpha(to);
    }

    void SetAlpha(float a)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i]; if (!r) continue;
            var mats = r.materials;
            for (int j = 0; j < mats.Length; j++)
            {
                var m = mats[j]; if (!m || !m.HasProperty("_Color")) continue;
                Color baseC = (_baseColors != null && _baseColors[i] != null && j < _baseColors[i].Length)
                            ? _baseColors[i][j] : m.color;
                baseC.a = Mathf.Clamp01(a * baseC.a);
                m.color = baseC;
            }
        }
    }

    void ApplyTimescale(float ts)
    {
        if (_inSlowmo) return;
        _origTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
        Time.timeScale = Mathf.Clamp(ts, 0.01f, 1f);
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        _inSlowmo = true;
    }

    void RestoreTimescale()
    {
        if (!_inSlowmo) return;
        Time.timeScale = _origTimeScale;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        _inSlowmo = false;
    }

    void PromptShow(bool on)
    {
        if (!ePrompt) return;
        if (on)
        {
            _promptScale = _promptBaseScale;
            _promptExtraPulse = 0f;
            ePrompt.localScale = _promptScale;
        }
        ePrompt.gameObject.SetActive(on);
    }

    void PromptPulse(bool pressedNow = false, float mashProgress = 0f)
    {
        if (!ePrompt) return;
        if (pressedNow) _promptExtraPulse += promptHitPop;

        float t = Time.unscaledTime;
        float basePulse = 1f + Mathf.Sin(t * Mathf.PI * 2f * Mathf.Max(0.01f, promptPulseHz)) * promptPulseAmt;
        float progPulse = mashProgress * 0.25f;
        float target = basePulse + progPulse + _promptExtraPulse;

        _promptExtraPulse = Mathf.MoveTowards(_promptExtraPulse, 0f, Time.unscaledDeltaTime * 2.5f);

        Vector3 s = _promptBaseScale * Mathf.Max(0.1f, target);
        _promptScale = Vector3.Lerp(_promptScale, s, 1f - Mathf.Exp(-promptAnimLerp * Time.unscaledDeltaTime));
        ePrompt.localScale = _promptScale;
    }
}
