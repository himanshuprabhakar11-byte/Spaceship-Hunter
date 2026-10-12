using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class SpaceMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] GameObject mainPanel;
    [SerializeField] GameObject settingsPanel;

    [Header("Settings Controls")]
    [SerializeField] AudioMixer mixer;              // expose a parameter named "MasterVol"
    [SerializeField] Slider volumeSlider;           // 0.0001 – 1
    [SerializeField] TMP_Dropdown qualityDropdown;
    [SerializeField] Toggle fullscreenToggle;

    [Header("Scene")]
    [SerializeField] string gameScene = "Game";

    const string VolKey = "vol", QualKey = "qual", FullKey = "full";

    void Start()
    {
        qualityDropdown.ClearOptions();
        qualityDropdown.AddOptions(new List<string>(QualitySettings.names));

        volumeSlider.value = PlayerPrefs.GetFloat(VolKey, 0.8f);
        qualityDropdown.value = PlayerPrefs.GetInt(QualKey, QualitySettings.GetQualityLevel());
        fullscreenToggle.isOn = PlayerPrefs.GetInt(FullKey, Screen.fullScreen ? 1 : 0) == 1;

        SetVolume(volumeSlider.value);
        SetQuality(qualityDropdown.value);
        SetFullscreen(fullscreenToggle.isOn);

        volumeSlider.onValueChanged.AddListener(SetVolume);
        qualityDropdown.onValueChanged.AddListener(SetQuality);
        fullscreenToggle.onValueChanged.AddListener(SetFullscreen);

        ShowMain();
    }

    // --- Buttons ---
    public void Play() => SceneManager.LoadScene(gameScene);
    public void ShowSettings() { mainPanel.SetActive(false); settingsPanel.SetActive(true); }
    public void ShowMain()     { settingsPanel.SetActive(false); mainPanel.SetActive(true); }

    public void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // --- Settings ---
    void SetVolume(float v)
    {
        mixer.SetFloat("MasterVol", Mathf.Log10(Mathf.Clamp(v, 0.0001f, 1f)) * 20f);
        PlayerPrefs.SetFloat(VolKey, v);
    }

    void SetQuality(int i)
    {
        QualitySettings.SetQualityLevel(i);
        PlayerPrefs.SetInt(QualKey, i);
    }

    void SetFullscreen(bool on)
    {
        Screen.fullScreen = on;
        PlayerPrefs.SetInt(FullKey, on ? 1 : 0);
    }
}
