using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Enemy base: HP, contact damage, shadow puff on death, homing "soul" orbs,
/// + transform-only hit/death animations. Supports 3D weapon hits.
/// Patched: all player damage uses PlayerController3D.ApplyDamage.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyBase : MonoBehaviour
{
    Vector3 _baseLocalPos;

    [Header("Stats")]
    public float maxHP = 20f;
    public float contactDamage = 10f;

    [Header("Player Link")]
    public string playerTag = "Player";

    [Header("Weapon Hit Recognition")]
    public bool preferWeaponHitbox = true;
    public float defaultWeaponDamage = 10f;
    public string weaponTag = "Weapon";
    public LayerMask weaponLayers = 0;

    // ----------------- HIT / DEATH ANIMATIONS (transform-only) -----------------
    [Header("Hit Animation")]
    public bool animateOnHit = true;
    public Transform animTarget;
    public float hitScale = 1.12f;
    public float hitDuration = 0.12f;
    public float hitShake = 0.035f;
    public AnimationCurve hitCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Death Animation")]
    public bool animateOnDeath = true;
    public float deathScale = 1.25f;
    public float deathDuration = 0.18f;
    public float deathSpinDegrees = 12f;
    public AnimationCurve deathCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // ----------------- SHADOW PUFF -----------------
    [Header("Shadow Puff (ParticleSystem)")]
    public bool spawnShadowPuff = true;
    public int puffBurstCount = 30;
    public float puffDuration = 0.6f;
    public float puffStartLifetime = 0.4f;
    public Vector2 puffStartSpeed = new Vector2(1.5f, 2.8f);
    public Vector2 puffStartSize = new Vector2(0.22f, 0.38f);
    public float puffSphereRadius = 0.25f;
    public Color puffColor = new Color(0f, 0f, 0f, 0.85f);
    public string puffSortingLayer = "Effects";

    // ----------------- HOMING SOULS -----------------
    [Header("Homing Souls (no prefab)")]
    public bool spawnHomingSouls = true;
    public int soulCount = 10;
    public float soulLifetime = 1.4f;
    public float soulStartSpread = 1.0f;
    public float soulArcJitter = 0.6f;
    public float soulStartSize = 0.18f;
    public float soulEndSize = 0.06f;
    public Color soulColor = new Color(0f, 0f, 0f, 0.85f);
    public string soulSortingLayer = "Effects";

    [Header("Cleanup")]
    public float extraDespawnTime = 0.3f;

    // ----------------- CUSTOM DEATH (opt-in; boss enables this at runtime) -----------------
    [Header("Custom Death (optional)")]
    [Tooltip("If true, EnemyBase will NOT destroy itself on death. It will call OnEnemyBaseDeath(this) on the same GameObject instead.")]
    public bool customDeathHandled = false;
    [Tooltip("Disable all colliders when using custom death (prevents late collisions while boss outro runs).")]
    public bool disableCollidersOnCustomDeath = true;

    // === KILL EVENT for finale blood overlay ===
    public static event System.Action<EnemyBase> OnEnemyKilled; // fired only on *default* death (NOT for custom death bosses)

    float hp;
    Transform player;

    static Material sParticleMat;
    static Sprite sCircleSprite;

    // Animation bookkeeping
    Vector3 _baseScale;
    Quaternion _baseRot;
    bool _isDying;
    Coroutine _hitCo;

    // De-dup hits per swing
    readonly HashSet<int> _processedHitIds = new HashSet<int>();
    readonly Dictionary<Collider, float> _cooldowns = new Dictionary<Collider, float>();
    public float sameColliderHitCooldown = 0.05f; // fallback if no hitId

    void Awake()
    {
        hp = maxHP;
        if (!animTarget) animTarget = transform;
        _baseScale = animTarget.localScale;
        _baseRot = animTarget.localRotation;
        _baseLocalPos = animTarget.localPosition;

        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo) player = pgo.transform;

        if (sParticleMat == null)
        {
            var sh = Shader.Find("Particles/Standard Unlit");
            sParticleMat = new Material(sh) { color = puffColor };
        }
        if (sCircleSprite == null)
            sCircleSprite = CreateCircleSprite(16);

        if (hitCurve == null || hitCurve.length == 0) hitCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        if (deathCurve == null || deathCurve.length == 0) deathCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    }

    // ----------------- DAMAGE INTAKE (Enemy takes) -----------------
    public void TakeDamage(float amount)
    {
        if (_isDying) return;

        hp -= amount;

        if (animateOnHit && hp > 0f)
        {
            if (_hitCo != null) StopCoroutine(_hitCo);
            _hitCo = StartCoroutine(HitAnim());
        }

        if (hp <= 0f) StartCoroutine(DeathSequence());
    }

    // ----------------- PLAYER CONTACT DAMAGE (Enemy deals) -----------------
    void OnCollisionEnter(Collision c)
    {
        if (TryApplyWeaponHit(c.collider)) return;   // enemy receiving weapon hit
        TryDealContactDamage(c.collider);            // enemy dealing contact damage to player
    }

    void OnTriggerEnter(Collider other)
    {
        if (TryApplyWeaponHit(other)) return;
        TryDealContactDamage(other);
    }

    void TryDealContactDamage(Component hit)
    {
        if (!hit) return;

        // Only damage the player-tag object
        var root = hit.GetComponentInParent<Transform>();
        if (!root || !root.CompareTag(playerTag)) return;

        // New health system: route via PlayerController3D
        var pc = hit.GetComponentInParent<PlayerController3D>();
        if (pc) pc.ApplyDamage(contactDamage);
    }

    // ----------------- ENEMY RECEIVING WEAPON HITS -----------------
    bool TryApplyWeaponHit(Collider col)
    {
        // Optional layer gate
        if (weaponLayers.value != 0 && ((1 << col.gameObject.layer) & weaponLayers.value) == 0)
        {
            if (preferWeaponHitbox && !col.GetComponentInParent<WeaponHitbox>()) return false;
        }

        // Optional tag gate
        if (!string.IsNullOrEmpty(weaponTag) && !col.CompareTag(weaponTag))
        {
            if (preferWeaponHitbox && !col.GetComponentInParent<WeaponHitbox>()) return false;
        }

        float dmg = defaultWeaponDamage;

        WeaponHitbox hb = preferWeaponHitbox ? col.GetComponentInParent<WeaponHitbox>() : null;
        if (hb)
        {
            // Ensure the owner is the player (so enemy weapons don't hurt enemies)
            if (!string.IsNullOrEmpty(playerTag) && hb.owner && !hb.owner.CompareTag(playerTag)) return false;

            // Deduplicate per-swing
            if (hb.hitId != 0)
            {
                if (_processedHitIds.Contains(hb.hitId)) return false;
                _processedHitIds.Add(hb.hitId);
            }

            dmg = hb.damage > 0f ? hb.damage : defaultWeaponDamage;
        }
        else
        {
            // Collider-based small cooldown if no hitId present
            if (_cooldowns.TryGetValue(col, out float last))
            {
                if (Time.time - last < sameColliderHitCooldown) return false;
            }
            _cooldowns[col] = Time.time;
        }

        TakeDamage(dmg);
        return true;
    }

    // ----------------- ANIMATIONS -----------------
    System.Collections.IEnumerator HitAnim()
    {
        float t = 0f;
        while (t < hitDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, hitDuration));
            float e = hitCurve.Evaluate(u);

            float s = Mathf.Lerp(1f, hitScale, e);
            animTarget.localScale = _baseScale * s;

            Vector2 offs = Random.insideUnitCircle * hitShake * (1f - u);
            animTarget.localPosition = _baseLocalPos + (Vector3)offs;

            yield return null;
        }

        animTarget.localPosition = _baseLocalPos;
        animTarget.localScale = _baseScale;
        _hitCo = null;
    }

    System.Collections.IEnumerator DeathSequence()
    {
        _isDying = true;

        // === Custom boss death path ===
        if (customDeathHandled)
        {
            if (disableCollidersOnCustomDeath)
            {
                var cols = GetComponentsInChildren<Collider>();
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            }

            SendMessage("OnEnemyBaseDeath", this, SendMessageOptions.DontRequireReceiver);
            yield break;
        }

        // === Normal enemies keep their VFX and die ===
        if (animateOnDeath)
        {
            float t = 0f;
            while (t < deathDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, deathDuration));
                float e = deathCurve.Evaluate(u);

                float s = Mathf.Lerp(1f, deathScale, e);
                animTarget.localScale = _baseScale * s;

                animTarget.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, deathSpinDegrees, e));
                yield return null;
            }
        }

        if (spawnShadowPuff) SpawnShadowPuff();
        if (spawnHomingSouls) SpawnHomingSouls();

        OnEnemyKilled?.Invoke(this);

        yield return null;

        animTarget.localScale = _baseScale;
        animTarget.localRotation = _baseRot;
        animTarget.localPosition = _baseLocalPos;

        Destroy(gameObject);
    }

    // ----------------- SHADOW PUFF -----------------
    void SpawnShadowPuff()
    {
        var go = new GameObject("EnemyDeath_ShadowPuff");
        go.transform.position = transform.position;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.duration = puffDuration;
        main.startLifetime = puffStartLifetime;
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
            new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) }
        );
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        var curve = new AnimationCurve(
            new Keyframe(0f, 0.9f),
            new Keyframe(0.2f, 1.15f),
            new Keyframe(1f, 0.0f)
        );
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.material = sParticleMat;
        if (!string.IsNullOrEmpty(puffSortingLayer)) pr.sortingLayerName = puffSortingLayer;

        ps.Play();
        Destroy(go, puffDuration + puffStartLifetime + extraDespawnTime);
    }

    // ----------------- HOMING SOULS -----------------
    void SpawnHomingSouls()
    {
        if (!player) return;

        for (int i = 0; i < soulCount; i++)
        {
            var orb = new GameObject("SoulOrb");
            orb.transform.position = transform.position + (Vector3)(Random.insideUnitCircle * soulStartSpread);

            var sr = orb.AddComponent<SpriteRenderer>();
            sr.sprite = sCircleSprite;
            sr.color = soulColor;
            if (!string.IsNullOrEmpty(soulSortingLayer)) sr.sortingLayerName = soulSortingLayer;

            var mover = orb.AddComponent<SoulMover>();
            mover.target = player;
            mover.lifetime = soulLifetime;
            mover.arcJitter = soulArcJitter;
            mover.startSize = soulStartSize;
            mover.endSize = soulEndSize;
        }
    }

    private class SoulMover : MonoBehaviour
    {
        public Transform target;
        public float lifetime = 1.4f;
        public float arcJitter = 0.6f;
        public float startSize = 0.18f;
        public float endSize = 0.06f;

        Vector3 start, arc;
        float t;

        void Start()
        {
            start = transform.position;
            arc = Random.insideUnitCircle.normalized * arcJitter;
            transform.localScale = Vector3.one * startSize;
        }

        void Update()
        {
            if (!target) { Destroy(gameObject); return; }

            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, lifetime));
            float e = u * u * (3f - 2f * u);

            Vector3 end = target.position;
            Vector3 mid = Vector3.Lerp(start, end, 0.5f) + arc;
            Vector3 p1 = Vector3.Lerp(start, mid, e);
            Vector3 p2 = Vector3.Lerp(mid, end, e);
            transform.position = Vector3.Lerp(p1, p2, e);

            float s = Mathf.Lerp(startSize, endSize, e);
            transform.localScale = new Vector3(s, s, 1f);

            if (u >= 1f) Destroy(gameObject);
        }
    }

    // ----------------- Utility -----------------
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
}
