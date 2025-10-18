using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class AttackMotionAuthority : MonoBehaviour
{
    [Header("Refs")]
    public Animator anim;
    public MonoBehaviour normalMover;   // your movement script to disable
    public NavMeshAgent agent;          // optional: disable during attack
    public CharacterController cc;      // optional: disable during attack

    [Header("When to move (Animator state)")]
    public string attackStateName = "SpinAttack"; // exact state name (layer 0)
    public float startNorm = 0.05f;   // move only within this window
    public float endNorm = 0.95f;

    [Header("Motion")]
    public float forwardSpeed = 6f;   // constant forward speed while active

    Rigidbody rb;
    int attackHash;
    bool inAttack;
    bool stateMode; // use animator state detection vs manual events

    void Awake()
    {
        if (!anim) anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        attackHash = Animator.StringToHash(attackStateName);

        // if mover/agents not assigned, try auto-find
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!cc) cc = GetComponent<CharacterController>();
    }

    // --- Use this if you prefer Animation Events ---
    public void AttackStart() { BeginAuthority(); inAttack = true; stateMode = false; }
    public void AttackEnd() { EndAuthority(); inAttack = false; }

    void OnEnable() { stateMode = true; } // default: state-driven
    void OnDisable() { if (inAttack) EndAuthority(); }

    void FixedUpdate()
    {
        // State-driven activation (no events needed)
        if (stateMode && anim)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            bool isThisState = st.shortNameHash == attackHash;
            bool window = isThisState && st.normalizedTime >= startNorm && st.normalizedTime <= endNorm;

            if (window && !inAttack) BeginAuthority();
            else if (!isThisState && inAttack) EndAuthority();

            inAttack = window;
        }

        if (!inAttack) return;

        // Move forward via RB so position is REAL (no snap back)
        Vector3 v = rb.linearVelocity;
        Vector3 f = transform.forward * forwardSpeed;
        rb.linearVelocity = new Vector3(f.x, v.y, f.z); // keep gravity on Y
    }

    void BeginAuthority()
    {
        // Disable anything that writes to position
        if (normalMover) normalMover.enabled = false;
        if (agent && agent.enabled) agent.enabled = false;
        if (cc && cc.enabled) cc.enabled = false;
        // IMPORTANT: do NOT set transform.position anywhere here.
    }

    void EndAuthority()
    {
        // Stop forward push but KEEP final position
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        if (normalMover) normalMover.enabled = true;
        // leave agent/cc off for one frame to avoid teleport; re-enable next frame
        StartCoroutine(ReenableNextFrame());
    }

    System.Collections.IEnumerator ReenableNextFrame()
    {
        yield return null; // wait one frame
        if (agent) agent.enabled = false; // keep off unless you really need it
        if (cc) cc.enabled = true;     // or re-enable if you use CC
    }
}
