using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelLoad : MonoBehaviour
{
    public Button[] button;
    public GameObject levelButtons;

    private void Awake()
    {
        ButtonToArray();
       
    }


    public void OpenLevel(int levelId)
    {
        string levelname = "Level " + levelId;
        SceneManager.LoadScene(levelname);
    }

    void ButtonToArray()
    {
        int childCount = levelButtons.transform.childCount;
        button = new Button[childCount];
        for (int i = 0; i < childCount; i++)
        {
            button[i] = levelButtons.transform.GetChild(i).gameObject.GetComponent<Button>();

            int levelIndex = i + 1;

            button[i].onClick.AddListener(() => OpenLevel(levelIndex));
             
        }

       
    }
}
