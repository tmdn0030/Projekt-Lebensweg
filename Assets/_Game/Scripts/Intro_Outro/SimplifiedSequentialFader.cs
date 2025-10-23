using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class SimplifiedSequentialFader : MonoBehaviour
{
    [Header("UI-Objekte (in Reihenfolge)")]
    [Tooltip("Liste aller UI-Objekte, die nacheinander angezeigt werden sollen.")]
    public List<GameObject> objectsToProcess = new List<GameObject>();

    [Header("Fade Einstellungen")]
    [Tooltip("Dauer des Überblendens (in Sekunden).")]
    public float fadeDuration = 1f;

    private int currentIndex = 0;
    private bool isAnimating = false;
    private List<CanvasGroup> canvasGroups = new List<CanvasGroup>();

    private void Start()
    {
        // CanvasGroups vorbereiten
        canvasGroups.Clear();

        for (int i = 0; i < objectsToProcess.Count; i++)
        {
            GameObject go = objectsToProcess[i];
            if (go == null)
            {
                canvasGroups.Add(null);
                continue;
            }

            // CanvasGroup sicherstellen
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = go.AddComponent<CanvasGroup>();

            canvasGroups.Add(cg);

            // Sichtbarkeit setzen
            if (i == 0)
            {
                cg.alpha = 0f; // Erster beginnt unsichtbar, wird gleich eingeblendet
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            else
            {
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
        }

        // Erstes Objekt automatisch einblenden
        if (canvasGroups.Count > 0)
            StartCoroutine(FadeInFirst());
    }

    private void Update()
    {
        if (isAnimating) return;

        bool clicked = false;

        // Mausklick oder Touch
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            clicked = true;
        else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            clicked = true;

        if (clicked)
            TriggerNext();
    }

    public void TriggerNext()
    {
        if (isAnimating) return;
        if (currentIndex >= canvasGroups.Count - 1) return; // Letztes erreicht

        StartCoroutine(FadeOutCurrentFadeInNext());
    }

    IEnumerator FadeInFirst()
    {
        isAnimating = true;

        CanvasGroup first = canvasGroups[0];
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            first.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }

        first.alpha = 1f;
        first.interactable = true;
        first.blocksRaycasts = true;

        isAnimating = false;
    }

    IEnumerator FadeOutCurrentFadeInNext()
    {
        isAnimating = true;

        CanvasGroup current = canvasGroups[currentIndex];
        CanvasGroup next = canvasGroups[currentIndex + 1];

        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            if (current != null)
                current.alpha = Mathf.Lerp(1f, 0f, t);
            if (next != null)
                next.alpha = Mathf.Lerp(0f, 1f, t);

            yield return null;
        }

        if (current != null)
        {
            current.alpha = 0f;
            current.interactable = false;
            current.blocksRaycasts = false;
        }

        if (next != null)
        {
            next.alpha = 1f;
            next.interactable = true;
            next.blocksRaycasts = true;
        }

        currentIndex++;
        isAnimating = false;
    }
}
