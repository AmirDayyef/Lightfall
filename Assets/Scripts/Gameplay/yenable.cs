using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EnableColliderByPlayerY : MonoBehaviour
{
    public string playerTag = "Player";
    private Transform player;

    public float enableAtY = 5f;

    public bool disableBelow = false;

    public float checkInterval = 0f;
    private float nextCheckTime;

    private Collider col;

    void Awake()
    {
        col = GetComponent<Collider>();
        GameObject pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo) player = pgo.transform;
    }

    void Update()
    {
        if (!player) return;
        if (checkInterval > 0f && Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkInterval;

        float py = player.position.y;
        if (py > enableAtY && !col.enabled)
            col.enabled = true;
        else if (disableBelow && py <= enableAtY && col.enabled)
            col.enabled = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(-999, enableAtY, 0), new Vector3(999, enableAtY, 0));
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, GetComponent<Collider>() ? GetComponent<Collider>().bounds.size : Vector3.one);
    }
#endif
}
