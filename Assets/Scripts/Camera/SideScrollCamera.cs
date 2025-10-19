using UnityEngine;

[ExecuteAlways]
public class SideScrollCamera : MonoBehaviour
{
    public Transform target;

    public float minX = 17f;
    public bool useXMax = false;
    public float maxX = 99999f;
    public float xOffset = 0f;

    public float pivotZ = 0f;
    public float height = 2f;
    public float distance = 12f;
    [Range(-89f, 89f)] public float pitch = 15f;
    public float yaw = 0f;
    public Vector3 lookOffset = new Vector3(0f, 1.2f, 0f);

    public bool followY = false;
    public float yOffset = 0f;
    public bool useYMin = false;
    public float minY = 0f;
    public bool useYMax = false;
    public float maxY = 1000f;
    public bool lockY = true;
    public float lockedY = 17f;

    public float posSmoothTime = 0.18f;
    public float posMaxSpeed = Mathf.Infinity;

    public float defaultFOV = 60f;
    public float zoomSmoothTime = 0.25f;

    [System.Serializable]
    public struct PointPairZone
    {
        public Transform A;
        public Transform B;
        public float targetFOV;
        public bool changeY;
        public float targetY;
        public bool addToLockedY;
    }

    public bool usePointPairs = false;
    public PointPairZone[] pointPairs;

    public Transform[] zoomPoints;
    public float pairTargetFOV = 55f;

    [System.Serializable]
    public struct RangeZoom { public float xMin, xMax, targetFOV; }
    public bool useXRanges = false;
    public RangeZoom[] xRanges;

    public bool autoDistanceFromFOV = false;
    public float framedWorldWidth = 22f;

    Camera _cam;
    Vector3 _pivotVel;
    float _zoomVel;
    Vector3 _pivotPos;

    void Awake()
    {
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = gameObject.AddComponent<Camera>();
        _cam.orthographic = false;
        _cam.fieldOfView = defaultFOV;
    }

    void LateUpdate()
    {
        if (!target) return;

        float desiredX = Mathf.Max(target.position.x + xOffset, minX);
        if (useXMax) desiredX = Mathf.Clamp(desiredX, minX, Mathf.Max(minX, maxX));

        float desiredY;
        if (lockY)
            desiredY = lockedY;
        else if (followY)
        {
            float y = target.position.y + yOffset;
            if (useYMin) y = Mathf.Max(y, minY);
            if (useYMax) y = Mathf.Min(y, maxY);
            desiredY = y;
        }
        else
            desiredY = _pivotPos.y != 0 ? _pivotPos.y : transform.position.y;

        float desiredFOV = defaultFOV;
        if (usePointPairs && pointPairs != null && pointPairs.Length > 0)
        {
            foreach (var p in pointPairs)
            {
                if (!p.A || !p.B) continue;
                float min = Mathf.Min(p.A.position.x, p.B.position.x);
                float max = Mathf.Max(p.A.position.x, p.B.position.x);
                if (desiredX >= min && desiredX <= max)
                {
                    desiredFOV = p.targetFOV;
                    if (p.changeY)
                        desiredY = p.addToLockedY ? lockedY + p.targetY : p.targetY;
                    break;
                }
            }
        }
        else if (usePointPairs && zoomPoints != null && zoomPoints.Length >= 2)
        {
            for (int i = 0; i + 1 < zoomPoints.Length; i += 2)
            {
                if (!zoomPoints[i] || !zoomPoints[i + 1]) continue;
                float min = Mathf.Min(zoomPoints[i].position.x, zoomPoints[i + 1].position.x);
                float max = Mathf.Max(zoomPoints[i].position.x, zoomPoints[i + 1].position.x);
                if (desiredX >= min && desiredX <= max)
                {
                    desiredFOV = pairTargetFOV;
                    break;
                }
            }
        }

        if (useXRanges && xRanges != null && xRanges.Length > 0)
        {
            foreach (var r in xRanges)
            {
                if (desiredX >= r.xMin && desiredX <= r.xMax)
                {
                    desiredFOV = r.targetFOV;
                    break;
                }
            }
        }

        Vector3 desiredPivot = new Vector3(desiredX, desiredY, pivotZ);
        _pivotPos = Vector3.SmoothDamp(
            _pivotPos == Vector3.zero ? desiredPivot : _pivotPos,
            desiredPivot, ref _pivotVel, Mathf.Max(0.0001f, posSmoothTime), posMaxSpeed, Time.deltaTime);

        ApplyFOV(desiredFOV);

        float dist = distance;
        if (autoDistanceFromFOV)
        {
            float fovRad = _cam.fieldOfView * Mathf.Deg2Rad;
            dist = (framedWorldWidth * 0.5f) / Mathf.Tan(fovRad * 0.5f);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 camPos = _pivotPos + rot * new Vector3(0f, height, -dist);
        transform.position = camPos;
        transform.rotation = Quaternion.LookRotation((_pivotPos + lookOffset) - camPos, Vector3.up);
    }

    void ApplyFOV(float desired)
    {
        _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, desired, ref _zoomVel, zoomSmoothTime, Mathf.Infinity, Time.deltaTime);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(minX, -1000f, 0f), new Vector3(minX, 1000f, 0f));
        if (useXMax)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(Mathf.Max(minX, maxX), -1000f, 0f), new Vector3(Mathf.Max(minX, maxX), 1000f, 0f));
        }
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
    }
#endif
}
