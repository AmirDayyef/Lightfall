using UnityEngine;
using System.Collections;

[RequireComponent(typeof(EnemyBase))]
[DisallowMultipleComponent]
public class WalkerDualAttackEnemy3D : MonoBehaviour
{
    public enum State { GuardIdle, Chase, LightSwipe, HeavySwing, Recover, ReturnToPost, Dead }

    public float moveSpeed = 3.0f;
    public float standOffDistance = 1.35f;
    public float minPersonalSpace = 1.10f;
    public float backOffSpeedMult = 0.6f;

    public string playerTag = "Player";
    public float faceDeadzone = 0.05f;
    public float faceSpeedDegPerSec = 720f;

    public float lightBias = 0.6f;
    public float lightRange = 1.3f;
    public float heavyRange = 2.0f;

    public float lightWindup = 0.18f;
    public float lightActive = 0.06f;
    public float lightRecover = 0.25f;
    public float lightDamage = 10f;
    public float lightHitRadius = 0.9f;

    public float heavyWindup = 0.45f;
    public float heavyActive = 0.10f;
    public float heavyRecover = 0.45f;
    public float heavyDamage = 18f;
    public float heavyHitRadius = 1.2f;
    public float heavyDashTime = 0.15f;
    public float heavyDashSpeed = 6.5f;
    public float heavyDashMinStop = 1.0f;

    public Animator animator;
    public string animLightTrigger = "Light";
    public string animHeavyTrigger = "Heavy";
    public string animRunTrigger = "Run";
    public string animIdleTrigger = "Idle";
    public string animDeathTrigger = "Death";
    public string animHitTrigger = "Hit";

    public float deathAnimTime = 0.8f;
    public float hitAnimMinInterval = 0.05f;

    public LayerMask playerLayers = ~0;

    public bool stopHardOnAttackStart = true;
    public float attackCooldown = 0.3f;

    public Transform guardCenter;
    public float guardRadius = 6f;
    public float leashRadius = 12f;
    public float forgetPlayerAfter = 2.0f;
    public float returnSpeedMult = 1.1f;
    public float postArriveTolerance = 0.15f;

    public bool autoGuardFromGroundTag = true;
    public string groundTag = "Ground";
    public float sameYTolerance = 0.25f;
    public float boundsPadding = 0.0f;
    public bool useBoxGuard = true;

    public string bossTag = "Boss";
    public float bossPresenceRecheckInterval = 1.0f;

    public ParticleSystem deathBurstPrefab;
    public int fallbackBurstCount = 30;
    public float fadeOutTime = 0.6f;

    public bool drawRanges = true;
    public bool drawGuardGizmos = true;

    EnemyBase _base;
    Rigidbody _rb;
    Transform _player;
    PlayerController3D _playerHP;
    State _state;
    float _stateT;
    bool _lastWasLight = true;
    float _lastAttackStart = -999f;

    Vector3 _spawnPos;
    Vector3 _computedCenter;
    Bounds _guardBounds;
    float _computedSphereRadius;
    bool _hasComputedBounds;
    float _lastInsideGuardTime = -999f;

    bool _bossPresent;
    float _nextBossCheck;
    bool _dying;
    float _lastHitAnimTime = -999f;

    void Awake()
    {
        _base = GetComponent<EnemyBase>();
        _base.customDeathHandled = true;
        _base.contactDamage = 0f;

        _rb = gameObject.GetComponent<Rigidbody>();
        if (!_rb) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        var pgo = GameObject.FindGameObjectWithTag(string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag);
        if (pgo)
        {
            _player = pgo.transform;
            _playerHP = pgo.GetComponentInChildren<PlayerController3D>();
        }

        _spawnPos = transform.position;

        _bossPresent = BossExists();
        _nextBossCheck = Time.time + Mathf.Max(0f, bossPresenceRecheckInterval);

        if (!_bossPresent)
        {
            if (autoGuardFromGroundTag) ComputeGuardBoundsFromGrounds();
            else _computedCenter = guardCenter ? guardCenter.position : _spawnPos;
        }

        _state = _bossPresent ? State.Chase : State.GuardIdle;
        FireIdleOrRunForState(_state);
    }

    void Update()
    {
        if (_dying || !_player) return;
        _stateT += Time.deltaTime;

        if (bossPresenceRecheckInterval > 0f && Time.time >= _nextBossCheck)
        {
            _nextBossCheck = Time.time + bossPresenceRecheckInterval;
            _bossPresent = BossExists();
            if (_bossPresent && _state == State.ReturnToPost) Change(State.Chase);
        }

        Vector3 toPlayer = _player.position - transform.position;
        Vector3 dirToPlayerXZ = new Vector3(toPlayer.x, 0f, toPlayer.z);
        float distToPlayerXZ = dirToPlayerXZ.magnitude;
        Vector3 dirXZ = distToPlayerXZ > 1e-6f ? dirToPlayerXZ / distToPlayerXZ : Vector3.forward;

        if (_bossPresent) { PureRushTick(dirXZ, distToPlayerXZ); return; }

        Vector3 center = autoGuardFromGroundTag ? _computedCenter : (guardCenter ? guardCenter.position : _spawnPos);
        bool playerInsideGuard = IsInsideGuardArea(_player.position);

        float distFromCenterXZ = new Vector2((transform.position - center).x, (transform.position - center).z).magnitude;
        bool leashExceeded = useBoxGuard ? !IsInsideGuardArea(transform.position)
                                         : distFromCenterXZ > leashRadius;

        if (playerInsideGuard) _lastInsideGuardTime = Time.time;
        bool playerForgotten = !playerInsideGuard && (Time.time - _lastInsideGuardTime >= forgetPlayerAfter);

        switch (_state)
        {
            case State.GuardIdle:
                FaceTowards(_player.position, faceSpeedDegPerSec);
                if (playerInsideGuard) { Change(State.Chase); break; }
                ReturnHomeStep(center);
                break;

            case State.Chase:
                if (leashExceeded || playerForgotten) { Change(State.ReturnToPost); break; }

                FaceTowards(_player.position, faceSpeedDegPerSec);

                bool moving = false;
                if (distToPlayerXZ > standOffDistance + 0.05f)
                {
                    transform.position += dirXZ * moveSpeed * Time.deltaTime;
                    moving = true;
                }
                else if (distToPlayerXZ < minPersonalSpace)
                {
                    transform.position -= dirXZ * (moveSpeed * backOffSpeedMult) * Time.deltaTime;
                    moving = true;
                }
                FireRunOrIdle(moving);

                if (distToPlayerXZ <= lightRange && distToPlayerXZ >= minPersonalSpace)
                    TryStartAttack(dirXZ, distToPlayerXZ);
                else if (distToPlayerXZ <= heavyRange && distToPlayerXZ > standOffDistance * 0.7f)
                    TryStartAttack(dirXZ, distToPlayerXZ);
                break;

            case State.Recover:
                FireRunOrIdle(false);
                if (leashExceeded || playerForgotten) { Change(State.ReturnToPost); break; }

                float need = (_lastWasLight ? lightRecover : heavyRecover);
                if (distToPlayerXZ >= standOffDistance + 0.05f || _stateT >= need)
                    Change(State.Chase);
                break;

            case State.ReturnToPost:
                FireRunOrIdle(true);
                if (playerInsideGuard) { Change(State.Chase); break; }

                Vector3 target = ClampToGuardArea(center);
                Vector3 toPost = target - transform.position;
                Vector3 toPostXZ = new Vector3(toPost.x, 0f, toPost.z);
                float d = toPostXZ.magnitude;

                if (d <= postArriveTolerance) { Change(State.GuardIdle); break; }

                Vector3 stepDir = toPostXZ / Mathf.Max(1e-6f, d);
                transform.position += stepDir * (moveSpeed * returnSpeedMult) * Time.deltaTime;
                FaceTowards(transform.position + stepDir, faceSpeedDegPerSec);
                break;
        }
    }

    void PureRushTick(Vector3 dirXZ, float distToPlayerXZ)
    {
        FaceTowards(_player.position, faceSpeedDegPerSec);

        bool moving = false;
        if (distToPlayerXZ > standOffDistance + 0.05f)
        {
            transform.position += dirXZ * moveSpeed * Time.deltaTime;
            moving = true;
        }
        else if (distToPlayerXZ < minPersonalSpace)
        {
            transform.position -= dirXZ * (moveSpeed * backOffSpeedMult) * Time.deltaTime;
            moving = true;
        }
        FireRunOrIdle(moving);

        if (distToPlayerXZ <= lightRange && distToPlayerXZ >= minPersonalSpace)
            TryStartAttack(dirXZ, distToPlayerXZ);
        else if (distToPlayerXZ <= heavyRange && distToPlayerXZ > standOffDistance * 0.7f)
            TryStartAttack(dirXZ, distToPlayerXZ);
    }

    void TryStartAttack(Vector3 dirXZ, float distXZ)
    {
        if (Time.time - _lastAttackStart < attackCooldown) return;

        bool canLight = distXZ <= lightRange && distXZ >= minPersonalSpace;
        bool canHeavy = (distXZ <= heavyRange && distXZ > standOffDistance * 0.7f);

        int pick = 0;
        if (canLight && canHeavy) pick = (Random.value < lightBias ? 1 : 2);
        else if (canLight) pick = 1;
        else if (canHeavy) pick = 2;
        if (pick == 0) return;

        _lastAttackStart = Time.time;

        if (pick == 1) StartCoroutine(CoLight());
        else StartCoroutine(CoHeavy(dirXZ, distXZ));
    }

    IEnumerator CoLight()
    {
        Change(State.LightSwipe);
        _lastWasLight = true;

        if (stopHardOnAttackStart) FireRunOrIdle(false);
        if (animator && !string.IsNullOrEmpty(animLightTrigger))
            animator.SetTrigger(animLightTrigger);

        yield return new WaitForSeconds(lightWindup);
        DealDamage(lightHitRadius, lightDamage);
        yield return new WaitForSeconds(lightActive);

        Change(State.Recover);
    }

    IEnumerator CoHeavy(Vector3 dirXZ, float distAtStart)
    {
        Change(State.HeavySwing);
        _lastWasLight = false;

        if (stopHardOnAttackStart) FireRunOrIdle(false);
        if (animator && !string.IsNullOrEmpty(animHeavyTrigger))
            animator.SetTrigger(animHeavyTrigger);

        yield return new WaitForSeconds(heavyWindup);

        float desiredEnd = Mathf.Max(heavyDashMinStop, standOffDistance);
        float dashDist = Mathf.Max(0f, distAtStart - desiredEnd);
        float dashTime = Mathf.Min(heavyDashTime, dashDist / Mathf.Max(0.001f, heavyDashSpeed));

        float t = 0f;
        while (t < dashTime)
        {
            t += Time.deltaTime;
            transform.position += dirXZ * heavyDashSpeed * Time.deltaTime;
            yield return null;
        }

        DealDamage(heavyHitRadius, heavyDamage);
        yield return new WaitForSeconds(heavyActive);

        Change(State.Recover);
    }

    void DealDamage(float radius, float dmg)
    {
        var hits = Physics.OverlapSphere(transform.position, radius, playerLayers, QueryTriggerInteraction.Collide);
        foreach (var h in hits)
        {
            if (!h) continue;
            string tagToCheck = string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag;
            if (!h.CompareTag(tagToCheck)) continue;

            if (_playerHP) { _playerHP.ApplyDamage(dmg); break; }
            var pc = h.GetComponentInParent<PlayerController3D>();
            if (pc) { pc.ApplyDamage(dmg); break; }
        }
    }

    void OnEnemyBaseHit(float damage)
    {
        if (_dying) return;
        if (!animator || string.IsNullOrEmpty(animHitTrigger)) return;
        if (Time.time - _lastHitAnimTime < hitAnimMinInterval) return;

        _lastHitAnimTime = Time.time;
        animator.SetTrigger(animHitTrigger);
    }

    void OnEnemyBaseDeath(EnemyBase eb)
    {
        if (_dying || eb != _base) return;
        StartCoroutine(CoDie());
    }

    IEnumerator CoDie()
    {
        _dying = true;
        _state = State.Dead;

        StopAllCoroutines();
        if (_rb)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
#else
            _rb.isKinematic = true;
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
#endif
        }

        var cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        gameObject.tag = "Untagged";

        var all = GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var b in all) if (b && b != this && b.GetComponent<EnemyBase>() == null) b.enabled = false;

        if (animator && !string.IsNullOrEmpty(animDeathTrigger))
        {
            animator.ResetTrigger(animRunTrigger);
            animator.ResetTrigger(animIdleTrigger);
            animator.SetTrigger(animDeathTrigger);
        }

        yield return new WaitForSeconds(Mathf.Max(0f, deathAnimTime));

        if (deathBurstPrefab) Instantiate(deathBurstPrefab, transform.position, Quaternion.identity);
        else
        {
            var go = new GameObject("DeathBurst_Fallback");
            go.transform.position = transform.position;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.playOnAwake = false; main.loop = false; main.startLifetime = 0.4f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
            var emission = ps.emission; emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)fallbackBurstCount) });
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.25f;
            ps.Play();
            Destroy(go, 1.0f);
        }

        yield return FadeOutRenderers(fadeOutTime);

        Destroy(gameObject);
    }

    IEnumerator FadeOutRenderers(float time)
    {
        if (time <= 0f) yield break;
        var rends = GetComponentsInChildren<Renderer>(includeInactive: false);
        if (rends == null || rends.Length == 0) { yield return null; yield break; }

        var mats = new System.Collections.Generic.List<Material[]>();
        foreach (var r in rends) mats.Add(r.materials);

        float t = 0f;
        while (t < time)
        {
            t += Time.deltaTime;
            float a = 1f - Mathf.Clamp01(t / time);
            for (int i = 0; i < mats.Count; i++)
            {
                var arr = mats[i];
                for (int j = 0; j < arr.Length; j++)
                {
                    var m = arr[j];
                    if (!m) continue;
                    if (m.HasProperty("_Color"))
                    {
                        var c = m.color; c.a = a; m.color = c;
                    }
                }
            }
            yield return null;
        }
    }

    void Change(State s)
    {
        if (_dying) return;
        _state = s;
        _stateT = 0f;
        FireIdleOrRunForState(s);
    }

    void FireIdleOrRunForState(State s)
    {
        switch (s)
        {
            case State.GuardIdle:
            case State.Recover:
            case State.ReturnToPost:
                FireRunOrIdle(s == State.ReturnToPost);
                break;
            case State.Chase:
                FireRunOrIdle(true);
                break;
        }
    }

    string _lastMovementTrigger = null;
    void FireRunOrIdle(bool running)
    {
        if (!animator) return;
        string trig = running ? animRunTrigger : animIdleTrigger;
        if (string.IsNullOrEmpty(trig)) return;
        if (_lastMovementTrigger == trig) return;

        _lastMovementTrigger = trig;
        if (!string.IsNullOrEmpty(animRunTrigger)) animator.ResetTrigger(animRunTrigger);
        if (!string.IsNullOrEmpty(animIdleTrigger)) animator.ResetTrigger(animIdleTrigger);
        animator.SetTrigger(trig);
    }

    void ComputeGuardBoundsFromGrounds()
    {
        _hasComputedBounds = false;
        GameObject[] grounds;
        try { grounds = GameObject.FindGameObjectsWithTag(groundTag); } catch { grounds = null; }
        if (grounds == null || grounds.Length == 0) { _computedCenter = _spawnPos; return; }

        bool any = false;
        Bounds b = new Bounds();
        float yRef = _spawnPos.y;

        for (int i = 0; i < grounds.Length; i++)
        {
            var go = grounds[i]; if (!go) continue;
            Collider col = go.GetComponent<Collider>();
            if (col != null && col.enabled)
            {
                float cy = col.bounds.center.y;
                if (Mathf.Abs(cy - yRef) <= sameYTolerance) { if (!any) { b = col.bounds; any = true; } else b.Encapsulate(col.bounds); }
            }
            else
            {
                float cy = go.transform.position.y;
                if (Mathf.Abs(cy - yRef) <= sameYTolerance) { if (!any) { b = new Bounds(go.transform.position, Vector3.zero); any = true; } else b.Encapsulate(go.transform.position); }
            }
        }

        if (!any) { _computedCenter = _spawnPos; return; }

        b.Expand(new Vector3(boundsPadding * 2f, 0f, boundsPadding * 2f));
        _guardBounds = b;
        _computedCenter = new Vector3(b.center.x, _spawnPos.y, b.center.z);
        _computedSphereRadius = Mathf.Max(b.extents.x, b.extents.z);
        _hasComputedBounds = true;

        leashRadius = Mathf.Max(leashRadius, Mathf.Sqrt(b.extents.x * b.extents.x + b.extents.z * b.extents.z) + 0.5f);
        guardRadius = Mathf.Max(guardRadius, _computedSphereRadius);
    }

    bool IsInsideGuardArea(Vector3 worldPos)
    {
        if (autoGuardFromGroundTag && _hasComputedBounds)
        {
            if (useBoxGuard)
            {
                return worldPos.x >= _guardBounds.min.x && worldPos.x <= _guardBounds.max.x &&
                       worldPos.z >= _guardBounds.min.z && worldPos.z <= _guardBounds.max.z;
            }
            else
            {
                Vector3 flat = worldPos; flat.y = _computedCenter.y;
                return (flat - _computedCenter).sqrMagnitude <= (_computedSphereRadius * _computedSphereRadius);
            }
        }
        else
        {
            Vector3 center = guardCenter ? guardCenter.position : _spawnPos;
            Vector3 flat = worldPos; flat.y = center.y;
            return (flat - center).sqrMagnitude <= (guardRadius * guardRadius);
        }
    }

    Vector3 ClampToGuardArea(Vector3 desired)
    {
        if (autoGuardFromGroundTag && _hasComputedBounds && useBoxGuard)
        {
            float x = Mathf.Clamp(desired.x, _guardBounds.min.x, _guardBounds.max.x);
            float z = Mathf.Clamp(desired.z, _guardBounds.min.z, _guardBounds.max.z);
            return new Vector3(x, _spawnPos.y, z);
        }
        else
        {
            Vector3 center = autoGuardFromGroundTag ? _computedCenter : (guardCenter ? guardCenter.position : _spawnPos);
            Vector3 flat = desired; flat.y = center.y;
            float r = autoGuardFromGroundTag ? Mathf.Max(_computedSphereRadius, guardRadius) : guardRadius;
            Vector3 delta = flat - center;
            float d = new Vector2(delta.x, delta.z).magnitude;
            if (d <= r || d < 1e-6f) return new Vector3(flat.x, _spawnPos.y, flat.z);
            Vector3 clamp = center + (delta / d) * r;
            return new Vector3(clamp.x, _spawnPos.y, clamp.z);
        }
    }

    void FaceTowards(Vector3 worldPoint, float speedDegPerSec)
    {
        Vector3 to = worldPoint - transform.position;
        Vector3 toXZ = new Vector3(to.x, 0f, to.z);
        if (toXZ.sqrMagnitude < 1e-6f) return;

        Quaternion targetRot = Quaternion.LookRotation(toXZ.normalized, Vector3.up);
        float yawDelta = Quaternion.Angle(transform.rotation, targetRot) * Mathf.Deg2Rad;
        if (yawDelta <= faceDeadzone) return;

        float step = speedDegPerSec * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, step);
    }

    void ReturnHomeStep(Vector3 center)
    {
        Vector3 target = ClampToGuardArea(center);
        Vector3 toPost = target - transform.position;
        Vector3 toPostXZ = new Vector3(toPost.x, 0f, toPost.z);
        float d = toPostXZ.magnitude;
        if (d <= postArriveTolerance) { FireRunOrIdle(false); return; }

        Vector3 stepDir = toPostXZ / Mathf.Max(1e-6f, d);
        transform.position += stepDir * (moveSpeed * 0.35f) * Time.deltaTime;
        FaceTowards(transform.position + stepDir, faceSpeedDegPerSec);
    }

    bool BossExists()
    {
        if (string.IsNullOrEmpty(bossTag)) return false;
        GameObject boss = GameObject.FindGameObjectWithTag(bossTag);
        return boss != null;
    }

    void OnDrawGizmosSelected()
    {
        if (drawRanges)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, lightRange);
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, heavyRange);
        }

        if (!drawGuardGizmos) return;

        Vector3 center = Application.isPlaying
            ? (autoGuardFromGroundTag ? _computedCenter : (guardCenter ? guardCenter.position : transform.position))
            : (guardCenter ? guardCenter.position : transform.position);

        if (autoGuardFromGroundTag && _hasComputedBounds)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Vector3 size = new Vector3(_guardBounds.size.x, 0.01f, _guardBounds.size.z);
            Vector3 pos = new Vector3(_guardBounds.center.x, center.y, _guardBounds.center.z);
            Gizmos.DrawWireCube(pos, size);

            if (!useBoxGuard)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
                Gizmos.DrawWireSphere(center, _computedSphereRadius);
            }
        }
        else
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(center, guardRadius);
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, 0.2f);
    }
}
