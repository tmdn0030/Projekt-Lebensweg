using UnityEngine;
using TMPro;

public class SimpleFPSCounter : MonoBehaviour
{
    public TMP_Text fpsText;   // Dein TMP Text-Objekt im Canvas
    public float updateInterval = 0.5f;

    private float timer;

    void Update()
    {
        if (fpsText == null) return;

        timer += Time.unscaledDeltaTime;

        if (timer >= updateInterval)
        {
            float fps = 1f / Time.unscaledDeltaTime;
            fpsText.text = "FPS: " + Mathf.RoundToInt(fps);
            timer = 0f;
        }
    }
}
