using UnityEngine;
using System.Reflection;

[RequireComponent(typeof(EnemyBase))]
public class FlyingRushEnemy3D : MonoBehaviour
{
    public enum State { Orbit, Rush, Ascend }

    public string playerTag = "Player";
    public LayerMask playerLayers = ~0;

    public float orbitRadius = 3.25f;
    public float orbitRadiusJitter = 0.85f;
    public float orbitAngularSpeed = 1.9f;
    public float orbitCenterFollowLerp = 0.15f;
    public Vector2 orbitDurationRange = new Vector2(0.9f, 1.9f);
    public float orbitChaseSpeed = 10f;

    public bool orbitRelativeToPlayer = true;
    public float orbitHeightAbovePlayer = 2.25f;
    public float orbitWorldY = 4.0f;
    public float orbitHeightLerp = 10f;

    public float rushSpeed = 12.0f;
    public float rushContactRadius = 0.45f;
    public float rushHitDamage = 12f;
    public float rushTimeout = 0f;

    public bool ascendRelativeToPlayer = true;
    public float ascendHeightAbovePlayer = 4.0f;
    public float ascendWorldY = 9.0f;
    public float ascendSpeed = 11.5f;
    public float ascendYThreshold = 0.06f;

    public bool clampZ = true;
    public float minZ = -10f;
    public float maxZ = 10f;

    public Transform visual;
    public bool enableVisualFacing = false;
    public float faceTurnSpeed = 14f;

    public bool enableScaleAnim = true;
    [Range(0f, 0.5f)] public float orbitPulseAmount = 0.08f;
    public float orbitPulseHz = 2.2f;
    [Range(0f, 0.8f)] public float rushForwardStretch = 0.25f;
    public float rushScaleLerp = 12f;
    [Range(0f, 0.5f)] public float ascendVerticalStretch = 0.08f;
    public float ascendScaleLerp = 8f;
    public float relaxScaleLerp = 10f;

    public bool drawDebug = false;

    EnemyBase _base;
    Transform _player;

    State _state;
    float _stateT, _stateDur;

    Vector3 _orbitCenter;
    float _orbitAngle;
    float _orbitRadiusNow;

    float _ascendTargetY;
    bool _rushHitApplied;

    FieldInfo _hpField;
    float _lastHP = float.NaN;

    Vector3 _vel;
    Vector3 _visualBaseScale;
    Vector3 _visualScale;

    void Awake()
    {
        _base = GetComponent<EnemyBase>();
        _base.contactDamage = 0f;

        if (!visual) visual = transform;
        _visualBaseScale = visual ? visual.localScale : Vector3.one;
        _visualScale = _visualBaseScale;

        _hpField = typeof(EnemyBase).GetField("hp", BindingFlags.NonPublic | BindingFlags.Instance);
        _lastHP = GetCurrentHP();

        var pgo = GameObject.FindGameObjectWithTag(string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag);
        if (pgo) _player = pgo.transform;
        if (!_player) { enabled = false; return; }

        _orbitCenter = _player.position;
        var relXZ = new Vector3(transform.position.x - _orbitCenter.x, 0f, transform.position.z - _orbitCenter.z);
        _orbitAngle = Mathf.Atan2(relXZ.z, relXZ.x);
        _orbitRadiusNow = orbitRadius + Random.Range(-orbitRadiusJitter, orbitRadiusJitter);

        if (minZ > maxZ) { float t = minZ; minZ = maxZ; maxZ = t; }

        Enter(State.Orbit, Random.Range(orbitDurationRange.x, orbitDurationRange.y));
    }

    void Update()
    {
        if (!_player) return;

        float curHP = GetCurrentHP();
        if (!float.IsNaN(_lastHP) && curHP < _lastHP && _state != State.Ascend) BeginAscend();
        _lastHP = curHP;

        _stateT += Time.deltaTime;

        switch (_state)
        {
            case State.Orbit: TickOrbit(); break;
            case State.Rush: TickRush(); break;
            case State.Ascend: TickAscend(); break;
        }

        if (enableVisualFacing) UpdateVisualFacing();
        if (enableScaleAnim) UpdateScaleFakeAnim();
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
            _ascendTargetY = ascendRelativeToPlayer ? (_player.position.y + ascendHeightAbovePlayer) : ascendWorldY;
        }
    }

    void TickOrbit()
    {
        Vector3 targetCenter = _player.position;
        _orbitCenter = Vector3.Lerp(_orbitCenter, new Vector3(targetCenter.x, _orbitCenter.y, targetCenter.z), Mathf.Clamp01(orbitCenterFollowLerp));

        float desiredY = orbitRelativeToPlayer ? _player.position.y + orbitHeightAbovePlayer : orbitWorldY;
        float y = Mathf.Lerp(transform.position.y, desiredY, 1f - Mathf.Exp(-orbitHeightLerp * Time.deltaTime));

        _orbitAngle += orbitAngularSpeed * Time.deltaTime;
        Vector3 onCircle = new Vector3(Mathf.Cos(_orbitAngle), 0f, Mathf.Sin(_orbitAngle)) * _orbitRadiusNow;
        Vector3 orbitTarget = new Vector3(_orbitCenter.x, y, _orbitCenter.z) + onCircle;

        Vector3 prev = transform.position;
        Vector3 next = Vector3.MoveTowards(prev, orbitTarget, orbitChaseSpeed * Time.deltaTime);
        next = ApplyZClamp(next);
        transform.position = next;

        _vel = (transform.position - prev) / Mathf.Max(Time.deltaTime, 1e-5f);

        if (_stateT >= _stateDur) Enter(State.Rush, rushTimeout > 0f ? rushTimeout : 9999f);
    }

    void TickRush()
    {
        if (!_player) return;

        Vector3 prev = transform.position;
        Vector3 next = Vector3.MoveTowards(prev, _player.position, rushSpeed * Time.deltaTime);
        next = ApplyZClamp(next);
        transform.position = next;

        _vel = (transform.position - prev) / Mathf.Max(Time.deltaTime, 1e-5f);

        if (!_rushHitApplied)
        {
            var hits = Physics.OverlapSphere(transform.position, rushContactRadius, playerLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (!h || !h.CompareTag(string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag)) continue;

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

        if (rushTimeout > 0f && _stateT >= _stateDur) BeginAscend();
    }

    void TickAscend()
    {
        Vector3 prev = transform.position;
        float nextY = Mathf.MoveTowards(prev.y, _ascendTargetY, ascendSpeed * Time.deltaTime);
        Vector3 next = new Vector3(prev.x, nextY, prev.z);
        next = ApplyZClamp(next);
        transform.position = next;

        _vel = (transform.position - prev) / Mathf.Max(Time.deltaTime, 1e-5f);

        if (Mathf.Abs(nextY - _ascendTargetY) <= ascendYThreshold)
            Enter(State.Orbit, Random.Range(orbitDurationRange.x, orbitDurationRange.y));
    }

    void BeginAscend() => Enter(State.Ascend, 9999f);

    void UpdateVisualFacing()
    {
        if (!visual) return;
        Vector3 v = _vel; v.y = 0f;
        if (v.sqrMagnitude < 0.0001f) return;
        Quaternion t = Quaternion.LookRotation(v.normalized, Vector3.up);
        visual.rotation = Quaternion.Slerp(visual.rotation, t, faceTurnSpeed * Time.deltaTime);
    }

    void UpdateScaleFakeAnim()
    {
        if (!visual) return;

        if (_state == State.Orbit && orbitPulseAmount > 0f && orbitPulseHz > 0f)
        {
            float s = 1f + orbitPulseAmount * Mathf.Sin(Time.time * Mathf.PI * 2f * orbitPulseHz);
            _visualScale = Vector3.Lerp(_visualScale, _visualBaseScale * s, 1f - Mathf.Exp(-relaxScaleLerp * Time.deltaTime));
        }
        else if (_state == State.Rush && rushForwardStretch > 0f)
        {
            float z = 1f + rushForwardStretch;
            float xy = 1f / Mathf.Sqrt(z);
            Vector3 target = new Vector3(_visualBaseScale.x * xy, _visualBaseScale.y * xy, _visualBaseScale.z * z);
            _visualScale = Vector3.Lerp(_visualScale, target, 1f - Mathf.Exp(-rushScaleLerp * Time.deltaTime));
        }
        else if (_state == State.Ascend && ascendVerticalStretch > 0f)
        {
            float y = 1f + ascendVerticalStretch;
            float xz = 1f / Mathf.Sqrt(y);
            Vector3 target = new Vector3(_visualBaseScale.x * xz, _visualBaseScale.y * y, _visualBaseScale.z * xz);
            _visualScale = Vector3.Lerp(_visualScale, target, 1f - Mathf.Exp(-ascendScaleLerp * Time.deltaTime));
        }
        else
        {
            _visualScale = Vector3.Lerp(_visualScale, _visualBaseScale, 1f - Mathf.Exp(-relaxScaleLerp * Time.deltaTime));
        }

        visual.localScale = _visualScale;
    }

    Vector3 ApplyZClamp(Vector3 p)
    {
        if (!clampZ) return p;
        if (minZ > maxZ) { float t = minZ; minZ = maxZ; maxZ = t; }
        p.z = Mathf.Clamp(p.z, minZ, maxZ);
        return p;
    }

    void OnDrawGizmosSelected()
    {
        if (drawDebug)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, rushContactRadius);

            float y = ascendRelativeToPlayer
                ? (Application.isPlaying && _player ? _player.position.y + ascendHeightAbovePlayer : ascendHeightAbovePlayer)
                : ascendWorldY;

            Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
            Gizmos.DrawLine(new Vector3(transform.position.x - 10f, y, transform.position.z),
                            new Vector3(transform.position.x + 10f, y, transform.position.z));
        }

        if (clampZ)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f);
            Vector3 a = new Vector3(transform.position.x - 100f, transform.position.y, minZ);
            Vector3 b = new Vector3(transform.position.x + 100f, transform.position.y, minZ);
            Vector3 c = new Vector3(transform.position.x - 100f, transform.position.y, maxZ);
            Vector3 d = new Vector3(transform.position.x + 100f, transform.position.y, maxZ);
            Gizmos.DrawLine(a, b); Gizmos.DrawLine(c, d);
        }
    }
}
