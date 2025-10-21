using UnityEngine;
using UnityEngine.UI;
using TMPro; // falls du TextMeshPro verwendest

public class SliderValueDisplay : MonoBehaviour
{
    public Slider slider;
    public TMP_Text valueText; // Wenn du TextMeshPro nutzt
    // public Text valueText; // Wenn du den alten UI-Text benutzt, nimm diese Zeile stattdessen

    void Start()
    {
        UpdateText(slider.value);
        slider.onValueChanged.AddListener(UpdateText);
    }

    void UpdateText(float value)
    {
        if (valueText != null)
            valueText.text = value.ToString("0.00"); // z. B. 0.45
    }
}
