using UnityEngine;
using UnityEngine.Audio;
using System.Collections;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class AmbientAudioController : MonoBehaviour
{
    public bool dontDestroyOnLoad = true;
    public AudioMixerGroup outputMixerGroup;

    public AudioClip introClip;
    [Range(0f, 1f)] public float introVolume = 0.7f;
    public float introFadeIn = 0.5f;

    public AudioClip loopClip;
    [Range(0f, 1f)] public float loopVolume = 0.6f;
    public float loopFadeIn = 0.75f;
    public bool loopMusic = true;
    public bool playMusicOnStart = true;
    public bool scheduleGapless = true;

    public List<AudioClip> ambientClips = new List<AudioClip>();
    public Vector2 ambientIntervalSeconds = new Vector2(3f, 8f);
    public Vector2 ambientVolumeRange = new Vector2(0.06f, 0.15f);
    public Vector2 ambientPitchRange = new Vector2(0.95f, 1.05f);
    [Range(1, 16)] public int ambientVoices = 3;
    public bool ambientEnabled = true;

    AudioSource _introSrc;
    AudioSource _loopSrc;
    readonly List<AudioSource> _ambientPool = new List<AudioSource>();
    Coroutine _ambientLoopCo;

    void Awake()
    {
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        _introSrc = gameObject.AddComponent<AudioSource>();
        _introSrc.playOnAwake = false;
        _introSrc.loop = false;
        _introSrc.spatialBlend = 0f;
        _introSrc.outputAudioMixerGroup = outputMixerGroup;

        _loopSrc = gameObject.AddComponent<AudioSource>();
        _loopSrc.playOnAwake = false;
        _loopSrc.loop = loopMusic;
        _loopSrc.spatialBlend = 0f;
        _loopSrc.outputAudioMixerGroup = outputMixerGroup;

        for (int i = 0; i < Mathf.Max(1, ambientVoices); i++)
        {
            var go = new GameObject($"AmbientVoice_{i}");
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
            s.outputAudioMixerGroup = outputMixerGroup;
            _ambientPool.Add(s);
        }
    }

    void Start()
    {
        if (playMusicOnStart)
        {
            if (introClip)
                PlayIntroThenLoop(introClip, loopClip, introVolume, loopVolume, introFadeIn, loopFadeIn, scheduleGapless);
            else if (loopClip)
                PlayLoop(loopClip, loopVolume, loopFadeIn);
        }

        if (ambientEnabled && ambientClips != null && ambientClips.Count > 0)
            _ambientLoopCo = StartCoroutine(AmbientLoop());
    }

    void OnEnable()
    {
        if (_ambientLoopCo == null && ambientEnabled && ambientClips.Count > 0)
            _ambientLoopCo = StartCoroutine(AmbientLoop());
    }

    void OnDisable()
    {
        if (_ambientLoopCo != null) { StopCoroutine(_ambientLoopCo); _ambientLoopCo = null; }
    }

    public void PlayIntroThenLoop(AudioClip intro, AudioClip loop, float introVol = 1f, float loopVol = 1f, float introFade = 0f, float loopFade = 0f, bool gapless = true)
    {
        StopAllMusicImmediate();
        if (!intro && !loop) return;

        if (intro)
        {
            _introSrc.clip = intro;
            _introSrc.volume = 0f;
            _introSrc.Play();

            if (introFade > 0f)
                StartCoroutine(FadeVolume(_introSrc, 0f, Mathf.Clamp01(introVol), introFade));
            else
                _introSrc.volume = Mathf.Clamp01(introVol);

            if (loop)
            {
                _loopSrc.clip = loop;
                _loopSrc.loop = loopMusic;
                _loopSrc.volume = 0f;

                if (gapless)
                {
                    double now = AudioSettings.dspTime;
                    double introDur = (double)intro.samples / intro.frequency;
                    _loopSrc.PlayScheduled(now + introDur);

                    if (loopFade > 0f)
                        StartCoroutine(FadeVolumeAtTime(_loopSrc, Mathf.Clamp01(loopVol), loopFade, (float)introDur));
                    else
                        StartCoroutine(SetVolumeAtTime(_loopSrc, Mathf.Clamp01(loopVol), (float)introDur));
                }
                else
                {
                    StartCoroutine(StartLoopAfter(_loopSrc, intro.length, Mathf.Clamp01(loopVol), loopFade));
                }
            }
        }
        else
        {
            PlayLoop(loop, loopVol, loopFade);
        }
    }

    public void PlayLoop(AudioClip loop, float volume = 1f, float fadeIn = 0f)
    {
        if (!loop) return;
        StopAllMusicImmediate();

        _loopSrc.clip = loop;
        _loopSrc.loop = loopMusic;
        _loopSrc.volume = 0f;
        _loopSrc.Play();

        if (fadeIn > 0f)
            StartCoroutine(FadeVolume(_loopSrc, 0f, Mathf.Clamp01(volume), fadeIn));
        else
            _loopSrc.volume = Mathf.Clamp01(volume);
    }

    public void StopMusic(float fadeOut = 0f)
    {
        if (fadeOut <= 0f) { StopAllMusicImmediate(); return; }
        if (_introSrc && _introSrc.isPlaying) StartCoroutine(FadeVolume(_introSrc, _introSrc.volume, 0f, fadeOut, stopWhenDone: true));
        if (_loopSrc && _loopSrc.isPlaying) StartCoroutine(FadeVolume(_loopSrc, _loopSrc.volume, 0f, fadeOut, stopWhenDone: true));
    }

    public void SetAmbientEnabled(bool enabled)
    {
        ambientEnabled = enabled;
        if (enabled)
        {
            if (_ambientLoopCo == null && ambientClips.Count > 0)
                _ambientLoopCo = StartCoroutine(AmbientLoop());
        }
        else
        {
            if (_ambientLoopCo != null) { StopCoroutine(_ambientLoopCo); _ambientLoopCo = null; }
        }
    }

    public void PlayAmbientNow() => PlayOneAmbient();

    void StopAllMusicImmediate()
    {
        if (_introSrc)
        {
            _introSrc.Stop();
            _introSrc.clip = null;
            _introSrc.volume = 0f;
        }
        if (_loopSrc)
        {
            _loopSrc.Stop();
            _loopSrc.clip = null;
            _loopSrc.volume = 0f;
        }
    }

    IEnumerator StartLoopAfter(AudioSource loopSrc, float delay, float targetVol, float fadeIn)
    {
        float t = 0f;
        while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }
        loopSrc.Play();
        if (fadeIn > 0f) yield return FadeVolume(loopSrc, 0f, targetVol, fadeIn);
        else loopSrc.volume = targetVol;
        if (_introSrc) { _introSrc.Stop(); _introSrc.clip = null; _introSrc.volume = 0f; }
    }

    IEnumerator FadeVolumeAtTime(AudioSource src, float targetVol, float dur, float startDelay)
    {
        float t = 0f;
        while (t < startDelay) { t += Time.unscaledDeltaTime; yield return null; }
        yield return FadeVolume(src, 0f, targetVol, dur);
        if (_introSrc) { _introSrc.Stop(); _introSrc.clip = null; _introSrc.volume = 0f; }
    }

    IEnumerator SetVolumeAtTime(AudioSource src, float vol, float startDelay)
    {
        float t = 0f;
        while (t < startDelay) { t += Time.unscaledDeltaTime; yield return null; }
        src.volume = vol;
        if (_introSrc) { _introSrc.Stop(); _introSrc.clip = null; _introSrc.volume = 0f; }
    }

    IEnumerator FadeVolume(AudioSource src, float from, float to, float duration, bool stopWhenDone = false)
    {
        if (!src) yield break;
        float t = 0f;
        duration = Mathf.Max(0.0001f, duration);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            src.volume = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        src.volume = to;
        if (stopWhenDone) src.Stop();
    }

    IEnumerator AmbientLoop()
    {
        while (ambientEnabled && ambientClips != null && ambientClips.Count > 0)
        {
            float wait = Random.Range(ambientIntervalSeconds.x, ambientIntervalSeconds.y);
            if (wait < 0f) wait = 0f;
            float t = 0f;
            while (t < wait && ambientEnabled) { t += Time.unscaledDeltaTime; yield return null; }
            if (ambientEnabled) PlayOneAmbient();
        }
        _ambientLoopCo = null;
    }

    void PlayOneAmbient()
    {
        if (ambientClips == null || ambientClips.Count == 0) return;

        AudioSource voice = null;
        for (int i = 0; i < _ambientPool.Count; i++)
            if (!_ambientPool[i].isPlaying) { voice = _ambientPool[i]; break; }
        if (voice == null) return;

        var clip = ambientClips[Random.Range(0, ambientClips.Count)];
        if (!clip) return;

        float vol = Random.Range(ambientVolumeRange.x, ambientVolumeRange.y);
        float pit = Random.Range(ambientPitchRange.x, ambientPitchRange.y);

        voice.spatialBlend = 0f;
        voice.transform.localPosition = Vector3.zero;
        voice.pitch = pit;
        voice.volume = Mathf.Clamp01(vol);
        voice.PlayOneShot(clip, 1f);
    }
}
