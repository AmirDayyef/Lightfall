using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;   // <-- needed for Dictionary

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController3D : MonoBehaviour
{

    [Header("Light / Health")]
    public float maxLight = 100f;
    public float currentLight = 100f;
    public bool invulnerable = false;

    [Header("Movement")]
    public float moveSpeed = 7f;
    public float airControlMult = 0.9f;
    public float jumpForce = 13f;

    [Header("Jump Assist")]
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;

    [Header("Grounding")]
    public LayerMask groundMask;
    public Transform groundCheck;
    public float groundCheckRadius = 0.2f;
    public float groundSkin = 0.07f;

    [Header("Visual / Animator")]
    [SerializeField] Animator anim;
    public Transform visual;

    [Header("Facing Lerp")]
    public float rightYaw = 90f;
    public float leftYaw = 270f;
    public bool onlyLerpWhenMoving = true;
    public float yawLerpSpeedDegPerSec = 720f;

    [Header("Anim Triggers")]
    public string jumpTrigger = "Jump";

    [Header("Axis Lock")]
    public bool freezeZAxis = true;

    // ------------ Blocking -------------
    [Header("Blocking")]
    public KeyCode blockKey = KeyCode.Mouse1;
    [Range(0f, 1f)] public float blockDamageMultiplier = 0.5f;
    public string blockBoolParam = "Blocking";
    public string blockStartTrigger = "BlockStart";
    public string blockEndTrigger = "BlockEnd";
    public float blockMoveMult = 0.7f;
    public bool isBlocking { get; private set; }
    // -----------------------------------

    // ------------ Life / Light coupling -------------
    [Header("Life / Light Coupling")]
    public float maxHP = 100f;
    public float currentHP = 100f;

    public Light playerLight;
    public float lightIntensityAtFull = 600f;
    public float lightIntensityAtZero = 60f;
    public float lightRangeAtFull = 12f;
    public float lightRangeAtZero = 6f;

    [Header("Screen/UI Dim")]
    public CanvasGroup screenDimGroup;
    [Range(0f, 1f)] public float uiMaxDimAtZeroHP = 0.55f;
    [Range(0f, 1f)] public float uiExtraHitDim = 0.25f;

    [Header("Adaptive Damage (lower when hurt)")]
    public float incomingDamageMultAtFull = 1.0f;
    public float incomingDamageMultAtZero = 0.6f;

    [Header("Post-Hit Mitigation & Slow")]
    [Range(0f, 1f)] public float postHitDamageMultiplier = 0.6f;
    public float hitSlowDuration = 0.50f;
    public float hitSlowMoveMult = 0.65f;

    // --------- EWeapon damage detection ----------
    [Header("EWeapon Damage Detection")]
    public string enemyWeaponTag = "EWeapon";
    public float defaultWeaponDamage = 10f;
    public float weaponRepeatWindow = 0.20f;

    public bool autoCreateHurtboxTrigger = true;
    public Vector3 hurtboxLocalCenter = new Vector3(0f, 1.0f, 0f);
    public Vector3 hurtboxSize = new Vector3(0.7f, 1.8f, 0.5f);
    // ------------------------------------------------

    // --------- Death / Outro ---------
    [Header("Death / Outro")]
    public bool autoFullBlackOnDeath = true;
    public float deathFullBlackDuration = 1.2f;

    public Image endOverlayImage;
    public float endImageFadeDuration = 0.8f;
    public float restartDelayAfterImage = 1.0f;

    public string restartSceneName = "";
    [Range(0.8f, 1.0f)] public float fullBlackAlphaThreshold = 0.98f;
    // ---------------------------------

    Rigidbody rb;
    CapsuleCollider capsule;
    float lastGroundedTime, lastJumpPressedTime;
    bool jumpHeld;
    float inX;

    bool externalPlanarAuthority = false;
    Vector3 externalPlanarVel;

    float targetYaw;
    float hitEffectEndTime = -999f;

    // ✅ Correctly declared dictionary (this was your line ~123)
    private readonly Dictionary<int, float> _lastHitTime = new Dictionary<int, float>();

    // outro state
    bool _deathSequenceStarted = false;
    bool _lockPresentation = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        if (freezeZAxis) rb.constraints |= RigidbodyConstraints.FreezePositionZ;

        capsule = GetComponent<CapsuleCollider>();

        if (!anim) anim = GetComponentInChildren<Animator>(true);
        if (anim) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }

        var t = visual ? visual : transform;
        targetYaw = t.eulerAngles.y;

        currentHP = Mathf.Clamp(currentHP, 0f, maxHP);
        ApplyPresentationFromHP(0f, forceApply: true);

        if (autoCreateHurtboxTrigger) CreateOrUpdateHurtbox();

        if (endOverlayImage)
        {
            var c = endOverlayImage.color;
            c.a = 0f;
            endOverlayImage.color = c;
            if (!endOverlayImage.gameObject.activeSelf) endOverlayImage.gameObject.SetActive(true);
        }
    }

    void CreateOrUpdateHurtbox()
    {
        const string HB_NAME = "Hurtbox";
        Transform hb = transform.Find(HB_NAME);
        if (!hb)
        {
            hb = new GameObject(HB_NAME).transform;
            hb.SetParent(transform, false);
        }
        hb.localPosition = hurtboxLocalCenter;
        hb.localRotation = Quaternion.identity;

        var box = hb.GetComponent<BoxCollider>();
        if (!box) box = hb.gameObject.AddComponent<BoxCollider>();
        box.size = hurtboxSize;
        box.center = Vector3.zero;
        box.isTrigger = true;

        var fw = hb.GetComponent<ChildTriggerForwarder>();
        if (!fw) fw = hb.gameObject.AddComponent<ChildTriggerForwarder>();
        fw.owner = this;
    }

    void Update()
    {
        // Input
        inX = Input.GetAxisRaw("Horizontal");
        if (Input.GetButtonDown("Jump")) lastJumpPressedTime = Time.time;
        jumpHeld = Input.GetButton("Jump");

        // Blocking
        bool wasBlocking = isBlocking;
        isBlocking = Input.GetKey(blockKey);
        if (anim)
        {
            if (!string.IsNullOrEmpty(blockBoolParam)) anim.SetBool(blockBoolParam, isBlocking);
            if (!wasBlocking && isBlocking && !string.IsNullOrEmpty(blockStartTrigger)) { anim.ResetTrigger(blockStartTrigger); anim.SetTrigger(blockStartTrigger); }
            else if (wasBlocking && !isBlocking && !string.IsNullOrEmpty(blockEndTrigger)) { anim.ResetTrigger(blockEndTrigger); anim.SetTrigger(blockEndTrigger); }
        }

        // Movement with HP/block/hit scaling
        float hpFrac = (maxHP <= 0.0001f) ? 0f : (currentHP / maxHP);
        float healthMoveScale = Mathf.Lerp(0.6f, 1.0f, hpFrac);
        float hitMoveScale = (Time.time < hitEffectEndTime) ? Mathf.Clamp01(hitSlowMoveMult) : 1f;
        float blockMoveScale = isBlocking ? Mathf.Clamp01(blockMoveMult) : 1f;
        float effectiveMoveSpeed = moveSpeed * healthMoveScale * hitMoveScale * blockMoveScale;

        if (!externalPlanarAuthority)
        {
            Vector3 planarVel = new Vector3(rb.linearVelocity.x, 0f, 0f);
            Vector3 inputDir = new Vector3(inX, 0f, 0f).normalized;
            float accel = IsGrounded() ? 1f : Mathf.Clamp01(airControlMult);
            Vector3 targetPlanar = inputDir * effectiveMoveSpeed;
            Vector3 newPlanar = Vector3.Lerp(planarVel, targetPlanar, accel);
            rb.linearVelocity = new Vector3(newPlanar.x, rb.linearVelocity.y, 0f);
        }
        else
        {
            rb.linearVelocity = new Vector3(externalPlanarVel.x, rb.linearVelocity.y, 0f);
        }

        // Jump
        if (CanJump()) DoJump();

        // Facing
        if (!onlyLerpWhenMoving || Mathf.Abs(inX) > 0.01f)
        {
            if (inX > 0.01f) targetYaw = rightYaw;
            else if (inX < -0.01f) targetYaw = leftYaw;
        }
        LerpYawTowardsTarget();

        // Animator
        if (anim)
        {
            float speedAbs = Mathf.Abs(rb.linearVelocity.x);
            anim.SetFloat("Speed", speedAbs);
            anim.SetFloat("YVel", rb.linearVelocity.y);
            anim.SetBool("Grounded", IsGrounded());
            anim.SetFloat("HPFrac", hpFrac);
        }

        // HP presentation
        if (!_lockPresentation)
            ApplyPresentationFromHP(Time.deltaTime, forceApply: false);

        // Death/outro triggers
        if (autoFullBlackOnDeath && !_deathSequenceStarted && currentHP <= 0f)
        {
            _deathSequenceStarted = true;
            StartCoroutine(DeathOutroRoutine());
        }
        if (!_deathSequenceStarted && screenDimGroup && screenDimGroup.alpha >= fullBlackAlphaThreshold)
        {
            _deathSequenceStarted = true;
            StartCoroutine(DeathOutroRoutine(skipToFullBlack: true));
        }
    }

    System.Collections.IEnumerator DeathOutroRoutine(bool skipToFullBlack = false)
    {
        _lockPresentation = true;

        // 1) ramp to full black
        if (screenDimGroup)
        {
            float startA = screenDimGroup.alpha;
            float t = 0f;
            float dur = Mathf.Max(0.01f, deathFullBlackDuration);
            if (skipToFullBlack) t = dur;

            while (t < dur)
            {
                t += Time.deltaTime;
                screenDimGroup.alpha = Mathf.Lerp(startA, 1f, t / dur);
                yield return null;
            }
            screenDimGroup.alpha = 1f;
        }

        // 2) fade in overlay image
        if (endOverlayImage)
        {
            var c = endOverlayImage.color;
            float start = c.a;
            float t = 0f;
            float dur = Mathf.Max(0.01f, endImageFadeDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                c.a = Mathf.Lerp(start, 1f, t / dur);
                endOverlayImage.color = c;
                yield return null;
            }
            c.a = 1f; endOverlayImage.color = c;
        }

        // 3) wait then restart
        if (restartDelayAfterImage > 0f)
            yield return new WaitForSeconds(restartDelayAfterImage);

        if (!string.IsNullOrEmpty(restartSceneName))
            SceneManager.LoadScene(restartSceneName);
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    void LerpYawTowardsTarget()
    {
        Transform t = visual ? visual : transform;
        float current = t.eulerAngles.y;
        float next = Mathf.MoveTowardsAngle(current, targetYaw, yawLerpSpeedDegPerSec * Time.deltaTime);
        t.rotation = Quaternion.Euler(0f, next, 0f);
    }

    bool CanJump()
    {
        return (Time.time - lastJumpPressedTime <= jumpBufferTime) &&
               (Time.time - lastGroundedTime <= coyoteTime);
    }

    void DoJump()
    {
        lastJumpPressedTime = -999f;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, 0f);
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        if (anim && !string.IsNullOrEmpty(jumpTrigger)) { anim.ResetTrigger(jumpTrigger); anim.SetTrigger(jumpTrigger); }
    }

    bool IsGrounded()
    {
        if (groundCheck &&
            Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore))
        {
            lastGroundedTime = Time.time;
            return true;
        }

        float top = (capsule.height * 0.5f) - capsule.radius;
        Vector3 p1 = transform.position + Vector3.up * (capsule.radius + 0.01f);
        Vector3 p2 = transform.position + Vector3.up * (Mathf.Max(top, 0f) + 0.01f);

        if (Physics.CapsuleCast(p1, p2, capsule.radius * 0.95f, Vector3.down,
                                out RaycastHit _, groundSkin,
                                groundMask, QueryTriggerInteraction.Ignore))
        {
            lastGroundedTime = Time.time;
            return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (groundCheck)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }
#if UNITY_EDITOR
        if (autoCreateHurtboxTrigger)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
            Matrix4x4 m = Matrix4x4.TRS(transform.position + transform.rotation * hurtboxLocalCenter, transform.rotation, Vector3.one);
            Gizmos.matrix = m;
            Gizmos.DrawWireCube(Vector3.zero, hurtboxSize);
        }
#endif
    }

    // ----------------- PUBLIC ATTACK HOOKS -----------------
    public void AttackMotionBegin(float forwardSpeed)
    {
        externalPlanarAuthority = true;
        externalPlanarVel = new Vector3(transform.right.x * forwardSpeed, 0f, 0f);
    }
    public void AttackMotionSet(float forwardSpeed)
    {
        if (!externalPlanarAuthority) return;
        externalPlanarVel = new Vector3(transform.right.x * forwardSpeed, 0f, 0f);
    }
    public void AttackMotionEnd() { externalPlanarAuthority = false; }
    // -------------------------------------------------------

    // ----------------- DAMAGE API ------------------
    public float ApplyDamage(float rawDamage)
    {
        if (invulnerable) return 0f;
        if (rawDamage <= 0f || currentHP <= 0f) return 0f;

        float dmg = isBlocking ? rawDamage * Mathf.Clamp01(blockDamageMultiplier) : rawDamage;

        float hpFrac = (maxHP <= 0.0001f) ? 0f : (currentHP / maxHP);
        float adaptive = Mathf.Lerp(incomingDamageMultAtZero, incomingDamageMultAtFull, hpFrac);
        dmg *= Mathf.Clamp(adaptive, 0f, 10f);

        if (Time.time < hitEffectEndTime) dmg *= Mathf.Clamp01(postHitDamageMultiplier);

        dmg = Mathf.Max(dmg, 0f);
        currentHP = Mathf.Clamp(currentHP - dmg, 0f, maxHP);

        hitEffectEndTime = Time.time + hitSlowDuration;

        if (!_lockPresentation)
            ApplyPresentationFromHP(0f, forceApply: true);

        return dmg;
    }

    public float ModifyDamage(float rawDamage)
    {
        if (rawDamage <= 0f) return 0f;
        float dmg = rawDamage;
        if (isBlocking) dmg *= Mathf.Clamp01(blockDamageMultiplier);
        float hpFrac = (maxHP <= 0.0001f) ? 0f : (currentHP / maxHP);
        float adaptive = Mathf.Lerp(incomingDamageMultAtZero, incomingDamageMultAtFull, hpFrac);
        dmg *= Mathf.Clamp(adaptive, 0f, 10f);
        if (Time.time < hitEffectEndTime) dmg *= Mathf.Clamp01(postHitDamageMultiplier);
        return Mathf.Max(dmg, 0f);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || currentHP <= 0f) return;
        currentHP = Mathf.Clamp(currentHP + amount, 0f, maxHP);
        if (!_lockPresentation)
            ApplyPresentationFromHP(0f, forceApply: true);
    }

    void ApplyPresentationFromHP(float dt, bool forceApply)
    {
        if (!screenDimGroup && !playerLight) return;

        float hpFrac = (maxHP <= 0.0001f) ? 0f : (currentHP / maxHP);

        // Base dim from HP
        float baseDim = Mathf.Lerp(uiMaxDimAtZeroHP, 0f, hpFrac);

        // Extra temp dim when recently hit
        float extra = 0f;
        if (Time.time < hitEffectEndTime)
        {
            float t = 1f - Mathf.InverseLerp(Time.time - hitSlowDuration, hitEffectEndTime, Time.time);
            extra = uiExtraHitDim * Mathf.Clamp01(t);
        }

        // UI dim
        if (screenDimGroup && !_lockPresentation)
        {
            float targetAlpha = Mathf.Clamp01(baseDim + extra);
            if (forceApply) screenDimGroup.alpha = targetAlpha;
            else screenDimGroup.alpha = Mathf.MoveTowards(screenDimGroup.alpha, targetAlpha, dt * 3.5f);
        }

        // Player light
        if (playerLight)
        {
            float targetIntensity = Mathf.Lerp(lightIntensityAtZero, lightIntensityAtFull, hpFrac);
            float addHitDip = (extra > 0f) ? Mathf.Lerp(0f, targetIntensity * 0.25f, extra / Mathf.Max(uiExtraHitDim, 0.0001f)) : 0f;
            float finalIntensity = Mathf.Max(0f, targetIntensity - addHitDip);

            if (forceApply || _lockPresentation) playerLight.intensity = finalIntensity;
            else playerLight.intensity = Mathf.MoveTowards(playerLight.intensity, finalIntensity, dt * 400f);

            float targetRange = Mathf.Lerp(lightRangeAtZero, lightRangeAtFull, hpFrac);
            if (lightRangeAtFull > 0f || lightRangeAtZero > 0f)
            {
                if (forceApply || _lockPresentation) playerLight.range = targetRange;
                else playerLight.range = Mathf.MoveTowards(playerLight.range, targetRange, dt * 20f);
            }
        }
    }
    // ------------------------------------------------

    // --------------- EWeapon detection hooks ---------------
    void OnCollisionEnter(Collision c)
    {
        if (c.collider) TryHitFromCollider(c.collider);
    }

    internal void __OnChildTriggerEnter(Collider other) { TryHitFromCollider(other); }
    internal void __OnChildTriggerStay(Collider other) { TryHitFromCollider(other); }

    void TryHitFromCollider(Collider other)
    {
        if (!other || !other.gameObject.activeInHierarchy) return;
        if (!other.CompareTag(enemyWeaponTag)) return;
        if (currentHP <= 0f) return;

        int id = other.GetInstanceID();
        float now = Time.time;
        if (_lastHitTime.TryGetValue(id, out float last) && (now - last) < weaponRepeatWindow) return;
        _lastHitTime[id] = now;

        ApplyDamage(defaultWeaponDamage);
    }


    // Called by pickups (e.g., light orbs) and healing effects
    public void AddLight(float amount)
    {
        if (amount <= 0f) return;
        currentLight = Mathf.Clamp(currentLight + amount, 0f, maxLight);

        // (optional) brighten visuals / UI here
        // UpdateLightVisuals();
    }

    public void SetInvulnerable(bool value) { invulnerable = value; }
}

/// <summary>
/// Lives in the SAME file. Attached to the auto-created "Hurtbox" child.
/// Forwards trigger events back to the PlayerController3D owner.
/// </summary>
public class ChildTriggerForwarder : MonoBehaviour
{
    public PlayerController3D owner;
    void OnTriggerEnter(Collider other) { if (owner) owner.__OnChildTriggerEnter(other); }
    void OnTriggerStay(Collider other) { if (owner) owner.__OnChildTriggerStay(other); }
}
