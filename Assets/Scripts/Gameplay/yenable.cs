using UnityEngine;

/// <summary>
/// Enables this object's collider when the player's Y position
/// passes a defined threshold.
/// 
/// Attach this to the object with a Collider.
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnableColliderByPlayerY : MonoBehaviour
{
    [Header("Player Settings")]
    [Tooltip("Tag used to find the player in the scene.")]
    public string playerTag = "Player";
    private Transform player;

    [Header("Y Threshold")]
    [Tooltip("The collider enables when player's Y > this value.")]
    public float enableAtY = 5f;

    [Tooltip("If true, disables collider when player goes back below threshold.")]
    public bool disableBelow = false;

    [Header("Check Frequency")]
    [Tooltip("How often to check (0 = every frame).")]
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

        // Enable when above threshold
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
