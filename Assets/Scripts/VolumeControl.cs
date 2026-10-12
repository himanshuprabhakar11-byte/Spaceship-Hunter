using UnityEngine;

public class VolumeControl : MonoBehaviour
{
    void Start()
    {
        if (PlayerPrefs.GetInt("Muted") == 1)
        {
            AudioListener.volume = 0;
        }
        else
        {
            AudioListener.volume = 1;
        }
    }


    public void MuteSound()
    {
        AudioListener.volume = 0;
        PlayerPrefs.SetInt("Muted", 1);
    }


    public void UnmuteSound()
    {
        AudioListener.volume = 1;
        PlayerPrefs.SetInt("Muted", 0);
    }
}
