using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SoundType
{
    TileBurn,
    Shuffle,
    TileSelected,
    WordRegular,
    WordBonus,
    WordGold,
    WordDiamond,
    TilesStart,
    TileClick,
    Lose,
    InvalidWord
}

[System.Serializable]
public struct SoundGroup
{
    [SerializeField] private string name;
    [SerializeField] private AudioClip[] sounds;
    [SerializeField] private float pitchVariation;

    public readonly AudioClip[] Sounds { get { return sounds; } }
    public readonly float PitchVariation { get { return pitchVariation; } }
}

[RequireComponent(typeof(AudioSource))]
public class AudioManager : Singleton<AudioManager>
{
    [SerializeField] private AudioSource soundAudioSource;
    [SerializeField] private AudioSource musicAudioSource;
    [SerializeField] private AudioClip[] musicTracks;
    [SerializeField] private float minimumSilenceSeconds = 60f;
    [SerializeField] private float maximumSilenceSeconds = 180f;
    [SerializeField] private float musicFadeSeconds = 4f;
    [SerializeField, Range(0f, 1f)] private float musicOutputMultiplier = 0.7f;
    [SerializeField] private SoundGroup[] soundList;

    private readonly List<AudioClip> validMusicTracks = new();
    private Coroutine soundDebounceCoroutine;
    private Coroutine musicDebounceCoroutine;
    private float musicVolume;
    private float musicFadeMultiplier;
    private int previousMusicTrackIndex = -1;

    private const float debounceDelay = 0.5f;

    private void Start()
    {
        float soundVolume = PlayerPrefs.GetFloat("SoundVolume", 1f);
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("MusicVolume", 1f));

        soundAudioSource.volume = soundVolume;
        SetMusicFadeMultiplier(0f);

        UIManager uiManager = UIManager.Instance;
        uiManager.SetSoundVolumeSliderValue(soundVolume);
        uiManager.SetMusicVolumeSliderValue(musicVolume);

        PopulateValidMusicTracks();
        if (validMusicTracks.Count == 0)
        {
            Debug.LogWarning("Music playback is disabled because AudioManager has no valid music tracks.");
            return;
        }

        if (validMusicTracks.Count == 1)
        {
            Debug.LogWarning("AudioManager has only one valid music track, so consecutive repeats cannot be avoided.");
        }

        StartCoroutine(PlayMusicLoop());
    }

    /// <summary>
    /// UI dynamic method called when sound volume slider is changed
    /// (every frame while held down, must be debounced)
    /// </summary>
    /// <param name="value">The new volume value</param>
    public void OnSoundSliderChanged(float value)
    {
        // immediately update volume
        soundAudioSource.volume = value;

        // debounce then save sound value
        if (soundDebounceCoroutine != null)
        {
            StopCoroutine(soundDebounceCoroutine);
        }
        soundDebounceCoroutine = StartCoroutine(DebounceSaveSound(value));
    }

    /// <summary>
    /// UI dynamic method called when music volume slider is changed
    /// (every frame while held down, must be debounced)
    /// </summary>
    /// <param name="value">The new volume value</param>
    public void OnMusicSliderChanged(float value)
    {
        // immediately update volume
        musicVolume = Mathf.Clamp01(value);
        UpdateMusicVolume();

        // debounce then save music value
        if (musicDebounceCoroutine != null)
        {
            StopCoroutine(musicDebounceCoroutine);
        }
        musicDebounceCoroutine = StartCoroutine(DebounceSaveMusic(musicVolume));
    }

    public void PlaySound(SoundType type, float volume = 1f)
    {
        SoundGroup group = soundList[(int)type];
        AudioClip[] clips = group.Sounds;
        AudioClip clipToPlay = clips[Random.Range(0, clips.Length)];
        soundAudioSource.pitch = 1f + Random.Range(-group.PitchVariation, group.PitchVariation);
        soundAudioSource.PlayOneShot(clipToPlay, volume);
    }

    private IEnumerator DebounceSaveSound(float volume)
    {
        yield return new WaitForSeconds(debounceDelay);
        PlayerPrefs.SetFloat("SoundVolume", volume);
        soundDebounceCoroutine = null;
        Debug.Log($"Sound volume saved: {volume}");
    }

    private IEnumerator DebounceSaveMusic(float volume)
    {
        yield return new WaitForSeconds(debounceDelay);
        PlayerPrefs.SetFloat("MusicVolume", volume);
        musicDebounceCoroutine = null;
        Debug.Log($"Music volume saved: {volume}");
    }

    private void PopulateValidMusicTracks()
    {
        validMusicTracks.Clear();
        if (musicTracks == null)
        {
            return;
        }

        foreach (AudioClip track in musicTracks)
        {
            if (track != null && !validMusicTracks.Contains(track))
            {
                validMusicTracks.Add(track);
            }
        }
    }

    private IEnumerator PlayMusicLoop()
    {
        while (true)
        {
            int trackIndex = SelectNextMusicTrackIndex(validMusicTracks.Count, previousMusicTrackIndex);
            previousMusicTrackIndex = trackIndex;

            AudioClip track = validMusicTracks[trackIndex];
            musicAudioSource.clip = track;
            musicAudioSource.loop = false;
            SetMusicFadeMultiplier(0f);
            musicAudioSource.Play();

            yield return FadeWhilePlaying(track);

            musicAudioSource.Stop();
            musicAudioSource.clip = null;
            SetMusicFadeMultiplier(0f);

            float minimumSilence = Mathf.Max(0f, minimumSilenceSeconds);
            float maximumSilence = Mathf.Max(minimumSilence, maximumSilenceSeconds);
            yield return new WaitForSecondsRealtime(Random.Range(minimumSilence, maximumSilence));
        }
    }

    private IEnumerator FadeWhilePlaying(AudioClip track)
    {
        float fadeDuration = Mathf.Min(Mathf.Max(0f, musicFadeSeconds), track.length * 0.5f);

        while (musicAudioSource.isPlaying)
        {
            float fadeMultiplier = 1f;
            if (fadeDuration > 0f)
            {
                float elapsed = musicAudioSource.time;
                float remaining = Mathf.Max(0f, track.length - elapsed);

                if (elapsed < fadeDuration)
                {
                    fadeMultiplier = Mathf.SmoothStep(0f, 1f, elapsed / fadeDuration);
                }
                else if (remaining < fadeDuration)
                {
                    fadeMultiplier = Mathf.SmoothStep(0f, 1f, remaining / fadeDuration);
                }
            }

            SetMusicFadeMultiplier(fadeMultiplier);
            yield return null;
        }
    }

    private static int SelectNextMusicTrackIndex(int trackCount, int previousTrackIndex)
    {
        if (trackCount <= 1)
        {
            return 0;
        }

        if (previousTrackIndex < 0 || previousTrackIndex >= trackCount)
        {
            return Random.Range(0, trackCount);
        }

        int trackIndex = Random.Range(0, trackCount - 1);
        if (trackIndex >= previousTrackIndex)
        {
            trackIndex++;
        }
        return trackIndex;
    }

    private void SetMusicFadeMultiplier(float value)
    {
        musicFadeMultiplier = Mathf.Clamp01(value);
        UpdateMusicVolume();
    }

    private void UpdateMusicVolume()
    {
        musicAudioSource.volume = CalculateMusicOutputVolume(
            musicVolume,
            musicFadeMultiplier,
            musicOutputMultiplier);
    }

    private static float CalculateMusicOutputVolume(float volume, float fadeMultiplier, float outputMultiplier)
    {
        return Mathf.Clamp01(volume) * Mathf.Clamp01(fadeMultiplier) * Mathf.Clamp01(outputMultiplier);
    }
}
