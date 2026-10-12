using UnityEngine;
using UnityEngine.InputSystem;

public class TogglePanel : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private GameObject crosshair;

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool isPanelActive = !panel.activeSelf;

            panel.SetActive(isPanelActive);
            crosshair.SetActive(!isPanelActive);

            Time.timeScale = isPanelActive ? 0f : 1f;

            Cursor.lockState = isPanelActive ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isPanelActive;
        }
    }
}