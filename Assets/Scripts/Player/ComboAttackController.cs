using UnityEngine;

/// Zero-asset combo system with per-attack forward motion that applies
/// only during the attack animation, and stops immediately (or damped) on exit.
/// Now also toggles per-attack weapon hitboxes (multi-collider) during specific windows.
[DisallowMultipleComponent]
public class ComboAttackControllerSimple : MonoBehaviour
{
    [HideInInspector] public bool frozen = false;

    public enum Btn { Light, Heavy }
    public enum ApplyPlanarMode { ReplacePlanar, AddAcceleration }

    [System.Serializable]
    public struct AttackHitbox
    {
        [Tooltip("Collider to toggle for this attack (e.g., Mesh/Box/Sphere).")]
        public Collider collider;
        [Tooltip("Start of active window (normalized 0..1 within the state).")]
        [Range(0f, 1f)] public float enableStart;
        [Tooltip("End of active window (normalized 0..1 within the state).")]
        [Range(0f, 1f)] public float enableEnd;
        [Tooltip("Damage to assign to the WeaponHitbox while active.")]
        public float damage;
        [Tooltip("Force isTrigger=true while active.")]
        public bool forceTrigger;
    }

    [System.Serializable]
    public struct Attack
    {
        [Header("Animator")]
        [Tooltip("Animator state name (Layer 0) for this attack.")]
        public string stateName;
        [Tooltip("Crossfade time when entering this state.")]
        public float crossFadeTime;

        [Header("Combo Window (normalized 0..1)")]
        [Range(0f, 1f)] public float comboOpen;
        [Range(0f, 1f)] public float comboClose;
        [Range(0f, 1f)] public float earliestExit;

        [Header("Next Indices (-1 = none)")]
        public int nextOnLight;
        public int nextOnHeavy;

        [Header("One-shot Impulse (on enter)")]
        public float impulseForward;
        public float impulseUp;

        [Header("Forward Motion Curve (applies ONLY while state is active)")]
        public bool useForwardCurve;
        public AnimationCurve forwardCurve;   // evaluate 0..1
        public float forwardScale;            // units/sec multiplier
        public ApplyPlanarMode applyMode;     // Replace XZ velocity or add acceleration
        public bool preserveYVelocity;        // keep Y when replacing planar vel

        [Header("Per-attack Weapon Hitboxes")]
        public AttackHitbox[] hitboxes;

        // runtime cache
        [System.NonSerialized] public int hash;
    }

    [Header("Refs")]
    public Animator animator;
    public Rigidbody rb; // recommended for physics consistency
    [Tooltip("Who owns the weapon colliders (usually player root). Pushed into WeaponHitbox.owner.")]
    public Transform weaponOwner;

    [Header("Attacks (inline, no assets)")]
    public Attack[] attacks;

    [Header("Starters (indices into 'attacks')")]
    public int lightStartIndex = 0;
    public int heavyStartIndex = 1;

    [Header("Inputs & Flow")]
    public string lightInput = "Fire1";
    public string heavyInput = "Fire2";
    public float inputBufferTime = 0.2f;  // seconds
    public float idleReturnGrace = 0.2f;  // normalized overflow after 1.0 -> idle

    [Header("Exit control")]
    [Tooltip("If true, zero/damp XZ velocity the instant we leave the attack state.")]
    public bool stopPlanarOnExit = true;
    [Tooltip("0 = hard stop; >0 = smooth planar damp time (seconds) after exit.")]
    public float exitDampTime = 0f;

    [Header("Hitbox management")]
    [Tooltip("Add WeaponHitbox on hitbox objects if missing.")]
    public bool autoAddWeaponHitbox = true;
    [Tooltip("Auto-set isTrigger true while active (if enabled in AttackHitbox).")]
    public bool respectForceTrigger = true;

    // --- state ---
    int currentIndex = -1;
    int queuedIndex = -1;

    Btn? bufferedBtn = null;
    float bufferedAt = -999f;

    // Track the currently-driven state's hash so motion & hitboxes only apply while it's active
    int activeAttackHash = 0;
    bool wasInAttack = false;
    Vector3 planarVelDamp;  // SmoothDamp velocity for exit damping

    // per-swing ID shared by all hitboxes for the current attack
    int currentSwingHitId = 0;

    const int BaseLayer = 0;

    void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        if (!weaponOwner) weaponOwner = transform;
    }

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>(true);
        if (!weaponOwner) weaponOwner = transform;

        // cache hashes
        for (int i = 0; i < attacks.Length; i++)
            attacks[i].hash = string.IsNullOrEmpty(attacks[i].stateName)
                ? 0 : Animator.StringToHash(attacks[i].stateName);

        // ensure all hitboxes start disabled
        DisableAllHitboxes();
    }

    void OnDisable()
    {
        DisableAllHitboxes();
    }

    void Update()
    {
        if (frozen)
        {
            // make sure nothing progresses / no inputs are consumed
            DisableAllHitboxes();
            return;
        }

        // ---- inputs + buffer ----
        if (Input.GetButtonDown(lightInput)) Buffer(Btn.Light);
        if (Input.GetButtonDown(heavyInput)) Buffer(Btn.Heavy);

        var st = animator.GetCurrentAnimatorStateInfo(BaseLayer);
        bool inAttack = currentIndex >= 0 && currentIndex < attacks.Length &&
                        st.shortNameHash == attacks[currentIndex].hash;

        // ---- If idle/not in current attack: try start from buffer ----
        if (!inAttack)
        {
            // make sure no hitboxes are leaking
            if (wasInAttack) DisableAllHitboxes();

            if (HasBuffered())
            {
                int start = bufferedBtn == Btn.Light ? lightStartIndex : heavyStartIndex;
                TryPlay(start);
                ClearBuffer();
            }
            return;
        }

        // ---- Within an attack: handle combo window ----
        var cur = attacks[currentIndex];
        float nt = st.normalizedTime;

        // toggle hitboxes for this attack based on nt%1
        ToggleHitboxesFor(cur, nt);

        bool windowOpen = nt >= cur.comboOpen && nt <= cur.comboClose;

        if (HasBuffered() && windowOpen)
        {
            int next = NextIndex(currentIndex, bufferedBtn.Value);
            if (IsValidIndex(next))
            {
                queuedIndex = next;
                ClearBuffer();
            }
        }

        // ---- Transition when allowed ----
        if (IsValidIndex(queuedIndex) && nt >= cur.earliestExit)
        {
            TryPlay(queuedIndex);
            queuedIndex = -1;
            return;
        }

        // ---- Auto-finish when clip ends ----
        if (nt > 1f + idleReturnGrace)
        {
            currentIndex = -1;
            queuedIndex = -1;
            DisableAllHitboxes();
        }
    }

    void FixedUpdate()
    {
        if (frozen) return;
        // Apply forward curve ONLY while the active attack state is playing
        var st = animator ? animator.GetCurrentAnimatorStateInfo(BaseLayer) : default;

        bool inThisAttack =
            animator && activeAttackHash != 0 && st.shortNameHash == activeAttackHash;

        if (!inThisAttack)
        {
            // We just left the attack state ? stop/damp planar velocity + hard disable hitboxes
            if (wasInAttack)
            {
                if (stopPlanarOnExit && rb)
                {
                    if (exitDampTime <= 0f)
                    {
                        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                    }
                    else
                    {
                        Vector3 planar = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                        planar = Vector3.SmoothDamp(
                            planar, Vector3.zero, ref planarVelDamp,
                            exitDampTime, Mathf.Infinity, Time.fixedDeltaTime);
                        rb.linearVelocity = new Vector3(planar.x, rb.linearVelocity.y, planar.z);
                    }
                }
                DisableAllHitboxes();
            }
            wasInAttack = false;
            return;
        }

        wasInAttack = true;

        if (currentIndex < 0 || currentIndex >= attacks.Length) return;

        var cur = attacks[currentIndex];
        if (!cur.useForwardCurve || cur.forwardCurve == null || cur.forwardCurve.length == 0) return;

        float nt = st.normalizedTime % 1f; if (nt < 0f) nt += 1f;
        float k = Mathf.Max(0f, cur.forwardCurve.Evaluate(nt)); // 0..?
        float speed = k * cur.forwardScale;                      // units/sec

        Vector3 desiredPlanar = transform.forward * speed;

        if (rb)
        {
            if (cur.applyMode == ApplyPlanarMode.ReplacePlanar)
            {
                float y = cur.preserveYVelocity ? rb.linearVelocity.y : 0f;
                rb.linearVelocity = new Vector3(desiredPlanar.x, y, desiredPlanar.z);
            }
            else // AddAcceleration
            {
                rb.AddForce(desiredPlanar, ForceMode.Acceleration);
            }
        }
        else
        {
            // Fallback (no RB)
            transform.position += desiredPlanar * Time.fixedDeltaTime;
        }
    }

    // -------- helpers --------
    void Buffer(Btn btn) { bufferedBtn = btn; bufferedAt = Time.time; }
    bool HasBuffered() => bufferedBtn.HasValue && (Time.time - bufferedAt) <= inputBufferTime;
    void ClearBuffer() { bufferedBtn = null; bufferedAt = -999f; }

    bool TryPlay(int index)
    {
        if (!IsValidIndex(index)) return false;
        var a = attacks[index];
        if (a.hash == 0) return false;

        animator.CrossFade(a.hash, a.crossFadeTime, BaseLayer);
        currentIndex = index;

        // mark which state we will drive motion/hitboxes for
        activeAttackHash = a.hash;
        wasInAttack = true; // entering now

        // New swing ID for de-dup across all weapon colliders this attack
        currentSwingHitId = Mathf.Abs(System.Environment.TickCount ^ GetHashCode() ^ a.hash);

        // One-time impulse on enter
        if (rb && (a.impulseForward != 0f || a.impulseUp != 0f))
        {
            Vector3 fwd = transform.forward;
            rb.AddForce(fwd * a.impulseForward + Vector3.up * a.impulseUp, ForceMode.VelocityChange);
        }

        // Ensure all hitboxes start disabled at state entry
        DisableAllHitboxes();
        // also prime WeaponHitbox components (owner, damage, hitId)
        PrimeHitboxesFor(a);

        return true;
    }

    void PrimeHitboxesFor(Attack a)
    {
        if (a.hitboxes == null) return;
        for (int i = 0; i < a.hitboxes.Length; i++)
        {
            var hbEntry = a.hitboxes[i];
            if (!hbEntry.collider) continue;

            var wh = hbEntry.collider.GetComponent<WeaponHitbox>();
            if (!wh && autoAddWeaponHitbox) wh = hbEntry.collider.gameObject.AddComponent<WeaponHitbox>();
            if (!wh) continue;

            wh.owner = weaponOwner ? weaponOwner : transform;
            wh.damage = hbEntry.damage > 0f ? hbEntry.damage : wh.damage;
            wh.hitId = currentSwingHitId;
        }
    }

    void ToggleHitboxesFor(Attack a, float normalizedTimeRaw)
    {
        if (a.hitboxes == null || a.hitboxes.Length == 0) return;

        float nt = normalizedTimeRaw % 1f; if (nt < 0f) nt += 1f;

        for (int i = 0; i < a.hitboxes.Length; i++)
        {
            var hb = a.hitboxes[i];
            if (!hb.collider) continue;

            bool shouldEnable = nt >= Mathf.Min(hb.enableStart, hb.enableEnd)
                             && nt <= Mathf.Max(hb.enableStart, hb.enableEnd);

            if (respectForceTrigger && hb.forceTrigger && hb.collider is Collider col)
            {
                // set isTrigger when active, restore when inactive
                // (Unity has various collider subclasses; they all have isTrigger)
                col.isTrigger = shouldEnable ? true : col.isTrigger;
            }

            if (hb.collider.enabled != shouldEnable)
            {
                hb.collider.enabled = shouldEnable;

                // When enabling, refresh WeaponHitbox.hitId so multi-hitboxes share the same swing ID
                if (shouldEnable)
                {
                    var wh = hb.collider.GetComponent<WeaponHitbox>();
                    if (!wh && autoAddWeaponHitbox) wh = hb.collider.gameObject.AddComponent<WeaponHitbox>();
                    if (wh)
                    {
                        wh.owner = weaponOwner ? weaponOwner : transform;
                        if (hb.damage > 0f) wh.damage = hb.damage;
                        wh.hitId = currentSwingHitId;
                    }
                }
            }
        }
    }

    void DisableAllHitboxes()
    {
        if (attacks == null) return;
        for (int ai = 0; ai < attacks.Length; ai++)
        {
            var arr = attacks[ai].hitboxes;
            if (arr == null) continue;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].collider) arr[i].collider.enabled = false;
            }
        }
    }
    public void SetFrozen(bool on)
    {
        if (frozen == on) return;
        frozen = on;

        if (on)
        {
            DisableAllHitboxes();     // ensure no stray damage
                                      // Optionally stop planar on freeze
            if (rb && stopPlanarOnExit)
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        }
    }

    bool IsValidIndex(int i) => i >= 0 && i < attacks.Length;
    int NextIndex(int from, Btn btn) => !IsValidIndex(from) ? -1 :
        (btn == Btn.Light ? attacks[from].nextOnLight : attacks[from].nextOnHeavy);
}
