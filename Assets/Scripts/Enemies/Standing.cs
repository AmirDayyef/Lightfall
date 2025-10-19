using UnityEngine;
using System.Collections;

[RequireComponent(typeof(EnemyBase))]
[DisallowMultipleComponent]
public class StationaryHitPulseEnemy3D : MonoBehaviour
{
    public Transform visualRoot;
    public float baseScale = 1f;

    public float pulseScaleMultiplier = 1.15f;
    public float pulseDuration = 0.12f;
    public bool flashColorOnHit = true;
    public Color hitFlashColor = new Color(1f, 0.7f, 0.7f, 1f);
    public float flashDuration = 0.08f;

    public bool fallOnDeath = true;
    public bool addAutomaticColliderIfMissing = true;
    public float fallImpulse = 1.2f;
    public float torqueImpulse = 2.5f;
    public float despawnAfterSeconds = 10f;

    public bool useFixedGroundY = false;
    public float fixedGroundY = 0f;
    public float groundSnapTolerance = 0.05f;
    public float minSettleSpeed = 0.2f;

    public bool forceCustomDeathHandled = true;
    public bool allowEnemyBaseVFX = true;
    public bool keepEnemyCollidersEnabled = false;

    EnemyBase _eb;
    Vector3 _baseLocalScale;
    bool _pulsing;
    Renderer[] _renderers;
    Color[] _origColors;

    void Awake()
    {
        _eb = GetComponent<EnemyBase>();
        if (!visualRoot) visualRoot = transform;
        if (forceCustomDeathHandled && _eb)
        {
            _eb.customDeathHandled = true;
            _eb.spawnVFXOnCustomDeath = allowEnemyBaseVFX;
            _eb.disableCollidersOnCustomDeath = !keepEnemyCollidersEnabled;
        }
        _baseLocalScale = Vector3.one * Mathf.Max(0.0001f, baseScale);
        _renderers = visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
        _origColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var mat = _renderers[i].material;
            _origColors[i] = mat.HasProperty("_Color") ? mat.color : Color.white;
        }
        visualRoot.localScale = _baseLocalScale;
    }

    void OnEnemyBaseHit(float dmg)
    {
        if (!_pulsing) StartCoroutine(HitPulse());
        if (flashColorOnHit) StartCoroutine(HitFlash());
    }

    void OnEnemyBaseDeath(EnemyBase eb)
    {
        if (!fallOnDeath || !visualRoot) return;

        _pulsing = false;
        visualRoot.localScale = _baseLocalScale;

        visualRoot.SetParent(null, worldPositionStays: true);

        Collider col = visualRoot.GetComponentInChildren<Collider>();
        if (!col && addAutomaticColliderIfMissing)
        {
            var rends = visualRoot.GetComponentsInChildren<Renderer>();
            if (rends != null && rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                var box = visualRoot.gameObject.AddComponent<BoxCollider>();
                Vector3 localCenter = visualRoot.InverseTransformPoint(b.center);
                Vector3 localSize = visualRoot.InverseTransformVector(b.size);
                box.center = localCenter;
                box.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
                col = box;
            }
            else
            {
                col = visualRoot.gameObject.AddComponent<BoxCollider>();
            }
        }

        var rb = visualRoot.GetComponent<Rigidbody>();
        if (!rb) rb = visualRoot.gameObject.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * 0.2f;
        rb.AddForce(dir.normalized * fallImpulse, ForceMode.Impulse);
        rb.AddTorque(Random.onUnitSphere * torqueImpulse, ForceMode.Impulse);

        if (useFixedGroundY) StartCoroutine(ClampToFixedGroundY(rb));
        if (despawnAfterSeconds > 0f)
        {
            Destroy(visualRoot.gameObject, despawnAfterSeconds);
            Destroy(gameObject, despawnAfterSeconds + 0.1f);
        }
    }

    IEnumerator HitPulse()
    {
        _pulsing = true;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, pulseDuration);
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = 1f - (1f - u) * (1f - u);
            float k = Mathf.Lerp(1f, pulseScaleMultiplier, e);
            visualRoot.localScale = _baseLocalScale * k;
            yield return null;
        }
        visualRoot.localScale = _baseLocalScale;
        _pulsing = false;
    }

    IEnumerator HitFlash()
    {
        float half = Mathf.Max(0.0001f, flashDuration) * 0.5f;
        for (int i = 0; i < _renderers.Length; i++)
        {
            var mat = _renderers[i].material;
            if (mat.HasProperty("_Color")) mat.color = hitFlashColor;
        }
        float t = 0f; while (t < half) { t += Time.deltaTime; yield return null; }
        for (int i = 0; i < _renderers.Length; i++)
        {
            var mat = _renderers[i].material;
            if (mat.HasProperty("_Color")) mat.color = _origColors[i];
        }
        t = 0f; while (t < half) { t += Time.deltaTime; yield return null; }
    }

    IEnumerator ClampToFixedGroundY(Rigidbody rb)
    {
        float tol = Mathf.Max(0.0001f, groundSnapTolerance);
        float minSpeed = Mathf.Max(0f, minSettleSpeed);
        float timeout = Time.time + 8f;

        while (Time.time < timeout)
        {
            Vector3 p = rb.position;
            float vy = rb.linearVelocity.y;

            bool nearY = p.y <= fixedGroundY + tol;
            bool slow = Mathf.Abs(vy) <= minSpeed;

            if (nearY && slow)
            {
                p.y = fixedGroundY;
                rb.position = p;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                break;
            }

            if (p.y < fixedGroundY - 1.0f)
            {
                p.y = fixedGroundY;
                rb.position = p;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                break;
            }

            yield return null;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Simulate Hit (Editor)")]
    void _SimHit() { OnEnemyBaseHit(1f); }

    [ContextMenu("Simulate Death (Editor)")]
    void _SimDeath()
    {
        if (_eb == null) _eb = GetComponent<EnemyBase>();
        if (_eb)
        {
            _eb.customDeathHandled = true;
            OnEnemyBaseDeath(_eb);
        }
    }
#endif
}
