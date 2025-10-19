using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Audio;

[RequireComponent(typeof(Collider))]
public class EnemyBase : MonoBehaviour
{
    public AudioMixerGroup sfxMixerGroup;

    public float maxHP = 20f;
    public float contactDamage = 10f;
    public float gateEpsilonPct = 0.2f;
    public string playerTag = "Player";

    public bool preferWeaponHitbox = true;
    public float defaultWeaponDamage = 10f;
    public string weaponTag = "Weapon";
    public LayerMask weaponLayers = 0;

    public bool customDeathHandled = false;
    public bool disableCollidersOnCustomDeath = true;
    public bool spawnVFXOnCustomDeath = false;

    public float sameColliderHitCooldown = 0.05f;

    public bool alwaysFacePlayer = true;
    public bool faceYAxisOnly = true;
    public float faceSpeedDegPerSec = 720f;

    public bool snapToColliderBase = true;
    public float colliderBaseExtraYOffset = 0.02f;
    public float vfxYOffset = 0f;

    public bool spawnShadowPuff = true;
    public int puffBurstCount = 30;
    public float puffStartLifetime = 0.45f;
    public Vector2 puffStartSpeed = new Vector2(2.0f, 3.2f);
    public Vector2 puffStartSize = new Vector2(0.20f, 0.32f);
    public float puffSphereRadius = 0.25f;
    public Color puffColor = new Color(1f, 1f, 1f, 0.95f);
    public string puffSortingLayer = "Effects";
    public float puffKeepSeconds = 1.5f;

    public bool spawnHomingSouls = true;
    public int soulCount = 10;
    public float soulLifetime = 1.4f;
    public float soulStartSpread = 1.0f;
    public float soulArcJitter = 0.6f;
    public float soulStartSize = 0.18f;
    public float soulEndSize = 0.06f;
    public Color soulColor = new Color(1f, 1f, 1f, 0.95f);
    public string soulSortingLayer = "Effects";

    public Transform soulDestination;
    public string soulDestinationTag = "";
    public bool resolveDestinationInAwake = true;

    public bool healPlayerOnSoulArrival = true;
    public float healPerSoul = 2f;
    public float lightPerSoul = 0f;

    public bool isBossHPReadable = false;
    public bool allowExternalSetHP = false;
    public float CurrentHP => isBossHPReadable ? _hp : -1f;
    public void SetHPFromController(float value) { if (allowExternalSetHP) _hp = Mathf.Max(0f, value); }

    public AudioClip[] hitClips;
    public Vector2 hitPitchRange = new Vector2(0.95f, 1.05f);
    [Range(0f, 1f)] public float hitVolume = 0.9f;

    public AudioClip[] deathClips;
    public Vector2 deathPitchRange = new Vector2(0.95f, 1.05f);
    [Range(0f, 1f)] public float deathVolume = 1.0f;

    public AudioClip[] contactHitPlayerClips;
    public Vector2 contactPitchRange = new Vector2(0.95f, 1.05f);
    [Range(0f, 1f)] public float contactVolume = 0.9f;

    public AudioClip[] attackClips;
    public Vector2 attackPitchRange = new Vector2(0.98f, 1.02f);
    [Range(0f, 1f)] public float attackVolume = 1.0f;

    public bool movementLoopEnabled = false;
    public AudioClip movementLoopClip;
    [Range(0f, 1f)] public float movementLoopVolume = 0.35f;
    public float movementSpeedThreshold = 0.05f;
    public bool useRigidbodySpeed = true;
    public float movementFadeTime = 0.15f;

    public bool audio3D = true;
    [Range(0f, 1f)] public float spatialBlend = 1f;
    public float minDistance = 1.5f;
    public float maxDistance = 12f;
    public bool ignoreListenerPause = true;

    public static event System.Action<EnemyBase> OnEnemyKilled;
    public static event System.Action<EnemyBase, float, float> OnEnemyDamaged;

    float _hp;
    Transform _player;

    readonly HashSet<int> _processedHitIds = new HashSet<int>();
    readonly Dictionary<Collider, float> _cooldowns = new Dictionary<Collider, float>();
    bool _isDying;

    static Material sParticleMat;
    static Sprite sCircleSprite;

    AudioSource _sfx;
    AudioSource _moveLoop;
    Rigidbody _rb;
    Vector3 _lastPos;
    float _moveFadeV;

    BossFivePhaseController3D _boss;

    void Awake()
    {
        _hp = maxHP;

        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo) _player = pgo.transform;

        if (resolveDestinationInAwake && !soulDestination && !string.IsNullOrEmpty(soulDestinationTag))
        {
            var tgtGo = GameObject.FindGameObjectWithTag(soulDestinationTag);
            if (tgtGo) soulDestination = tgtGo.transform;
        }

        if (sParticleMat == null)
        {
            var sh = Shader.Find("Particles/Standard Unlit");
            sParticleMat = sh ? new Material(sh) : new Material(Shader.Find("Sprites/Default"));
            sParticleMat.color = puffColor;
        }
        if (sCircleSprite == null) sCircleSprite = CreateCircleSprite(16);

        var aroot = new GameObject("Audio");
        aroot.transform.SetParent(transform, false);

        _sfx = aroot.AddComponent<AudioSource>();
        _sfx.playOnAwake = false;
        _sfx.loop = false;
        _sfx.spatialBlend = audio3D ? spatialBlend : 0f;
        _sfx.rolloffMode = AudioRolloffMode.Linear;
        _sfx.minDistance = minDistance;
        _sfx.maxDistance = maxDistance;
        _sfx.ignoreListenerPause = ignoreListenerPause;

        _moveLoop = aroot.AddComponent<AudioSource>();
        _moveLoop.playOnAwake = false;
        _moveLoop.loop = true;
        _moveLoop.clip = movementLoopClip;
        _moveLoop.volume = 0f;
        _moveLoop.spatialBlend = audio3D ? spatialBlend : 0f;
        _moveLoop.rolloffMode = AudioRolloffMode.Linear;
        _moveLoop.minDistance = minDistance;
        _moveLoop.maxDistance = maxDistance;
        _moveLoop.ignoreListenerPause = ignoreListenerPause;
        if (movementLoopEnabled && movementLoopClip) _moveLoop.Play();

        if (sfxMixerGroup)
        {
            _sfx.outputAudioMixerGroup = sfxMixerGroup;
            _moveLoop.outputAudioMixerGroup = sfxMixerGroup;
        }

        _rb = GetComponent<Rigidbody>();
        _lastPos = transform.position;

        _boss = GetComponent<BossFivePhaseController3D>();
        if (!_boss) _boss = GetComponentInParent<BossFivePhaseController3D>();
    }

    void Update()
    {
        if (!_isDying && alwaysFacePlayer && _player) FaceTowards(_player.position);

        if (movementLoopEnabled && movementLoopClip)
        {
            float speed = 0f;
            if (useRigidbodySpeed && _rb != null) speed = _rb.linearVelocity.magnitude;
            else
            {
                var p = transform.position;
                speed = (p - _lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
                _lastPos = p;
            }

            float targetVol = speed > movementSpeedThreshold ? movementLoopVolume : 0f;
            _moveLoop.volume = Mathf.SmoothDamp(_moveLoop.volume, targetVol, ref _moveFadeV, Mathf.Max(0.01f, movementFadeTime));
            if (!_moveLoop.isPlaying && targetVol > 0.001f) _moveLoop.Play();
            if (_moveLoop.isPlaying && targetVol <= 0.001f && _moveLoop.volume <= 0.01f) _moveLoop.Pause();
        }
    }

    public void TakeDamage(float amount)
    {
        if (_isDying) return;

        float before = _hp;

        if (_boss != null)
        {
            float after = before - amount;
            float gateAbs = GetNextGateHPAbs(before);
            if (gateAbs > 0f)
            {
                float epsAbs = Mathf.Abs(gateEpsilonPct) * 0.01f * maxHP;
                _hp = (after < gateAbs - epsAbs) ? gateAbs : after;
            }
            else
            {
                _hp = after;
            }
        }
        else
        {
            _hp = before - amount;
        }

        if (_hp > 0f)
        {
            PlayRandomOneShot(hitClips, hitVolume, hitPitchRange);
            OnEnemyDamaged?.Invoke(this, amount, _hp);
            SendMessage("OnEnemyBaseHit", amount, SendMessageOptions.DontRequireReceiver);
            return;
        }

        StartCoroutine(DeathSequence());
    }

    float GetNextGateHPAbs(float hpBefore)
    {
        float g2, g3, g4, g5;
        if (_boss == null)
        {
            return -1f;
        }
        g2 = _boss.phase2HP; g3 = _boss.phase3HP; g4 = _boss.phase4HP; g5 = _boss.phase5HP;

        float pctBefore = Mathf.Clamp01(hpBefore / Mathf.Max(0.0001f, maxHP)) * 100f;
        float eps = Mathf.Abs(gateEpsilonPct);

        float gatePct = -1f;
        if (pctBefore > g2 + eps) gatePct = g2;
        else if (pctBefore > g3 + eps) gatePct = g3;
        else if (pctBefore > g4 + eps) gatePct = g4;
        else if (pctBefore > g5 + eps) gatePct = g5;

        if (gatePct < 0f) return -1f;
        return Mathf.Clamp01(gatePct / 100f) * maxHP;
    }

    void OnCollisionEnter(Collision c)
    {
        if (TryApplyWeaponHit(c.collider)) return;
        TryDealContactDamage(c.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        if (TryApplyWeaponHit(other)) return;
        TryDealContactDamage(other);
    }

    void TryDealContactDamage(Component hit)
    {
        if (!hit) return;

        var root = hit.GetComponentInParent<Transform>();
        if (!root || !root.CompareTag(playerTag)) return;

        var pc = hit.GetComponentInParent<PlayerController3D>();
        if (pc)
        {
            pc.ApplyDamage(contactDamage);
            PlayRandomOneShot(contactHitPlayerClips, contactVolume, contactPitchRange);
        }
    }

    bool TryApplyWeaponHit(Collider col)
    {
        if (weaponLayers.value != 0 && ((1 << col.gameObject.layer) & weaponLayers.value) == 0)
        {
            if (preferWeaponHitbox && !col.GetComponentInParent<WeaponHitbox>()) return false;
        }

        if (!string.IsNullOrEmpty(weaponTag) && !col.CompareTag(weaponTag))
        {
            if (preferWeaponHitbox && !col.GetComponentInParent<WeaponHitbox>()) return false;
        }

        float dmg = defaultWeaponDamage;

        WeaponHitbox hb = preferWeaponHitbox ? col.GetComponentInParent<WeaponHitbox>() : null;
        if (hb)
        {
            if (!string.IsNullOrEmpty(playerTag) && hb.owner && !hb.owner.CompareTag(playerTag)) return false;

            if (hb.hitId != 0)
            {
                if (_processedHitIds.Contains(hb.hitId)) return false;
                _processedHitIds.Add(hb.hitId);
            }

            dmg = hb.damage > 0f ? hb.damage : defaultWeaponDamage;
        }
        else
        {
            if (_cooldowns.TryGetValue(col, out float last))
            {
                if (Time.time - last < sameColliderHitCooldown) return false;
            }
            _cooldowns[col] = Time.time;
        }

        TakeDamage(dmg);
        return true;
    }

    System.Collections.IEnumerator DeathSequence()
    {
        _isDying = true;

        if (_moveLoop && _moveLoop.isPlaying) _moveLoop.Stop();
        PlayRandomOneShot(deathClips, deathVolume, deathPitchRange);

        bool shouldSpawnVFX = !customDeathHandled || (customDeathHandled && spawnVFXOnCustomDeath);
        if (shouldSpawnVFX)
        {
            if (spawnShadowPuff) SpawnShadowPuff_Unscaled();
            if (spawnHomingSouls) SpawnHomingSouls_Unscaled();
        }

        if (customDeathHandled)
        {
            if (disableCollidersOnCustomDeath)
            {
                var cols = GetComponentsInChildren<Collider>(includeInactive: false);
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            }
            SendMessage("OnEnemyBaseDeath", this, SendMessageOptions.DontRequireReceiver);
            yield break;
        }

        OnEnemyKilled?.Invoke(this);
        yield return null;
        Destroy(gameObject);
    }

    public void OnEnemyAttack()
    {
        PlayRandomOneShot(attackClips, attackVolume, attackPitchRange);
    }

    Vector3 GetVFXSpawnPosition()
    {
        Vector3 pos = transform.position;

        if (snapToColliderBase)
        {
            var col = GetComponent<Collider>();
            if (col && col.enabled)
            {
                float baseY = col.bounds.min.y + colliderBaseExtraYOffset;
                pos = new Vector3(transform.position.x, baseY, transform.position.z);
            }
        }

        pos += Vector3.up * vfxYOffset;
        return pos;
    }

    void SpawnShadowPuff_Unscaled()
    {
        var go = new GameObject("EnemyDeath_ShadowPuff");
        go.transform.position = GetVFXSpawnPosition();

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(puffStartLifetime, puffStartLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(puffStartSpeed.x, puffStartSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(puffStartSize.x, puffStartSize.y);
        main.startColor = puffColor;
        main.maxParticles = Mathf.Max(puffBurstCount, 1);

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)puffBurstCount) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = puffSphereRadius;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(puffColor, 0f), new GradientColorKey(puffColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = sParticleMat;
        if (!string.IsNullOrEmpty(puffSortingLayer)) rend.sortingLayerName = puffSortingLayer;

        ps.Play();
        StartCoroutine(DestroyRealtime(go, puffKeepSeconds));
    }

    void SpawnHomingSouls_Unscaled()
    {
        Transform target = soulDestination ? soulDestination : _player;
        if (!target) return;

        PlayerController3D healTarget = null;
        if (healPlayerOnSoulArrival)
        {
            healTarget = target.GetComponentInParent<PlayerController3D>();
            if (!healTarget && _player) healTarget = _player.GetComponentInParent<PlayerController3D>();
        }

        Vector3 basePos = GetVFXSpawnPosition();

        for (int i = 0; i < soulCount; i++)
        {
            var orb = new GameObject("SoulOrb");
            orb.transform.position = basePos + (Vector3)(Random.insideUnitCircle * soulStartSpread);

            var sr = orb.AddComponent<SpriteRenderer>();
            sr.sprite = sCircleSprite;
            sr.color = soulColor;
            if (!string.IsNullOrEmpty(soulSortingLayer)) sr.sortingLayerName = soulSortingLayer;

            var mover = orb.AddComponent<SoulMoverUnscaled>();
            mover.target = target;
            mover.lifetime = soulLifetime;
            mover.arcJitter = soulArcJitter;
            mover.startSize = soulStartSize;
            mover.endSize = soulEndSize;

            mover.healTarget = healTarget;
            mover.healHP = healPerSoul;
            mover.addLight = lightPerSoul;
        }
    }

    private class SoulMoverUnscaled : MonoBehaviour
    {
        public Transform target;
        public float lifetime = 1.4f;
        public float arcJitter = 0.6f;
        public float startSize = 0.18f;
        public float endSize = 0.06f;

        public PlayerController3D healTarget;
        public float healHP;
        public float addLight;

        Vector3 start, arc;
        float t;

        void Start()
        {
            start = transform.position;
            Vector2 j = Random.insideUnitCircle.normalized * arcJitter;
            arc = new Vector3(j.x, 0f, j.y);
            transform.localScale = Vector3.one * startSize;
        }

        void Update()
        {
            if (!target) { Destroy(gameObject); return; }

            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, lifetime));
            float e = u * u * (3f - 2f * u);

            Vector3 end = target.position; end.y = transform.position.y;

            Vector3 mid = Vector3.Lerp(start, end, 0.5f) + arc;
            Vector3 p1 = Vector3.Lerp(start, mid, e);
            Vector3 p2 = Vector3.Lerp(mid, end, e);
            transform.position = Vector3.Lerp(p1, p2, e);

            float s = Mathf.Lerp(startSize, endSize, e);
            transform.localScale = new Vector3(s, s, 1f);

            if (u >= 1f)
            {
                if (healTarget)
                {
                    if (healHP > 0f) healTarget.Heal(healHP);
                    if (addLight > 0f) healTarget.AddLight(addLight);
                }
                Destroy(gameObject);
            }
        }
    }

    void FaceTowards(Vector3 worldPoint)
    {
        Vector3 to = worldPoint - transform.position;

        if (faceYAxisOnly)
        {
            to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return;
            Quaternion target = Quaternion.LookRotation(to.normalized, Vector3.up);
            if (faceSpeedDegPerSec <= 0f) transform.rotation = target;
            else transform.rotation = Quaternion.RotateTowards(transform.rotation, target, faceSpeedDegPerSec * Time.deltaTime);
        }
        else
        {
            if (to.sqrMagnitude < 1e-6f) return;
            Quaternion target = Quaternion.LookRotation(to.normalized, Vector3.up);
            if (faceSpeedDegPerSec <= 0f) transform.rotation = target;
            else transform.rotation = Quaternion.RotateTowards(transform.rotation, target, faceSpeedDegPerSec * Time.deltaTime);
        }
    }

    static Sprite CreateCircleSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        var cx = (size - 1) * 0.5f;
        var cy = (size - 1) * 0.5f;
        float r = size * 0.5f;

        var cols = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx, dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - (d - (r - 1f)));
                cols[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(cols);
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    System.Collections.IEnumerator DestroyRealtime(GameObject go, float seconds)
    {
        float end = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
        while (Time.realtimeSinceStartup < end) yield return null;
        if (go) Destroy(go);
    }

    void PlayRandomOneShot(AudioClip[] pool, float vol, Vector2 pitchRange)
    {
        if (_sfx == null || pool == null || pool.Length == 0) return;
        var clip = pool[Random.Range(0, pool.Length)];
        if (!clip) return;

        _sfx.pitch = Random.Range(pitchRange.x, pitchRange.y);
        _sfx.volume = Mathf.Clamp01(vol);
        _sfx.PlayOneShot(clip, 1f);
    }
}
