using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenu : MonoBehaviour
{
    [SerializeField] GameObject pauseMenu;

   
    public void Home()
    {
        SceneManager.LoadScene("Main Menu");
        Time.timeScale = 1;
        Debug.Log("Working");

    }

    public void Resume()
    {
        pauseMenu.SetActive(false);
        Time.timeScale = 1;

    }
    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        Time.timeScale = 1;

    }
}
