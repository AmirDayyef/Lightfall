using UnityEngine;

[RequireComponent(typeof(Collider))]
public class PickupLightOrb3D : MonoBehaviour
{
    public Vector2 restoreRange = new Vector2(15f, 25f);
    public int scoreValue = 50;
    public GlowWrapper glow;
    public string playerTag = "Player";

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
            return;

        var pc = other.GetComponentInParent<PlayerController3D>();
        if (pc == null) return;

        float restore = Random.Range(restoreRange.x, restoreRange.y);

        pc.AddLight(restore);

        try
        {
            var gmType = System.Type.GetType("GameManager");
            if (gmType != null)
            {
                var instProp = gmType.GetProperty("I") ?? gmType.GetProperty("Instance");
                var inst = instProp != null ? instProp.GetValue(null) : null;
                if (inst != null)
                {
                    var m = gmType.GetMethod("AddScore", new[] { typeof(int) });
                    if (m != null) m.Invoke(inst, new object[] { scoreValue });
                }
            }
        }
        catch {  }

        if (glow != null) glow.PulseOnce();

        Destroy(gameObject);
    }

    [System.Serializable]
    public class GlowWrapper
    {
        public Light unityLight;
        public Renderer emissiveRenderer;
        public float pulseIntensity = 2f;
        public float pulseTime = 0.12f;
        public Color emissionColor = Color.white;

        public void PulseOnce(MonoBehaviour host = null)
        {
            if (unityLight == null && emissiveRenderer == null) return;
            if (host == null && Application.isPlaying)
            {
                var fallback = Object.FindFirstObjectByType<PickupCoroutineHost>();
                if (!fallback)
                {
                    var go = new GameObject("PickupCoroutineHost");
                    Object.DontDestroyOnLoad(go);
                    fallback = go.AddComponent<PickupCoroutineHost>();
                }
                fallback.Run(PulseRoutine());
            }
            else if (host != null)
            {
                host.StartCoroutine(PulseRoutine());
            }
        }

        System.Collections.IEnumerator PulseRoutine()
        {
            float t = 0f;
            float baseIntensity = unityLight ? unityLight.intensity : 0f;

            Material mat = null;
            Color baseEmiss = Color.black;
            if (emissiveRenderer && emissiveRenderer.material && emissiveRenderer.material.HasProperty("_EmissionColor"))
            {
                mat = emissiveRenderer.material;
                baseEmiss = mat.GetColor("_EmissionColor");
                mat.EnableKeyword("_EMISSION");
            }

            while (t < pulseTime)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, pulseTime));
                float k = 1f - (u - 1f) * (u - 1f);
                if (unityLight) unityLight.intensity = baseIntensity + pulseIntensity * k;
                if (mat) mat.SetColor("_EmissionColor", Color.Lerp(baseEmiss, emissionColor * (1.5f * pulseIntensity), k));
                yield return null;
            }

            if (unityLight) unityLight.intensity = baseIntensity;
            if (mat) mat.SetColor("_EmissionColor", baseEmiss);
        }
    }

    private class PickupCoroutineHost : MonoBehaviour
    {
        public void Run(System.Collections.IEnumerator r) => StartCoroutine(r);
    }
}
