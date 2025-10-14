using UnityEngine;
using TMPro;

public class FPSDisplay : MonoBehaviour
{
    public TextMeshProUGUI fpsText;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (fpsText == null) return;

        float fps = 1f / Time.unscaledDeltaTime;
        fpsText.text = $"FPS: {Mathf.RoundToInt(fps)}";
    }
}
