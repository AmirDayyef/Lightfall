using UnityEngine;
using System.Reflection;

/// <summary>
/// Flying rush enemy (3D "bat" version).
/// - ORBIT: circles the player on the XZ plane at a target height.
/// - RUSH: dives/swoops toward the player, damages on contact sphere.
/// - ASCEND: flies straight up to a target Y (relative or absolute), then returns to ORBIT.
/// - If this enemy takes damage (hp decreases), it immediately begins ASCEND.
/// 
/// Notes:
/// * Requires EnemyBase on the same GameObject. Set EnemyBase.contactDamage = 0 (damage comes from rush hit only).
/// * Uses 3D physics queries (OverlapSphere) for rush contact.
/// </summary>
[RequireComponent(typeof(EnemyBase))]
public class FlyingRushEnemy3D : MonoBehaviour
{
    public enum State { Orbit, Rush, Ascend }

    [Header("Links")]
    public string playerTag = "Player";
    public LayerMask playerLayers = ~0;   // optional filter for overlap checks

    [Header("Orbit (circle player on XZ)")]
    public float orbitRadius = 3.25f;
    public float orbitRadiusJitter = 0.85f;
    public float orbitAngularSpeed = 1.9f;           // radians/sec
    public float orbitCenterFollowLerp = 0.15f;
    public Vector2 orbitDurationRange = new Vector2(0.9f, 1.9f);
    [Tooltip("Horizontal chase speed used to move toward the ideal orbit point.")]
    public float orbitChaseSpeed = 10f;

    [Header("Orbit Height")]
    [Tooltip("Use a fixed Y offset above player (true) or a fixed world Y (false).")]
    public bool orbitRelativeToPlayer = true;
    public float orbitHeightAbovePlayer = 2.25f;
    public float orbitWorldY = 4.0f;
    public float orbitHeightLerp = 10f;

    [Header("Rush (swoop)")]
    public float rushSpeed = 12.0f;
    public float rushContactRadius = 0.45f;
    public float rushHitDamage = 12f;
    [Tooltip("If > 0, bail to Ascend after this many seconds if we miss.")]
    public float rushTimeout = 0f;              // 0 = must hit to leave Rush

    [Header("Ascend (straight up, then orbit)")]
    [Tooltip("If true: ascend to playerY + ascendHeightAbovePlayer. If false: ascend to fixed world Y: ascendWorldY.")]
    public bool ascendRelativeToPlayer = true;
    public float ascendHeightAbovePlayer = 4.0f;
    public float ascendWorldY = 9.0f;
    public float ascendSpeed = 11.5f;
    public float ascendYThreshold = 0.06f;

    [Header("Look/Bank (visual only)")]
    public Transform visual;                // optional visual root to rotate
    public float faceTurnSpeed = 14f;       // how quickly to face movement dir
    public float bankAmount = 22f;          // degrees to roll when turning
    public float bankLerp = 10f;

    [Header("Debug")]
    public bool drawDebug = false;

    // Internals
    EnemyBase _base;
    Transform _player;

    State _state;
    float _stateT, _stateDur;

    Vector3 _orbitCenter;   // follows player position on XZ
    float _orbitAngle;
    float _orbitRadiusNow;

    float _ascendTargetY;
    bool _rushHitApplied;

    // HP reflection polling
    FieldInfo _hpField;   // private "hp" in EnemyBase
    float _lastHP = float.NaN;

    // cached
    Vector3 _vel;         // for facing/banking only

    void Awake()
    {
        _base = GetComponent<EnemyBase>();
        _base.contactDamage = 0f;

        // reflection: find the private 'hp' field in EnemyBase
        _hpField = typeof(EnemyBase).GetField("hp", BindingFlags.NonPublic | BindingFlags.Instance);
        _lastHP = GetCurrentHP();

        var pgo = GameObject.FindGameObjectWithTag(string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag);
        if (pgo) _player = pgo.transform;

        if (!_player) { enabled = false; return; }

        // seed orbit
        _orbitCenter = _player.position;
        var relXZ = new Vector3(transform.position.x - _orbitCenter.x, 0f, transform.position.z - _orbitCenter.z);
        _orbitAngle = Mathf.Atan2(relXZ.z, relXZ.x);
        _orbitRadiusNow = orbitRadius + Random.Range(-orbitRadiusJitter, orbitRadiusJitter);

        Enter(State.Orbit, Random.Range(orbitDurationRange.x, orbitDurationRange.y));
    }

    void Update()
    {
        if (!_player) return;

        // if hp dropped since last frame ? ascend
        float curHP = GetCurrentHP();
        if (!float.IsNaN(_lastHP) && curHP < _lastHP && _state != State.Ascend)
            BeginAscend();
        _lastHP = curHP;

        _stateT += Time.deltaTime;

        switch (_state)
        {
            case State.Orbit: TickOrbit(); break;
            case State.Rush: TickRush(); break;
            case State.Ascend: TickAscend(); break;
        }

        UpdateVisualFacing();
    }

    float GetCurrentHP()
    {
        if (_hpField == null) return float.NaN;
        object v = _hpField.GetValue(_base);
        return v is float f ? f : float.NaN;
    }

    void Enter(State s, float dur)
    {
        _state = s;
        _stateT = 0f;
        _stateDur = Mathf.Max(0.01f, dur);

        if (s == State.Orbit)
        {
            _orbitRadiusNow = orbitRadius + Random.Range(-orbitRadiusJitter, orbitRadiusJitter);
            var center = _player ? _player.position : transform.position;
            var relXZ = new Vector3(transform.position.x - center.x, 0f, transform.position.z - center.z);
            if (relXZ.sqrMagnitude > 0.0001f) _orbitAngle = Mathf.Atan2(relXZ.z, relXZ.x);
        }
        else if (s == State.Rush)
        {
            _rushHitApplied = false;
        }
        else if (s == State.Ascend)
        {
            _ascendTargetY = ascendRelativeToPlayer
                ? (_player.position.y + ascendHeightAbovePlayer)
                : ascendWorldY;
        }
    }

    // -------- ORBIT --------
    void TickOrbit()
    {
        // follow player center (on XZ)
        Vector3 targetCenter = _player.position;
        _orbitCenter = Vector3.Lerp(_orbitCenter, new Vector3(targetCenter.x, _orbitCenter.y, targetCenter.z), orbitCenterFollowLerp);

        // target height for orbit
        float desiredY = orbitRelativeToPlayer ? _player.position.y + orbitHeightAbovePlayer : orbitWorldY;
        float y = Mathf.Lerp(transform.position.y, desiredY, 1f - Mathf.Exp(-orbitHeightLerp * Time.deltaTime));

        // advance angle, compute orbit point (on XZ)
        _orbitAngle += orbitAngularSpeed * Time.deltaTime;
        Vector3 onCircle = new Vector3(Mathf.Cos(_orbitAngle), 0f, Mathf.Sin(_orbitAngle)) * _orbitRadiusNow;
        Vector3 orbitTarget = new Vector3(_orbitCenter.x, y, _orbitCenter.z) + onCircle;

        // move toward orbit target
        Vector3 prev = transform.position;
        transform.position = Vector3.MoveTowards(transform.position, orbitTarget, orbitChaseSpeed * Time.deltaTime);
        _vel = (transform.position - prev) / Mathf.Max(Time.deltaTime, 1e-5f);

        if (_stateT >= _stateDur)
            Enter(State.Rush, rushTimeout > 0f ? rushTimeout : 9999f);
    }

    // -------- RUSH --------
    void TickRush()
    {
        if (!_player) return;

        Vector3 prev = transform.position;
        Vector3 target = _player.position;
        transform.position = Vector3.MoveTowards(transform.position, target, rushSpeed * Time.deltaTime);
        _vel = (transform.position - prev) / Mathf.Max(Time.deltaTime, 1e-5f);

        if (!_rushHitApplied)
        {
            // contact sphere
            var hits = Physics.OverlapSphere(transform.position, rushContactRadius, playerLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (!h || !h.CompareTag(string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag))
                    continue;

                // >>> Patched to PlayerController3D health system <<<
                var pc = h.GetComponentInParent<PlayerController3D>();
                if (pc)
                {
                    float dmg = pc.ModifyDamage(rushHitDamage);
                    pc.ApplyDamage(dmg);
                }

                _rushHitApplied = true;
                BeginAscend();
                return;
            }
        }

        if (rushTimeout > 0f && _stateT >= _stateDur)
            BeginAscend();
    }

    // -------- ASCEND (straight up, then ORBIT) --------
    void TickAscend()
    {
        Vector3 prev = transform.position;
        float nextY = Mathf.MoveTowards(prev.y, _ascendTargetY, ascendSpeed * Time.deltaTime);
        transform.position = new Vector3(prev.x, nextY, prev.z);
        _vel = (transform.position - prev) / Mathf.Max(Time.deltaTime, 1e-5f);

        if (Mathf.Abs(nextY - _ascendTargetY) <= ascendYThreshold)
            Enter(State.Orbit, Random.Range(orbitDurationRange.x, orbitDurationRange.y));
    }

    void BeginAscend() => Enter(State.Ascend, 9999f); // exit when Y reached

    void UpdateVisualFacing()
    {
        // face velocity direction; bank while turning (optional)
        Transform t = visual ? visual : transform;
        Vector3 v = _vel;
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(v.normalized, Vector3.up);

        // banking: roll based on lateral turn (approx. by cross product sign)
        float bank = 0f;
        Vector3 fwd = t.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.0001f)
        {
            float signed = Vector3.SignedAngle(fwd.normalized, v.normalized, Vector3.up); // -180..180
            bank = Mathf.Clamp(-signed * 0.25f, -bankAmount, bankAmount);
        }

        Quaternion withBank = target * Quaternion.Euler(0f, 0f, bank);
        t.rotation = Quaternion.Slerp(t.rotation, withBank, faceTurnSpeed * Time.deltaTime);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawDebug) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, rushContactRadius);

        // Ascend target preview
        float y = ascendRelativeToPlayer
            ? (Application.isPlaying && _player ? _player.position.y + ascendHeightAbovePlayer : ascendHeightAbovePlayer)
            : ascendWorldY;

        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawLine(new Vector3(transform.position.x - 10f, y, transform.position.z),
                        new Vector3(transform.position.x + 10f, y, transform.position.z));
    }
}
