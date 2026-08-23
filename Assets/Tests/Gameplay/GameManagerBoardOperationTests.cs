using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class GameManagerBoardOperationTests
{
    private const string FireCriticalPreference = "ShowFireCriticalModal";

    private Type gameManagerType;
    private Type tileType;
    private Type tilePosType;
    private object gameManager;
    private object uiManager;
    private object levelManager;
    private int originalFireCriticalPreference;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        originalFireCriticalPreference = PlayerPrefs.GetInt(FireCriticalPreference, 1);
        PlayerPrefs.SetInt(FireCriticalPreference, 0);

        SceneManager.LoadScene("Main");
        yield return null;

        gameManagerType = FindType("GameManager");
        Type letterTileType = FindType("LetterTile");
        tileType = letterTileType.GetNestedType("TileType");
        tilePosType = FindType("TilePos");
        gameManager = FindComponent(gameManagerType);
        uiManager = FindComponent(FindType("UIManager"));
        levelManager = FindComponent(FindType("LevelManager"));

        yield return WaitUntil(() => GetBoardOperationState() == "Idle" && GetProperty<int>(levelManager, "Level") > 0);

        SetField(gameManager, "saveGameOverride", (Func<Task>)(() => Task.CompletedTask));
        SetField(gameManager, "deleteDataOverride", (Func<Task>)(() => Task.CompletedTask));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        PlayerPrefs.SetInt(FireCriticalPreference, originalFireCriticalPreference);
        SceneManager.LoadScene("Title");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ShuffleClearsNormalBonusAndFireSelectionBeforeMutation()
    {
        object normalTile = ConfigureTile(0, 0, 'C', "Normal");
        object bonusTile = ConfigureTile(0, 1, 'A', "Bonus");
        object fireTile = ConfigureTile(0, 2, 'T', "Fire");

        ClickTile(0, 0);
        ClickTile(0, 1);
        ClickTile(0, 2);

        Assert.That((IList)GetField(gameManager, "selectedTiles"), Has.Count.EqualTo(3));
        Assert.That(GetField<GameObject>(fireTile, "submitHint").activeSelf, Is.True);

        Invoke(gameManager, "ShuffleTiles");

        Assert.That((IList)GetField(gameManager, "selectedTiles"), Is.Empty);
        Assert.That(GetField<bool>(normalTile, "isSelected"), Is.False);
        Assert.That(GetField<bool>(bonusTile, "isSelected"), Is.False);
        Assert.That(GetField<bool>(fireTile, "isSelected"), Is.False);
        Assert.That(GetField<GameObject>(fireTile, "submitHint").activeSelf, Is.False);
        AssertSelectionUiIsClear();

        int scoreAfterShuffle = GetProperty<int>(levelManager, "TotalScore");
        Invoke(gameManager, "TrySubmitCurrentWord");
        Assert.That(GetProperty<int>(levelManager, "TotalScore"), Is.EqualTo(scoreAfterShuffle));

        yield return WaitUntil(() => GetBoardOperationState() == "Idle");

        Invoke(gameManager, "TrySubmitCurrentWord");
        Assert.That(GetProperty<int>(levelManager, "TotalScore"), Is.EqualTo(scoreAfterShuffle));

        object firstNewTile = GetTile(1, 0);
        ClickTile(1, 0);
        Assert.That((IList)GetField(gameManager, "selectedTiles"), Has.Count.EqualTo(1));
        char firstNewLetter = Invoke<char>(firstNewTile, "GetLetter");
        string expectedWord = firstNewLetter == 'Q' ? "QU" : firstNewLetter.ToString();
        Assert.That(GetProperty<string>(GetField(uiManager, "currentWordText"), "text"), Is.EqualTo(expectedWord));
    }

    [UnityTest]
    public IEnumerator ShuffleWithNoSelectionStillClearsStaleUi()
    {
        Invoke(uiManager, "SetCurrentWord", "stale");
        Invoke(uiManager, "SetCurrentWordScore", 25, Enum.Parse(tileType, "Gold"));

        Invoke(gameManager, "ShuffleTiles");

        Assert.That((IList)GetField(gameManager, "selectedTiles"), Is.Empty);
        AssertSelectionUiIsClear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator OverlayAndRepeatedClicksCannotStartAnotherShuffle()
    {
        UnityEngine.Object[][] originalBoard = SnapshotBoard();
        Invoke(uiManager, "BlockTaps");

        Invoke(gameManager, "ShuffleTiles");

        Assert.That(GetBoardOperationState(), Is.EqualTo("Idle"));
        AssertBoardReferencesEqual(originalBoard, SnapshotBoard());

        Invoke(uiManager, "UnblockTaps");
        Invoke(gameManager, "ShuffleTiles");
        UnityEngine.Object[][] firstShuffleBoard = SnapshotBoard();
        Invoke(gameManager, "ShuffleTiles");

        Assert.That(GetBoardOperationState(), Is.EqualTo("Active"));
        Assert.That(GetField<int>(gameManager, "activeBoardOperationCount"), Is.EqualTo(1));
        Assert.That(GetField<int>(gameManager, "maximumActiveBoardOperationCount"), Is.EqualTo(1));
        AssertBoardReferencesEqual(firstShuffleBoard, SnapshotBoard());
        yield return null;
    }

    [UnityTest]
    public IEnumerator SubmissionWithoutFireBlocksShuffleUntilDropCompletes()
    {
        ConfigureAllTilesAsNormal();
        SetField(levelManager, "heatProbabilityA", 0f);
        SetField(levelManager, "heatProbabilityD", 0f);
        ConfigureValidWord();

        Invoke(gameManager, "TrySubmitCurrentWord");
        UnityEngine.Object[][] submittedBoard = SnapshotBoard();
        Invoke(gameManager, "ShuffleTiles");

        Assert.That(GetBoardOperationState(), Is.EqualTo("Active"));
        AssertBoardReferencesEqual(submittedBoard, SnapshotBoard());

        yield return WaitUntil(() => GetBoardOperationState() == "Idle");

        AssertLogicalCoordinatesMatchTransforms();
    }

    [UnityTest]
    public IEnumerator ExistingFireKeepsOperationLockedThroughSecondDrop()
    {
        ConfigureAllTilesAsNormal();
        ConfigureTile(0, 2, 'F', "Fire");

        Invoke(gameManager, "ShuffleTiles");
        yield return new WaitForSeconds(0.6f);

        Assert.That(GetBoardOperationState(), Is.EqualTo("Active"));
        Assert.That(GetField<int>(gameManager, "activeBoardOperationCount"), Is.EqualTo(1));

        yield return WaitUntil(() => GetBoardOperationState() == "Idle");

        Assert.That(GetField<int>(gameManager, "maximumActiveBoardOperationCount"), Is.EqualTo(1));
        AssertLogicalCoordinatesMatchTransforms();
    }

    [UnityTest]
    public IEnumerator FireAtBottomPermanentlyLocksBoardAfterGameOver()
    {
        ConfigureAllTilesAsNormal();
        ConfigureTile(0, 0, 'F', "Fire");

        Invoke(gameManager, "ShuffleTiles");
        yield return WaitUntil(() => GetBoardOperationState() == "GameOver");

        UnityEngine.Object[][] gameOverBoard = SnapshotBoard();
        Invoke(gameManager, "ShuffleTiles");
        ClickTile(1, 0);

        Assert.That(GetBoardOperationState(), Is.EqualTo("GameOver"));
        Assert.That(GetField<int>(gameManager, "activeBoardOperationCount"), Is.Zero);
        AssertBoardReferencesEqual(gameOverBoard, SnapshotBoard());
        Assert.That(GetField<GameObject>(uiManager, "gameOverOverlay").activeSelf, Is.True);
    }

    [UnityTest]
    public IEnumerator SynchronousSaveFailureReleasesOperationForLaterInput()
    {
        ConfigureAllTilesAsNormal();
        SetField(levelManager, "heatProbabilityA", 0f);
        SetField(levelManager, "heatProbabilityD", 0f);
        ConfigureValidWord();
        SetField(gameManager, "saveGameOverride", (Func<Task>)(() => throw new InvalidOperationException("injected save failure")));
        LogAssert.Expect(LogType.Error, "Gameplay save failed: injected save failure");

        Invoke(gameManager, "TrySubmitCurrentWord");
        yield return WaitUntil(() => GetBoardOperationState() == "Idle");

        SetField(gameManager, "saveGameOverride", (Func<Task>)(() => Task.CompletedTask));
        Invoke(gameManager, "ShuffleTiles");

        Assert.That(GetBoardOperationState(), Is.EqualTo("Active"));
        Assert.That(GetField<int>(gameManager, "activeBoardOperationCount"), Is.EqualTo(1));
    }

    private void ConfigureValidWord()
    {
        ConfigureTile(0, 0, 'C', "Normal");
        ConfigureTile(0, 1, 'A', "Normal");
        ConfigureTile(0, 2, 'T', "Normal");
        ClickTile(0, 0);
        ClickTile(0, 1);
        ClickTile(0, 2);
    }

    private void ConfigureAllTilesAsNormal()
    {
        Array columns = (Array)GetField(gameManager, "letterTiles");
        for (int column = 0; column < columns.Length; column++)
        {
            IList tiles = (IList)columns.GetValue(column);
            for (int row = 0; row < tiles.Count; row++)
            {
                object tile = tiles[row];
                Invoke(tile, "Initialize", Invoke<char>(tile, "GetLetter"), column, row, Enum.Parse(tileType, "Normal"));
            }
        }
    }

    private object ConfigureTile(int column, int row, char letter, string type)
    {
        object tile = GetTile(column, row);
        Invoke(tile, "Initialize", letter, column, row, Enum.Parse(tileType, type));
        return tile;
    }

    private object GetTile(int column, int row)
    {
        Array columns = (Array)GetField(gameManager, "letterTiles");
        return ((IList)columns.GetValue(column))[row];
    }

    private void ClickTile(int column, int row)
    {
        Invoke(gameManager, "OnTileClick", Activator.CreateInstance(tilePosType, column, row));
    }

    private UnityEngine.Object[][] SnapshotBoard()
    {
        Array columns = (Array)GetField(gameManager, "letterTiles");
        UnityEngine.Object[][] snapshot = new UnityEngine.Object[columns.Length][];
        for (int column = 0; column < columns.Length; column++)
        {
            IList tiles = (IList)columns.GetValue(column);
            snapshot[column] = tiles.Cast<UnityEngine.Object>().ToArray();
        }
        return snapshot;
    }

    private static void AssertBoardReferencesEqual(UnityEngine.Object[][] expected, UnityEngine.Object[][] actual)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (int column = 0; column < expected.Length; column++)
        {
            Assert.That(actual[column], Is.EqualTo(expected[column]));
        }
    }

    private void AssertSelectionUiIsClear()
    {
        Assert.That(GetProperty<string>(GetField(uiManager, "currentWordText"), "text"), Is.Empty);
        Assert.That(GetProperty<string>(GetField(uiManager, "currentWordScore"), "text"), Is.Empty);
        Assert.That(GetField<GameObject>(uiManager, "validWordSubmitButton").activeSelf, Is.False);
        Assert.That(((Component)GetField(uiManager, "validWordBackground")).gameObject.activeSelf, Is.False);
    }

    private void AssertLogicalCoordinatesMatchTransforms()
    {
        Array columns = (Array)GetField(gameManager, "letterTiles");
        for (int column = 0; column < columns.Length; column++)
        {
            IList tiles = (IList)columns.GetValue(column);
            for (int row = 0; row < tiles.Count; row++)
            {
                object tile = tiles[row];
                object data = Invoke(tile, "ToLetterTileData");
                float expectedY = column % 2 == 0 ? -4f + (0.8f * row) : -4.5f + (0.8f * row);
                Assert.That(GetPublicField<int>(data, "column"), Is.EqualTo(column));
                Assert.That(GetPublicField<int>(data, "row"), Is.EqualTo(row));
                Assert.That(((Component)tile).transform.position.y, Is.EqualTo(expectedY).Within(0.001f));
            }
        }
    }

    private string GetBoardOperationState()
    {
        return GetField(gameManager, "boardOperationState").ToString();
    }

    private static IEnumerator WaitUntil(Func<bool> predicate, float timeout = 10f)
    {
        float startedAt = Time.realtimeSinceStartup;
        while (!predicate())
        {
            if (Time.realtimeSinceStartup - startedAt > timeout)
            {
                Assert.Fail("Timed out waiting for gameplay state.");
            }
            yield return null;
        }
    }

    private static Type FindType(string name)
    {
        Type type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false))
            .FirstOrDefault(candidate => candidate != null);
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

    private static void SetField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static object Invoke(object target, string methodName, params object[] arguments)
    {
        return target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Invoke(target, arguments);
    }

    private static T Invoke<T>(object target, string methodName, params object[] arguments)
    {
        return (T)Invoke(target, methodName, arguments);
    }
}
