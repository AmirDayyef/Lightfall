using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(EnemyBase))]
public class BossFivePhaseController3D : MonoBehaviour
{

    public bool debugLog = true;
    public float debugHPDeltaStep = 1f;
    public float debugMinInterval = 0.25f;
    Phase _dbgLastPhase;
    float _dbgLastHP = -1f;
    float _dbgLastLogTime = -1f;
    public Transform poseReturnPoint;
    public Vector3 poseReturnOffset = new Vector3(6f, 0f, 0f);
    public float runOffSpeed = 10f;
    public float runOffAccel = 20f;
    public float runOffRightMargin = 0.2f;
    public enum Phase { P1, P2, P3, P4, P5, Dead }

    public Transform visualRoot;
    public string playerTag = "Player";

    public Animator animator;
    public string animLightTrigger = "Light";
    public string animHeavyTrigger = "Heavy";
    public string animMoveBool = "Moving";
    public string animTauntTrigger = "Taunt";
    public string animHitTrigger = "Hit";
    bool _p4ActiveLock;
    public float lightAnimSpeed = 1.0f;
    public float heavyAnimSpeed = 1.0f;
    public float hitAnimSpeed = 1.0f;
    public float hitMinInterval = 0.08f;
    public bool tauntOnPose = true;

    float _defaultAnimSpeed = 1.0f;

    public bool lockY = true;
    public bool lockYUseStart = true;
    public float lockYValue = 0f;
    public float lockYOffset = 0f;

    [Range(0, 100)] public float phase2HP = 80f;
    [Range(0, 100)] public float phase3HP = 60f;
    [Range(0, 100)] public float phase4HP = 35f;
    [Range(0, 100)] public float phase5HP = 15f;

    public Transform posePoint;
    public Vector3 poseOffset = new Vector3(-6f, 0f, 0f);
    public float poseTravelSpeed = 12f;
    public Vector3 poseEulerRotation = new Vector3(0f, 180f, 0f);
    public float poseRotateSpeedDeg = 240f;
    public float poseHoldSeconds = 0f;

    public bool returnOnPhaseStart = true;
    public float returnSpeed = 12f;
    public float returnXOffset = -2f;
    public float returnArriveDistance = 0.3f;
    public float returnMaxTime = 5f;
    public bool snapOnReturnTimeout = true;

    public float p1_moveSpeed = 2.5f;
    public float p1_attackInterval = 1.2f;

    public float lightWindup = 0.18f;
    public float lightActive = 0.06f;
    public float lightRecover = 0.25f;
    public float lightDamage = 10f;
    public float lightRange = 1.2f;

    public float heavyWindup = 0.4f;
    public float heavyDashTime = 0.18f;
    public float heavyDashSpeed = 7.5f;
    public float heavyActive = 0.08f;
    public float heavyDamage = 18f;
    public float heavyHitRadius = 1.3f;

    public float attackCooldown = 0.6f;
    public float lightCooldown = 0f;
    public float heavyCooldown = 0f;
    float _attackCDUntil = 0f;

    public List<GameObject> groundDualAttackPrefabs;
    public Vector2 groundAreaA_Min = new Vector2(-14f, -3f);
    public Vector2 groundAreaA_Max = new Vector2(-6f, 3f);
    public Vector2 groundAreaB_Min = new Vector2(6f, -3f);
    public Vector2 groundAreaB_Max = new Vector2(14f, 3f);

    public float groundSpawnY = 0f;
    public bool useBossGroundYForSpawns = true;

    public int groundPerWave_P2 = 4;
    public int groundWaves_P2 = 3;
    public float timeBetweenGroundWaves_P2 = 2.0f;
    public float p2TimeGateSeconds = 10f;

    public float p3_moveSpeed = 2.3f;
    public float p3_attackInterval = 1.0f;

    public List<GameObject> flyingPrefabs;
    public int flyingPerWave_P4 = 3;
    public int flyingWaves_P4 = 3;
    public float timeBetweenFlyingWaves_P4 = 2.2f;
    public float p4TimeGateSeconds = 10f;

    public float flyingSpawnY = 6.0f;
    public float flyingSpawnXSpan = 16f;
    public float flyingSpawnZSpan = 0f;
    public float flyingSpawnZBase = 0f;

    public float p5_moveSpeed = 2.6f;
    public float p5_attackInterval = 0.9f;
    public int groundPerPulse_P5 = 2;
    public float groundPulseInterval_P5 = 4.0f;
    public int flyingPerPulse_P5 = 2;
    public float flyingPulseInterval_P5 = 4.5f;

    public bool finaleEnable = true;
    public float finaleDuration = 2.0f;
    public string finaleSafePlayerTag = "Untagged";
    public float finalePlayerExtraLightIntensity = 4.0f;
    public Color finaleRendererTint = new Color(1f, 1f, 1f, 1f);

    public List<GameObject> finaleGroundPrefabs;
    public int finaleGroundCountPerSide = 6;
    public List<GameObject> finaleFlyingPrefabs;
    public int finaleFlyingCount = 10;

    public int explosionBurstCount = 60;
    public float explosionDuration = 0.8f;
    public float explosionStartLifetime = 0.5f;
    public Vector2 explosionStartSpeed = new Vector2(2.5f, 5.0f);
    public Vector2 explosionStartSize = new Vector2(0.18f, 0.4f);
    public float fadeToWhiteTime = 1.0f;
    public string nextSceneName = "NextScene";
    public string effectsSortingLayer = "Effects";

    public string cameraScriptTypeName = "SideScrollCamera";
    public Behaviour[] extraCameraBehavioursToDisable;
    public float finaleZoomFOV = 35f;
    public float finaleZoomSeconds = 1.2f;
    public float finaleSlowmoScale = 0.15f;
    public float finaleSlowmoSeconds = 1.8f;
    public string finalePlayerNewName = "The Light";
    public float finaleFlashWhiteSeconds = 0.35f;
    public float finaleSpawnPulseSeconds = 0.8f;
    public int finaleExtraGroundPerSide = 2;
    public int finaleExtraFlyingPerPulse = 2;
    public Color bloodColor = new Color(0.5f, 0f, 0f, 0f);
    public float bloodAlphaPerKill = 0.06f;
    public float bloodMaxAlpha = 0.9f;
    public float bloodEaseSpeed = 1.35f;

    public float bloodKillGraceSeconds = 0.5f;

    public bool useCustomDeathFromEnemyBase = true;

    public bool drawAreas = true;

    EnemyBase _base;
    Transform _player;
    Phase _phase = Phase.P1;
    float _nextAttackTime;
    bool _lastWasLight;

    float _groundY;
    bool _returning;
    Coroutine _phase2Co, _phase4Co, _returnCo;
    bool _p2Done, _p4Done;

    Vector3 _startPos;
    Quaternion _startRot;

    float _lastHP = -1f;

    static bool s_OutroStarted;
    bool _finaleRunning;

    void Awake()
    {

        _dbgLastPhase = _phase;
        _dbgLastHP = GetCurrentHP();
        _dbgLastLogTime = Time.time;


        _base = GetComponent<EnemyBase>();
        if (!string.IsNullOrEmpty(_base.playerTag)) playerTag = _base.playerTag;

        if (useCustomDeathFromEnemyBase && _base) _base.customDeathHandled = true;

        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo) _player = pgo.transform;

        _groundY = (lockY ? (lockYUseStart ? transform.position.y : lockYValue) + lockYOffset : transform.position.y);
        transform.position = new Vector3(transform.position.x, _groundY, transform.position.z);

        _startPos = transform.position;
        _startRot = transform.rotation;

        if (useBossGroundYForSpawns) groundSpawnY = _groundY;

        _phase = Phase.P1;
        _nextAttackTime = Time.time + 0.5f;

        if (animator) _defaultAnimSpeed = animator.speed;

        _lastHP = (_base && _base.CurrentHP >= 0f) ? _base.CurrentHP : _base.maxHP;
    }

    void Update()
    {

        if (debugLog)
        {
            float hpNow = GetCurrentHP();
            if (_phase != _dbgLastPhase)
            {
                Debug.Log($"[BOSS] Phase={_phase} HP={hpNow:0.##}/{(_base ? _base.maxHP : 0f):0.##} ({GetHPPercent():0.##}%)");
                _dbgLastPhase = _phase;
                _dbgLastLogTime = Time.time;
                _dbgLastHP = hpNow;
            }
            else if (hpNow >= 0f)
            {
                bool bigChange = Mathf.Abs(hpNow - _dbgLastHP) >= Mathf.Max(0.01f, debugHPDeltaStep);
                bool interval = (Time.time - _dbgLastLogTime) >= Mathf.Max(0.01f, debugMinInterval);
                if (bigChange && interval)
                {
                    Debug.Log($"[BOSS] HP={hpNow:0.##}/{_base.maxHP:0.##} ({GetHPPercent():0.##}%) Phase={_phase}");
                    _dbgLastHP = hpNow;
                    _dbgLastLogTime = Time.time;
                }
            }
        }


        if (_finaleRunning) return;
        if (!_player || _phase == Phase.Dead) return;

        if (lockY && !Mathf.Approximately(transform.position.y, _groundY))
            transform.position = new Vector3(transform.position.x, _groundY, transform.position.z);

        HitAnimTick();

        float hpPct = GetHPPercent();

        switch (_phase)
        {
            case Phase.P1:
                if (!_p2Done && hpPct <= phase2HP && _phase2Co == null)
                    _phase2Co = StartCoroutine(CoPhase2());
                TickP1();
                break;

            case Phase.P2:
                if (_phase2Co == null) _phase2Co = StartCoroutine(CoPhase2());
                break;

            case Phase.P3:
                if (!_p4Done && hpPct <= phase4HP && _phase4Co == null)
                    _phase4Co = StartCoroutine(CoPhase4());
                TickBossCombat(p3_moveSpeed, p3_attackInterval);
                break;

            case Phase.P4:
                if (_phase4Co == null) _phase4Co = StartCoroutine(CoPhase4());
                break;

            case Phase.P5:
                TickBossCombat(p5_moveSpeed, p5_attackInterval);
                break;
        }

        if ((_phase == Phase.P3 || _phase == Phase.P4) && hpPct <= phase5HP && _phase != Phase.P5)
            StartPhase5();

    
    }

    void TickP1()
    {
        bool moved = MoveTowardPlayerXOnly(p1_moveSpeed);
        SetAnimMoving(moved);

        if (Time.time >= Mathf.Max(_nextAttackTime, _attackCDUntil))
        {
            StartCoroutine(CoLightAttack());
            _nextAttackTime = Time.time + p1_attackInterval;
        }
    }

    IEnumerator CoPhase2()
    {
        if (_p2Done) yield break;
        _phase = Phase.P2;
        if (debugLog) Debug.Log("[BOSS] P2 start");

        // --- Run TO main pose ---
        yield return SprintToPosePointBlocking();
        transform.rotation = Quaternion.Euler(poseEulerRotation);
        yield return null;

        if (tauntOnPose) DoTaunt();
        if (poseHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(poseHoldSeconds);

        // --- Spawn ground waves ---
        for (int w = 0; w < groundWaves_P2; w++)
        {
            SpawnGroundWaveDual(groundPerWave_P2, false);
            if (w < groundWaves_P2 - 1)
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, timeBetweenGroundWaves_P2));
        }

        // --- Optional delay ---
        if (p2TimeGateSeconds > 0f)
            yield return new WaitForSecondsRealtime(p2TimeGateSeconds);

        // --- Run TO return pose ---
        if (debugLog) Debug.Log("[BOSS] P2 -> returning pose for P3");
        yield return SprintToReturnPoseBlocking();

        // --- Phase transition ---
        _phase2Co = null;
        _p2Done = true;
        StartPhase3_WithReturn();
    }






    void ForceExitPhase2ToP3()
    {
        _phase2Co = null;
        _p2Done = true;

        transform.rotation = _startRot;
        transform.position = new Vector3(_startPos.x, _groundY, _startPos.z);

        StartPhase3_WithReturn();
    }
    void StartPhase3()
    {
        if (_p4ActiveLock) return;
        _phase = Phase.P3;
        _nextAttackTime = Time.time + 0.5f;
        _lastWasLight = false;
    }
    void StartPhase3_WithReturn()
    {
        if (_p4ActiveLock) return;
        StartPhase3();
        StartReturnToPlayerBlocking();
    }
    IEnumerator CoPhase4()
    {
        if (_phase != Phase.P3 || _p4Done) yield break;
        _phase4Co = this.StartCoroutine(Dummy());
        _p4ActiveLock = true;
        _phase = Phase.P4;
        if (debugLog) Debug.Log("[BOSS] P4 start");

        // --- Run TO main pose ---
        yield return SprintToPosePointBlocking();
        transform.rotation = Quaternion.Euler(poseEulerRotation);
        yield return null;

        if (tauntOnPose) DoTaunt();
        if (poseHoldSeconds > 0f)
            yield return new WaitForSecondsRealtime(poseHoldSeconds);

        // --- Spawn flying waves ---
        for (int w = 0; w < flyingWaves_P4; w++)
        {
            SpawnFlyingWave(flyingPerWave_P4, false);
            if (w < flyingWaves_P4 - 1)
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, timeBetweenFlyingWaves_P4));
        }

        // --- Optional delay ---
        float stay = Mathf.Max(0.25f, p4TimeGateSeconds);
        if (stay > 0f)
            yield return new WaitForSecondsRealtime(stay);

        // --- Run TO return pose ---
        if (debugLog) Debug.Log("[BOSS] P4 -> returning pose for P5");
        yield return SprintToReturnPoseBlocking();

        // --- Phase transition ---
        _p4Done = true;
        _p4ActiveLock = false;
        _phase4Co = null;

        if (GetHPPercent() <= phase5HP)
            StartPhase5_WithReturn();
        else
            StartPhase3_WithReturn();

        IEnumerator Dummy() { yield break; }
    }


    void StartPhase5_WithReturn()
    {
        _phase = Phase.P5;
        _nextAttackTime = Time.time + 0.5f;
        _lastWasLight = false;
        StartReturnToPlayerBlocking();
        StartCoroutine(CoGroundPulses_P5());
        StartCoroutine(CoFlyingPulses_P5());
    }
    void StartReturnToPlayerBlocking()
    {
        if (_returnCo != null) StopCoroutine(_returnCo);
        _returnCo = StartCoroutine(CoReturnToPlayerBlocking());
    }
    IEnumerator CoReturnToPlayerBlocking()
    {
        _returning = true;
        float t0 = Time.time;
        float lastX = transform.position.x;
        float stuckTimer = 0f;

        while (true)
        {
            if (!_player) break;

            float targetX = _player.position.x + returnXOffset;
            float nx = Mathf.MoveTowards(transform.position.x, targetX, returnSpeed * Time.deltaTime);
            transform.position = new Vector3(nx, _groundY, transform.position.z);

            SetAnimMoving(true);

            if (Mathf.Abs(transform.position.x - targetX) <= returnArriveDistance)
                break;

            if (Mathf.Abs(transform.position.x - lastX) <= 0.0005f)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer >= 0.75f)
                {
                    if (snapOnReturnTimeout)
                        transform.position = new Vector3(targetX, _groundY, transform.position.z);
                    break;
                }
            }
            else
            {
                stuckTimer = 0f;
                lastX = transform.position.x;
            }

            if (Time.time - t0 >= returnMaxTime)
            {
                if (snapOnReturnTimeout)
                    transform.position = new Vector3(targetX, _groundY, transform.position.z);
                break;
            }

            yield return null;
        }

        SetAnimMoving(false);
        _returning = false;
        _returnCo = null;
        _nextAttackTime = Time.time + 0.35f;
    }



    void StartPhase5()
    {
        _phase = Phase.P5;
        _nextAttackTime = Time.time + 0.5f;
        _lastWasLight = false;
        if (returnOnPhaseStart) StartReturnToPlayerBlocking();
        StartCoroutine(CoGroundPulses_P5());
        StartCoroutine(CoFlyingPulses_P5());
    }
    IEnumerator CoGroundPulses_P5()
    {
        while (_phase == Phase.P5)
        {
            SpawnGroundWaveDual(groundPerPulse_P5);
            yield return new WaitForSeconds(groundPulseInterval_P5);
        }
    }
    IEnumerator CoFlyingPulses_P5()
    {
        while (_phase == Phase.P5)
        {
            SpawnFlyingWave(flyingPerPulse_P5);
            yield return new WaitForSeconds(flyingPulseInterval_P5);
        }
    }
    void TickBossCombat(float moveSpeed, float attackInterval)
    {
        bool moved = MoveTowardPlayerXOnly(moveSpeed);
        SetAnimMoving(moved);

        if (Time.time >= Mathf.Max(_nextAttackTime, _attackCDUntil))
        {
            if (_lastWasLight) StartCoroutine(CoHeavyAttack());
            else StartCoroutine(CoLightAttack());

            _lastWasLight = !_lastWasLight;
            _nextAttackTime = Time.time + attackInterval;
        }
    }

    IEnumerator CoLightAttack()
    {
        if (animator)
        {
            if (lightAnimSpeed > 0f) animator.speed = _defaultAnimSpeed * lightAnimSpeed;
            if (!string.IsNullOrEmpty(animLightTrigger))
            {
                animator.ResetTrigger(animLightTrigger);
                animator.SetTrigger(animLightTrigger);
            }
        }
        else
        {
            yield return ScalePunch(visualRoot, lightWindup, 1.08f);
        }

        yield return new WaitForSeconds(lightWindup);
        DealDamageAround3D(lightRange, lightDamage);
        yield return new WaitForSeconds(lightActive);
        yield return new WaitForSeconds(lightRecover);
        if (animator) animator.speed = _defaultAnimSpeed;

        float cd = (lightCooldown > 0f) ? lightCooldown : attackCooldown;
        _attackCDUntil = Time.time + Mathf.Max(0f, cd);
    }

    IEnumerator CoHeavyAttack()
    {
        float dirX = Mathf.Sign((_player.position.x - transform.position.x));

        if (animator)
        {
            if (heavyAnimSpeed > 0f) animator.speed = _defaultAnimSpeed * heavyAnimSpeed;
            if (!string.IsNullOrEmpty(animHeavyTrigger))
            {
                animator.ResetTrigger(animHeavyTrigger);
                animator.SetTrigger(animHeavyTrigger);
            }
        }
        else
        {
            yield return ScalePunch(visualRoot, heavyWindup, 1.16f);
        }

        yield return new WaitForSeconds(heavyWindup);

        float t = 0f;
        while (t < heavyDashTime)
        {
            t += Time.deltaTime;
            float nx = transform.position.x + dirX * heavyDashSpeed * Time.deltaTime;
            transform.position = new Vector3(nx, _groundY, transform.position.z);
            yield return null;
        }

        DealDamageAround3D(heavyHitRadius, heavyDamage);
        yield return new WaitForSeconds(heavyActive);
        if (animator) animator.speed = _defaultAnimSpeed;

        float cd = (heavyCooldown > 0f) ? heavyCooldown : attackCooldown;
        _attackCDUntil = Time.time + Mathf.Max(0f, cd);
    }
    void SpawnGroundWaveDual(int totalCount, bool finaleMode = false)
    {
        if ((groundDualAttackPrefabs == null || groundDualAttackPrefabs.Count == 0) &&
            (finaleGroundPrefabs == null || finaleGroundPrefabs.Count == 0)) return;

        var source = (finaleMode && finaleGroundPrefabs != null && finaleGroundPrefabs.Count > 0)
            ? finaleGroundPrefabs : groundDualAttackPrefabs;

        int halfA = totalCount / 2;
        int halfB = totalCount - halfA;

        float y = useBossGroundYForSpawns ? _groundY : groundSpawnY;

        for (int i = 0; i < halfA; i++)
        {
            var prefab = source[Random.Range(0, source.Count)];
            float x = Random.Range(groundAreaA_Min.x, groundAreaA_Max.x);
            float z = Random.Range(groundAreaA_Min.y, groundAreaA_Max.y);
            var g = Instantiate(prefab, new Vector3(x, y, z), Quaternion.identity);
            if (finaleMode) ForceFinaleOneHPAndRush(g);
        }
        for (int i = 0; i < halfB; i++)
        {
            var prefab = source[Random.Range(0, source.Count)];
            float x = Random.Range(groundAreaB_Min.x, groundAreaB_Max.x);
            float z = Random.Range(groundAreaB_Min.y, groundAreaB_Max.y);
            var g = Instantiate(prefab, new Vector3(x, y, z), Quaternion.identity);
            if (finaleMode) ForceFinaleOneHPAndRush(g);
        }
    }

    void SpawnFlyingWave(int count, bool finaleMode = false)
    {
        if (((flyingPrefabs == null || flyingPrefabs.Count == 0) &&
            (finaleFlyingPrefabs == null || finaleFlyingPrefabs.Count == 0)) || !_player) return;

        var source = (finaleMode && finaleFlyingPrefabs != null && finaleFlyingPrefabs.Count > 0)
            ? finaleFlyingPrefabs : flyingPrefabs;

        float halfX = flyingSpawnXSpan * 0.5f;
        float halfZ = flyingSpawnZSpan * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var prefab = source[Random.Range(0, source.Count)];
            float x = _player.position.x + Random.Range(-halfX, halfX);
            float z = (flyingSpawnZSpan <= 0f) ? flyingSpawnZBase : Random.Range(-halfZ, halfZ);
            var g = Instantiate(prefab, new Vector3(x, flyingSpawnY, z), Quaternion.identity);
            if (finaleMode) ForceFinaleOneHPAndRush(g);
        }
    }

    bool MoveTowardPlayerXOnly(float speed)
    {
        float before = transform.position.x;
        float targetX = _player.position.x;
        float nx = Mathf.MoveTowards(transform.position.x, targetX, speed * Time.deltaTime);
        transform.position = new Vector3(nx, _groundY, transform.position.z);
        return !Mathf.Approximately(before, nx);
    }

    void SetAnimMoving(bool moving)
    {
        if (animator && !string.IsNullOrEmpty(animMoveBool))
            animator.SetBool(animMoveBool, moving);
    }

    public void DoTaunt()
    {
        if (!animator || string.IsNullOrEmpty(animTauntTrigger)) return;
        animator.ResetTrigger(animTauntTrigger);
        animator.SetTrigger(animTauntTrigger);
    }

    void DoHit()
    {
        if (animator && !string.IsNullOrEmpty(animHitTrigger))
        {
            float now = Time.time;
            if (now - _lastHitAnimTime >= hitMinInterval)
            {
                _lastHitAnimTime = now;
                float prev = animator.speed;
                if (hitAnimSpeed > 0f) animator.speed = _defaultAnimSpeed * hitAnimSpeed;

                animator.ResetTrigger(animHitTrigger);
                animator.SetTrigger(animHitTrigger);

                StartCoroutine(CoRestoreAnimSpeed(prev));
            }
        }
        else
        {
            StartCoroutine(ScalePunch(visualRoot ? visualRoot : transform, 0.06f, 1.06f));
        }
    }

    float _lastHitAnimTime = -999f;
    IEnumerator CoRestoreAnimSpeed(float toSpeed)
    {
        yield return null;
        if (animator) animator.speed = toSpeed;
    }

    void HitAnimTick()
    {
        float curHP = GetCurrentHP();
        if (curHP < 0f) return;
        if (_lastHP < 0f) { _lastHP = curHP; return; }

        if (curHP < _lastHP) DoHit();
        _lastHP = curHP;
    }

    float GetCurrentHP()
    {
        return _base ? _base.CurrentHP : -1f;
    }

    float GetHPPercent()
    {
        if (!_base) return 100f;
        float curHP = _base.CurrentHP;
        float max = Mathf.Max(0.0001f, _base.maxHP);
        if (curHP < 0f) return 100f;
        return Mathf.Clamp01(curHP / max) * 100f;
    }
    void DealDamageAround3D(float radius, float dmg)
    {
        var hits = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i];
            if (!h || !h.CompareTag(playerTag)) continue;

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
        float up = Mathf.Max(0.01f, windup * 0.6f);
        float a = 0f;
        while (a < up)
        {
            a += Time.deltaTime;
            float u = Mathf.Clamp01(a / up);
            float s = Mathf.Lerp(1f, peakScale, u);
            t.localScale = new Vector3(Mathf.Sign(baseS.x) * s, baseS.y * s, baseS.z * s);
            yield return null;
        }
        float downT = windup - up;
        float b = 0f;
        while (b < downT)
        {
            b += Time.deltaTime;
            float u = Mathf.Clamp01(b / Mathf.Max(0.0001f, downT));
            float s = Mathf.Lerp(peakScale, 1f, u);
            t.localScale = new Vector3(Mathf.Sign(baseS.x) * s, baseS.y * s, baseS.z * s);
            yield return null;
        }
        t.localScale = baseS;
    }
    void OnEnemyBaseDeath(EnemyBase eb)
    {
        if (s_OutroStarted) return;
        if (eb != _base) return;
        s_OutroStarted = true;

        SpawnLightExplosion(transform.position);
        InstantWhiteOverlayFlash(finaleFlashWhiteSeconds);
        FreezeBossForFinale();
        var cfg = BuildOutroConfig();
        CoroutineHost.Run(CoOutro(cfg));
    }

    void FreezeBossForFinale()
    {
        _finaleRunning = true;
        _phase = Phase.Dead;

        StopAllCoroutines();

        var allBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < allBehaviours.Length; i++)
        {
            var b = allBehaviours[i];
            if (!b || b == this || b == _base) continue; 
            b.enabled = false;
        }

        var rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        var cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        if (animator)
        {
            animator.speed = 0f;
            if (!string.IsNullOrEmpty(animMoveBool)) animator.SetBool(animMoveBool, false);
        }

        HideAllRenderers();
    }

    void HideAllRenderers()
    {
        var rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++) if (rends[i]) rends[i].enabled = false;
    }

    struct OutroConfig
    {
        public bool enable;
        public string playerTag, safeTag;
        public float seconds, extraLightIntensity;
        public Color fallbackTint;

        public Vector2 gAmin, gAmax, gBmin, gBmax;
        public float groundY;

        public List<GameObject> finaleGround;
        public int groundPerSide;
        public List<GameObject> finaleFlying;
        public int flyingCount;

        public float flyY, flySpanX, flySpanZ, flyZBase;
        public string effectsSortingLayer;

        public Vector3 pos;
        public int burstCount;
        public float duration, startLifetime;
        public Vector2 startSpeed, startSize;
        public float fadeTime;
        public string nextScene;

        public string cameraScriptTypeName;
        public Behaviour[] extraCameraBehavioursToDisable;
        public float zoomFOV, zoomSeconds;
        public float slowmoScale, slowmoSeconds;
        public string playerNewName;
        public float flashWhiteSeconds;
        public float spawnPulseSeconds;
        public int extraGroundPerSide, extraFlyingPerPulse;
        public Color bloodColor;
        public float bloodAlphaPerKill, bloodMaxAlpha, bloodEaseSpeed;
    }

    OutroConfig BuildOutroConfig()
    {
        return new OutroConfig
        {
            enable = true,
            playerTag = playerTag,
            safeTag = string.IsNullOrEmpty(finaleSafePlayerTag) ? "Untagged" : finaleSafePlayerTag,
            seconds = finaleDuration,
            extraLightIntensity = finalePlayerExtraLightIntensity,
            fallbackTint = finaleRendererTint,

            gAmin = groundAreaA_Min,
            gAmax = groundAreaA_Max,
            gBmin = groundAreaB_Min,
            gBmax = groundAreaB_Max,
            groundY = useBossGroundYForSpawns ? _groundY : groundSpawnY,
            finaleGround = (finaleGroundPrefabs != null && finaleGroundPrefabs.Count > 0) ? finaleGroundPrefabs : groundDualAttackPrefabs,
            groundPerSide = finaleGroundCountPerSide,
            finaleFlying = (finaleFlyingPrefabs != null && finaleFlyingPrefabs.Count > 0) ? finaleFlyingPrefabs : flyingPrefabs,
            flyingCount = finaleFlyingCount,
            flyY = flyingSpawnY,
            flySpanX = flyingSpawnXSpan,
            flySpanZ = flyingSpawnZSpan,
            flyZBase = flyingSpawnZBase,

            effectsSortingLayer = effectsSortingLayer,

            pos = transform.position,
            burstCount = explosionBurstCount,
            duration = explosionDuration,
            startLifetime = explosionStartLifetime,
            startSpeed = explosionStartSpeed,
            startSize = explosionStartSize,
            fadeTime = fadeToWhiteTime,
            nextScene = nextSceneName,

            cameraScriptTypeName = cameraScriptTypeName,
            extraCameraBehavioursToDisable = extraCameraBehavioursToDisable,
            zoomFOV = finaleZoomFOV,
            zoomSeconds = finaleZoomSeconds,
            slowmoScale = finaleSlowmoScale,
            slowmoSeconds = finaleSlowmoSeconds,
            playerNewName = finalePlayerNewName,
            flashWhiteSeconds = finaleFlashWhiteSeconds,
            spawnPulseSeconds = finaleSpawnPulseSeconds,
            extraGroundPerSide = finaleExtraGroundPerSide,
            extraFlyingPerPulse = finaleExtraFlyingPerPulse,
            bloodColor = bloodColor,
            bloodAlphaPerKill = bloodAlphaPerKill,
            bloodMaxAlpha = bloodMaxAlpha,
            bloodEaseSpeed = bloodEaseSpeed
        };
    }

    static IEnumerator CoOutro(OutroConfig C)
    {
        var fxRoot = new GameObject("BossOutroRoot3D");
        Object.DontDestroyOnLoad(fxRoot);
        var canvasGO = new GameObject("FinaleCanvas");
        canvasGO.transform.SetParent(fxRoot.transform, false);
        var canvas = canvasGO.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>(); canvasGO.AddComponent<GraphicRaycaster>();

        var flashGO = new GameObject("FlashWhite");
        flashGO.transform.SetParent(canvasGO.transform, false);
        var flash = flashGO.AddComponent<Image>();
        flash.color = new Color(1f, 1f, 1f, 1f);
        var rtF = flash.rectTransform; rtF.anchorMin = Vector2.zero; rtF.anchorMax = Vector2.one; rtF.offsetMin = Vector2.zero; rtF.offsetMax = Vector2.zero;

        var bloodGO = new GameObject("BloodOverlay");
        bloodGO.transform.SetParent(canvasGO.transform, false);
        var blood = bloodGO.AddComponent<Image>();
        blood.color = new Color(C.bloodColor.r, C.bloodColor.g, C.bloodColor.b, 0f);
        var rtB = blood.rectTransform; rtB.anchorMin = Vector2.zero; rtB.anchorMax = Vector2.one; rtB.offsetMin = Vector2.zero; rtB.offsetMax = Vector2.zero;
        Camera cam = Camera.main;
        float originalFOV = cam ? cam.fieldOfView : 60f;
        Quaternion camStartRot = cam ? cam.transform.rotation : Quaternion.identity;
        Vector3 camStartPos = cam ? cam.transform.position : Vector3.zero;

        if (cam)
        {
            if (!string.IsNullOrEmpty(C.cameraScriptTypeName))
            {
                var t = System.Type.GetType(C.cameraScriptTypeName) ?? (cam.GetComponent(C.cameraScriptTypeName)?.GetType());
                if (t != null)
                {
                    var mb = cam.GetComponent(t) as Behaviour;
                    if (mb) mb.enabled = false;
                }
            }
            if (C.extraCameraBehavioursToDisable != null)
            {
                for (int i = 0; i < C.extraCameraBehavioursToDisable.Length; i++)
                {
                    var b = C.extraCameraBehavioursToDisable[i];
                    if (b) b.enabled = false;
                }
            }
        }

        GameObject playerGO = GameObject.FindGameObjectWithTag(C.playerTag);
        Vector3 targetMid = playerGO ? (0.5f * (playerGO.transform.position + C.pos)) : C.pos;

        if (cam)
        {
            float tZoom = 0f;
            float startFOV = cam.fieldOfView;
            Vector3 startPos = cam.transform.position;

            Vector3 toMid = targetMid - cam.transform.position;
            Vector3 flat = new Vector3(toMid.x, 0f, toMid.z);
            Vector3 newPosTarget = cam.transform.position + flat * 0.35f;
            Quaternion newRotTarget = Quaternion.LookRotation((targetMid - newPosTarget).normalized, Vector3.up);

            while (tZoom < C.zoomSeconds)
            {
                tZoom += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(tZoom / Mathf.Max(0.0001f, C.zoomSeconds));
                cam.fieldOfView = Mathf.Lerp(startFOV, C.zoomFOV, u);
                cam.transform.position = Vector3.Lerp(startPos, newPosTarget, u);
                cam.transform.rotation = Quaternion.Slerp(camStartRot, newRotTarget, u);

                float flashU = 1f - Mathf.Clamp01(tZoom / Mathf.Max(0.0001f, C.flashWhiteSeconds));
                flash.color = new Color(1f, 1f, 1f, flashU);

                yield return null;
            }
        }
        else
        {
            float t = 0f;
            while (t < C.flashWhiteSeconds)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / Mathf.Max(0.0001f, C.flashWhiteSeconds));
                flash.color = new Color(1f, 1f, 1f, a);
                yield return null;
            }
        }
        flash.color = new Color(1f, 1f, 1f, 0f);

        float prevScale = Time.timeScale;
        Time.timeScale = Mathf.Clamp(C.slowmoScale, 0.01f, 1f);

        string originalTag = playerGO ? playerGO.tag : null;
        string originalName = playerGO ? playerGO.name : null;

        if (playerGO)
        {
            if (!string.IsNullOrEmpty(C.playerNewName)) playerGO.name = C.playerNewName;
            playerGO.tag = C.safeTag;

            var light = playerGO.GetComponentInChildren<Light>();
            if (light) light.intensity += C.extraLightIntensity;
            else
            {
                var rends = playerGO.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < rends.Length; i++)
                {
                    if (!rends[i] || !rends[i].sharedMaterial) continue;
                    if (rends[i].material.HasProperty("_Color"))
                        rends[i].material.color = C.fallbackTint;
                }
            }

            var pc = playerGO.GetComponentInChildren<PlayerController3D>();
            if (pc != null)
            {
                var m = pc.GetType().GetMethod("SetInvulnerable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m != null) m.Invoke(pc, new object[] { true });
                else
                {
                    var f = pc.GetType().GetField("invulnerable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (f != null && f.FieldType == typeof(bool)) f.SetValue(pc, true);
                }
            }
        }

        SpawnFinaleBurst(C);

        int kills = 0;
        float killEnableAt = Time.unscaledTime + Mathf.Max(0.05f, C.slowmoSeconds * 0.1f, FindGraceSeconds());

        EnemyBase bossEB = null;
        {
            var boss = Object.FindFirstObjectByType<BossFivePhaseController3D>();
            if (boss) bossEB = boss.GetComponent<EnemyBase>();
        }

        blood.gameObject.SetActive(false);

        System.Action<EnemyBase> onKill = (EnemyBase e) =>
        {
            if (e == null) return;
            if (bossEB && ReferenceEquals(e, bossEB)) return;
            if (Time.unscaledTime < killEnableAt) return;

            var credit = e.GetComponent<FinaleKillCredit>();
            if (credit == null || !credit.active) return;

            kills++;
            if (!blood.gameObject.activeSelf) blood.gameObject.SetActive(true);
        };
        EnemyBase.OnEnemyKilled += onKill;

        float tF = 0f;
        float spawnPulseTimer = 0f;
        float currentBloodAlpha = 0f;

        while (tF < C.seconds)
        {
            spawnPulseTimer += Time.unscaledDeltaTime;
            if (spawnPulseTimer >= Mathf.Max(0.05f, C.spawnPulseSeconds))
            {
                spawnPulseTimer = 0f;
                PulseMore(C);
            }

            float targetAlpha = (kills > 0) ? Mathf.Min(C.bloodMaxAlpha, kills * C.bloodAlphaPerKill) : 0f;
            currentBloodAlpha = Mathf.MoveTowards(currentBloodAlpha, targetAlpha, C.bloodEaseSpeed * Time.unscaledDeltaTime);
            blood.color = new Color(C.bloodColor.r, C.bloodColor.g, C.bloodColor.b, currentBloodAlpha);

            tF += Time.unscaledDeltaTime;
            yield return null;
        }

        EnemyBase.OnEnemyKilled -= onKill;

        Time.timeScale = prevScale;

        var go = new GameObject("BossLightExplosion_Final");
        go.transform.SetParent(fxRoot.transform, false);
        go.transform.position = C.pos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(true);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.duration = C.duration;
        main.startLifetime = C.startLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(C.startSpeed.x, C.startSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(C.startSize.x, C.startSize.y);
        main.maxParticles = Mathf.Max(C.burstCount, 1);

        var emission = ps.emission; emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)C.burstCount) });

        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sortingLayerName = C.effectsSortingLayer;

        ps.Play();

        float tRed = 0f;
        float blackoutSeconds = Mathf.Max(0.15f, C.fadeTime);
        while (tRed < blackoutSeconds)
        {
            tRed += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(blood.color.a, C.bloodMaxAlpha, tRed / blackoutSeconds);
            blood.color = new Color(C.bloodColor.r, 0f, 0f, a);
            yield return null;
        }
        blood.color = new Color(C.bloodColor.r, 0f, 0f, C.bloodMaxAlpha);

        if (!string.IsNullOrEmpty(C.nextScene))
            SceneManager.LoadScene(C.nextScene);

        yield return null;
        Object.Destroy(fxRoot);

        s_OutroStarted = false;

        float FindGraceSeconds()
        {
            var boss = Object.FindFirstObjectByType<BossFivePhaseController3D>();
            return boss ? Mathf.Max(0.05f, boss.bloodKillGraceSeconds) : 0.4f;
        }
    }

    private static void SpawnFinaleBurst(OutroConfig C)
    {
        if (C.finaleGround != null && C.finaleGround.Count > 0)
        {
            for (int i = 0; i < C.groundPerSide; i++)
            {
                var pf = C.finaleGround[Random.Range(0, C.finaleGround.Count)];
                float x = Random.Range(C.gAmin.x, C.gAmax.x);
                float z = Random.Range(C.gAmin.y, C.gAmax.y);
                var g = Object.Instantiate(pf, new Vector3(x, C.groundY, z), Quaternion.identity);
                ForceFinaleOneHPAndRush(g);
            }
            for (int i = 0; i < C.groundPerSide; i++)
            {
                var pf = C.finaleGround[Random.Range(0, C.finaleGround.Count)];
                float x = Random.Range(C.gBmin.x, C.gBmax.x);
                float z = Random.Range(C.gBmin.y, C.gBmax.y);
                var g = Object.Instantiate(pf, new Vector3(x, C.groundY, z), Quaternion.identity);
                ForceFinaleOneHPAndRush(g);
            }
        }

        if (C.finaleFlying != null && C.finaleFlying.Count > 0)
        {
            float centerX = 0f;
            var player = GameObject.FindGameObjectWithTag(C.playerTag);
            if (player) centerX = player.transform.position.x;

            float halfX = C.flySpanX * 0.5f;
            float halfZ = C.flySpanZ * 0.5f;
            for (int i = 0; i < C.flyingCount; i++)
            {
                var pf = C.finaleFlying[Random.Range(0, C.finaleFlying.Count)];
                float x = centerX + Random.Range(-halfX, halfX);
                float z = (C.flySpanZ <= 0f) ? C.flyZBase : Random.Range(-halfZ, halfZ);
                var g = Object.Instantiate(pf, new Vector3(x, C.flyY, z), Quaternion.identity);
                ForceFinaleOneHPAndRush(g);
            }
        }
    }

    private static void PulseMore(OutroConfig C)
    {
        if (C.finaleGround != null && C.finaleGround.Count > 0 && C.extraGroundPerSide > 0)
        {
            for (int i = 0; i < C.extraGroundPerSide; i++)
            {
                var pf = C.finaleGround[Random.Range(0, C.finaleGround.Count)];
                float x = Random.Range(C.gAmin.x, C.gAmax.x);
                float z = Random.Range(C.gAmin.y, C.gAmax.y);
                var g = Object.Instantiate(pf, new Vector3(x, C.groundY, z), Quaternion.identity);
                ForceFinaleOneHPAndRush(g);
            }
            for (int i = 0; i < C.extraGroundPerSide; i++)
            {
                var pf = C.finaleGround[Random.Range(0, C.finaleGround.Count)];
                float x = Random.Range(C.gBmin.x, C.gBmax.x);
                float z = Random.Range(C.gBmin.y, C.gBmax.y);
                var g = Object.Instantiate(pf, new Vector3(x, C.groundY, z), Quaternion.identity);
                ForceFinaleOneHPAndRush(g);
            }
        }

        if (C.finaleFlying != null && C.finaleFlying.Count > 0 && C.extraFlyingPerPulse > 0)
        {
            float centerX = 0f;
            var player = GameObject.FindGameObjectWithTag(C.playerTag);
            if (player) centerX = player.transform.position.x;

            float halfX = C.flySpanX * 0.5f;
            float halfZ = C.flySpanZ * 0.5f;
            for (int i = 0; i < C.extraFlyingPerPulse; i++)
            {
                var pf = C.finaleFlying[Random.Range(0, C.finaleFlying.Count)];
                float x = centerX + Random.Range(-halfX, halfX);
                float z = (C.flySpanZ <= 0f) ? C.flyZBase : Random.Range(-halfZ, halfZ);
                var g = Object.Instantiate(pf, new Vector3(x, C.flyY, z), Quaternion.identity);
                ForceFinaleOneHPAndRush(g);
            }
        }
    }
    static void ForceFinaleOneHPAndRush(GameObject g)
    {
        if (!g) return;

        var eb = g.GetComponent<EnemyBase>();
        if (eb)
        {
            eb.maxHP = 1f;
            eb.allowExternalSetHP = true;
            eb.SetHPFromController(1f);

            if (eb.CurrentHP < 0f)
            {
                var f = typeof(EnemyBase).GetField("_hp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f == null) f = typeof(EnemyBase).GetField("hp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(eb, 1f);
            }
        }

        if (!g.GetComponent<FinaleRusher>())
            g.AddComponent<FinaleRusher>();

        var credit = g.GetComponent<FinaleKillCredit>();
        if (!credit) credit = g.AddComponent<FinaleKillCredit>();
        credit.armDelay = 0.4f;
    }

    class FinaleRusher : MonoBehaviour
    {
        public float speed = 6.5f;
        string playerTag = "Player";
        Transform t, player;

        void Awake()
        {
            t = transform;
            var boss = FindFirstObjectByType<BossFivePhaseController3D>();
            if (boss) playerTag = boss.playerTag;
            var pgo = GameObject.FindGameObjectWithTag(playerTag);
            if (pgo) player = pgo.transform;
        }

        void Update()
        {
            if (!player) return;
            Vector3 pos = t.position;
            Vector3 target = new Vector3(player.position.x, pos.y, player.position.z);
            Vector3 dir = (target - pos);
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            t.position += dir * speed * Time.deltaTime;
        }
    }

    void SpawnLightExplosion(Vector3 at)
    {
        var go = new GameObject("BossDeath_LightBurst");
        go.transform.position = at;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.duration = Mathf.Max(0.35f, explosionDuration * 0.6f);
        main.startLifetime = explosionStartLifetime;
        main.startSpeed = new ParticleSystem.MinMaxCurve(explosionStartSpeed.x * 1.2f, explosionStartSpeed.y * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(explosionStartSize.x * 0.8f, explosionStartSize.y * 1.1f);
        main.maxParticles = Mathf.Max(explosionBurstCount / 2, 1);

        var emission = ps.emission; emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(explosionBurstCount / 2)) });

        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(g);

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.sortingLayerName = effectsSortingLayer;

        ps.Play();
        Destroy(go, main.duration + explosionStartLifetime + 0.5f);
    }

    static void InstantWhiteOverlayFlash(float seconds)
    {
        var fx = new GameObject("InstantWhiteOverlay");
        Object.DontDestroyOnLoad(fx);
        var canvas = fx.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fx.AddComponent<CanvasScaler>(); fx.AddComponent<GraphicRaycaster>();
        var imgGO = new GameObject("White");
        imgGO.transform.SetParent(fx.transform, false);
        var img = imgGO.AddComponent<Image>();
        var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        img.color = new Color(1f, 1f, 1f, 1f);
        CoroutineHost.Run(FadeAndKill(img, seconds, fx));
        IEnumerator FadeAndKill(Image im, float s, GameObject root)
        {
            float t = 0f;
            while (t < s)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / Mathf.Max(0.0001f, s));
                if (im) im.color = new Color(1f, 1f, 1f, a);
                yield return null;
            }
            if (root) Object.Destroy(root);
        }
    }

    Vector3 GetPosePoint()
    {
        Vector3 p = posePoint ? posePoint.position : (_startPos + poseOffset);
        return new Vector3(p.x, _groundY, p.z);
    }
    Vector3 GetPoseReturnPoint()
    {
        Vector3 p = poseReturnPoint ? poseReturnPoint.position : (_startPos + poseReturnOffset);
        return new Vector3(p.x, _groundY, p.z);
    }
    IEnumerator SprintToReturnPoseBlocking()
    {
        Vector3 target = GetPoseReturnPoint();
        float vx = 0f;
        SetAnimMoving(true);

        while ((transform.position - target).sqrMagnitude > 0.0004f)
        {
            Vector3 dir = (target - transform.position).normalized;
            Quaternion faceDir = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, faceDir, poseRotateSpeedDeg * Time.deltaTime);

            vx = Mathf.MoveTowards(vx, runOffSpeed, runOffAccel * Time.deltaTime);
            transform.position += dir * vx * Time.deltaTime;
            yield return null;
        }

        SetAnimMoving(false);
        transform.position = new Vector3(target.x, _groundY, target.z);
    }
    IEnumerator SprintToPosePointBlocking()
    {
        Vector3 target = GetPosePoint();
        float vx = 0f;
        SetAnimMoving(true);

        while ((transform.position - target).sqrMagnitude > 0.0004f)
        {
            Quaternion faceAway = Quaternion.LookRotation(Vector3.right, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, faceAway, poseRotateSpeedDeg * Time.deltaTime);

            vx = Mathf.MoveTowards(vx, runOffSpeed, runOffAccel * Time.deltaTime);
            Vector3 p = transform.position;
            Vector3 dir = (new Vector3(target.x, _groundY, target.z) - new Vector3(p.x, _groundY, p.z)).normalized;
            transform.position = new Vector3(p.x + dir.x * vx * Time.deltaTime, _groundY, p.z + dir.z * vx * Time.deltaTime);
            yield return null;
        }

        SetAnimMoving(false);
        transform.position = new Vector3(target.x, _groundY, target.z);
    }



    class CoroutineHost : MonoBehaviour
    {
        public static void Run(IEnumerator routine)
        {
            var go = new GameObject("BossOutroHost");
            DontDestroyOnLoad(go);
            var host = go.AddComponent<CoroutineHost>();
            host.StartCoroutine(host.RunAndClean(routine));
        }
        IEnumerator RunAndClean(IEnumerator r)
        {
            yield return StartCoroutine(r);
            Destroy(gameObject);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawAreas) return;

        Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.25f);
        float gy = Application.isPlaying ? _groundY : (lockY ? (lockYUseStart ? transform.position.y : lockYValue) + lockYOffset : transform.position.y);

        Vector3 aMin = new Vector3(groundAreaA_Min.x, gy, groundAreaA_Min.y);
        Vector3 aMax = new Vector3(groundAreaA_Max.x, gy, groundAreaA_Max.y);
        Vector3 bMin = new Vector3(groundAreaB_Min.x, gy, groundAreaB_Min.y);
        Vector3 bMax = new Vector3(groundAreaB_Max.x, gy, groundAreaB_Max.y);

        Vector3 aCenter = (aMin + aMax) * 0.5f;
        Vector3 aSize = new Vector3(Mathf.Abs(aMax.x - aMin.x), 0.01f, Mathf.Abs(aMax.z - aMin.z));
        Gizmos.DrawWireCube(aCenter, aSize);

        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.25f);
        Vector3 bCenter = (bMin + bMax) * 0.5f;
        Vector3 bSize = new Vector3(Mathf.Abs(bMax.x - bMin.x), 0.01f, Mathf.Abs(bMax.z - bMin.z));
        Gizmos.DrawWireCube(bCenter, bSize);
    }
}

public class FinaleKillCredit : MonoBehaviour
{
    public bool active { get; private set; }
    public float armDelay = 0.4f;
    IEnumerator Start()
    {
        active = false;
        yield return new WaitForSecondsRealtime(armDelay);
        active = true;
    }
}
