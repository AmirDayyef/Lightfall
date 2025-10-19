using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Audio;

[DisallowMultipleComponent]
public class PauseMenu : MonoBehaviour
{
    public string mainMenuSceneName = "MainMenu";

    public GameObject pausePanel;
    public GameObject optionsPanel;

    public Button resumeButton;
    public Button optionsButton;
    public Button exitToMenuButton;

    public Slider musicSlider;
    public Slider sfxSlider;
    public Button backButton;

    public AudioMixer mainMixer;
    public string musicParam = "MusicVol";
    public string sfxParam = "SFXVol";

    public bool manageCursor = true;
    public bool pauseAudioListener = true;
    public KeyCode pauseKey = KeyCode.Escape;

    public static bool IsPaused { get; private set; }

    void Awake()
    {
        if (resumeButton) resumeButton.onClick.AddListener(Resume);
        if (optionsButton) optionsButton.onClick.AddListener(OpenOptions);
        if (exitToMenuButton) exitToMenuButton.onClick.AddListener(ExitToMenu);
        if (backButton) backButton.onClick.AddListener(CloseOptions);

        if (musicSlider) musicSlider.onValueChanged.AddListener(SetMusicVolume);
        if (sfxSlider) sfxSlider.onValueChanged.AddListener(SetSFXVolume);

        LoadSavedVolumes();
        SetPaused(false, true);
    }

    void Update()
    {
        if (Input.GetKeyDown(pauseKey))
            TogglePause();
    }


    public void TogglePause() => SetPaused(!IsPaused, false);
    public void Resume() => SetPaused(false, false);

    void SetPaused(bool pause, bool silent)
    {
        IsPaused = pause;
        Time.timeScale = pause ? 0f : 1f;
        if (pauseAudioListener) AudioListener.pause = pause;

        if (pausePanel) pausePanel.SetActive(pause && !optionsPanel.activeSelf);
        if (!silent && optionsPanel) optionsPanel.SetActive(false);

        if (manageCursor)
        {
            Cursor.visible = pause;
            Cursor.lockState = pause ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    public void OpenOptions()
    {
        if (pausePanel) pausePanel.SetActive(false);
        if (optionsPanel) optionsPanel.SetActive(true);
    }

    public void CloseOptions()
    {
        if (optionsPanel) optionsPanel.SetActive(false);
        if (pausePanel) pausePanel.SetActive(true);
    }

    public void ExitToMenu()
    {
        SetPaused(false, true);
        SceneManager.LoadScene(mainMenuSceneName);
    }


    void SetMusicVolume(float value)
    {
        if (mainMixer)
            mainMixer.SetFloat(musicParam, Mathf.Log10(Mathf.Clamp(value, 0.001f, 1f)) * 20f);

        PlayerPrefs.SetFloat("MusicVol", value);
    }

    void SetSFXVolume(float value)
    {
        if (mainMixer)
            mainMixer.SetFloat(sfxParam, Mathf.Log10(Mathf.Clamp(value, 0.001f, 1f)) * 20f);

        PlayerPrefs.SetFloat("SFXVol", value);
    }

    void LoadSavedVolumes()
    {
        float musicVal = PlayerPrefs.GetFloat("MusicVol", 1f);
        float sfxVal = PlayerPrefs.GetFloat("SFXVol", 1f);
        if (musicSlider) musicSlider.value = musicVal;
        if (sfxSlider) sfxSlider.value = sfxVal;
        SetMusicVolume(musicVal);
        SetSFXVolume(sfxVal);
    }

    void OnDisable()
    {
        if (IsPaused)
        {
            Time.timeScale = 1f;
            if (pauseAudioListener) AudioListener.pause = false;
            IsPaused = false;
        }
    }
}
