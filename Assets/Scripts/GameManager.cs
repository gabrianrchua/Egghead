using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Egghead.DictionaryData;
using Egghead.SaveSystem;
using UnityEngine.Serialization;

public class GameManager : Singleton<GameManager>
{
    private enum BoardOperationState
    {
        Initializing,
        Idle,
        Active,
        GameOver
    }

    private readonly struct BoardMutationResult
    {
        public TilePos[] CreatedFireTiles { get; }
        public List<LetterTile> MovingTiles { get; }
        public float DestructionDuration { get; }

        public BoardMutationResult(TilePos[] createdFireTiles, List<LetterTile> movingTiles, float destructionDuration)
        {
            CreatedFireTiles = createdFireTiles;
            MovingTiles = movingTiles;
            DestructionDuration = destructionDuration;
        }
    }

    [FormerlySerializedAs("csvReader")]
    [SerializeField] private DictionaryDataProvider dictionaryDataProvider;
    [SerializeField] private LetterTile letterTilePrefab;
    [SerializeField] private float[] bonusTileTypeScoreMultipliers = { 1.25f, 1.5f, 2f }; // order: bonus, gold, diamond
    [SerializeField] private float[] bonusTileTypeProbabilityMultipliers = { 1f, 0.5f, 0.2f }; // bonus, gold, diamond
    // simple ax+b, with max cap. default values = impossible to get bonus <200 score; max out at 75% with 950 score move
    [SerializeField] private float bonusTileA = 0.001f;
    [SerializeField] private float bonusTileB = -0.2f;
    [SerializeField] private float bonusTileMax = 0.75f;

    private Dictionary<LetterTile.TileType, float> tileTypeMultipliersDict;
    private List<LetterTile>[] letterTiles;
    private List<TilePos> selectedTiles;
    private readonly HashSet<LetterTile> fireWarningTiles = new();
    private BoardOperationState boardOperationState = BoardOperationState.Initializing;
    private int activeBoardOperationCount;
    private int maximumActiveBoardOperationCount;
    private System.Func<Task> saveGameOverride = null;
    private System.Func<Task> deleteDataOverride = null;
    private float previousMoveScore;

    private const float letterBaseYOdd = -4.5f;
    private const float letterBaseYEven = -4f;
    private const float letterDeltaY = 0.8f;
    private const float letterBaseX = -2.38f;
    private const float letterDeltaX = 0.8f;

    internal void PrepareForInitialization()
    {
        boardOperationState = BoardOperationState.Initializing;
        activeBoardOperationCount = 0;
        fireWarningTiles.Clear();

        tileTypeMultipliersDict = new()
        {
            { LetterTile.TileType.Normal, 1f },
            { LetterTile.TileType.Fire, 1f },
            { LetterTile.TileType.Bonus, bonusTileTypeScoreMultipliers[0] },
            { LetterTile.TileType.Gold, bonusTileTypeScoreMultipliers[1] },
            { LetterTile.TileType.Diamond, bonusTileTypeScoreMultipliers[2] }
        };

        // initialize lettertiles
        letterTiles = new List<LetterTile>[7];
        selectedTiles = new List<TilePos>();

        // clear children to prepare for managed lettertiles
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
    }

    internal async Task InitializeBoardAsync(SaveData data)
    {
        if (data.LetterTileData == null)
        {
            Debug.Log("Initializing as new game");
            // new game
            for (int i = 0; i < letterTiles.Length; i++)
            {
                List<LetterTile> current = new();

                // even index should have 7 tiles, odd should have 8
                bool isEven = i % 2 == 0;
                int numTiles = isEven ? 7 : 8;
                for (int j = 0; j < numTiles; j++)
                {
                    float x = letterBaseX + (letterDeltaX * i);
                    float y = isEven ? letterBaseYEven + (letterDeltaY * j) : letterBaseYOdd + (letterDeltaY * j);
                    LetterTile newTile = Instantiate(letterTilePrefab, new Vector3(x, y, 0), Quaternion.identity, transform);
                    newTile.Initialize(SimpleNextLetter(), i, j, LetterTile.TileType.Normal);
                    current.Add(newTile);
                }

                letterTiles[i] = current;
            }
        }
        else
        {
            Debug.Log("Initializing as saved game");
            // load saved game
            SavedLetterTileData[][] tileData = data.LetterTileData;
            for (int i = 0; i < letterTiles.Length; i++)
            {
                List<LetterTile> current = new();

                // even index should have 7 tiles, odd should have 8
                bool isEven = i % 2 == 0;
                int numTiles = isEven ? 7 : 8;
                for (int j = 0; j < numTiles; j++)
                {
                    float x = letterBaseX + (letterDeltaX * i);
                    float y = isEven ? letterBaseYEven + (letterDeltaY * j) : letterBaseYOdd + (letterDeltaY * j);
                    LetterTile newTile = Instantiate(letterTilePrefab, new Vector3(x, y, 0), Quaternion.identity, transform);
                    SavedLetterTileData currentTileData = tileData[i][j];
                    newTile.Initialize(currentTileData.letter, i, j, (LetterTile.TileType)currentTileData.tileType);
                    current.Add(newTile);
                }

                letterTiles[i] = current;
            }
        }
        AudioManager.Instance.PlaySound(SoundType.TilesStart);
        await WaitForTileIntroAsync();
        RefreshFireWarnings();
    }

    internal void CompleteInitialization()
    {
        boardOperationState = BoardOperationState.Idle;
    }

    private Task WaitForTileIntroAsync()
    {
        TaskCompletionSource<bool> completion = new();
        StartCoroutine(CompleteTileIntro(completion));
        return completion.Task;
    }

    private IEnumerator CompleteTileIntro(TaskCompletionSource<bool> completion)
    {
        yield return new WaitForSeconds(LetterTile.IntroAnimationDuration);
        yield return null;
        completion.TrySetResult(true);
    }

    private async Task SaveGame()
    {
        SaveData saveData = new()
        {
            SchemaVersion = SaveDataValidator.CurrentSchemaVersion,
            Score = LevelManager.Instance.TotalScore,
            Timestamp = System.DateTime.UtcNow,
            LetterTileData = GetLetterTileData()
        };
        await SaveManager.Instance.SaveGame(saveData);
    }

    /// <summary>
    /// Export <c>letterTiles</c> as a saved tile-data jagged array for
    /// the purpose of saving
    /// </summary>
    /// <returns>Jagged array of <c>LetterTileData</c> representation of the current board</returns>
    public SavedLetterTileData[][] GetLetterTileData()
    {
        SavedLetterTileData[][] data = new SavedLetterTileData[letterTiles.Length][];

        for (int i = 0; i < letterTiles.Length; i++)
        {
            List<SavedLetterTileData> column = new();
            for (int j = 0; j < letterTiles[i].Count; j++)
            {
                column.Add(letterTiles[i][j].ToLetterTileData());
            }
            data[i] = column.ToArray();
        }

        return data;
    }

    /// <summary>
    /// Randomly pick a letter according to adjusted probability distribution
    /// Considers current board state <c>letterTiles</c> and boosts / reduces probability
    /// of letters that are currently under / over represented in the board
    /// </summary>
    /// <returns>A <c>char</c> with the randomly chosen next letter</returns>
    private char NextLetter()
    {
        // build adjusted weights based on letters currently on board
        Dictionary<char, int> boardCounts = GetBoardLetterCounts();
        int totalBoardLetters = Mathf.Max(1, boardCounts.Values.Sum());

        float[] adjustedWeights = new float[dictionaryDataProvider.LetterCount];
        float adjustmentPower = 0.7f; // tuning parameter (0.5 – 1.0 ideal)

        // Compute expected frequency proportional to base weight
        for (int i = 0; i < dictionaryDataProvider.LetterCount; i++)
        {
            LetterData letterData = dictionaryDataProvider.GetLetter(i);
            char c = letterData.Letter;
            float expectedFreq = letterData.Weight;
            float currentFreq = boardCounts.TryGetValue(c, out int count)
                ? (float)count / totalBoardLetters
                : 0.0001f; // avoid division by zero

            // boost underrepresented letters
            float correction = Mathf.Pow(expectedFreq / currentFreq, adjustmentPower);
            correction = Mathf.Clamp(correction, 0.5f, 2.0f); // avoid wild swings

            adjustedWeights[i] = letterData.Weight * correction;
        }

        float totalAdjusted = adjustedWeights.Sum();

        // weighted random draw
        float rand = Random.value * totalAdjusted;
        float cumulative = 0;
        for (int i = 0; i < dictionaryDataProvider.LetterCount; i++)
        {
            cumulative += adjustedWeights[i];
            if (cumulative > rand)
                return dictionaryDataProvider.GetLetter(i).Letter;
        }

        return dictionaryDataProvider.GetLetter(dictionaryDataProvider.LetterCount - 1).Letter; // fallback, should not happen
    }

    /// <summary>
    /// Randomly pick a letter according to letter probability distribution
    /// Does NOT take into account current board state, so only use when initializing board
    /// </summary>
    /// <returns>A <c>char</c> with the randomly chosen next letter</returns>
    private char SimpleNextLetter()
    {
        float rand = Random.value * dictionaryDataProvider.LetterWeightsTotal;

        float cumulativeWeight = 0;
        for (int i = 0; i < dictionaryDataProvider.LetterCount; i++)
        {
            LetterData letterData = dictionaryDataProvider.GetLetter(i);
            cumulativeWeight += letterData.Weight;
            if (cumulativeWeight > rand)
            {
                return letterData.Letter;
            }
        }

        return dictionaryDataProvider.GetLetter(dictionaryDataProvider.LetterCount - 1).Letter; // should not happen
    }

    /// <summary>
    /// Helper to count letters currently on board
    /// </summary>
    /// <returns>Dictionary with letter as key and number of times that letter appears as value</returns>
    private Dictionary<char, int> GetBoardLetterCounts()
    {
        Dictionary<char, int> counts = new();
        foreach (List<LetterTile> tileColumn in letterTiles)
        {
            foreach (LetterTile tile in tileColumn)
            {
                char c = tile.GetLetter();
                counts[c] = counts.TryGetValue(c, out int count) ? count + 1 : 1;
            }
        }
        return counts;
    }

    /// <summary>
    /// Each <c>LetterTile</c> will call this when it is clicked.
    /// The appropriate action will be taken (e.g. activate tile, accept word...)
    /// </summary>
    /// <param name="position">Position of the tile</param>
    public void OnTileClick(TilePos position)
    {
        if (IsBoardInputBlocked()) return;

        HideCurrentSubmitHint();

        (int column, int row) = position;

        // if tile clicked is in the selected tiles list
        int index = selectedTiles.IndexOf(new TilePos(column, row));
        if (index != -1)
        {
            if (index == selectedTiles.Count - 1)
            {
                // if user clicked the most recent tile selected again...
                if (selectedTiles.Count == 1)
                {
                    // deselect the only tile selected
                    letterTiles[column][row].SetIsSelected(false);
                    selectedTiles.Clear();

                    AudioManager.Instance.PlaySound(SoundType.TileSelected);
                }
                else
                {
                    // accept word if last tile clicked AND is valid word
                    try
                    {
                        SubmitCurrentWord();
                    }
                    catch (System.InvalidOperationException)
                    {
                        AudioManager.Instance.PlaySound(SoundType.InvalidWord);
                        Debug.Log("User tried to submit invalid word!");
                        return;
                    }
                }
            }
            else
            {
                // if the user clicked some tile in the selected chain...
                // deselect all after it
                for (int i = index; i < selectedTiles.Count; i++)
                {
                    TilePos coordToDeselect = selectedTiles[i];
                    letterTiles[coordToDeselect.Column][coordToDeselect.Row].SetIsSelected(false);
                }
                selectedTiles.RemoveRange(index, selectedTiles.Count - index);

                // then, readd the selected tile
                letterTiles[column][row].SetIsSelected(true);
                selectedTiles.Add(new TilePos(column, row));

                AudioManager.Instance.PlaySound(SoundType.TileSelected);
            }
        }
        else if (selectedTiles.Count == 0)
        {
            // if the tile clicked is not selected
            // start new word if no selected tiles yet
            selectedTiles.Add(new TilePos(column, row));
            letterTiles[column][row].SetIsSelected(true);

            AudioManager.Instance.PlaySound(SoundType.TileSelected);
        }
        else
        {
            // else, add to selected tiles if adjacent
            TilePos mostRecentTile = selectedTiles[^1];
            if (AreTilesAdjacent(mostRecentTile.Column, mostRecentTile.Row, column, row))
            {
                letterTiles[column][row].SetIsSelected(true);
                selectedTiles.Add(new TilePos(column, row));

                AudioManager.Instance.PlaySound(SoundType.TileSelected);
            }
            else
            {
                // else, clear selected
                foreach (TilePos tile in selectedTiles)
                {
                    letterTiles[tile.Column][tile.Row].SetIsSelected(false);
                }
                selectedTiles.Clear();
            }
        }

        // update word UI
        (string word, int score, LetterTile.TileType highestType) = GetCurrentWord();
        UIManager.Instance.SetCurrentWord(word);
        if (score == -1)
        {
            UIManager.Instance.ClearCurrentWordScore();
        }
        else
        {
            UIManager.Instance.SetCurrentWordScore(score, highestType);
            ShowCurrentSubmitHint();
            switch (highestType)
            {
                case LetterTile.TileType.Normal:
                case LetterTile.TileType.Fire:
                    AudioManager.Instance.PlaySound(SoundType.WordRegular);
                    break;
                case LetterTile.TileType.Bonus:
                    AudioManager.Instance.PlaySound(SoundType.WordBonus);
                    break;
                case LetterTile.TileType.Gold:
                    AudioManager.Instance.PlaySound(SoundType.WordGold);
                    break;
                case LetterTile.TileType.Diamond:
                    AudioManager.Instance.PlaySound(SoundType.WordDiamond);
                    break;
            }
        }
    }

    public void DeselectAllTiles()
    {
        if (IsBoardInputBlocked() || selectedTiles == null) return;

        ResetSelectionState();
    }

    private void ResetSelectionState()
    {
        HideCurrentSubmitHint();

        foreach (TilePos tile in selectedTiles)
        {
            letterTiles[tile.Column][tile.Row].SetIsSelected(false);
        }

        selectedTiles.Clear();
        UIManager.Instance.SetCurrentWord("");
        UIManager.Instance.ClearCurrentWordScore();
    }

    /// <summary>
    /// Public helper which tries to submit the current word if valid.
    /// </summary>
    public void TrySubmitCurrentWord()
    {
        if (IsBoardInputBlocked()) return;

        try
        {
            SubmitCurrentWord();
        }
        catch
        {
            // no-op
        }
    }

    private void SubmitCurrentWord()
    {
        // first check if word is valid
        (string word, int score, _) = GetCurrentWord();
        if (score == -1) throw new System.InvalidOperationException("Invalid word");
        if (!TryBeginBoardOperation()) return;

        try
        {
            HideCurrentSubmitHint();

            Debug.Log("Submitted word '" + word + "' for " + score.ToString());

            // increment score and display
            previousMoveScore = score;

            // cache instances
            LevelManager levelManager = LevelManager.Instance;
            UIManager uiManager = UIManager.Instance;

            bool leveledUp = levelManager.AddScore(score);
            uiManager.SetLevel(levelManager.Level);
            uiManager.SetCurrentScore(levelManager.TotalScore, levelManager.LevelPercentage);
            uiManager.ClearCurrentWordScore();
            uiManager.SetCurrentWord("");
            if (leveledUp)
            {
                uiManager.ShowLevelUpAnimation(levelManager.Level);
            }

            BoardMutationResult mutation = DestroyTiles(selectedTiles.ToArray(), LetterTile.TileDestroyReason.Selected);
            selectedTiles.Clear();
            AudioManager.Instance.PlaySound(SoundType.TileClick);

            StartCoroutine(CompleteBoardOperation(mutation));
        }
        catch
        {
            EndBoardOperation();
            throw;
        }
    }

    private void ShowCurrentSubmitHint()
    {
        if (selectedTiles.Count == 0) return;

        TilePos lastTile = selectedTiles[^1];
        letterTiles[lastTile.Column][lastTile.Row].ShowSubmitHint();
    }

    private void HideCurrentSubmitHint()
    {
        if (selectedTiles == null || selectedTiles.Count == 0) return;

        TilePos lastTile = selectedTiles[^1];
        letterTiles[lastTile.Column][lastTile.Row].HideSubmitHint();
    }

    private async void OnLose()
    {
        EnterGameOverState();
        AudioManager.Instance.PlaySound(SoundType.Lose);
        Debug.Log("You lose!");
        Task deletion;
        try
        {
            deletion = deleteDataOverride?.Invoke() ?? SaveManager.Instance.DeleteData();
        }
        catch (System.Exception ex)
        {
            deletion = Task.FromException(ex);
        }
        // TODO: save high score other stats etc.
        LevelManager levelManager = LevelManager.Instance;
        UIManager.Instance.ShowGameOverOverlay(levelManager.Level, levelManager.TotalScore);

        try
        {
            await deletion;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Game-over save deletion failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Clears out all tiles on board and replaces with new ones.
    /// Keeps fire tiles, advancing them one move, and removes any bonus+ tiles.
    /// Creates between one and three new fire tiles at the top of the board
    /// </summary>
    public void ShuffleTiles()
    {
        if (!TryBeginBoardOperation()) return;

        try
        {
            ResetSelectionState();

            int numFireTiles = Mathf.RoundToInt(Random.Range(1f, 3f));
            int[] fireTileLocations = SelectArrayPositions(letterTiles.Length, numFireTiles);
            List<TilePos> newFireTiles = new(numFireTiles);

            for (int i = 0; i < letterTiles.Length; i++)
            {
                for (int j = 0; j < letterTiles[i].Count; j++)
                {
                    LetterTile tile = letterTiles[i][j];
                    if (tile.GetTileType() != LetterTile.TileType.Fire)
                    {
                        // replace the tile with a new one
                        tile.DestroyTile(LetterTile.TileDestroyReason.Shuffled);
                        fireWarningTiles.Remove(tile);
                        letterTiles[i].RemoveAt(j);

                        // if at the top of the list and this column needs a new fire tile, spawn it
                        bool isEven = i % 2 == 0;
                        int numTiles = isEven ? 7 : 8;
                        if (j == numTiles - 1 && fireTileLocations.Contains(i))
                        {
                            TilePos position = new(i, j);
                            newFireTiles.Add(position);
                            letterTiles[i].Insert(j, InstantiateNewTile(LetterTile.TileType.Fire, position));
                        }
                        else
                        {
                            letterTiles[i].Insert(j, InstantiateNewTile(LetterTile.TileType.Normal, new TilePos(i, j)));
                        }
                    }
                }
            }
            AudioManager.Instance.PlaySound(SoundType.Shuffle);
            RefreshFireWarnings();
            BoardMutationResult mutation = new(
                newFireTiles.ToArray(),
                new List<LetterTile>(),
                LetterTile.GetDestroyAnimationDuration(LetterTile.TileDestroyReason.Shuffled));
            StartCoroutine(CompleteBoardOperation(mutation));
        }
        catch
        {
            EndBoardOperation();
            throw;
        }
    }

    /// <summary>
    /// Helper to pick <c>n</c> distinct random positions in the range <c>[0, arrayLength?1]</c>
    /// </summary>
    /// <param name="arrayLength">The length of the array</param>
    /// <param name="n">Number of positions to pick (must be <= arrayLength)</param>
    /// <returns><c>int</c> array containing the chosen indices</returns>
    private int[] SelectArrayPositions(int arrayLength, int n)
    {
        if (n < 0 || n > arrayLength)
            throw new System.ArgumentException("n must be between 0 and arrayLength");

        // Fisher-Yates shuffle (fast and deterministic)
        int[] indices = new int[arrayLength];
        for (int i = 0; i < arrayLength; i++)
            indices[i] = i;

        for (int i = 0; i < n; i++)
        {
            int r = Random.Range(i, arrayLength); // inclusive lower, exclusive upper
            (indices[r], indices[i]) = (indices[i], indices[r]); // swap indices[i] and indices[r]
        }

        int[] result = new int[n];
        System.Array.Copy(indices, result, n);
        return result;
    }

    /// <summary>
    /// Helper that safely destroys tiles in the <c>tileLocations</c> list from <c>letterTiles</c>,
    /// then creates and initializes new tiles accordingly using <c>previousMoveScore</c> as part of
    /// a standard move by the player
    /// </summary>
    /// <param name="tileLocations">List of indices of tiles to destroy</param>
    /// <param name="reason">Reason for tile destruction, where <c>TileDestroyReason.Selected</c> is
    ///     because the user submitted them, and <c>TileDestroyReason.Fire</c> is because they was
    ///     destroyed by fire</param>
    /// <returns>Details needed to wait for the mutation's visual completion</returns>
    private BoardMutationResult DestroyTiles(TilePos[] tileLocations, LetterTile.TileDestroyReason reason)
    {
        LevelManager levelManager = LevelManager.Instance;

        // destroy selected tiles and spawn new ones
        List<(int col, LetterTile tile)> tilesToDestroy = new();
        foreach ((int col, int row) in tileLocations)
        {
            LetterTile tileToDestroy = letterTiles[col][row];
            tilesToDestroy.Add((col, tileToDestroy));
            tileToDestroy.DestroyTile(reason);
            // The tile is being removed, so leave its destruction animation intact.
            fireWarningTiles.Remove(tileToDestroy);
        }
        foreach ((int col, LetterTile tile) in tilesToDestroy)
        {
            letterTiles[col].Remove(tile);
        }

        // refresh board and tell new tiles their new positions
        List<TilePos> createdFireTiles = new();
        List<LetterTile> movingTiles = new();
        for (int i = 0; i < letterTiles.Length; i++)
        {
            // even index should have 7 tiles, odd should have 8
            bool isEven = i % 2 == 0;
            int numTiles = isEven ? 7 : 8;
            int numInColumn = letterTiles[i].Count;
            for (int j = 0; j < numTiles; j++)
            {
                if (j >= numInColumn)
                {
                    // need to create a new tile
                    LetterTile.TileType nextTileType = GetNextTileType(levelManager.Heat, previousMoveScore);
                    LetterTile newTile = InstantiateNewTile(nextTileType, new TilePos(i, j));
                    if (nextTileType == LetterTile.TileType.Fire)
                    {
                        createdFireTiles.Add(new TilePos(i, j));
                    }
                    letterTiles[i].Add(newTile);
                }
                else
                {
                    // need to tell existing tile what its position is
                    (float x, float y) = CalculateTilePositionFromTilePos(new TilePos(i, j));
                    if (letterTiles[i][j].SetPositionAndReportMovement(x, y, i, j))
                    {
                        movingTiles.Add(letterTiles[i][j]);
                    }
                    letterTiles[i][j].SetIsSelected(false);
                }
            }
        }
        RefreshFireWarnings();
        float destructionDuration = tileLocations.Length == 0
            ? 0f
            : LetterTile.GetDestroyAnimationDuration(reason);
        return new BoardMutationResult(createdFireTiles.ToArray(), movingTiles, destructionDuration);
    }

    /// <summary>
    /// Calculates x and y positions of a tile relative to <c>GameManager</c>'s transform
    /// given its position in the board
    /// </summary>
    /// <param name="tilePos">The tile's position in the board</param>
    /// <returns></returns>
    private (float x, float y) CalculateTilePositionFromTilePos(TilePos position)
    {
        (int col, int row) = position;
        float x = letterBaseX + (letterDeltaX * col);
        float y = col % 2 == 0 ? letterBaseYEven + (letterDeltaY * row) : letterBaseYOdd + (letterDeltaY * row);
        return (x, y);
    }

    /// <summary>
    /// Helper to instantiate a new LetterTile and initialize it
    /// </summary>
    /// <param name="type">Which <c>TileType</c> this tile is</param>
    /// <param name="position">The tile's position in the board</param>
    /// <param name="x">The tile's x position relative to <c>GameManager</c>'s transform</param>
    /// <param name="y">The tile's y position relative to <c>GameManager</c>'s transform</param>
    /// <returns></returns>
    private LetterTile InstantiateNewTile(LetterTile.TileType type, TilePos position)
    {
        (int col, int row) = position;
        (float x, float y) = CalculateTilePositionFromTilePos(new TilePos(col, row));
        LetterTile newTile = Instantiate(letterTilePrefab, new Vector3(x, y, 0), Quaternion.identity, transform);
        newTile.Initialize(NextLetter(), col, row, type);
        return newTile;
    }

    /// <summary>
    /// Waits for an accepted board operation's visual phases, then advances fire and saves.
    /// </summary>
    private IEnumerator CompleteBoardOperation(BoardMutationResult initialMutation)
    {
        bool reachedGameOver = false;
        try
        {
            yield return WaitForMutationCompletion(initialMutation);

            TilePos[] fireTiles = GetAllFireTileLocations();
            if (fireTiles.Length == 0)
            {
                QueueGameplaySave();
                yield break;
            }

            List<TilePos> tilesToDestroy = new();
            List<TilePos> ignoreTiles = new(initialMutation.CreatedFireTiles);
            bool fireCriticalTriggered = false;
            foreach ((int col, int row) in fireTiles)
            {
                if (row == 1)
                {
                    letterTiles[col][row].TriggerFireCritical();
                    fireCriticalTriggered = true;
                }
                else if (row == 0)
                {
                    reachedGameOver = true;
                    OnLose();
                    yield break;
                }
                if (letterTiles[col][row - 1].GetTileType() != LetterTile.TileType.Fire
                    && ignoreTiles.IndexOf(new TilePos(col, row)) == -1)
                {
                    tilesToDestroy.Add(new TilePos(col, row - 1));
                }
            }
            if (fireCriticalTriggered && PlayerPrefs.GetInt("ShowFireCriticalModal", 1) == 1)
            {
                Modal.Instance.OpenModal(null, () =>
                {
                    PlayerPrefs.SetInt("ShowFireCriticalModal", 0);
                }, "Caution! A fire tile is critically close to burning up! Clear it this turn or it's game over!", "", "Okay");
            }
            BoardMutationResult fireMutation = DestroyTiles(tilesToDestroy.ToArray(), LetterTile.TileDestroyReason.Fire);
            if (tilesToDestroy.Count > 0)
            {
                AudioManager.Instance.PlaySound(SoundType.TileBurn);
            }
            yield return WaitForMutationCompletion(fireMutation);
            QueueGameplaySave();
        }
        finally
        {
            if (!reachedGameOver)
            {
                EndBoardOperation();
            }
        }
    }

    private IEnumerator WaitForMutationCompletion(BoardMutationResult mutation)
    {
        if (mutation.DestructionDuration > 0f)
        {
            yield return new WaitForSeconds(mutation.DestructionDuration);
        }

        while (mutation.MovingTiles.Any(tile => tile != null && tile.IsDropAnimating))
        {
            yield return null;
        }
    }

    private bool IsBoardInputBlocked()
    {
        return boardOperationState != BoardOperationState.Idle || UIManager.Instance.IsOverlayActive;
    }

    private bool TryBeginBoardOperation()
    {
        if (IsBoardInputBlocked()) return false;

        boardOperationState = BoardOperationState.Active;
        activeBoardOperationCount++;
        maximumActiveBoardOperationCount = Mathf.Max(maximumActiveBoardOperationCount, activeBoardOperationCount);
        Debug.Assert(activeBoardOperationCount == 1, "More than one board operation became active.");
        return true;
    }

    private void EndBoardOperation()
    {
        if (boardOperationState != BoardOperationState.Active) return;

        activeBoardOperationCount = Mathf.Max(0, activeBoardOperationCount - 1);
        boardOperationState = BoardOperationState.Idle;
    }

    private void EnterGameOverState()
    {
        if (boardOperationState == BoardOperationState.Active)
        {
            activeBoardOperationCount = Mathf.Max(0, activeBoardOperationCount - 1);
        }
        boardOperationState = BoardOperationState.GameOver;
    }

    private void QueueGameplaySave()
    {
        try
        {
            ObserveSave(saveGameOverride?.Invoke() ?? SaveGame());
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Gameplay save failed: " + ex.Message);
        }
    }

    /// <summary>Observe a queued gameplay save without blocking input or discarding failures.</summary>
    private async void ObserveSave(Task saveTask)
    {
        try
        {
            await saveTask;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Gameplay save failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Helper that finds all fire tiles on the board from <c>letterTiles</c>
    /// </summary>
    /// <returns>An array of the indices of the fire tiles in <c>letterTiles</c></returns>
    private TilePos[] GetAllFireTileLocations()
    {
        List<TilePos> fireTiles = new();
        for (int i = 0; i < letterTiles.Length; i++)
        {
            for (int j = 0; j < letterTiles[i].Count; j++)
            {
                LetterTile tile = letterTiles[i][j];
                if (tile.GetTileType() == LetterTile.TileType.Fire)
                {
                    fireTiles.Add(new TilePos(i, j));
                }
            }
        }
        return fireTiles.ToArray();
    }

    /// <summary>
    /// Ensures that only non-fire tiles directly below an active fire tile display a fire warning.
    /// </summary>
    private void RefreshFireWarnings()
    {
        HashSet<LetterTile> nextWarningTiles = new();
        foreach ((int col, int row) in GetAllFireTileLocations())
        {
            if (row == 0)
            {
                continue;
            }

            LetterTile tileBelow = letterTiles[col][row - 1];
            if (tileBelow.GetTileType() != LetterTile.TileType.Fire)
            {
                nextWarningTiles.Add(tileBelow);
            }
        }

        foreach (LetterTile tile in fireWarningTiles)
        {
            if (!nextWarningTiles.Contains(tile))
            {
                tile.UntriggerFireWarning();
            }
        }

        foreach (LetterTile tile in nextWarningTiles)
        {
            if (!fireWarningTiles.Contains(tile))
            {
                tile.TriggerFireWarning();
            }
        }

        fireWarningTiles.Clear();
        fireWarningTiles.UnionWith(nextWarningTiles);
    }

    /// <summary>
    /// Helper to get a random tile given current heat and previous move's score
    /// </summary>
    /// <param name="heat">Current heat given by LevelManager</param>
    /// <param name="score">Score of the previous move</param>
    /// <returns></returns>
    private LetterTile.TileType GetNextTileType(float heat, float score)
    {
        float baseProbability = Mathf.Min(bonusTileA * score + bonusTileB, bonusTileMax);
        float bonusProbability = baseProbability * bonusTileTypeProbabilityMultipliers[0];
        float goldProbability = baseProbability * bonusTileTypeProbabilityMultipliers[1];
        float diamondProbability = baseProbability * bonusTileTypeProbabilityMultipliers[2];

        float rand = Random.value;

        if (rand < diamondProbability)
        {
            return LetterTile.TileType.Diamond;
        }
        else if (rand < goldProbability)
        {
            return LetterTile.TileType.Gold;
        }
        else if (rand < bonusProbability)
        {
            return LetterTile.TileType.Bonus;
        }
        else if (rand < heat)
        {
            return LetterTile.TileType.Fire;
        }
        return LetterTile.TileType.Normal;
    }

    /// <summary>
    /// Helper function to calculate the current word from the <c>selectedTiles</c> and return the score if applicable.
    /// </summary>
    /// <returns>Tuple with the current word string, its score int (-1 if not a valid word), and the highest tile type in this word.</returns>
    private (string, int, LetterTile.TileType) GetCurrentWord()
    {
        LetterTile.TileType highestType = LetterTile.TileType.Normal;

        StringBuilder sb = new();
        float multiplier = 1f;
        foreach ((int col, int row) in selectedTiles)
        {
            LetterTile tile = letterTiles[col][row];
            char letter = tile.GetLetter();
            LetterTile.TileType type = tile.GetTileType();
            if ((int)type > (int)highestType)
            {
                highestType = type;
            }
            multiplier *= tileTypeMultipliersDict[type];
            sb.Append(letter == 'Q' ? "QU" : letter);
        }
        string word = sb.ToString().ToLowerInvariant();
        if (dictionaryDataProvider.TryGetPoints(word, out int points))
        {
            return (word, Mathf.FloorToInt(points * multiplier), highestType);
        }

        return (word, -1, LetterTile.TileType.Normal);
    }

    /// <summary>
    /// Helper function for <c>OnTileClick</c> to determine if two tiles are adjacent and thus valid to connect.
    /// </summary>
    /// <param name="col1">Column (x) outer index for first tile</param>
    /// <param name="row1">Row (y) inner index for first tile</param>
    /// <param name="col2">Column (x) outer index for second tile</param>
    /// <param name="row2">Row (y) inner index for second tile</param>
    /// <returns><c>true</c> if the tiles are adjacent, else <c>false</c></returns>
    private bool AreTilesAdjacent(int col1, int row1, int col2, int row2)
    {
        // if same column, can go up or down in row by 1
        if (col1 == col2)
        {
            return Mathf.Abs(row1 - row2) == 1;
        }
        if (Mathf.Abs(col1 - col2) > 1) return false;
        int rowDiff = row1 - row2;
        if (col1 % 2 == 0)
        {
            // if even index, sides can be -1 or 0.
            return rowDiff == 0 || rowDiff == -1;
        }
        else
        {
            // if odd index, sides can be 0 or +1.
            return rowDiff == 0 || rowDiff == 1;
        }
    }
}
