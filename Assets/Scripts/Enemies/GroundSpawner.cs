using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Spawns ground (non-flying) enemies on positions sampled by a downward ray
/// onto a specified Ground layer, within a 3D rectangle (XZ) area.
/// - Area is defined in this transform's LOCAL space (X,Z).
/// - We raycast from probeHeight above down by probeDown on groundMask.
/// - Spawn position can be offset upward by spawnYOffset.
/// - NEW: Per-prefab amounts (initial & target) with per-type respawn delays.
/// </summary>
[DisallowMultipleComponent]
public class GroundEnemySpawner3D : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        [Min(0)] public int initialCount = 0;     // spawn this many at Start
        [Min(0)] public int targetAlive = 0;      // keep this many alive over time
        [Min(0f)] public float respawnDelay = 4f; // delay before replacing a dead one
        [HideInInspector] public bool respawnPending; // internal throttle
    }

    [Header("Area (local XZ)")]
    [Tooltip("Min XZ in local space of this spawner.")]
    public Vector2 areaMin = new Vector2(-10f, -10f);
    [Tooltip("Max XZ in local space of this spawner.")]
    public Vector2 areaMax = new Vector2(10f, 10f);

    [Header("Ground Probe")]
    public LayerMask groundMask;
    [Tooltip("Ray start height above the sample point.")]
    public float probeHeight = 10f;
    [Tooltip("Raycast length downward.")]
    public float probeDown = 30f;
    [Tooltip("Try to align spawned object 'up' to the surface normal.")]
    public bool alignToSurfaceNormal = true;

    [Header("Spawn Offset")]
    [Tooltip("Extra world Y offset added to the hit point.")]
    public float spawnYOffset = 0f;

    [Header("Per-Prefab Setup")]
    [Tooltip("Configure amounts per prefab.")]
    public SpawnEntry[] entries;

    [Header("Global Limits (optional)")]
    [Tooltip("0 or less = no cap. If >0, total alive across all entries won't exceed this.")]
    public int maxAliveGlobal = 0;

    [Header("Debug")]
    public bool drawArea = true;
    public Color gizmoColor = new Color(0f, 1f, 0f, 0.9f);

    // Tracking
    readonly List<GameObject> _aliveAll = new();
    // Map from instance to entry index for cleanup accounting
    readonly Dictionary<GameObject, int> _whoOwns = new();

    void Start()
    {
        // Initial pass: spawn each entry.initialCount
        if (entries == null) return;

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || !e.prefab) continue;

            int toSpawn = Mathf.Max(0, e.initialCount);
            for (int k = 0; k < toSpawn; k++)
            {
                if (!CanSpawnMoreGlobally()) break;
                TrySpawnOne(i);
            }
        }
    }

    void Update()
    {
        // Cleanup destroyed refs and bookkeeping per-entry counts
        for (int i = _aliveAll.Count - 1; i >= 0; i--)
        {
            var go = _aliveAll[i];
            if (go == null)
            {
                _aliveAll.RemoveAt(i);
                continue;
            }
        }

        // Per-entry maintenance towards targetAlive
        if (entries == null) return;

        // Count alive per entry on demand
        int[] alivePerEntry = CountAlivePerEntry();

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || !e.prefab) continue;

            int alive = alivePerEntry[i];
            if (alive < e.targetAlive && !e.respawnPending && CanSpawnMoreGlobally())
            {
                StartCoroutine(RespawnAfterDelay(i, e.respawnDelay));
            }
        }
    }

    IEnumerator RespawnAfterDelay(int entryIndex, float delay)
    {
        var e = entries[entryIndex];
        e.respawnPending = true;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        TrySpawnOne(entryIndex);
        e.respawnPending = false;
    }

    bool CanSpawnMoreGlobally()
    {
        if (maxAliveGlobal <= 0) return true;
        return _aliveAll.Count < maxAliveGlobal;
    }

    int[] CountAlivePerEntry()
    {
        int[] counts = new int[entries != null ? entries.Length : 0];

        // Recount from the dictionary, and prune nulls
        List<GameObject> toRemove = null;

        foreach (var kvp in _whoOwns)
        {
            var go = kvp.Key;
            if (go == null)
            {
                (toRemove ??= new List<GameObject>()).Add(go);
                continue;
            }
            int idx = kvp.Value;
            if (idx >= 0 && idx < counts.Length) counts[idx]++;
        }

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
            {
                _whoOwns.Remove(toRemove[i]);
                _aliveAll.Remove(toRemove[i]);
            }
        }

        // Also sweep _aliveAll for safety
        for (int i = _aliveAll.Count - 1; i >= 0; i--)
        {
            if (_aliveAll[i] == null) _aliveAll.RemoveAt(i);
        }

        return counts;
    }

    void TrySpawnOne(int entryIndex)
    {
        if (entries == null || entryIndex < 0 || entryIndex >= entries.Length) return;

        var e = entries[entryIndex];
        if (e == null || !e.prefab) return;

        const int kMaxAttempts = 16;
        for (int attempts = 0; attempts < kMaxAttempts; attempts++)
        {
            // Sample a local XZ point inside the rectangle
            float lx = Random.Range(areaMin.x, areaMax.x);
            float lz = Random.Range(areaMin.y, areaMax.y); // Vector2.y used as Z extent
            Vector3 local = new Vector3(lx, 0f, lz);

            // Convert to world position on the spawner's plane
            Vector3 worldXZ = transform.TransformPoint(local);

            // Cast from above downwards on groundMask
            Vector3 origin = worldXZ + Vector3.up * probeHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probeDown, groundMask, QueryTriggerInteraction.Ignore))
            {
                Quaternion rot = alignToSurfaceNormal
                    ? Quaternion.FromToRotation(Vector3.up, hit.normal)
                    : Quaternion.identity;

                Vector3 spawnPos = hit.point + Vector3.up * spawnYOffset;

                var go = Instantiate(e.prefab, spawnPos, rot);
                _aliveAll.Add(go);
                _whoOwns[go] = entryIndex;

                // optional: automatically clean bookkeeping when object is destroyed
                var cleaner = go.AddComponent<_SpawnerItemTracker>();
                cleaner.Init(this, go);

                return;
            }
        }
    }

    // Called by tracker when its object is destroyed
    internal void NotifyDestroyed(GameObject go)
    {
        _whoOwns.Remove(go);
        _aliveAll.Remove(go);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawArea) return;

        Gizmos.color = gizmoColor;
        // Draw the XZ rectangle at the spawner's Y
        Vector3 p1 = transform.TransformPoint(new Vector3(areaMin.x, 0f, areaMin.y));
        Vector3 p2 = transform.TransformPoint(new Vector3(areaMax.x, 0f, areaMin.y));
        Vector3 p3 = transform.TransformPoint(new Vector3(areaMax.x, 0f, areaMax.y));
        Vector3 p4 = transform.TransformPoint(new Vector3(areaMin.x, 0f, areaMax.y));

        Gizmos.DrawLine(p1, p2); Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4); Gizmos.DrawLine(p4, p1);

        // Center probe line
        Vector3 center = transform.TransformPoint(new Vector3(
            (areaMin.x + areaMax.x) * 0.5f, 0f, (areaMin.y + areaMax.y) * 0.5f));
        Vector3 start = center + Vector3.up * probeHeight;
        Vector3 end = start + Vector3.down * probeDown;
        Gizmos.DrawLine(start, end);
    }

    /// <summary>Tracker to notify spawner when a spawned object is destroyed.</summary>
    private class _SpawnerItemTracker : MonoBehaviour
    {
        GroundEnemySpawner3D _owner;
        GameObject _me;
        public void Init(GroundEnemySpawner3D owner, GameObject me)
        {
            _owner = owner; _me = me;
        }
        void OnDestroy()
        {
            if (_owner) _owner.NotifyDestroyed(_me);
        }
    }
}
