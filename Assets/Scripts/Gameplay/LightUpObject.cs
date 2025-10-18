using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Level gate object (torch/altar/crystal) that must be lit.
/// Provides a built-in placeholder "light-up" animation (color + Light2D pulse).
/// </summary>
[DisallowMultipleComponent]
public class LightUpObject : MonoBehaviour
{
    [Header("Refs")]
    public SpriteRenderer spriteRenderer;     // auto-grab if null
    public Light2D glowLight;                 // auto-grab in children

    [Header("State")]
    public bool isLit;
    public Color unlitColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    public Color litColor = new Color(1.0f, 0.95f, 0.4f, 1f);

    [Header("Placeholder Animation")]
    public float lightUpDuration = 0.4f;   // tween to lit
    public float pulseSpeed = 3f;          // idle pulse when lit
    public float pulseAmount = 0.2f;       // +/- intensity
    float baseIntensity;

    void Reset()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        if (!glowLight) glowLight = GetComponentInChildren<Light2D>(true);
    }

    void Awake()
    {
        if (!spriteRenderer) spriteRenderer = GetComponent<SpriteRenderer>();
        if (!glowLight) glowLight = GetComponentInChildren<Light2D>(true);

        if (glowLight) { baseIntensity = Mathf.Max(0.6f, glowLight.intensity); }
        ApplyVisuals(immediate: true);
    }

    void Update()
    {
        if (isLit && glowLight)
        {
            glowLight.intensity = baseIntensity + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        }
    }

    public void SetLit(bool lit)
    {
        if (isLit == lit) return;
        isLit = lit;
        StopAllCoroutines();
        StartCoroutine(LightupAnim());
    }

    System.Collections.IEnumerator LightupAnim()
    {
        float t = 0f;
        Color c0 = spriteRenderer ? spriteRenderer.color : Color.white;
        float i0 = glowLight ? glowLight.intensity : 1f;

        // make sure light is enabled when animating to lit
        if (glowLight && isLit) glowLight.enabled = true;

        while (t < lightUpDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / lightUpDuration);

            if (spriteRenderer)
                spriteRenderer.color = Color.Lerp(c0, isLit ? litColor : unlitColor, u);

            if (glowLight)
                glowLight.intensity = Mathf.Lerp(i0, isLit ? baseIntensity : 0f, u);

            yield return null;
        }

        ApplyVisuals(immediate: true);
    }

    void ApplyVisuals(bool immediate)
    {
        if (spriteRenderer) spriteRenderer.color = isLit ? litColor : unlitColor;
        if (glowLight)
        {
            glowLight.enabled = isLit;
            glowLight.intensity = isLit ? baseIntensity : 0f;
        }
    }

    // handy for testing in Inspector
    [ContextMenu("Toggle Lit")]
    void _Toggle() => SetLit(!isLit);
}
