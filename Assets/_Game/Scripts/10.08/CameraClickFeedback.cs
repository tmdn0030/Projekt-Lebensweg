using UnityEngine;

public class CameraClickFeedback : MonoBehaviour
{
    public Transform cameraTransform; // deine Kamera
    public AudioSource clickSource;   // AudioSource mit Klick-Sound
    public float stepDistance = 0.5f; // Abstand pro Klick
    public float pitchMin = 0.95f;    // minimale Tonh�he
    public float pitchMax = 1.05f;    // maximale Tonh�he

    private Vector3 lastClickPos;

    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;

        lastClickPos = cameraTransform.position;
    }

    void Update()
    {
        float distanceSinceLastClick = Vector3.Distance(cameraTransform.position, lastClickPos);

        if (distanceSinceLastClick >= stepDistance)
        {
            // Pitch leicht variieren f�r nat�rlicheres Gef�hl
            clickSource.pitch = Random.Range(pitchMin, pitchMax);
            clickSource.PlayOneShot(clickSource.clip);

            lastClickPos = cameraTransform.position;
        }
    }
}
