using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Exit gate with multiple light-up stations (3D version).
/// - Player presses E near a station to light it (Light pulse when lit).
/// - Each station can optionally require specific enemies to be destroyed BEFORE it can be lit.
/// - Exit works when ALL stations are lit.
/// - OPTIONAL: Each station can display an "orb" (ball) that appears when lit and bobs up/down. Also does a one-shot pop on light-up.
/// - When player enters gate trigger AND all stations are lit -> fade to black, then go to next scene.
/// Attach to Exit object (needs a 3D Collider with "Is Trigger").
/// </summary>
[RequireComponent(typeof(Collider))]
public class LevelLightGate3D : MonoBehaviour
{
    [System.Serializable]
    public class Station
    {
        [Header("Station Refs")]
        public Transform root;     // station object in scene
        public Light light3D;      // Point/Spot/Area; assign or auto-find

        [Header("Interact")]
        public float interactRadius = 1.6f; // distance to interact
        public bool isLit;

        [Header("Enemy Requirement (optional, per-station)")]
        [Tooltip("If true, the listed enemies must be destroyed (null) before this station can be lit.")]
        public bool requireEnemiesClearedToLight = false;

        [Tooltip("Drag specific enemies that must be dead for this station to be lit.")]
        public GameObject[] requiredEnemies;

        [Tooltip("If set, this station will auto-find enemies with this tag at Awake() and require them to be dead to light.")]
        public string autoFindEnemiesByTag = "";

        [Tooltip("Seconds between cleanup checks for destroyed enemies for this station.")]
        public float enemyCheckInterval = 0.5f;

        float nextEnemyCheckTime;

        [Header("Light Pulse")]
        public float baseIntensity = 1.0f;
        public float pulseSpeed = 3f;
        public float pulseAmount = 0.2f;

        [Header("Orb (Ball)")]
        [Tooltip("Optional visual ball that hovers when lit. If left null, will try to find child named 'Orb' under root.")]
        public Transform orb;
        [Tooltip("If true, the orb is only visible when lit.")]
        public bool orbOnlyWhenLit = true;
        [Tooltip("Hover bob amplitude when lit.")]
        public float orbBobHeight = 0.20f;
        [Tooltip("Hover bob speed when lit.")]
        public float orbBobSpeed = 2.2f;
        [Tooltip("One-shot pop height applied right when station lights up.")]
        public float orbPopHeight = 0.35f;
        [Tooltip("Duration of the one-shot pop animation (seconds).")]
        public float orbPopDuration = 0.55f;
        [Tooltip("Scale of the orb when unlit.")]
        public float orbScaleUnlit = 0.75f;
        [Tooltip("Scale of the orb when lit.")]
        public float orbScaleLit = 1.00f;

        // ---- Internals for orb animation ----
        Vector3 orbLocalRestPos;
        bool orbRestCaptured;
        float orbBobPhaseOffset;
        float orbPopTimer; // counts down from orbPopDuration

        public void Autofill()
        {
            if (root && !light3D) light3D = root.GetComponentInChildren<Light>(true);

            // Try to auto-find an "Orb" child if none assigned
            if (root && !orb)
            {
                var orbTf = root.Find("Orb");
                if (orbTf) orb = orbTf;
                else
                {
                    // Fallback heuristic
                    for (int i = 0; i < root.childCount; i++)
                    {
                        var c = root.GetChild(i);
                        if (c.GetComponent<MeshRenderer>())
                        {
                            var n = c.name.ToLowerInvariant();
                            if (n.Contains("orb") || n.Contains("ball") || n.Contains("sphere"))
                            {
                                orb = c;
                                break;
                            }
                        }
                    }
                }
            }

            if (orb && !orbRestCaptured)
            {
                orbLocalRestPos = orb.localPosition;
                orbRestCaptured = true;
            }

            // Randomize bob phase so multiple stations don't sync perfectly
            orbBobPhaseOffset = Random.value * Mathf.PI * 2f;

            ApplyImmediate();
        }

        public void ApplyImmediate()
        {
            if (light3D)
            {
                light3D.enabled = isLit;
                light3D.intensity = isLit ? baseIntensity : 0f;
            }

            if (orb)
            {
                if (!orbRestCaptured)
                {
                    orbLocalRestPos = orb.localPosition;
                    orbRestCaptured = true;
                }

                // Visibility
                if (orbOnlyWhenLit)
                    orb.gameObject.SetActive(isLit);
                else
                    orb.gameObject.SetActive(true);

                // Scale
                float targetScale = isLit ? orbScaleLit : orbScaleUnlit;
                orb.localScale = Vector3.one * targetScale;

                // Position (reset to rest; bob/pop handled in Tick)
                orb.localPosition = orbLocalRestPos;
            }
        }

        public void Tick()
        {
            // Light continuous pulse when lit
            if (isLit && light3D)
            {
                light3D.intensity = baseIntensity + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            }

            // Enemy list maintenance
            if (requireEnemiesClearedToLight && Time.time >= nextEnemyCheckTime)
            {
                nextEnemyCheckTime = Time.time + Mathf.Max(0.1f, enemyCheckInterval);
                CompactEnemyArray();
            }

            // Orb animation
            if (orb)
            {
                if (orbOnlyWhenLit) orb.gameObject.SetActive(isLit);

                // Lerp scale toward target
                float targetScale = isLit ? orbScaleLit : orbScaleUnlit;
                orb.localScale = Vector3.Lerp(orb.localScale, Vector3.one * targetScale, Time.deltaTime * 8f);

                // Base position is rest; add components below
                Vector3 pos = orbLocalRestPos;

                // Gentle hover bob only while lit
                if (isLit && orbBobHeight > 0f && orbBobSpeed > 0f)
                {
                    pos.y += Mathf.Sin(Time.time * orbBobSpeed + orbBobPhaseOffset) * orbBobHeight;
                }

                // One-shot pop (up-and-down) right after lighting
                if (orbPopTimer > 0f && orbPopDuration > 0.0001f)
                {
                    float t = 1f - (orbPopTimer / orbPopDuration); // 0 -> 1
                    float pop = Mathf.Sin(t * Mathf.PI) * orbPopHeight;
                    pos.y += pop;
                    orbPopTimer -= Time.deltaTime;
                    if (orbPopTimer < 0f) orbPopTimer = 0f;
                }

                orb.localPosition = pos;
            }
        }

        public void AwakeStation()
        {
            if (requireEnemiesClearedToLight && !string.IsNullOrEmpty(autoFindEnemiesByTag))
            {
                requiredEnemies = GameObject.FindGameObjectsWithTag(autoFindEnemiesByTag);
            }
        }

        /// <summary>Returns true if either requirement disabled OR every required enemy is destroyed/null.</summary>
        public bool EnemiesCleared()
        {
            if (!requireEnemiesClearedToLight) return true;
            if (requiredEnemies == null || requiredEnemies.Length == 0) return true;
            foreach (var go in requiredEnemies)
                if (go) return false; // still alive
            return true;
        }

        void CompactEnemyArray()
        {
            if (requiredEnemies == null || requiredEnemies.Length == 0) return;
            int alive = 0;
            for (int i = 0; i < requiredEnemies.Length; i++)
                if (requiredEnemies[i]) alive++;
            if (alive == requiredEnemies.Length || alive == 0)
            {
                if (alive == 0) requiredEnemies = System.Array.Empty<GameObject>();
                return;
            }
            var tmp = new GameObject[alive];
            int idx = 0;
            for (int i = 0; i < requiredEnemies.Length; i++)
                if (requiredEnemies[i]) tmp[idx++] = requiredEnemies[i];
            requiredEnemies = tmp;
        }

        /// <summary>Call when the station becomes lit to trigger the one-shot pop.</summary>
        public void TriggerLitEffects()
        {
            orbPopTimer = orbPopDuration;
        }
    }

    [Header("Stations")]
    public Station[] stations;

    [Header("Player")]
    public string playerTag = "Player";
    public KeyCode interactKey = KeyCode.E;
    Transform player;

    Collider exitTrigger;

    // --------------- Fade & Transition ---------------
    [Header("Fade & Transition")]
    [Tooltip("If assigned, uses this CanvasGroup for fade. If null, a runtime fader will be created automatically.")]
    public CanvasGroup fadeCanvasGroup;     // optional reference (fullscreen)
    [Tooltip("Fade color for the runtime-created fader image.")]
    public Color fadeColor = Color.black;
    [Tooltip("Seconds to fade to black before loading next scene.")]
    public float fadeDuration = 0.8f;
    [Tooltip("Extra wait (seconds) after fully faded before loading.")]
    public float waitAfterFade = 0.15f;

    [Header("Scene To Load")]
    [Tooltip("If not empty, load this scene by name. If empty, loads next scene in Build Settings.")]
    public string nextSceneName = "";

    bool _isFading;

    void Reset()
    {
        exitTrigger = GetComponent<Collider>();
        if (exitTrigger) exitTrigger.isTrigger = true;
    }

    void Awake()
    {
        if (!exitTrigger) exitTrigger = GetComponent<Collider>();
        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo) player = pgo.transform;

        if (stations != null)
        {
            foreach (var s in stations)
            {
                if (s == null) continue;
                s.Autofill();
                s.AwakeStation();
            }
        }

        // If a CanvasGroup is assigned in inspector, ensure initial alpha = 0
        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.alpha = 0f;
            if (!fadeCanvasGroup.gameObject.activeSelf)
                fadeCanvasGroup.gameObject.SetActive(true);
            var img = fadeCanvasGroup.GetComponentInChildren<Image>(true);
            if (img) img.color = fadeColor;
            fadeCanvasGroup.blocksRaycasts = true; // block clicks during fade
            fadeCanvasGroup.interactable = false;
        }
    }

    void Update()
    {
        if (!player || stations == null) return;

        foreach (var s in stations)
        {
            if (s == null || s.root == null) continue;

            float dist = Vector3.Distance(player.position, s.root.position);

            // Only allow lighting if within range AND station's enemy requirement is cleared (if any)
            if (!s.isLit && dist <= s.interactRadius && Input.GetKeyDown(interactKey))
            {
                if (s.EnemiesCleared())
                {
                    s.isLit = true;
                    s.ApplyImmediate();
                    s.TriggerLitEffects(); // <-- start the orb pop
                }
                else
                {
                    Debug.Log("[LevelLightGate3D] Station locked: required enemies not cleared for this station.");
                }
            }

            s.Tick();
        }
    }

    bool AllLit()
    {
        if (stations == null || stations.Length == 0) return true;
        foreach (var s in stations) if (s != null && !s.isLit) return false;
        return true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (_isFading) return;
        if (!other.CompareTag(playerTag)) return;
        if (!AllLit()) return;

        StartCoroutine(FadeThenLoadNext());
    }

    IEnumerator FadeThenLoadNext()
    {
        _isFading = true;

        // Ensure we have a fader
        var cg = fadeCanvasGroup ? fadeCanvasGroup : CreateRuntimeFader();

        // Fade to black
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / Mathf.Max(0.0001f, fadeDuration));
            cg.alpha = a;
            yield return null;
        }
        cg.alpha = 1f;

        if (waitAfterFade > 0f)
            yield return new WaitForSeconds(waitAfterFade);

        // ---- LOAD NEXT SCENE ----
        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            int idx = SceneManager.GetActiveScene().buildIndex;
            if (idx >= 0 && idx + 1 < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(idx + 1);
            else
                Debug.LogWarning("[LevelLightGate3D] No next scene in Build Settings, and nextSceneName is empty.");
        }
    }

    CanvasGroup CreateRuntimeFader()
    {
        // Canvas
        var go = new GameObject("~FadeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(go);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760; // very high

        // Image
        var imgGO = new GameObject("Fade", typeof(Image), typeof(CanvasGroup));
        imgGO.transform.SetParent(go.transform, false);
        var img = imgGO.GetComponent<Image>();
        img.color = fadeColor;
        img.raycastTarget = true;

        // Fullscreen rect
        var rt = imgGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var cg = imgGO.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = true;
        cg.interactable = false;

        // cache for next time
        fadeCanvasGroup = cg;
        return cg;
    }

    void OnDrawGizmosSelected()
    {
        if (stations == null) return;

        foreach (var s in stations)
        {
            if (s == null) continue;

            if (s.root)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(s.root.position, s.interactRadius);
            }

            if (s.requireEnemiesClearedToLight && s.requiredEnemies != null)
            {
                Gizmos.color = Color.red;
                foreach (var e in s.requiredEnemies)
                    if (e) Gizmos.DrawWireSphere(e.transform.position, 0.2f);
            }

            // Draw orb position if present
            if (s.root && s.orb)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(s.orb.position, 0.12f);
            }
        }
    }
}
