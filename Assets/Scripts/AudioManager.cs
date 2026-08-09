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
    TileClick
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
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private SoundGroup[] soundList;

    public void PlaySound(SoundType type, float volume = 1f)
    {
        SoundGroup group = soundList[(int)type];
        AudioClip[] clips = group.Sounds;
        AudioClip clipToPlay = clips[Random.Range(0, clips.Length)];
        audioSource.pitch = 1f + Random.Range(-group.PitchVariation, group.PitchVariation);
        audioSource.PlayOneShot(clipToPlay, volume);
    }
}
