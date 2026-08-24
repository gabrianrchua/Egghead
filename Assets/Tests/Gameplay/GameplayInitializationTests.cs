using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Egghead.SaveSystem;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class GameplayInitializationTests
{
    private object saveManager;
    private TaskCompletionSource<SaveData> pendingLoad;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        pendingLoad?.TrySetResult(NewGameData());
        yield return new WaitForSeconds(0.6f);

        SetStaticField(FindType("GameplayBootstrap"), "loadSaveOverride", null);

        if (saveManager != null && (UnityEngine.Object)saveManager != null)
        {
            SetField(saveManager, "loadGameOverride", null);
            Invoke(saveManager, "InvalidateSaveCache");
        }
    }

    [UnityTest]
    public IEnumerator DelayedLoadKeepsGameplayBlankAndBlockedThroughTileIntro()
    {
        int loadCount = 0;
        pendingLoad = new TaskCompletionSource<SaveData>();
        PrepareMainSceneLoad(() =>
        {
            loadCount++;
            return pendingLoad.Task;
        });

        yield return null;

        object bootstrap = FindComponent(FindType("GameplayBootstrap"));
        object gameManager = FindComponent(FindType("GameManager"));
        object levelManager = FindComponent(FindType("LevelManager"));
        object uiManager = FindComponent(FindType("UIManager"));
        object touchscreen = FindComponent(FindType("TouchscreenHandler"));

        Assert.That(loadCount, Is.EqualTo(1));
        AssertGameplayPresentationIsBlank(uiManager);
        Assert.That(((Component)gameManager).transform.childCount, Is.Zero);
        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Initializing"));
        Assert.That(((Behaviour)touchscreen).enabled, Is.False);
        Assert.That(GetField<Button>(uiManager, "shuffleButton").interactable, Is.False);

        Invoke(gameManager, "ShuffleTiles");
        Invoke(gameManager, "TrySubmitCurrentWord");
        Invoke(gameManager, "DeselectAllTiles");
        Assert.That(((Component)gameManager).transform.childCount, Is.Zero);
        Assert.That(GetProperty<int>(levelManager, "TotalScore"), Is.Zero);

        SaveData savedGame = SavedGameData(2400);
        pendingLoad.SetResult(savedGame);
        yield return null;

        Assert.That(((Component)gameManager).transform.childCount, Is.EqualTo(52));
        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Initializing"));
        Assert.That(GetProperty<bool>(bootstrap, "IsInitialized"), Is.False);
        Assert.That(GetProperty<int>(levelManager, "TotalScore"), Is.EqualTo(2400));
        Assert.That(GetField<TextMeshProUGUI>(uiManager, "scoreText").text, Is.EqualTo("2,400"));
        Assert.That(((Behaviour)touchscreen).enabled, Is.False);

        yield return new WaitForSeconds(0.2f);
        Invoke(gameManager, "ShuffleTiles");
        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Initializing"));

        yield return WaitUntil(() => GetProperty<bool>(bootstrap, "IsInitialized"));

        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Idle"));
        Assert.That(((Behaviour)touchscreen).enabled, Is.True);
        Assert.That(GetField<Button>(uiManager, "shuffleButton").interactable, Is.True);
        AssertBoardMatches(gameManager, savedGame.LetterTileData);
    }

    [UnityTest]
    public IEnumerator NewGameInitializesOnlyAfterRevealCompletes()
    {
        PrepareMainSceneLoad(() => Task.FromResult(NewGameData()));
        yield return null;

        object bootstrap = FindComponent(FindType("GameplayBootstrap"));
        object gameManager = FindComponent(FindType("GameManager"));
        object levelManager = FindComponent(FindType("LevelManager"));

        Assert.That(((Component)gameManager).transform.childCount, Is.EqualTo(52));
        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Initializing"));
        yield return WaitUntil(() => GetProperty<bool>(bootstrap, "IsInitialized"));

        Assert.That(GetProperty<int>(levelManager, "Level"), Is.EqualTo(1));
        Assert.That(GetProperty<int>(levelManager, "TotalScore"), Is.Zero);
        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Idle"));
    }

    [UnityTest]
    public IEnumerator FailedLoadShowsModalAndRetryStartsOneRecoverableAttempt()
    {
        int loadCount = 0;
        Func<Task<SaveData>> load = () =>
        {
            loadCount++;
            return Task.FromException<SaveData>(new InvalidOperationException("injected startup failure"));
        };
        PrepareMainSceneLoad(load);
        LogAssert.Expect(LogType.Error, "Gameplay initialization failed: injected startup failure");

        yield return null;

        object bootstrap = FindComponent(FindType("GameplayBootstrap"));
        object gameManager = FindComponent(FindType("GameManager"));
        object uiManager = FindComponent(FindType("UIManager"));
        object modal = FindComponent(FindType("Modal"));

        Assert.That(loadCount, Is.EqualTo(1));
        AssertGameplayPresentationIsBlank(uiManager);
        Assert.That(((Component)gameManager).transform.childCount, Is.Zero);
        Assert.That(GetProperty<bool>(modal, "IsOpen"), Is.True);
        Assert.That(GetField<TextMeshProUGUI>(modal, "promptText").text,
            Is.EqualTo("We couldn't load your game. Try again or return to the main menu."));
        Assert.That(GetField<TextMeshProUGUI>(modal, "negativeActionText").text, Is.EqualTo("Main Menu"));
        Assert.That(GetField<TextMeshProUGUI>(modal, "positiveActionText").text, Is.EqualTo("Retry"));

        pendingLoad = new TaskCompletionSource<SaveData>();
        SetStaticField(FindType("GameplayBootstrap"), "loadSaveOverride", (Func<Task<SaveData>>)(() =>
        {
            loadCount++;
            return pendingLoad.Task;
        }));

        Invoke(modal, "OnPositiveActionClick");
        Invoke(modal, "OnPositiveActionClick");
        yield return WaitUntil(() => loadCount == 2);

        Assert.That(GetProperty<bool>(bootstrap, "IsInitialized"), Is.False);
        pendingLoad.SetResult(NewGameData());
        yield return WaitUntil(() => GetProperty<bool>(bootstrap, "IsInitialized"));

        Assert.That(loadCount, Is.EqualTo(2));
        Assert.That(GetField(gameManager, "boardOperationState").ToString(), Is.EqualTo("Idle"));
    }

    [UnityTest]
    public IEnumerator FailedLoadMainMenuActionReturnsToTitle()
    {
        PrepareMainSceneLoad(() =>
            Task.FromException<SaveData>(new InvalidOperationException("injected navigation failure")));
        LogAssert.Expect(LogType.Error, "Gameplay initialization failed: injected navigation failure");
        yield return null;

        object modal = FindComponent(FindType("Modal"));
        saveManager = FindComponent(FindType("SaveManager"));
        SetField(saveManager, "loadGameOverride", (Func<Task<SaveData>>)(() => Task.FromResult(NewGameData())));
        Invoke(saveManager, "InvalidateSaveCache");

        Invoke(modal, "OnNegativeActionClick");
        yield return WaitUntil(() => SceneManager.GetActiveScene().name == "Title");
        yield return null;

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Title"));
    }

    [UnityTest]
    public IEnumerator ConcurrentCacheMissesShareLoadAndFailureCanRetry()
    {
        TaskCompletionSource<SaveData> bootstrapLoad = new();
        SetStaticField(FindType("GameplayBootstrap"), "loadSaveOverride",
            (Func<Task<SaveData>>)(() => bootstrapLoad.Task));
        SceneManager.LoadScene("Main");
        yield return null;

        object bootstrap = FindComponent(FindType("GameplayBootstrap"));
        saveManager = FindComponent(FindType("SaveManager"));
        pendingLoad = new TaskCompletionSource<SaveData>();
        int loadCount = 0;
        SetField(saveManager, "loadGameOverride", (Func<Task<SaveData>>)(() =>
        {
            loadCount++;
            return pendingLoad.Task;
        }));
        Invoke(saveManager, "InvalidateSaveCache");

        Task<SaveData> first = (Task<SaveData>)Invoke(saveManager, "GetCurrentSaveData");
        Task<SaveData> second = (Task<SaveData>)Invoke(saveManager, "GetCurrentSaveData");

        Assert.That(second, Is.SameAs(first));
        Assert.That(loadCount, Is.EqualTo(1));

        pendingLoad.SetException(new InvalidOperationException("shared failure"));
        yield return WaitUntil(() => first.IsCompleted);
        Assert.That(first.IsFaulted, Is.True);
        Assert.That(second.IsFaulted, Is.True);
        Assert.That(first.Exception.InnerException.Message, Is.EqualTo("shared failure"));

        SetField(saveManager, "loadGameOverride", (Func<Task<SaveData>>)(() =>
        {
            loadCount++;
            return Task.FromResult(NewGameData());
        }));
        Task<SaveData> retry = (Task<SaveData>)Invoke(saveManager, "GetCurrentSaveData");
        yield return WaitUntil(() => retry.IsCompleted);

        Assert.That(retry.IsCompletedSuccessfully, Is.True);
        Assert.That(loadCount, Is.EqualTo(2));

        bootstrapLoad.SetResult(NewGameData());
        yield return WaitUntil(() => GetProperty<bool>(bootstrap, "IsInitialized"));
    }

    private void PrepareMainSceneLoad(Func<Task<SaveData>> load)
    {
        SetStaticField(FindType("GameplayBootstrap"), "loadSaveOverride", load);
        SceneManager.LoadScene("Main");
    }

    private static void AssertGameplayPresentationIsBlank(object uiManager)
    {
        Assert.That(GetField<TextMeshProUGUI>(uiManager, "levelText").text, Is.Empty);
        Assert.That(GetField<TextMeshProUGUI>(uiManager, "scoreText").text, Is.Empty);
        Assert.That(GetField<TextMeshProUGUI>(uiManager, "currentWordText").text, Is.Empty);
        Assert.That(GetField<TextMeshProUGUI>(uiManager, "currentWordScore").text, Is.Empty);
        Assert.That(GetField<Slider>(uiManager, "levelScoreSlider").value, Is.Zero);
        Assert.That(GetField<GameObject>(uiManager, "validWordSubmitButton").activeSelf, Is.False);
        Assert.That(GetField<Image>(uiManager, "validWordBackground").gameObject.activeSelf, Is.False);
    }

    private static void AssertBoardMatches(object gameManager, SavedLetterTileData[][] expected)
    {
        Array columns = (Array)GetField(gameManager, "letterTiles");
        for (int column = 0; column < columns.Length; column++)
        {
            IList tiles = (IList)columns.GetValue(column);
            Assert.That(tiles.Count, Is.EqualTo(expected[column].Length));
            for (int row = 0; row < tiles.Count; row++)
            {
                object data = Invoke(tiles[row], "ToLetterTileData");
                Assert.That(GetPublicField<char>(data, "letter"), Is.EqualTo(expected[column][row].letter));
                Assert.That(GetPublicField<int>(data, "tileType"), Is.EqualTo(expected[column][row].tileType));
            }
        }
    }

    private static SaveData NewGameData()
    {
        return new SaveData
        {
            SchemaVersion = SaveDataValidator.CurrentSchemaVersion,
            Timestamp = DateTime.UtcNow
        };
    }

    private static SaveData SavedGameData(int score)
    {
        SavedLetterTileData[][] board = new SavedLetterTileData[7][];
        for (int column = 0; column < board.Length; column++)
        {
            int rows = column % 2 == 0 ? 7 : 8;
            board[column] = new SavedLetterTileData[rows];
            for (int row = 0; row < rows; row++)
            {
                board[column][row] = new SavedLetterTileData
                {
                    letter = (char)('A' + ((column + row) % 26)),
                    column = column,
                    row = row,
                    tileType = 0
                };
            }
        }

        return new SaveData
        {
            SchemaVersion = SaveDataValidator.CurrentSchemaVersion,
            Score = score,
            Timestamp = DateTime.UtcNow,
            LetterTileData = board
        };
    }

    private static IEnumerator WaitUntil(Func<bool> predicate, float timeout = 10f)
    {
        float startedAt = Time.realtimeSinceStartup;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup - startedAt > timeout)
            {
                Assert.Fail("Timed out waiting for gameplay initialization.");
            }
            yield return null;
        }
    }

    private static Type FindType(string name)
    {
        Type type = null;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(name, false);
            if (type != null)
            {
                break;
            }
        }
        Assert.That(type, Is.Not.Null, $"Could not find runtime type {name}.");
        return type;
    }

    private static object FindComponent(Type type)
    {
        UnityEngine.Object component = UnityEngine.Object.FindAnyObjectByType(type);
        Assert.That(component, Is.Not.Null, $"Could not find component {type.Name}.");
        return component;
    }

    private static object GetField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
        return field.GetValue(target);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        return (T)GetField(target, fieldName);
    }

    private static T GetPublicField<T>(object target, string fieldName)
    {
        return (T)target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public).GetValue(target);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        return (T)target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public).GetValue(target);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static void SetStaticField(Type type, string fieldName, object value)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing static field {fieldName} on {type.Name}.");
        field.SetValue(null, value);
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}.");
        return method.Invoke(target, arguments);
    }
}
