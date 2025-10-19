using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;

[RequireComponent(typeof(Collider))]
public class LevelLightGate3D : MonoBehaviour
{
    [System.Serializable]
    public class Station
    {
        public Transform root;
        public Light light3D;

        public float interactRadius = 1.6f;
        public bool isLit;

        public bool requireEnemiesClearedToLight = false;
        public GameObject[] requiredEnemies;
        public string autoFindEnemiesByTag = "";
        public float enemyCheckInterval = 0.5f;
        float nextEnemyCheckTime;

        public float baseIntensity = 1.0f;
        public float pulseSpeed = 3f;
        public float pulseAmount = 0.2f;

        public Transform orb;
        public bool orbOnlyWhenLit = true;
        public float orbBobHeight = 0.20f;
        public float orbBobSpeed = 2.2f;
        public float orbPopHeight = 0.35f;
        public float orbPopDuration = 0.55f;
        public float orbScaleUnlit = 0.75f;
        public float orbScaleLit = 1.00f;

        Vector3 orbLocalRestPos;
        bool orbRestCaptured;
        float orbBobPhaseOffset;
        float orbPopTimer;

        internal AudioSource src;
        internal LevelLightGate3D _owner;

        public void Autofill(LevelLightGate3D owner)
        {
            _owner = owner;

            if (!root)
            {
                var helper = new GameObject($"{owner.name}_Station");
                helper.transform.SetParent(owner.transform, false);
                root = helper.transform;
            }

            if (root && !light3D) light3D = root.GetComponentInChildren<Light>(true);

            if (root && !orb)
            {
                var orbTf = root.Find("Orb");
                if (orbTf) orb = orbTf;
                else
                {
                    for (int i = 0; i < root.childCount; i++)
                    {
                        var c = root.GetChild(i);
                        if (c.GetComponent<MeshRenderer>())
                        {
                            var n = c.name.ToLowerInvariant();
                            if (n.Contains("orb") || n.Contains("ball") || n.Contains("sphere")) { orb = c; break; }
                        }
                    }
                }
            }

            if (orb && !orbRestCaptured)
            {
                orbLocalRestPos = orb.localPosition;
                orbRestCaptured = true;
            }

            orbBobPhaseOffset = Random.value * Mathf.PI * 2f;
            EnsureAudioSource();
            ApplyImmediate();
        }

        void EnsureAudioSource()
        {
            if (!root) return;
            src = root.GetComponent<AudioSource>();
            if (!src) src = root.gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.outputAudioMixerGroup = _owner ? _owner.stationOutputMixer : null;
            src.volume = 1f;
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

                if (orbOnlyWhenLit) orb.gameObject.SetActive(isLit); else orb.gameObject.SetActive(true);
                float targetScale = isLit ? orbScaleLit : orbScaleUnlit;
                orb.localScale = Vector3.one * targetScale;
                orb.localPosition = orbLocalRestPos;
            }
        }

        public void Tick()
        {
            if (isLit && light3D)
                light3D.intensity = baseIntensity + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;

            if (requireEnemiesClearedToLight && Time.time >= nextEnemyCheckTime)
            {
                nextEnemyCheckTime = Time.time + Mathf.Max(0.1f, enemyCheckInterval);
                CompactEnemyArray();
            }

            if (orb)
            {
                if (orbOnlyWhenLit) orb.gameObject.SetActive(isLit);

                float targetScale = isLit ? orbScaleLit : orbScaleUnlit;
                orb.localScale = Vector3.Lerp(orb.localScale, Vector3.one * targetScale, Time.deltaTime * 8f);

                Vector3 pos = orbLocalRestPos;

                if (isLit && orbBobHeight > 0f && orbBobSpeed > 0f)
                    pos.y += Mathf.Sin(Time.time * orbBobSpeed + orbBobPhaseOffset) * orbBobHeight;

                if (orbPopTimer > 0f && orbPopDuration > 0.0001f)
                {
                    float t = 1f - (orbPopTimer / orbPopDuration);
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
                requiredEnemies = GameObject.FindGameObjectsWithTag(autoFindEnemiesByTag);
        }

        public bool EnemiesCleared()
        {
            if (!requireEnemiesClearedToLight) return true;
            if (requiredEnemies == null || requiredEnemies.Length == 0) return true;
            foreach (var go in requiredEnemies) if (go) return false;
            return true;
        }

        void CompactEnemyArray()
        {
            if (requiredEnemies == null || requiredEnemies.Length == 0) return;
            int alive = 0;
            for (int i = 0; i < requiredEnemies.Length; i++) if (requiredEnemies[i]) alive++;
            if (alive == requiredEnemies.Length || alive == 0)
            {
                if (alive == 0) requiredEnemies = System.Array.Empty<GameObject>();
                return;
            }
            var tmp = new GameObject[alive];
            int idx = 0;
            for (int i = 0; i < requiredEnemies.Length; i++) if (requiredEnemies[i]) tmp[idx++] = requiredEnemies[i];
            requiredEnemies = tmp;
        }

        public void TriggerLitEffects()
        {
            orbPopTimer = orbPopDuration;
            if (_owner && src && _owner.stationPowerUpClip)
            {
                src.pitch = Random.Range(0.98f, 1.02f);
                src.PlayOneShot(_owner.stationPowerUpClip, Mathf.Clamp01(_owner.stationPowerUpVolume));
            }
        }
    }

    public Station[] stations;

    public string playerTag = "Player";
    public KeyCode interactKey = KeyCode.E;
    Transform player;

    public AudioMixerGroup stationOutputMixer;
    public AudioClip stationPowerUpClip;
    [Range(0f, 1f)] public float stationPowerUpVolume = 0.9f;

    public CanvasGroup fadeCanvasGroup;
    public Color fadeColor = Color.black;
    public float fadeDuration = 0.8f;
    public float waitAfterFade = 0.15f;

    public string nextSceneName = "";

    bool _isFading;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c) c.isTrigger = true;
    }

    void Awake()
    {
        var c = GetComponent<Collider>();
        if (c) c.isTrigger = true;

        var pgo = GameObject.FindGameObjectWithTag(playerTag);
        if (pgo) player = pgo.transform;

        if (stations != null)
        {
            foreach (var s in stations)
            {
                if (s == null) continue;
                s.Autofill(this);
                s.AwakeStation();
                if (s.src) s.src.outputAudioMixerGroup = stationOutputMixer;
            }
        }

        if (fadeCanvasGroup)
        {
            fadeCanvasGroup.alpha = 0f;
            if (!fadeCanvasGroup.gameObject.activeSelf) fadeCanvasGroup.gameObject.SetActive(true);
            var img = fadeCanvasGroup.GetComponentInChildren<Image>(true);
            if (img) img.color = fadeColor;
            fadeCanvasGroup.blocksRaycasts = true;
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

            if (!s.isLit && dist <= s.interactRadius && Input.GetKeyDown(interactKey))
            {
                if (s.EnemiesCleared())
                {
                    s.isLit = true;
                    s.ApplyImmediate();
                    s.TriggerLitEffects();
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

        var cg = fadeCanvasGroup ? fadeCanvasGroup : CreateRuntimeFader();

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

        if (!string.IsNullOrEmpty(nextSceneName))
        {
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            int idx = SceneManager.GetActiveScene().buildIndex;
            if (idx >= 0 && idx + 1 < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(idx + 1);
        }
    }

    CanvasGroup CreateRuntimeFader()
    {
        var go = new GameObject("~FadeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(go);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;

        var imgGO = new GameObject("Fade", typeof(Image), typeof(CanvasGroup));
        imgGO.transform.SetParent(go.transform, false);
        var img = imgGO.GetComponent<Image>();
        img.color = fadeColor;
        img.raycastTarget = true;

        var rt = imgGO.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var cg = imgGO.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = true;
        cg.interactable = false;

        fadeCanvasGroup = cg;
        return cg;
    }
}
