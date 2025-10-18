using UnityEngine;
using System.Collections;

/// <summary>
/// Ground walker (3D) with guard area + dual attacks.
/// NEW (patched):
/// - Uses PlayerController3D.ApplyDamage for all damage so blocking/slow/light+UI dim work.
/// - Still supports Boss override + auto guard bounds from Ground tag.
/// </summary>
[RequireComponent(typeof(EnemyBase))]
public class WalkerDualAttackEnemy3D : MonoBehaviour
{
    public enum State { GuardIdle, Chase, LightSwipe, HeavySwing, Recover, ReturnToPost }

    [Header("Chase (XZ plane)")]
    public float moveSpeed = 3.0f;
    public float stopDistance = 1.2f;
    public float reengageDistance = 1.6f;

    [Header("Targeting")]
    public string playerTag = "Player";
    [Tooltip("Radians on Yaw below which we won't twitch-rotate (~3° = 0.052 rad).")]
    public float faceDeadzone = 0.05f;
    [Tooltip("Yaw slerp speed (deg/sec) when turning to face.")]
    public float faceSpeedDegPerSec = 720f;

    [Header("Attack Decide")]
    [Tooltip("0..1 probability to pick Light when both are valid")]
    public float lightBias = 0.6f;
    public float lightRange = 1.3f;
    public float heavyRange = 2.0f;

    [Header("LIGHT Attack (fast)")]
    public float lightWindup = 0.18f;
    public float lightActive = 0.06f;
    public float lightRecover = 0.25f;
    public float lightDamage = 10f;
    public float lightHitRadius = 0.9f;

    [Header("HEAVY Attack (slow)")]
    public float heavyWindup = 0.45f;
    public float heavyActive = 0.10f;
    public float heavyRecover = 0.45f;
    public float heavyDamage = 18f;
    public float heavyHitRadius = 1.2f;
    public float heavyDashTime = 0.15f;
    public float heavyDashSpeed = 6.5f;

    [Header("Animator (optional)")]
    public Animator animator;
    public string animLightAttack = "Light";
    public string animHeavyAttack = "Heavy";
    public float lightAnimSpeed = 1.25f;
    public float heavyAnimSpeed = 0.8f;

    [Header("No-Animator Placeholders")]
    public Transform visual;
    public float lightScalePunch = 1.07f;
    public float heavyScalePunch = 1.18f;

    [Header("Damage Query")]
    public LayerMask playerLayers = ~0;

    [Header("Stop & Cooldown")]
    public bool stopHardOnAttackStart = true;
    public float attackCooldown = 0.3f;

    // ---------------- GUARD AREA ----------------
    [Header("Guard Area (Manual fallback)")]
    public Transform guardCenter;
    public float guardRadius = 6f;
    public float leashRadius = 12f;
    public float forgetPlayerAfter = 2.0f;
    public float returnSpeedMult = 1.1f;
    public float postArriveTolerance = 0.15f;

    [Header("Auto-Guard From Ground Tag")]
    public bool autoGuardFromGroundTag = true;
    public string groundTag = "Ground";
    public float sameYTolerance = 0.25f;
    public float boundsPadding = 0.0f;
    public bool useBoxGuard = true;

    [Header("Boss Override")]
    public string bossTag = "Boss";
    public float bossPresenceRecheckInterval = 1.0f;

    [Header("Debug")]
    public bool drawRanges = true;
    public bool drawGuardGizmos = true;

    // --- runtime ---
    EnemyBase _base;
    Transform _player;
    PlayerController3D _playerHP; // <-- NEW
    State _state;
    float _stateT;
    float _defaultAnimSpeed = 1f;
    bool _lastWasLight = true;
    float _lastAttackStart = -999f;

    Vector3 _spawnPos;
    Vector3 _computedCenter;
    Bounds _guardBounds;           // computed from ground tag
    float _computedSphereRadius;   // derived from bounds extents
    bool _hasComputedBounds;

    float _lastInsideGuardTime = -999f;

    bool _bossPresent;
    float _nextBossCheck;

    void Awake()
    {
        _base = GetComponent<EnemyBase>();
        _base.contactDamage = 0f;
        if (!visual) visual = _base.animTarget != null ? _base.animTarget : transform;

        var pgo = GameObject.FindGameObjectWithTag(string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag);
        if (pgo)
        {
            _player = pgo.transform;
            _playerHP = pgo.GetComponentInChildren<PlayerController3D>(); // <-- cache health system
        }

        if (animator) _defaultAnimSpeed = animator.speed;

        _spawnPos = transform.position;

        // Boss presence at start
        _bossPresent = BossExists();
        _nextBossCheck = Time.time + Mathf.Max(0f, bossPresenceRecheckInterval);

        // Compute guard area if needed
        if (!_bossPresent)
        {
            if (autoGuardFromGroundTag)
                ComputeGuardBoundsFromGrounds();
            else
                _computedCenter = guardCenter ? guardCenter.position : _spawnPos;
        }

        _state = _bossPresent ? State.Chase : State.GuardIdle;
    }

    void OnDisable()
    {
        if (animator) animator.speed = _defaultAnimSpeed;
    }

    void Update()
    {
        if (!_player) return;

        _stateT += Time.deltaTime;

        // Refresh boss presence (optional cadence)
        if (bossPresenceRecheckInterval > 0f && Time.time >= _nextBossCheck)
        {
            _nextBossCheck = Time.time + bossPresenceRecheckInterval;
            _bossPresent = BossExists();
            if (_bossPresent && _state == State.ReturnToPost) _state = State.Chase;
        }

        // Basic XZ info
        Vector3 toPlayer = _player.position - transform.position;
        Vector3 dirToPlayerXZ = new Vector3(toPlayer.x, 0f, toPlayer.z);
        float distToPlayerXZ = dirToPlayerXZ.magnitude;
        Vector3 dirXZ = distToPlayerXZ > 1e-6f ? dirToPlayerXZ / distToPlayerXZ : Vector3.forward;

        if (_bossPresent)
        {
            PureRushTick(dirXZ, distToPlayerXZ);
            return;
        }

        // ---------- GUARD LOGIC PATH ----------
        Vector3 center = autoGuardFromGroundTag ? _computedCenter : (guardCenter ? guardCenter.position : _spawnPos);
        bool playerInsideGuard = IsInsideGuardArea(_player.position);

        float distFromCenterXZ = new Vector2((transform.position - center).x, (transform.position - center).z).magnitude;
        bool leashExceeded = useBoxGuard
            ? !IsInsideGuardArea(transform.position)
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

                if (distToPlayerXZ > stopDistance)
                    transform.position += dirXZ * moveSpeed * Time.deltaTime;

                if (distToPlayerXZ <= stopDistance)
                {
                    TryStartAttack(dirXZ, distToPlayerXZ);
                }
                else
                {
                    bool canLight = distToPlayerXZ <= lightRange;
                    bool canHeavy = distToPlayerXZ <= heavyRange && distToPlayerXZ > stopDistance * 0.75f;

                    if (canLight && canHeavy) TryStartAttack(dirXZ, distToPlayerXZ);
                    else if (canLight) TryStartAttack(dirXZ, distToPlayerXZ, forceLight: true);
                    else if (canHeavy) TryStartAttack(dirXZ, distToPlayerXZ, forceHeavy: true);
                }
                break;

            case State.Recover:
                if (leashExceeded || playerForgotten) { Change(State.ReturnToPost); break; }

                float need = (_lastWasLight ? lightRecover : heavyRecover);
                if (distToPlayerXZ >= reengageDistance || _stateT >= need)
                    Change(State.Chase);
                break;

            case State.ReturnToPost:
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

    // --------- PURE RUSH (Boss present) ----------
    void PureRushTick(Vector3 dirXZ, float distToPlayerXZ)
    {
        FaceTowards(_player.position, faceSpeedDegPerSec);

        if (distToPlayerXZ > stopDistance)
            transform.position += dirXZ * moveSpeed * Time.deltaTime;

        if (distToPlayerXZ <= stopDistance)
        {
            TryStartAttack(dirXZ, distToPlayerXZ);
        }
        else
        {
            bool canLight = distToPlayerXZ <= lightRange;
            bool canHeavy = distToPlayerXZ <= heavyRange && distToPlayerXZ > stopDistance * 0.75f;

            if (canLight && canHeavy) TryStartAttack(dirXZ, distToPlayerXZ);
            else if (canLight) TryStartAttack(dirXZ, distToPlayerXZ, forceLight: true);
            else if (canHeavy) TryStartAttack(dirXZ, distToPlayerXZ, forceHeavy: true);
        }
    }

    // ---- ATTACKS ----
    void TryStartAttack(Vector3 dirXZ, float distXZ, bool forceLight = false, bool forceHeavy = false)
    {
        if (Time.time - _lastAttackStart < attackCooldown) return;

        bool canLight = distXZ <= lightRange;
        bool canHeavy = (distXZ <= heavyRange && distXZ > stopDistance * 0.75f) || distXZ <= stopDistance;

        int pick = 0; // 1=light, 2=heavy
        if (forceLight && canLight) pick = 1;
        else if (forceHeavy && canHeavy) pick = 2;
        else if (canLight && canHeavy) pick = (Random.value < lightBias ? 1 : 2);
        else if (canLight) pick = 1;
        else if (canHeavy) pick = 2;

        if (pick == 0) return;

        _lastAttackStart = Time.time;

        if (pick == 1) StartCoroutine(CoLight());
        else StartCoroutine(CoHeavy(dirXZ));
    }

    IEnumerator CoLight()
    {
        Change(State.LightSwipe);
        _lastWasLight = true;

        if (animator)
        {
            float prev = animator.speed;
            animator.speed = lightAnimSpeed;
            if (!string.IsNullOrEmpty(animLightAttack)) animator.SetTrigger(animLightAttack);
            yield return new WaitForSeconds(lightWindup);
            animator.speed = prev;
        }
        else
        {
            yield return ScalePunch(visual, lightWindup, lightScalePunch);
        }

        DealDamage(lightHitRadius, lightDamage);
        yield return new WaitForSeconds(lightActive);

        Change(State.Recover);
    }

    IEnumerator CoHeavy(Vector3 dirXZ)
    {
        Change(State.HeavySwing);
        _lastWasLight = false;

        if (animator)
        {
            float prev = animator.speed;
            animator.speed = heavyAnimSpeed;
            if (!string.IsNullOrEmpty(animHeavyAttack)) animator.SetTrigger(animHeavyAttack);
            yield return new WaitForSeconds(heavyWindup);
            animator.speed = prev;
        }
        else
        {
            yield return ScalePunch(visual, heavyWindup, heavyScalePunch);
        }

        float t = 0f;
        while (t < heavyDashTime)
        {
            t += Time.deltaTime;
            transform.position += dirXZ * heavyDashSpeed * Time.deltaTime;
            yield return null;
        }

        DealDamage(heavyHitRadius, heavyDamage);
        yield return new WaitForSeconds(heavyActive);

        Change(State.Recover);
    }

    // === Patched: deals damage via PlayerController3D ===
    void DealDamage(float radius, float dmg)
    {
        var hits = Physics.OverlapSphere(transform.position, radius, playerLayers, QueryTriggerInteraction.Collide);
        foreach (var h in hits)
        {
            if (!h) continue;

            string tagToCheck = string.IsNullOrEmpty(_base.playerTag) ? playerTag : _base.playerTag;
            if (!h.CompareTag(tagToCheck)) continue;

            // Prefer cached controller (fast path)
            if (_playerHP)
            {
                _playerHP.ApplyDamage(dmg);
                break;
            }

            // Fallback: fetch from the hit collider hierarchy
            var pc = h.GetComponentInParent<PlayerController3D>();
            if (pc)
            {
                pc.ApplyDamage(dmg);
                break;
            }
        }
    }

    IEnumerator ScalePunch(Transform t, float windup, float peakScale)
    {
        if (!t) yield break;
        Vector3 baseS = t.localScale;
        float half = Mathf.Max(0.01f, windup * 0.6f);

        float t1 = 0f;
        while (t1 < half)
        {
            t1 += Time.deltaTime;
            float u = Mathf.Clamp01(t1 / half);
            float s = Mathf.Lerp(1f, peakScale, u);
            t.localScale = baseS * s;
            yield return null;
        }

        float t2 = 0f;
        while (t2 < windup - half)
        {
            t2 += Time.deltaTime;
            float u = Mathf.Clamp01(t2 / (windup - half));
            float s = Mathf.Lerp(peakScale, 1f, u);
            t.localScale = baseS * s;
            yield return null;
        }
        t.localScale = baseS;
    }

    void Change(State s) { _state = s; _stateT = 0f; }

    // --------- Auto-guard computation ----------
    void ComputeGuardBoundsFromGrounds()
    {
        _hasComputedBounds = false;

        GameObject[] grounds;
        try { grounds = GameObject.FindGameObjectsWithTag(groundTag); }
        catch { grounds = null; }

        if (grounds == null || grounds.Length == 0)
        {
            _computedCenter = _spawnPos;
            return;
        }

        bool any = false;
        Bounds b = new Bounds();
        float yRef = _spawnPos.y;

        for (int i = 0; i < grounds.Length; i++)
        {
            var go = grounds[i];
            if (!go) continue;

            Collider col = go.GetComponent<Collider>();
            if (col != null && col.enabled)
            {
                float cy = col.bounds.center.y;
                if (Mathf.Abs(cy - yRef) <= sameYTolerance)
                {
                    if (!any) { b = col.bounds; any = true; }
                    else b.Encapsulate(col.bounds);
                }
            }
            else
            {
                float cy = go.transform.position.y;
                if (Mathf.Abs(cy - yRef) <= sameYTolerance)
                {
                    if (!any) { b = new Bounds(go.transform.position, Vector3.zero); any = true; }
                    else b.Encapsulate(go.transform.position);
                }
            }
        }

        if (!any)
        {
            _computedCenter = _spawnPos;
            return;
        }

        // Pad in X/Z
        b.Expand(new Vector3(boundsPadding * 2f, 0f, boundsPadding * 2f));

        _guardBounds = b;
        _computedCenter = new Vector3(b.center.x, _spawnPos.y, b.center.z);
        _computedSphereRadius = Mathf.Max(b.extents.x, b.extents.z);
        _hasComputedBounds = true;

        leashRadius = Mathf.Max(leashRadius, Mathf.Sqrt(b.extents.x * b.extents.x + b.extents.z * b.extents.z) + 0.5f);
        guardRadius = Mathf.Max(guardRadius, _computedSphereRadius);
    }

    // --------- Guard area queries ----------
    bool IsInsideGuardArea(Vector3 worldPos)
    {
        if (autoGuardFromGroundTag && _hasComputedBounds)
        {
            if (useBoxGuard)
            {
                return worldPos.x >= _guardBounds.min.x &&
                       worldPos.x <= _guardBounds.max.x &&
                       worldPos.z >= _guardBounds.min.z &&
                       worldPos.z <= _guardBounds.max.z;
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
            Vector3 center = autoGuardFromGroundTag ? _computedCenter :
                             (guardCenter ? guardCenter.position : _spawnPos);
            Vector3 flat = desired; flat.y = center.y;
            float r = autoGuardFromGroundTag ? Mathf.Max(_computedSphereRadius, guardRadius) : guardRadius;
            Vector3 delta = flat - center;
            float d = new Vector2(delta.x, delta.z).magnitude;
            if (d <= r || d < 1e-6f) return new Vector3(flat.x, _spawnPos.y, flat.z);
            Vector3 clamp = center + (delta / d) * r;
            return new Vector3(clamp.x, _spawnPos.y, clamp.z);
        }
    }

    // --------- Helpers ----------
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
        if (d <= postArriveTolerance) return;

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

    // --------- Gizmos ----------
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
