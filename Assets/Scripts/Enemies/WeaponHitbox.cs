using UnityEngine;

public class WeaponHitbox : MonoBehaviour
{
    public Transform owner;
    public float damage = 10f;
    public int hitId = 0;

    public void NewSwing()
    {
        hitId = Mathf.Abs(System.Environment.TickCount ^ GetHashCode());
    }
}
