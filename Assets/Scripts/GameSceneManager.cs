

//using System.Collections;
//using UnityEngine;
//using UnityEngine.SceneManagement;

//public class GameSceneManager : MonoBehaviour
//{
//    public GameObject targetObject;

//    void Update()
//    {
//        // When the object is destroyed, it becomes null
//        if (targetObject == null)
//        {
//            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
//            enabled = false; // Disables this script so it doesn't try to load repeatedly
//        }
//    }

//    public void ReloadLevel()
//    {
//        StartCoroutine(ReloadLevelRoutine());
//    }

//    IEnumerator ReloadLevelRoutine()
//    {
//        yield return new WaitForSeconds(1f);
//        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
//    }
//}


using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSceneManager : MonoBehaviour
{
    public GameObject targetObject;

    [SerializeField] private int mainMenuSceneIndex = 0;
    [SerializeField] private float reloadDelay = 1f;

    private bool isLoading;

    void Update()
    {
        if (isLoading || targetObject != null) return;

        LoadNextOrMenu();
    }

    void LoadNextOrMenu()
    {
        isLoading = true;

        int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;

        if (nextIndex < SceneManager.sceneCountInBuildSettings)
            SceneManager.LoadScene(nextIndex);
        else

            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            SceneManager.LoadScene(mainMenuSceneIndex);
            
    }

    public void ReloadLevel()
    {
        if (isLoading) return;
        isLoading = true;
        StartCoroutine(ReloadLevelRoutine());
    }

    IEnumerator ReloadLevelRoutine()
    {
        yield return new WaitForSeconds(reloadDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}






















