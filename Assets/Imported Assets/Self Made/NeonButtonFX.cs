using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

// Attach to each menu button. Assign the button's TMP label.
public class NeonButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] TMP_Text label;
    [SerializeField] Color normal = new Color32(159, 176, 214, 255);  // #9FB0D6
    [SerializeField] Color hover  = new Color32(0, 229, 255, 255);    // #00E5FF
    [SerializeField] float scale = 1.08f, speed = 12f;

    Vector3 target = Vector3.one;
    Color targetColor;

    void Awake() { targetColor = normal; if (label) label.color = normal; }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, target, Time.unscaledDeltaTime * speed);
        if (label) label.color = Color.Lerp(label.color, targetColor, Time.unscaledDeltaTime * speed);
    }

    public void OnPointerEnter(PointerEventData e) { target = Vector3.one * scale; targetColor = hover; }
    public void OnPointerExit(PointerEventData e)  { target = Vector3.one;         targetColor = normal; }
}
