using UnityEngine;
using System.Collections;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance;

    [Header("🎚️ Fade Einstellungen")]
    [Tooltip("Wie lange der Crossfade zwischen Songs dauern soll (in Sekunden).")]
    public float crossfadeDuration = 2f;

    private AudioSource activeSource;
    private AudioSource secondarySource;

    private void Awake()
    {
        // Singleton Setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Zwei AudioSources für Crossfade
        activeSource = gameObject.AddComponent<AudioSource>();
        secondarySource = gameObject.AddComponent<AudioSource>();

        activeSource.loop = true;
        secondarySource.loop = true;
    }

    /// <summary>
    /// Startet sanften Übergang (Crossfade) zu einem neuen Song.
    /// </summary>
    public void PlayMusic(AudioClip newClip)
    {
        if (newClip == null) return;
        if (activeSource.clip == newClip) return; // bereits aktiv

        StopAllCoroutines();
        StartCoroutine(CrossfadeToNewClip(newClip));
    }

    private IEnumerator CrossfadeToNewClip(AudioClip newClip)
    {
        secondarySource.clip = newClip;
        secondarySource.volume = 0f;
        secondarySource.Play();

        float elapsed = 0f;
        float startVolume = activeSource.volume;

        while (elapsed < crossfadeDuration)
        {
            float t = elapsed / crossfadeDuration;
            activeSource.volume = Mathf.Lerp(startVolume, 0f, t);
            secondarySource.volume = Mathf.Lerp(0f, 1f, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        activeSource.Stop();

        // Quellen tauschen
        AudioSource temp = activeSource;
        activeSource = secondarySource;
        secondarySource = temp;

        activeSource.volume = 1f;
        secondarySource.volume = 0f;
    }
}
