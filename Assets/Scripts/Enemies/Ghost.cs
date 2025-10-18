using UnityEngine;
using System.Collections;

/// <summary>
/// GHOST STALKER (3D) � patched for PlayerController3D health/dim system
/// - Only ONE ghost can be active at a time (static gate).
/// - Spawns around the player either in FRONT or BEHIND (configurable distance & offset).
/// - Sprints towards the player. When close enough, plays a lunge/jump toward them and
///   enters a SLOW-MOTION "execution window".
/// - If player presses E during slow-mo: ghost dies -> LONG respawn cooldown.
/// - If player misses: ghost POSSESSES player -> drains HP via PlayerController3D.ApplyDamage();
///   player must MASH E some number of times to break free -> SMALL respawn cooldown.
/// - While possessed, slow-mo is off. Player RB can be frozen (controller stays enabled).
/// - While a ghost is active (chasing/possessing), no others can spawn/activate.
/// </summary>
[DisallowMultipleComponent]
public class GhostStalker3D : MonoBehaviour
{
    public enum GhostState { Inactive, Spawning, Chasing, LungeSlowmo, Possessing, Cooldown }

    // -------------------- GLOBAL SINGLETON-GATE --------------------
    static GhostStalker3D s_active; // only one active at a time

    // -------------------- REFS --------------------
    [Header("Refs")]
    public string playerTag = "Player";
    public Transform modelRoot;         // rotates to face player; defaults to transform
    public Animator animator;           // optional
    public string animRun = "Run";
    public string animLunge = "Lunge";
    public string animPossess = "Possess";
    public string animDeath = "Death";

    Transform _player;
    PlayerController3D _playerHP;       // NEW: health/dim/drain API
    Rigidbody _playerRB;                // to freeze on possess (optional)

    // -------------------- SPAWN PLACEMENT --------------------
    [Header("Spawn Around Player")]
    public bool autoActivateOnStart = true;
    [Tooltip("Meters in front of player for 'front' spawn.")]
    public float frontDistance = 8f;
    [Tooltip("Meters behind player for 'behind' spawn.")]
    public float behindDistance = 6f;
    [Tooltip("Random sideways offset at spawn.")]
    public Vector2 lateralOffsetRange = new Vector2(-2.0f, 2.0f);
    [Tooltip("Y offset for spawn (e.g., float slightly).")]
    public float spawnYOffset = 0.0f;
    [Tooltip("Chance to spawn in front instead of behind (0..1).")]
    [Range(0f, 1f)] public float frontWeight = 0.7f;

    // -------------------- CHASE --------------------
    [Header("Chase")]
    public float runSpeed = 9f;
    public float turnSpeed = 15f;
    [Tooltip("When within this distance, ghost transitions to lunge/slowmo.")]
    public float lungeTriggerDistance = 3.2f;

    // -------------------- LUNGE / SLOWMO EXECUTION WINDOW --------------------
    [Header("Lunge + Slow-Mo")]
    [Tooltip("Seconds of slow-motion window (UNSCALED time).")]
    public float slowmoWindow = 0.85f;
    [Range(0.05f, 1f)] public float slowmoTimeScale = 0.25f;
    public KeyCode executionKey = KeyCode.E;
    [Tooltip("Forward lunge distance during entry into slow-mo.")]
    public float lungeDistance = 2.5f;
    [Tooltip("Upward hop during lunge (meters).")]
    public float lungeUp = 0.6f;

    [Header("Cooldowns")]
    [Tooltip("Cooldown if the player kills the ghost in slow-mo.")]
    public float longRespawnCooldown = 6.0f;
    [Tooltip("Cooldown after a possession finishes/breaks.")]
    public float shortRespawnCooldown = 2.5f;

    // -------------------- POSSESSION --------------------
    [Header("Possession Drain")]
    [Tooltip("HP per second during possession, passed to PlayerController3D.ApplyDamage().")]
    public float possessDPS = 6f;
    [Tooltip("Freeze player's Rigidbody during possession (controller remains enabled so damage works).")]
    public bool freezePlayerDuringPossess = true;
    [Tooltip("Mash key to break free.")]
    public KeyCode mashKey = KeyCode.E;
    [Tooltip("Progress added per key press.")]
    public float mashPerPress = 1.0f;
    [Tooltip("Progress required to break possession.")]
    public float mashRequired = 12f;
    [Tooltip("Progress lost per second while possessed (decay).")]
    public float mashDecayPerSecond = 1.5f;

    // -------------------- VISUALS --------------------
    [Header("FX")]
    public bool fadeOnSpawnAndDeath = true;
    public float fadeTime = 0.25f;
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // -------------------- DEBUG --------------------
    [Header("Debug")]
    public bool drawSpawnGizmos = true;

    // -------------------- RUNTIME --------------------
    GhostState _state = GhostState.Inactive;
    float _stateT;
    Vector3 _spawnPos;
    Vector3 _lungeStartPos;
    Vector3 _lungeTargetPos;
    float _origTimeScale = 1f;
    bool _inSlowmo;
    float _mashProgress;
    bool _markedForRespawnLong;

    Renderer[] _renderers;
    Color[] _baseColors;

    void Awake()
    {
        if (!modelRoot) modelRoot = transform;

        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo)
        {
            _player = pgo.transform;
            _playerHP = pgo.GetComponentInChildren<PlayerController3D>(); // <-- NEW
            _playerRB = pgo.GetComponentInChildren<Rigidbody>();
        }

        CacheRenderers();
        SetAlpha(0f); // hidden at boot
    }

    void Start()
    {
        if (autoActivateOnStart) Activate();
    }

    void OnDisable()
    {
        RestoreTimescale();
        if (s_active == this) s_active = null;
    }

    // ----------------------------------------------------------------------
    // PUBLIC API
    // ----------------------------------------------------------------------
    public void Activate()
    {
        if (!_player) return;

        // Only one active at a time
        if (s_active && s_active != this)
            s_active.DeactivateImmediate();

        s_active = this;

        PlaceAtSpawnAroundPlayer();
        StartCoroutine(SpawnRoutine());
    }

    public void DeactivateImmediate()
    {
        StopAllCoroutines();
        RestoreTimescale();
        s_active = null;
        _state = GhostState.Inactive;
        SetAlpha(0f);
        gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------------
    // CORE LOOPS
    // ----------------------------------------------------------------------
    IEnumerator SpawnRoutine()
    {
        gameObject.SetActive(true);
        _state = GhostState.Spawning;
        _stateT = 0f;

        if (fadeOnSpawnAndDeath) yield return Fade(0f, 1f, fadeTime);
        else SetAlpha(1f);

        // Begin chase
        _state = GhostState.Chasing;
        _stateT = 0f;

        if (animator && !string.IsNullOrEmpty(animRun)) animator.Play(animRun, 0, 0f);

        while (_state == GhostState.Chasing && _player)
        {
            _stateT += Time.deltaTime;

            // Turn to face player (yaw only)
            Vector3 to = _player.position - transform.position;
            Vector3 dirXZ = new Vector3(to.x, 0f, to.z).normalized;
            if (dirXZ.sqrMagnitude > 1e-6f)
            {
                Quaternion look = Quaternion.LookRotation(dirXZ, Vector3.up);
                modelRoot.rotation = Quaternion.Slerp(modelRoot.rotation, look, turnSpeed * Time.deltaTime);
                transform.position += dirXZ * runSpeed * Time.deltaTime;
            }

            // Trigger lunge when close enough
            float distXZ = new Vector2(to.x, to.z).magnitude;
            if (distXZ <= lungeTriggerDistance)
            {
                yield return LungeAndSlowmoWindow();
                break;
            }

            yield return null;
        }

        // After LungeAndSlowmoWindow, either we killed (long) or we missed -> possession
        if (_state == GhostState.Possessing)
        {
            yield return PossessionLoop();
            _markedForRespawnLong = false;
            yield return CooldownAndRespawn(shortRespawnCooldown);
        }
        else if (_state == GhostState.Cooldown)
        {
            yield return CooldownAndRespawn(longRespawnCooldown);
        }
    }

    IEnumerator LungeAndSlowmoWindow()
    {
        _state = GhostState.LungeSlowmo;
        _stateT = 0f;

        // Lunge setup target (small hop & forward)
        _lungeStartPos = transform.position;

        Vector3 to = (_player ? _player.position - transform.position : modelRoot.forward * 2f);
        Vector3 dirXZ = new Vector3(to.x, 0f, to.z).normalized;
        _lungeTargetPos = _lungeStartPos + dirXZ * lungeDistance + Vector3.up * lungeUp;

        if (animator && !string.IsNullOrEmpty(animLunge)) animator.Play(animLunge, 0, 0f);

        // Enter slow-motion
        ApplyTimescale(slowmoTimeScale);

        float t = 0f;
        float dur = Mathf.Max(0.1f, slowmoWindow); // unscaled
        bool executed = false;

        // simple lunge motion during slow-mo
        Vector3 apex = Vector3.Lerp(_lungeStartPos, _lungeTargetPos, 0.5f) + Vector3.up * 0.25f;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;

            // Parabolic-ish interpolation during slowmo
            float u = Mathf.Clamp01(t / dur);
            Vector3 p1 = Vector3.Lerp(_lungeStartPos, apex, u);
            Vector3 p2 = Vector3.Lerp(apex, _lungeTargetPos, u);
            transform.position = Vector3.Lerp(p1, p2, u);

            // Player execution input
            if (Input.GetKeyDown(executionKey))
            {
                executed = true;
                break;
            }

            yield return null;
        }

        RestoreTimescale();

        if (executed)
        {
            // Player killed the ghost -> long cooldown
            if (animator && !string.IsNullOrEmpty(animDeath)) animator.Play(animDeath, 0, 0f);
            if (fadeOnSpawnAndDeath) yield return Fade(1f, 0f, fadeTime);
            else SetAlpha(0f);

            _markedForRespawnLong = true;
            _state = GhostState.Cooldown;
            yield break;
        }

        // Missed window -> possession
        _state = GhostState.Possessing;
    }

    IEnumerator PossessionLoop()
    {
        // Attach/lock to player
        if (animator && !string.IsNullOrEmpty(animPossess)) animator.Play(animPossess, 0, 0f);

        // Stick to player chest-ish
        Vector3 localOffset = new Vector3(0f, 1.2f, 0.4f);
        _mashProgress = 0f;

        // Freeze player's Rigidbody (controller remains enabled so ApplyDamage works)
        RigidbodyConstraints prevRB = RigidbodyConstraints.None;
        if (_playerRB && freezePlayerDuringPossess)
        {
            prevRB = _playerRB.constraints;
            _playerRB.constraints = RigidbodyConstraints.FreezeAll;
            _playerRB.linearVelocity = Vector3.zero;
            _playerRB.angularVelocity = Vector3.zero;
        }

        while (_state == GhostState.Possessing && _player)
        {
            // Follow player
            transform.position = _player.position + localOffset;
            modelRoot.LookAt(_player.position + Vector3.up * 1.2f, Vector3.up);

            // Drain HP using new system
            if (_playerHP && possessDPS > 0f)
                _playerHP.ApplyDamage(possessDPS * Time.deltaTime);

            // Mash meter (repeated KeyDown)
            _mashProgress = Mathf.Max(0f, _mashProgress - mashDecayPerSecond * Time.deltaTime);
            if (Input.GetKeyDown(mashKey)) _mashProgress += mashPerPress;

            if (_mashProgress >= mashRequired)
            {
                break; // break possession
            }

            yield return null;
        }

        // Restore player physics
        if (_playerRB && freezePlayerDuringPossess) _playerRB.constraints = prevRB;

        // Small vanish
        if (fadeOnSpawnAndDeath) yield return Fade(1f, 0f, fadeTime);
        else SetAlpha(0f);
    }

    IEnumerator CooldownAndRespawn(float cooldown)
    {
        _state = GhostState.Cooldown;
        _stateT = 0f;

        // If we didn't already fade out (execution path), do it now
        if (!_markedForRespawnLong && fadeOnSpawnAndDeath)
            yield return Fade(1f, 0f, fadeTime);

        // Wait cooldown
        float t = 0f;
        while (t < cooldown)
        {
            t += Time.deltaTime;
            yield return null;
        }

        // Respawn (only if still the active ghost gate)
        if (s_active == this)
        {
            PlaceAtSpawnAroundPlayer();
            yield return SpawnRoutine();
        }
    }

    // ----------------------------------------------------------------------
    // HELPERS
    // ----------------------------------------------------------------------
    void PlaceAtSpawnAroundPlayer()
    {
        if (!_player) return;

        bool spawnFront = (Random.value < frontWeight);
        float dist = spawnFront ? frontDistance : behindDistance;

        Vector3 basis = spawnFront ? _player.forward : -_player.forward;
        Vector3 side = Vector3.Cross(Vector3.up, basis).normalized;
        float lateral = Random.Range(lateralOffsetRange.x, lateralOffsetRange.y);

        _spawnPos = _player.position + basis * dist + side * lateral + Vector3.up * spawnYOffset;
        transform.position = _spawnPos;
        modelRoot.rotation = Quaternion.LookRotation((_player.position - transform.position).normalized, Vector3.up);
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

    void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var mr = _renderers[i] as MeshRenderer;
            if (mr && mr.sharedMaterial && mr.sharedMaterial.HasProperty("_Color"))
                _baseColors[i] = mr.sharedMaterial.color;
            else
                _baseColors[i] = Color.white;
        }
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        if (_renderers == null || _renderers.Length == 0 || duration <= 0f)
            yield break;

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
            var r = _renderers[i];
            if (!r) continue;

            if (r is MeshRenderer mr && mr.material && mr.material.HasProperty("_Color"))
            {
                var c = _baseColors[i];
                c.a = a * _baseColors[i].a;
                mr.material.color = c;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawSpawnGizmos || !_player) return;

        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Vector3 fwd = _player.forward;
        Vector3 frontPos = _player.position + fwd * frontDistance;
        Gizmos.DrawWireSphere(frontPos, Mathf.Abs(lateralOffsetRange.y - lateralOffsetRange.x) * 0.5f + 0.2f);

        Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.35f);
        Vector3 backPos = _player.position - fwd * behindDistance;
        Gizmos.DrawWireSphere(backPos, Mathf.Abs(lateralOffsetRange.y - lateralOffsetRange.x) * 0.5f + 0.2f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(frontPos, _player.position);
        Gizmos.DrawLine(backPos, _player.position);
    }
}
