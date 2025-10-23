using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MusicZone : MonoBehaviour
{
    [Header("🎵 Musik-Einstellungen")]
    public AudioClip musicClip;

    [Header("🔊 AudioSource-Parameter")]
    [Range(0f, 1f)] public float volume = 1f;      // jetzt dynamisch anpassbar
    [Range(0f, 1f)] public float spatialBlend = 0f; // 0 = 2D, 1 = 3D
    public bool loop = true;
    public bool playOnAwake = false;
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
    public float minDistance = 1f;
    public float maxDistance = 50f;

    [Header("⚙️ Verhalten")]
    [Tooltip("Wenn true: spielt Musik lokal (z. B. Radio, Soundquelle). Wenn false: über globalen MusicManager mit Crossfade.")]
    public bool useLocalPlayback = false;

    private AudioSource zoneAudioSource;

    private void Awake()
    {
        // Immer eigene AudioSource erzeugen
        zoneAudioSource = gameObject.AddComponent<AudioSource>();
        zoneAudioSource.loop = loop;
        zoneAudioSource.volume = volume;
        zoneAudioSource.spatialBlend = spatialBlend;
        zoneAudioSource.rolloffMode = rolloffMode;
        zoneAudioSource.minDistance = minDistance;
        zoneAudioSource.maxDistance = maxDistance;
        zoneAudioSource.playOnAwake = false; // Play erst manuell

        if (playOnAwake && useLocalPlayback)
            PlayLocalMusic();
    }

    private void Update()
    {
        // Dynamische Lautstärke im Spiel anpassen
        if (zoneAudioSource != null)
            zoneAudioSource.volume = volume;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("MainCamera")) return;

        if (useLocalPlayback)
            PlayLocalMusic();
        else
        {
            if (MusicManager.Instance != null)
                MusicManager.Instance.PlayMusic(musicClip);
            else
                Debug.LogWarning("MusicManager nicht gefunden!");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("MainCamera")) return;

        if (useLocalPlayback && zoneAudioSource.isPlaying)
            zoneAudioSource.Stop();
    }

    public void PlayLocalMusic()
    {
        if (musicClip == null)
        {
            Debug.LogWarning("Kein Musikclip zugewiesen!");
            return;
        }

        zoneAudioSource.clip = musicClip;
        zoneAudioSource.volume = volume; // Lautstärke immer vor Play setzen
        zoneAudioSource.Play();
    }

    // Optional: Lautstärke im Spiel ändern
    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (zoneAudioSource != null)
            zoneAudioSource.volume = volume;
    }
}
