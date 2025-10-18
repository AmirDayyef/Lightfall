using UnityEngine;
// using UnityEngine.Serialization; // uncomment if you want FormerlySerializedAs

[ExecuteAlways]
public class SideScrollCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;          // player

    [Header("Horizontal Follow & Clamp")]
    [Tooltip("Camera X won't go below this world X")]
    public float minX = 17f;          // never go below this X
    [Tooltip("If true, Camera X also won't go ABOVE maxX")]
    public bool useXMax = false;
    public float maxX = 99999f;       // only used if useXMax = true
    public float xOffset = 0f;        // look-ahead

    [Header("Depth & Rig (Perspective)")]
    [Tooltip("Fixed Z depth for the *pivot* (camera is placed from this pivot).")]
    public float pivotZ = 0f;
    [Tooltip("Meters above the pivot to position the camera before pitch/yaw.")]
    public float height = 2f;
    [Tooltip("Meters back from the pivot (positive pulls camera backward).")]
    public float distance = 12f;
    [Range(-89f, 89f)] public float pitch = 15f;
    public float yaw = 0f;
    [Tooltip("Small offset the camera looks at, relative to the pivot.")]
    public Vector3 lookOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Vertical Follow & Clamp")]
    [Tooltip("If true, camera Y follows target (with optional min/max clamps). If false and lockY is true, Y is fixed.")]
    public bool followY = false;
    [Tooltip("Additive world-space Y offset while following.")]
    public float yOffset = 0f;

    [Tooltip("If true, enforce a minimum Y (camera won't go below).")]
    public bool useYMin = false;
    public float minY = 0f;

    [Tooltip("If true, enforce a maximum Y (camera won't go above).")]
    public bool useYMax = false;
    public float maxY = 1000f;

    [Tooltip("If true, ignore followY and force a fixed Y at 'lockedY'.")]
    public bool lockY = true;
    public float lockedY = 17f;       // fixed Y if lockY = true

    [Header("Smoothing")]
    [Tooltip("0.12–0.25 feels good")]
    public float posSmoothTime = 0.18f;
    public float posMaxSpeed = Mathf.Infinity;

    [Header("Zoom (FOV for perspective)")]
    public float defaultFOV = 60f;
    public float zoomSmoothTime = 0.25f;

    // -------- NEW: Per-pair config with optional Y override --------
    [System.Serializable]
    public struct PointPairZone
    {
        public Transform A;       // left/right bound (order doesn't matter)
        public Transform B;       // left/right bound
        [Tooltip("Target FOV while the player is between A.x and B.x")]
        public float targetFOV;
        [Tooltip("If true, while within this pair the camera pivot Y is overridden.")]
        public bool changeY;
        [Tooltip("The Y value to use. If 'addToLockedY' is true, this value is added to lockedY instead of absolute.")]
        public float targetY;
        [Tooltip("If true, pivotY = lockedY + targetY; otherwise pivotY = targetY (absolute world Y).")]
        public bool addToLockedY;
    }

    [Header("Zoom by Point Pairs (per-pair FOV and optional Y)")]
    public bool usePointPairs = false;
    public PointPairZone[] pointPairs;

    // -------- Back-compat (optional) --------
    [Header("Back-compat Zoom by Old Arrays (optional)")]
    [Tooltip("If you still use the old system: pairs are [0,1], [2,3], ...")]
    public Transform[] zoomPoints;        // [A,B], [C,D], ...
    public float pairTargetFOV = 55f;

    [Header("Zoom by X Ranges (optional)")]
    public bool useXRanges = false;
    public RangeZoom[] xRanges;

    [Header("Auto Distance (optional)")]
    [Tooltip("Keep a constant world-width framed and compute distance from FOV automatically.")]
    public bool autoDistanceFromFOV = false;
    [Tooltip("Approximate width (in world units) you want visible across the screen.")]
    public float framedWorldWidth = 22f;

    [System.Serializable]
    public struct RangeZoom { public float xMin, xMax, targetFOV; }

    // internals
    Camera _cam;
    Vector3 _pivotVel;     // for SmoothDamp pivot
    float _zoomVel;
    Vector3 _pivotPos;     // smoothed pivot (what the camera orbits/looks at)

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = gameObject.AddComponent<Camera>();

        // Force perspective (2.5D look)
        _cam.orthographic = false;
        _cam.fieldOfView = defaultFOV;
    }

    void LateUpdate()
    {
        if (!target) return;

        // --- desired pivot X (min/max clamp + look-ahead) ---
        float desiredXRaw = target.position.x + xOffset;
        float desiredX = Mathf.Max(desiredXRaw, minX);
        if (useXMax)
        {
            float mx = Mathf.Max(minX, maxX);
            desiredX = Mathf.Clamp(desiredX, minX, mx);
        }

        // --- desired pivot Y (lock or follow + clamps) ---
        float desiredY;
        if (lockY)
        {
            desiredY = lockedY;
        }
        else if (followY)
        {
            float y = target.position.y + yOffset;
            if (useYMin) y = Mathf.Max(y, minY);
            if (useYMax) y = Mathf.Min(y, maxY);
            desiredY = y;
        }
        else
        {
            desiredY = _pivotPos.y != 0 ? _pivotPos.y : transform.position.y;
        }

        // --- zoom (FOV) and optional Y override from point pairs ---
        float desiredFOV = defaultFOV;
        if (usePointPairs && pointPairs != null && pointPairs.Length > 0)
        {
            for (int i = 0; i < pointPairs.Length; i++)
            {
                var p = pointPairs[i];
                if (!p.A || !p.B) continue;
                float a = p.A.position.x, b = p.B.position.x;
                float min = Mathf.Min(a, b), max = Mathf.Max(a, b);
                if (desiredX >= min && desiredX <= max)
                {
                    desiredFOV = p.targetFOV;

                    if (p.changeY)
                    {
                        float y = p.addToLockedY ? (lockedY + p.targetY) : p.targetY;
                        desiredY = y;
                    }
                    break; // first match wins
                }
            }
        }
        else
        {
            // Back-compat: old arrays (no Y override here)
            if (usePointPairs && zoomPoints != null && zoomPoints.Length >= 2)
            {
                for (int i = 0; i + 1 < zoomPoints.Length; i += 2)
                {
                    if (!zoomPoints[i] || !zoomPoints[i + 1]) continue;
                    float a = zoomPoints[i].position.x, b = zoomPoints[i + 1].position.x;
                    float min = Mathf.Min(a, b), max = Mathf.Max(a, b);
                    if (desiredX >= min && desiredX <= max)
                    {
                        desiredFOV = pairTargetFOV;
                        break;
                    }
                }
            }
        }

        // --- also consider X ranges (if enabled) ---
        if (useXRanges && xRanges != null && xRanges.Length > 0)
        {
            for (int i = 0; i < xRanges.Length; i++)
                if (desiredX >= xRanges[i].xMin && desiredX <= xRanges[i].xMax)
                {
                    desiredFOV = xRanges[i].targetFOV;
                    break;
                }
        }

        // Smooth pivot
        Vector3 desiredPivot = new Vector3(desiredX, desiredY, pivotZ);
        _pivotPos = Vector3.SmoothDamp(
            _pivotPos == Vector3.zero ? desiredPivot : _pivotPos,
            desiredPivot, ref _pivotVel,
            Mathf.Max(0.0001f, posSmoothTime), posMaxSpeed, Time.deltaTime);

        // Smooth FOV
        ApplyFOV(desiredFOV);

        // Auto distance (optional)
        float dist = distance;
        if (autoDistanceFromFOV)
        {
            float fovRad = _cam.fieldOfView * Mathf.Deg2Rad;
            float halfWidth = framedWorldWidth * 0.5f;
            dist = halfWidth / Mathf.Tan(fovRad * 0.5f);
        }

        // Rig placement + look
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 localOffset = new Vector3(0f, height, -dist);
        Vector3 camPos = _pivotPos + rot * localOffset;

        transform.position = camPos;
        transform.rotation = Quaternion.LookRotation((_pivotPos + lookOffset) - camPos, Vector3.up);
    }

    void ApplyFOV(float desired)
    {
        float cur = _cam.fieldOfView;
        _cam.fieldOfView = Mathf.SmoothDamp(cur, desired, ref _zoomVel, zoomSmoothTime, Mathf.Infinity, Time.deltaTime);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // minX / maxX
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(minX, -1000f, 0f), new Vector3(minX, 1000f, 0f));
        if (useXMax)
        {
            Gizmos.color = Color.red;
            float mx = Mathf.Max(minX, maxX);
            Gizmos.DrawLine(new Vector3(mx, -1000f, 0f), new Vector3(mx, 1000f, 0f));
        }

        // vertical clamps
        if (useYMin)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(new Vector3(-1000f, minY, 0f), new Vector3(1000f, minY, 0f));
        }
        if (useYMax)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(new Vector3(-1000f, maxY, 0f), new Vector3(1000f, maxY, 0f));
        }
        if (lockY)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(new Vector3(-1000f, lockedY, 0f), new Vector3(1000f, lockedY, 0f));
        }

        // Draw new point pairs + their Y overrides
        if (usePointPairs && pointPairs != null)
        {
            foreach (var p in pointPairs)
            {
                if (!p.A || !p.B) continue;
                float ax = p.A.position.x, bx = p.B.position.x;
                float min = Mathf.Min(ax, bx), max = Mathf.Max(ax, bx);

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(new Vector3(min, -50f, 0f), new Vector3(min, 50f, 0f));
                Gizmos.DrawLine(new Vector3(max, -50f, 0f), new Vector3(max, 50f, 0f));
                Gizmos.DrawLine(new Vector3(min, 0f, 0f), new Vector3(max, 0f, 0f));

                if (p.changeY)
                {
                    float y = p.addToLockedY ? (lockedY + p.targetY) : p.targetY;
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawLine(new Vector3(min, y, 0f), new Vector3(max, y, 0f));
                }
            }
        }
        // Back-compat gizmos for old zoomPoints
        else if (usePointPairs && zoomPoints != null)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i + 1 < zoomPoints.Length; i += 2)
            {
                var A = zoomPoints[i]; var B = zoomPoints[i + 1];
                if (!A || !B) continue;
                float min = Mathf.Min(A.position.x, B.position.x);
                float max = Mathf.Max(A.position.x, B.position.x);
                Gizmos.DrawLine(new Vector3(min, -50f, 0f), new Vector3(min, 50f, 0f));
                Gizmos.DrawLine(new Vector3(max, -50f, 0f), new Vector3(max, 50f, 0f));
                Gizmos.DrawLine(new Vector3(min, 0f, 0f), new Vector3(max, 0f, 0f));
            }
        }

        // x ranges
        if (useXRanges && xRanges != null)
        {
            Gizmos.color = Color.cyan;
            foreach (var r in xRanges)
            {
                Vector3 c = new Vector3((r.xMin + r.xMax) * 0.5f, lockY ? lockedY : (_pivotPos == Vector3.zero ? transform.position.y : _pivotPos.y), 0f);
                Vector3 s = new Vector3(Mathf.Abs(r.xMax - r.xMin), 40f, 0.1f);
                Gizmos.DrawWireCube(c, s);
            }
        }

        // draw the current pivot/camera rig preview
        Gizmos.color = Color.white;
        float previewDesiredXRaw = (target ? target.position.x : 0f) + xOffset;
        float previewX = Mathf.Max(previewDesiredXRaw, minX);
        if (useXMax)
        {
            float mx = Mathf.Max(minX, maxX);
            previewX = Mathf.Clamp(previewX, minX, mx);
        }
        float previewY = lockY ? lockedY : (target ? target.position.y + yOffset : 0f);
        Vector3 pivotPreview = new Vector3(previewX, previewY, pivotZ);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 localOffset = new Vector3(0f, height, -Mathf.Max(0.01f, distance));
        Vector3 camPos = pivotPreview + rot * localOffset;
        Gizmos.DrawWireSphere(pivotPreview, 0.25f);
        Gizmos.DrawLine(pivotPreview, camPos);
    }
#endif
}
