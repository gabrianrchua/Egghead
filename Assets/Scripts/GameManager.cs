using UnityEngine;
using System.Collections.Generic;
using static CSVReader;
using System.Text;
using System.Collections;

public class GameManager : MonoBehaviour
{
    [SerializeField] private CSVReader csvReader;
    [SerializeField] private LetterTile letterTilePrefab;
    [SerializeField] private float[] bonusTileTypeScoreMultipliers = { 1.25f, 1.5f, 2f }; // order: bonus, gold, diamond
    [SerializeField] private float[] bonusTileTypeProbabilityMultipliers = { 1f, 0.5f, 0.25f }; // bonus, gold, diamond
    // simple ax+b, with max cap. default values - max out at 50% with 1000 score move
    [SerializeField] private float bonusTileA = 0.0005f;
    [SerializeField] private float bonusTileB = 0f;
    [SerializeField] private float bonusTileMax = 0.5f;

    public static GameManager instance;

    private Dictionary<LetterTile.TileType, float> tileTypeMultipliersDict;
    private List<LetterTile>[] letterTiles;
    private List<TilePos> selectedTiles;
    private bool isAnimating;
    private float previousMoveScore;

    private const float letterBaseYOdd = -4.5f;
    private const float letterBaseYEven = -4f;
    private const float letterDeltaY = 0.8f;
    private const float letterBaseX = -2.38f;
    private const float letterDeltaX = 0.8f;
    private const float tileDropAnimationDuration = 0.5f;

    private void Awake()
    {
        if (instance != null)
        {
            Debug.LogWarning("More than one GameManager in the scene! This one will be disabled.");
            enabled = false;
            return;
        }
        instance = this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void Start()
    {
        tileTypeMultipliersDict = new()
        {
            { LetterTile.TileType.Normal, 1f },
            { LetterTile.TileType.Fire, 1f },
            { LetterTile.TileType.Bonus, bonusTileTypeScoreMultipliers[0] },
            { LetterTile.TileType.Gold, bonusTileTypeScoreMultipliers[1] },
            { LetterTile.TileType.Diamond, bonusTileTypeScoreMultipliers[2] }
        };

        // clear children to prepare for managed lettertiles
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // initialize lettertiles
        letterTiles = new List<LetterTile>[7];
        selectedTiles = new List<TilePos>();
        for (int i = 0; i < letterTiles.Length; i++)
        {
            List<LetterTile> current = new List<LetterTile>();

            // even index should have 7 tiles, odd should have 8
            bool isEven = i % 2 == 0;
            int numTiles = isEven ? 7 : 8;
            for (int j = 0; j < numTiles; j++)
            {
                float x = letterBaseX + (letterDeltaX * i);
                float y = isEven ? letterBaseYEven + (letterDeltaY * j) : letterBaseYOdd + (letterDeltaY * j);
                LetterTile newTile = Instantiate(letterTilePrefab, new Vector3(x, y, 0), Quaternion.identity, transform);
                newTile.Initialize(NextLetter(), i, j, LetterTile.TileType.Normal);
                current.Add(newTile);
            }

            letterTiles[i] = current;
        }

        // initialize UI
        // TODO: load saved game from disk
        UIManager ui = UIManager.instance;
        LevelManager levelManager = LevelManager.instance;

        ui.ClearCurrentWordScore();
        ui.SetCurrentWord("");
        ui.SetLevel(levelManager.Level);
        ui.SetCurrentScore(levelManager.TotalScore, levelManager.LevelPercentage);
    }

    /// <summary>
    /// Randomly pick a letter according to letter probability distribution
    /// </summary>
    /// <returns>A <c>char</c> with the randomly chosen next letter</returns>
    private char NextLetter()
    {
        float rand = Random.value * csvReader.letterWeightsTotal;

        float cumulativeWeight = 0;
        for (int i = 0; i < csvReader.letterList.letters.Length; i++)
        {
            cumulativeWeight += csvReader.letterWeights[i];
            if (cumulativeWeight > rand)
            {
                return csvReader.letterList.letters[i].letter;
            }
        }

        return csvReader.letterList.letters[^1].letter; // should not happen
    }

    /// <summary>
    /// Each <c>LetterTile</c> will call this when it is clicked.
    /// The appropriate action will be taken (e.g. activate tile, accept word...)
    /// </summary>
    /// <param name="position">Position of the tile</param>
    public void OnTileClick(TilePos position)
    {
        (int column, int row) = position;

        if (isAnimating) return;

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
                        Debug.Log("User tried to submit invalid word!");
                        return;
                    }
                }
            }
            else
            {
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
            }
        }
        else if (selectedTiles.Count == 0)
        {
            // if the tile clicked is not selected
            // start new word if no selected tiles yet
            selectedTiles.Add(new TilePos(column, row));
            letterTiles[column][row].SetIsSelected(true);
        }
        else
        {
            // else, add to selected tiles if adjacent
            TilePos mostRecentTile = selectedTiles[^1];
            if (AreTilesAdjacent(mostRecentTile.Column, mostRecentTile.Row, column, row))
            {
                letterTiles[column][row].SetIsSelected(true);
                selectedTiles.Add(new TilePos(column, row));
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
        (string word, int score) = GetCurrentWord();
        UIManager.instance.SetCurrentWord(word);
        if (score == -1)
        {
            UIManager.instance.ClearCurrentWordScore();
        }
        else
        {
            UIManager.instance.SetCurrentWordScore(score);
        }
    }

    public void SubmitCurrentWord()
    {
        // first check if word is valid
        (string word, int score) = GetCurrentWord();
        if (score == -1) throw new System.InvalidOperationException("Invalid word");

        Debug.Log("Submitted word '" + word + "' for " + score.ToString());

        // increment score and display
        previousMoveScore = score;

        // cache instances
        LevelManager levelManager = LevelManager.instance;
        UIManager uiManager = UIManager.instance;

        levelManager.AddScore(score);
        uiManager.SetLevel(levelManager.Level);
        uiManager.SetCurrentScore(levelManager.TotalScore, levelManager.LevelPercentage);
        uiManager.ClearCurrentWordScore();
        uiManager.SetCurrentWord("");
        // TODO: level up graphics

        TilePos[] fireTilesCreated = DestroyTiles(selectedTiles.ToArray(), LetterTile.TileDestroyReason.Selected);
        selectedTiles.Clear();

        StartCoroutine(WaitThenDestroyTilesUnderFire(tileDropAnimationDuration, fireTilesCreated));
    }

    /// <summary>
    /// Clears out all tiles on board and replaces with new ones.
    /// Keeps fire tiles, advancing them one move, and removes any bonus+ tiles.
    /// </summary>
    public void ShuffleTiles()
    {
        for (int i = 0; i < letterTiles.Length; i++)
        {
            for (int j = 0; j < letterTiles[i].Count; j++)
            {
                LetterTile tile = letterTiles[i][j];
                if (tile.GetTileType() != LetterTile.TileType.Fire)
                {
                    // replace the tile with a new one
                    tile.DestroyTile(LetterTile.TileDestroyReason.Shuffled);
                    letterTiles[i].RemoveAt(j);
                    letterTiles[i].Insert(j, InstantiateNewTile(LetterTile.TileType.Normal, new TilePos(i, j)));
                }
            }
        }
        StartCoroutine(WaitThenDestroyTilesUnderFire(tileDropAnimationDuration, new TilePos[] { }));
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
    /// <returns>List of locations of fire tiles that were created</returns>
    private TilePos[] DestroyTiles(TilePos[] tileLocations, LetterTile.TileDestroyReason reason)
    {
        LevelManager levelManager = LevelManager.instance;

        // destroy selected tiles and spawn new ones
        List<(int col, LetterTile tile)> tilesToDestroy = new();
        foreach ((int col, int row) in tileLocations)
        {
            LetterTile tileToDestroy = letterTiles[col][row];
            tilesToDestroy.Add((col, tileToDestroy));
            tileToDestroy.DestroyTile(reason);
        }
        foreach ((int col, LetterTile tile) in tilesToDestroy)
        {
            letterTiles[col].Remove(tile);
        }

        // refresh board and tell new tiles their new positions
        List<TilePos> createdFireTiles = new();
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
                    letterTiles[i][j].SetPosition(x, y, i, j);
                    letterTiles[i][j].SetIsSelected(false);
                }
            }
        }
        return createdFireTiles.ToArray();
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
    /// Waits duration (disabling clicks), allowing animation to play
    /// before destroying any tiles that are under a fire tile
    /// </summary>
    /// <param name="duration">Duration to block inputs and before destroying tiles under fire</param>
    /// <param name="immuneFireTiles">Fire tiles that will not burn this turn
    ///     (basically fire tires that have just been created)</param>
    /// <returns>Nothing, <c>null</c></returns>
    private IEnumerator WaitThenDestroyTilesUnderFire(float duration, TilePos[] immuneFireTiles)
    {
        // do not wait if no fire tiles are on the board
        TilePos[] fireTiles = GetAllFireTileLocations();
        if (fireTiles.Length == 0) yield break;

        // animate, blocking clicks
        isAnimating = true;
        yield return new WaitForSeconds(duration);

        // after waiting, destroy tiles under fire tiles
        List<TilePos> tilesToDestroy = new();
        List<TilePos> ignoreTiles = new(immuneFireTiles);
        foreach ((int col, int row) in fireTiles)
        {
            if (row == 0)
            {
                // lose, the tile is at the bottom
                // TODO: implement lose logic
                Debug.Log("You lose!");
                yield break;
            }
            // only destroy if tile below is not a fire type, and is not on the list of fire tiles to ignore
            if (letterTiles[col][row - 1].GetTileType() != LetterTile.TileType.Fire
                && ignoreTiles.IndexOf(new TilePos(col, row)) == -1)
            {
                tilesToDestroy.Add(new TilePos(col, row - 1));
            }
        }
        DestroyTiles(tilesToDestroy.ToArray(), LetterTile.TileDestroyReason.Fire);
        isAnimating = false;
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

        Debug.Log($"base {baseProbability} bonus {bonusProbability} gold {goldProbability} diamond {diamondProbability} heat {heat} rand {rand}");

        if (rand < diamondProbability)
        {
            Debug.Log("returning diamond");
            return LetterTile.TileType.Diamond;
        }
        else if (rand < goldProbability)
        {
            Debug.Log("returning gold");
            return LetterTile.TileType.Gold;
        }
        else if (rand < bonusProbability)
        {
            Debug.Log("returning bonus");
            return LetterTile.TileType.Bonus;
        }
        else if (rand < heat)
        {
            Debug.Log("returning fire");
            return LetterTile.TileType.Fire;
        }
        Debug.Log("returning normal");
        return LetterTile.TileType.Normal;
    }

    /// <summary>
    /// Helper function to calculate the current word from the <c>selectedTiles</c> and return the score if applicable.
    /// </summary>
    /// <returns>Tuple with the current word string and its score int, or -1 if not a valid word.</returns>
    private (string, int) GetCurrentWord()
    {
        StringBuilder sb = new();
        float multiplier = 1f;
        foreach ((int col, int row) in selectedTiles)
        {
            LetterTile tile = letterTiles[col][row];
            char letter = tile.GetLetter();
            multiplier *= tileTypeMultipliersDict[tile.GetTileType()];
            sb.Append(letter == 'Q' ? "QU" : letter);
        }
        string word = sb.ToString().ToLower();

        try
        {
            Word wordDetails = csvReader.wordList.FindWord(word);
            return (word, Mathf.FloorToInt(wordDetails.points * multiplier));
        }
        catch (System.InvalidOperationException)
        {
            return (word, -1);
        }
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
