using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class FlyingEnemySpawner2D : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public GameObject prefab;
        public int targetAlive = 3;
        public float spawnInterval = 2.5f;
        public bool randomizeInitialPhase = true;
        public float nextSpawnTime;
    }

    public SpawnEntry[] entries;

    public bool useCameraEdges = true;
    public float extraOffscreenX = 2.0f;
    public float fixedLeftX = -20f;
    public float fixedRightX = 20f;

    public float minY = -1.0f;
    public float maxY = 4.0f;

    public float minZ = 0f;
    public float maxZ = 0f;

    public int maxAliveGlobal = 0;

    public bool drawYBand = true;

    readonly List<GameObject> _aliveAll = new();
    readonly Dictionary<GameObject, int> _ownerIndex = new();

    void Start()
    {
        if (entries == null) return;

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            e.nextSpawnTime = Time.time + (e.randomizeInitialPhase
                ? Random.Range(0f, Mathf.Max(0.0001f, e.spawnInterval))
                : 0f);
        }
    }

    void Update()
    {
        for (int i = _aliveAll.Count - 1; i >= 0; i--)
            if (_aliveAll[i] == null) _aliveAll.RemoveAt(i);

        if (entries == null || entries.Length == 0) return;

        int[] alivePerEntry = CountAlivePerEntry();

        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e == null || !e.prefab) continue;

            if (alivePerEntry[i] < e.targetAlive && Time.time >= e.nextSpawnTime)
            {
                if (CanSpawnMoreGlobally() && TrySpawnOne(i))
                    e.nextSpawnTime = Time.time + Mathf.Max(0.0001f, e.spawnInterval);
                else
                    e.nextSpawnTime = Time.time + 0.1f;
            }
        }
    }

    bool CanSpawnMoreGlobally()
    {
        if (maxAliveGlobal <= 0) return true;
        return _aliveAll.Count < maxAliveGlobal;
    }

    int[] CountAlivePerEntry()
    {
        int[] counts = new int[(entries != null) ? entries.Length : 0];
        List<GameObject> toRemove = null;

        foreach (var kvp in _ownerIndex)
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
                _ownerIndex.Remove(toRemove[i]);
                _aliveAll.Remove(toRemove[i]);
            }
        }

        for (int i = _aliveAll.Count - 1; i >= 0; i--)
            if (_aliveAll[i] == null) _aliveAll.RemoveAt(i);

        return counts;
    }

    bool TrySpawnOne(int entryIndex)
    {
        if (entries == null || entryIndex < 0 || entryIndex >= entries.Length) return false;

        var e = entries[entryIndex];
        if (e == null || !e.prefab) return false;

        float y = Random.Range(minY, maxY);
        float z = (minZ == maxZ) ? minZ : Random.Range(minZ, maxZ);
        float x;

        if (useCameraEdges && Camera.main)
        {
            var cam = Camera.main;
            float midY = cam.pixelHeight * 0.5f;
            float leftX = cam.ScreenToWorldPoint(new Vector3(0f, midY, cam.nearClipPlane)).x;
            float rightX = cam.ScreenToWorldPoint(new Vector3(cam.pixelWidth, midY, cam.nearClipPlane)).x;
            bool fromLeft = Random.value < 0.5f;
            x = fromLeft ? (leftX - extraOffscreenX) : (rightX + extraOffscreenX);
        }
        else
        {
            bool fromLeft = Random.value < 0.5f;
            x = transform.position.x + (fromLeft ? fixedLeftX : fixedRightX);
        }

        var go = Instantiate(e.prefab, new Vector3(x, y, z), Quaternion.identity);
        _aliveAll.Add(go);
        _ownerIndex[go] = entryIndex;

        var tracker = go.AddComponent<_SpawnerItemTracker>();
        tracker.Init(this, go);

        return true;
    }

    internal void NotifyDestroyed(GameObject go)
    {
        _ownerIndex.Remove(go);
        _aliveAll.Remove(go);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawYBand) return;

        Gizmos.color = Color.cyan;
        Vector3 a = new Vector3(transform.position.x - 1f, minY, (minZ + maxZ) * 0.5f);
        Vector3 b = new Vector3(transform.position.x + 1f, minY, (minZ + maxZ) * 0.5f);
        Vector3 c = new Vector3(transform.position.x + 1f, maxY, (minZ + maxZ) * 0.5f);
        Vector3 d = new Vector3(transform.position.x - 1f, maxY, (minZ + maxZ) * 0.5f);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c);
        Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
    }

    private class _SpawnerItemTracker : MonoBehaviour
    {
        FlyingEnemySpawner2D _owner;
        GameObject _me;
        public void Init(FlyingEnemySpawner2D owner, GameObject me) { _owner = owner; _me = me; }
        void OnDestroy() { if (_owner) _owner.NotifyDestroyed(_me); }
    }
}
