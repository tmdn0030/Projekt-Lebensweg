using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class Local3DSound : MonoBehaviour
{
    [Header("🎵 Audio Einstellungen")]
    [Tooltip("Liste der möglichen Sounds. Bei jedem Start oder Loop wird ein zufälliger Clip gewählt.")]
    public AudioClip[] soundClips;
    [Range(0f, 1f)] public float maxVolume = 1f;
    public bool loop = true;

    [Header("⏳ Loop Cooldown (nur bei Loop aktiv)")]
    [Tooltip("Fester Abstand zwischen Loops (Sekunden).")]
    public float loopCooldown = 0f;
    [Tooltip("Zufällige Variation (+/- Sekunden).")]
    public float loopCooldownRandomRange = 0f;

    [Header("🎚️ Fade Einstellungen")]
    public float fadeInTime = 1f;
    public float fadeOutTime = 1f;

    [Header("🎯 Aktivierung (MainCamera)")]
    public float activationDistance = 10f;
    public float deactivationDistance = 12f;

    [Header("🎨 Editor Visualisierung")]
    public Color activationColor = new Color(0f, 1f, 0.8f, 0.4f);  // Türkis
    public Color deactivationColor = new Color(1f, 0.8f, 0f, 0.3f); // Orange

    private AudioSource audioSource;
    private Transform listener;
    private Coroutine fadeRoutine;
    private bool isActive = false;
    private bool isLooping = false;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D Sound
        audioSource.loop = false;      // wir managen Loop selbst
        audioSource.volume = 0f;
    }

    private void Start()
    {
        if (Camera.main != null)
            listener = Camera.main.transform;
    }

    private void Update()
    {
        if (listener == null) return;

        float distance = Vector3.Distance(listener.position, transform.position);

        if (!isActive && distance <= activationDistance)
        {
            isActive = true;
            fadeRoutine = StartCoroutine(FadeInAndPlay());
        }
        else if (isActive && distance > deactivationDistance)
        {
            isActive = false;
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            StartCoroutine(FadeOutAndStop());
        }
    }

    private IEnumerator FadeInAndPlay()
    {
        if (soundClips == null || soundClips.Length == 0) yield break;

        // Zufälligen Clip wählen
        audioSource.clip = soundClips[Random.Range(0, soundClips.Length)];
        audioSource.Play();

        float elapsed = 0f;
        while (elapsed < fadeInTime)
        {
            audioSource.volume = Mathf.Lerp(0f, maxVolume, elapsed / fadeInTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
        audioSource.volume = maxVolume;

        if (loop)
        {
            isLooping = true;
            StartCoroutine(LoopWithCooldown());
        }
    }

    private IEnumerator FadeOutAndStop()
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;

        while (elapsed < fadeOutTime)
        {
            audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeOutTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        audioSource.volume = 0f;
        audioSource.Stop();
        isLooping = false;
    }

    private IEnumerator LoopWithCooldown()
    {
        while (isActive && loop)
        {
            if (!audioSource.isPlaying)
            {
                // Zufälligen Clip wählen
                audioSource.clip = soundClips[Random.Range(0, soundClips.Length)];
                audioSource.Play();
            }

            yield return new WaitForSeconds(audioSource.clip.length);

            // Cooldown zwischen Loops
            if (loopCooldown > 0f || loopCooldownRandomRange > 0f)
            {
                float delay = loopCooldown;
                if (loopCooldownRandomRange > 0f)
                {
                    delay += Random.Range(-loopCooldownRandomRange, loopCooldownRandomRange);
                    delay = Mathf.Max(0f, delay);
                }
                yield return new WaitForSeconds(delay);
            }
        }
    }

    // 🔍 Editor-Gizmos
    private void OnDrawGizmos()
    {
        Gizmos.color = activationColor;
        Gizmos.DrawWireSphere(transform.position, activationDistance);

        Gizmos.color = deactivationColor;
        Gizmos.DrawWireSphere(transform.position, deactivationDistance);

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(transform.position, 0.2f);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(activationColor.r, activationColor.g, activationColor.b, 0.1f);
        Gizmos.DrawSphere(transform.position, activationDistance);

        Gizmos.color = new Color(deactivationColor.r, deactivationColor.g, deactivationColor.b, 0.1f);
        Gizmos.DrawSphere(transform.position, deactivationDistance);
    }
}
