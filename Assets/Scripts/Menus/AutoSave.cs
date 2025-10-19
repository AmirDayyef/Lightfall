using UnityEngine;
using UnityEngine.SceneManagement;

public class AutoSaveLastScene : MonoBehaviour
{
    public bool skipSavingHere = false;

    void Start()
    {
        if (skipSavingHere) return;

        string sceneName = SceneManager.GetActiveScene().name;
        if (!string.IsNullOrEmpty(sceneName))
        {
            PlayerPrefs.SetString(MainMenuController.LastSceneKey, sceneName);
            PlayerPrefs.Save();
#if UNITY_EDITOR
            Debug.Log($"[AutoSaveLastScene] Saved last scene: {sceneName}");
#endif
        }
    }
}
