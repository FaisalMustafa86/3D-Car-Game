using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class MusicManager : MonoBehaviour
{
    [SerializeField] AudioClip[] playlist;
    [SerializeField] float volume = 0.6f;
    [SerializeField] float fadeDuration = 2f;
    [SerializeField] bool shuffle = false;

    public string CurrentTrackName { get; private set; }

    AudioSource audioSource;
    int currentIndex = -1;
    bool transitioning = false;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.loop = false;
        audioSource.playOnAwake = false;
        audioSource.volume = 0f;
    }

    void Start()
    {
        if (playlist.Length > 0)
            StartCoroutine(PlayNextTrack());
    }

    void Update()
    {
        if (!transitioning && !audioSource.isPlaying && playlist.Length > 0)
            StartCoroutine(PlayNextTrack());
    }

    IEnumerator PlayNextTrack()
    {
        transitioning = true;

        yield return StartCoroutine(Fade(audioSource.volume, 0f));
        audioSource.Stop();

        currentIndex = shuffle
            ? Random.Range(0, playlist.Length)
            : (currentIndex + 1) % playlist.Length;

        audioSource.clip = playlist[currentIndex];
        CurrentTrackName  = playlist[currentIndex].name;
        audioSource.Play();

        yield return StartCoroutine(Fade(0f, volume));

        transitioning = false;
    }

    IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            audioSource.volume = Mathf.Lerp(from, to, elapsed / fadeDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        audioSource.volume = to;
    }
}
