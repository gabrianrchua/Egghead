using System.Collections;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

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
    [SerializeField] private SoundGroup[] soundList;

    private Coroutine soundDebounceCoroutine;
    private Coroutine musicDebounceCoroutine;

    private const float debounceDelay = 0.5f;

    private void Start()
    {
        float soundVolume = PlayerPrefs.GetFloat("SoundVolume", 1f);
        float musicVolume = PlayerPrefs.GetFloat("MusicVolume", 1f);

        soundAudioSource.volume = soundVolume;
        musicAudioSource.volume = musicVolume;

        UIManager uiManager = UIManager.Instance;
        uiManager.SetSoundVolumeSliderValue(soundVolume);
        uiManager.SetMusicVolumeSliderValue(musicVolume);
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
        musicAudioSource.volume = value;

        // debounce then save sound value
        if (musicDebounceCoroutine != null)
        {
            StopCoroutine(musicDebounceCoroutine);
        }
        musicDebounceCoroutine = StartCoroutine(DebounceSaveMusic(value));
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
}
