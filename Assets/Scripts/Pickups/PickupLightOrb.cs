using UnityEngine;

/// <summary>
/// Restore light and add score on touch (3D).
/// Requires a 3D Collider set as Trigger.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PickupLightOrb3D : MonoBehaviour
{
    [Header("Light Restore")]
    public Vector2 restoreRange = new Vector2(15f, 25f);

    [Header("Score")]
    public int scoreValue = 50;

    [Header("Optional Glow Pulse")]
    public GlowWrapper glow; // optional helper to pulse/animate the pickup

    [Header("Filtering")]
    public string playerTag = "Player"; // leave empty to skip tag check

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // Optional tag gate
        if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag))
            return;

        // Find the PlayerController3D on the incoming object or its parents
        var pc = other.GetComponentInParent<PlayerController3D>();
        if (pc == null) return;

        // Compute restore amount
        float restore = Random.Range(restoreRange.x, restoreRange.y);

        // Give light to the player (assumes your PlayerController3D exposes AddLight)
        pc.AddLight(restore);

        // Add score if you have a GameManager with a global instance + method
        // (safe-guarded so you can omit it if your project doesn't have one)
        try
        {
            var gmType = System.Type.GetType("GameManager");
            if (gmType != null)
            {
                // Expecting a static 'I' or 'Instance' with AddScore(int)
                var instProp = gmType.GetProperty("I") ?? gmType.GetProperty("Instance");
                var inst = instProp != null ? instProp.GetValue(null) : null;
                if (inst != null)
                {
                    var m = gmType.GetMethod("AddScore", new[] { typeof(int) });
                    if (m != null) m.Invoke(inst, new object[] { scoreValue });
                }
            }
        }
        catch { /* no-op if GameManager not present */ }

        // Optional: little pulse before despawn
        if (glow != null) glow.PulseOnce();

        Destroy(gameObject);
    }

    // ---------------- Optional glow helper ----------------
    [System.Serializable]
    public class GlowWrapper
    {
        public Light unityLight;               // standard 3D Light (optional)
        public Renderer emissiveRenderer;      // any renderer with _EmissionColor (optional)
        public float pulseIntensity = 2f;
        public float pulseTime = 0.12f;
        public Color emissionColor = Color.white;

        public void PulseOnce(MonoBehaviour host = null)
        {
            if (unityLight == null && emissiveRenderer == null) return;
            if (host == null && Application.isPlaying)
            {
                // Try to find a host to run a coroutine on
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

            // Cache emission
            Material mat = null;
            Color baseEmiss = Color.black;
            if (emissiveRenderer && emissiveRenderer.material && emissiveRenderer.material.HasProperty("_EmissionColor"))
            {
                mat = emissiveRenderer.material;
                baseEmiss = mat.GetColor("_EmissionColor");
                // Make sure GlobalIllumination knows it's emissive
                mat.EnableKeyword("_EMISSION");
            }

            while (t < pulseTime)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / Mathf.Max(0.0001f, pulseTime));
                float k = 1f - (u - 1f) * (u - 1f); // quick ease (peaks mid)
                if (unityLight) unityLight.intensity = baseIntensity + pulseIntensity * k;
                if (mat) mat.SetColor("_EmissionColor", Color.Lerp(baseEmiss, emissionColor * (1.5f * pulseIntensity), k));
                yield return null;
            }

            if (unityLight) unityLight.intensity = baseIntensity;
            if (mat) mat.SetColor("_EmissionColor", baseEmiss);
        }
    }

    // tiny host for coroutines if needed
    private class PickupCoroutineHost : MonoBehaviour
    {
        public void Run(System.Collections.IEnumerator r) => StartCoroutine(r);
    }
}
