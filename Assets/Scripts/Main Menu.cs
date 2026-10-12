using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public GameObject settingsPanel;

    public void StartGame()
    {
        SceneManager.LoadScene(1); // build index of your game scene
    }

   

    public void QuitGame()
    {
        Application.Quit();
    }
}




