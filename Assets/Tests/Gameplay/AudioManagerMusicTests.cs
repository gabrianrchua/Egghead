using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Random = UnityEngine.Random;

public class AudioManagerMusicTests
{
    private MethodInfo selectNextTrackMethod;
    private MethodInfo calculateMusicOutputVolumeMethod;

    [SetUp]
    public void SetUp()
    {
        Type audioManagerType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("AudioManager", false))
            .FirstOrDefault(type => type != null);
        Assert.That(audioManagerType, Is.Not.Null);

        selectNextTrackMethod = audioManagerType.GetMethod(
            "SelectNextMusicTrackIndex",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(selectNextTrackMethod, Is.Not.Null);

        calculateMusicOutputVolumeMethod = audioManagerType.GetMethod(
            "CalculateMusicOutputVolume",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(calculateMusicOutputVolumeMethod, Is.Not.Null);
    }

    [Test]
    public void InitialSelectionCanUseEveryTrack()
    {
        const int trackCount = 10;
        HashSet<int> selectedTracks = new();
        Random.State originalRandomState = Random.state;

        try
        {
            Random.InitState(12345);
            for (int selection = 0; selection < 1000; selection++)
            {
                selectedTracks.Add(SelectNextTrack(trackCount, -1));
            }
        }
        finally
        {
            Random.state = originalRandomState;
        }

        Assert.That(selectedTracks, Is.EquivalentTo(Enumerable.Range(0, trackCount)));
    }

    [Test]
    public void ConsecutiveSelectionNeverRepeatsPreviousTrack()
    {
        const int trackCount = 10;
        Random.State originalRandomState = Random.state;

        try
        {
            Random.InitState(67890);
            for (int previousTrack = 0; previousTrack < trackCount; previousTrack++)
            {
                for (int selection = 0; selection < 100; selection++)
                {
                    int nextTrack = SelectNextTrack(trackCount, previousTrack);
                    Assert.That(nextTrack, Is.InRange(0, trackCount - 1));
                    Assert.That(nextTrack, Is.Not.EqualTo(previousTrack));
                }
            }
        }
        finally
        {
            Random.state = originalRandomState;
        }
    }

    [Test]
    public void SingleTrackSelectionReturnsOnlyTrack()
    {
        Assert.That(SelectNextTrack(1, 0), Is.Zero);
    }

    [Test]
    public void MusicOutputCombinesSavedVolumeAndFadeProgress()
    {
        Assert.That(CalculateMusicOutputVolume(0.8f, 0.5f, 0.7f), Is.EqualTo(0.28f).Within(0.0001f));
        Assert.That(CalculateMusicOutputVolume(0f, 1f, 0.7f), Is.Zero);
        Assert.That(CalculateMusicOutputVolume(1f, 0f, 0.7f), Is.Zero);
        Assert.That(CalculateMusicOutputVolume(1f, 1f, 2f), Is.EqualTo(1f));
    }

    private int SelectNextTrack(int trackCount, int previousTrack)
    {
        return (int)selectNextTrackMethod.Invoke(null, new object[] { trackCount, previousTrack });
    }

    private float CalculateMusicOutputVolume(float volume, float fadeMultiplier, float outputMultiplier)
    {
        return (float)calculateMusicOutputVolumeMethod.Invoke(
            null,
            new object[] { volume, fadeMultiplier, outputMultiplier });
    }
}
