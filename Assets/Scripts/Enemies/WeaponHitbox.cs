using UnityEngine;

public class WeaponHitbox : MonoBehaviour
{
    [Header("Owner & Damage")]
    public Transform owner;       // usually the player root
    public float damage = 10f;

    [Header("Per-swing de-dup")]
    [Tooltip("Assign a new (non-zero) ID each swing to avoid multi-hits.")]
    public int hitId = 0;

    // Example: call this when starting an attack anim
    public void NewSwing()
    {
        // simple unique id; customize as needed
        hitId = Mathf.Abs(System.Environment.TickCount ^ GetHashCode());
    }
}
