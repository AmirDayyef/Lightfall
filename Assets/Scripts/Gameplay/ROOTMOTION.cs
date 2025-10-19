using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Rigidbody))]
public class AttackMotionAuthority : MonoBehaviour
{
    public Animator anim;
    public MonoBehaviour normalMover; 
    public NavMeshAgent agent;
    public CharacterController cc;

    [Header("When to move (Animator state)")]
    public string attackStateName = "SpinAttack"; 
    public float startNorm = 0.05f;
    public float endNorm = 0.95f;

    [Header("Motion")]
    public float forwardSpeed = 6f; 

    Rigidbody rb;
    int attackHash;
    bool inAttack;
    bool stateMode;

    void Awake()
    {
        if (!anim) anim = GetComponentInChildren<Animator>();
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = false;
        attackHash = Animator.StringToHash(attackStateName);

        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!cc) cc = GetComponent<CharacterController>();
    }

    public void AttackStart() { BeginAuthority(); inAttack = true; stateMode = false; }
    public void AttackEnd() { EndAuthority(); inAttack = false; }

    void OnEnable() { stateMode = true; }
    void OnDisable() { if (inAttack) EndAuthority(); }

    void FixedUpdate()
    {
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

        Vector3 v = rb.linearVelocity;
        Vector3 f = transform.forward * forwardSpeed;
        rb.linearVelocity = new Vector3(f.x, v.y, f.z); 
    }

    void BeginAuthority()
    {
        if (normalMover) normalMover.enabled = false;
        if (agent && agent.enabled) agent.enabled = false;
        if (cc && cc.enabled) cc.enabled = false;
    }

    void EndAuthority()
    {
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
        if (normalMover) normalMover.enabled = true;
        StartCoroutine(ReenableNextFrame());
    }

    System.Collections.IEnumerator ReenableNextFrame()
    {
        yield return null;
        if (agent) agent.enabled = false;
        if (cc) cc.enabled = true;
    }
}
