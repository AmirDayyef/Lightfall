using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    public const string LastSceneKey = "last_played_scene";

    public string newGameSceneName = "Level0";

    public Button continueButton;

    public bool useAsyncLoading = true;

    void Awake()
    {
        bool hasSave = PlayerPrefs.HasKey(LastSceneKey) && !string.IsNullOrEmpty(PlayerPrefs.GetString(LastSceneKey));
        if (continueButton) continueButton.interactable = hasSave;
    }


    public void OnClickContinue()
    {
        if (!PlayerPrefs.HasKey(LastSceneKey))
        {
            Debug.LogWarning("[MainMenu] No last scene found; starting new game instead.");
            LoadScene(newGameSceneName);
            return;
        }

        var sceneName = PlayerPrefs.GetString(LastSceneKey, newGameSceneName);
        if (!SceneExistsInBuild(sceneName))
        {
            Debug.LogWarning($"[MainMenu] Saved scene '{sceneName}' not in Build Settings; starting new game.");
            sceneName = newGameSceneName;
        }

        LoadScene(sceneName);
    }

    public void OnClickNewGame()
    {
        PlayerPrefs.DeleteKey(LastSceneKey);
        PlayerPrefs.Save();
        LoadScene(newGameSceneName);
    }

    public void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }


    void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[MainMenu] Scene name is empty.");
            return;
        }

        if (!SceneExistsInBuild(sceneName))
        {
            Debug.LogError($"[MainMenu] Scene '{sceneName}' is not in Build Settings.");
            return;
        }

        if (useAsyncLoading)
        {
            StartCoroutine(LoadAsync(sceneName));
        }
        else
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }

    System.Collections.IEnumerator LoadAsync(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        op.allowSceneActivation = true;
        while (!op.isDone)
            yield return null;
    }

    static bool SceneExistsInBuild(string sceneName)
    {
        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName) return true;
        }
        return false;
    }
}
