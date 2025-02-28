using UnityEngine;
using System.Collections.Generic;
using static CSVReader;
using System.Text;

public class GameManager : MonoBehaviour
{
    [SerializeField] private CSVReader csvReader;
    [SerializeField] private LetterTile letterTilePrefab;

    public static GameManager instance;

    private List<LetterTile>[] letterTiles;
    private List<(int col, int row)> selectedTiles;
    private int score;

    private const float letterBaseYOdd = -4.5f;
    private const float letterBaseYEven = -4f;
    private const float letterDeltaY = 0.8f;
    private const float letterBaseX = -2.38f;
    private const float letterDeltaX = 0.8f;

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
        // clear children to prepare for managed lettertiles
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        // initialize lettertiles
        letterTiles = new List<LetterTile>[7];
        selectedTiles = new List<(int col, int row)>();
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
        ui.ClearCurrentWordScore();
        ui.SetCurrentWord("");
        ui.SetLevel(1);
        ui.SetCurrentScore(0, 50f);
        score = 0;
    }

    // Randomly pick a letter according to letter probability distribution
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
    /// Each <c>LetterTile</c> will call this when they are clicked.
    /// The appropriate action will be taken (e.g. activate tile, accept word...)
    /// </summary>
    /// <param name="column">Column (x) outer index</param>
    /// <param name="row">Row (y) inner index</param>
    public void OnTileClick(int column, int row)
    {
        // if tile clicked is in the selected tiles list
        int index = selectedTiles.IndexOf((column, row));
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
                    (int col, int row) coordToDeselect = selectedTiles[i];
                    letterTiles[coordToDeselect.col][coordToDeselect.row].SetIsSelected(false);
                }
                selectedTiles.RemoveRange(index, selectedTiles.Count - index);

                // then, readd the selected tile
                letterTiles[column][row].SetIsSelected(true);
                selectedTiles.Add((column, row));
            }
        }
        else if (selectedTiles.Count == 0)
        {
            // if the tile clicked is not selected
            // start new word if no selected tiles yet
            selectedTiles.Add((column, row));
            letterTiles[column][row].SetIsSelected(true);
        }
        else
        {
            // else, add to selected tiles if adjacent
            (int col, int row) mostRecentTile = selectedTiles[^1];
            if (AreTilesAdjacent(mostRecentTile.col, mostRecentTile.row, column, row))
            {
                letterTiles[column][row].SetIsSelected(true);
                selectedTiles.Add((column, row));
            }
            else
            {
                // else, clear selected
                foreach ((int col, int row) tile in selectedTiles)
                {
                    letterTiles[tile.col][tile.row].SetIsSelected(false);
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

    private void SubmitCurrentWord()
    {
        // first check if word is valid
        (string word, int score) = GetCurrentWord();
        if (score == -1) throw new System.InvalidOperationException("Invalid word");

        Debug.Log("Submitted word " + word + " for " + score.ToString());

        // increment score and display
        this.score += score;
        UIManager.instance.SetCurrentScore(this.score, 50f);
        UIManager.instance.ClearCurrentWordScore();
        UIManager.instance.SetCurrentWord("");
        // TODO: implement levels and level percentage, level up

        // destroy selected tiles and spawn new ones
        List<(int col, LetterTile tile)> tilesToDestroy = new List<(int col, LetterTile tile)>();
        foreach ((int col, int row) in selectedTiles)
        {
            LetterTile tileToDestroy = letterTiles[col][row];
            tilesToDestroy.Add((col, tileToDestroy));
            tileToDestroy.DestroyTile();
        }
        foreach ((int col, LetterTile tile) in tilesToDestroy)
        {
            letterTiles[col].Remove(tile);
        }
        selectedTiles.Clear();

        // refresh board and tell new tiles their new positions
        for (int i = 0; i < letterTiles.Length; i++)
        {
            // even index should have 7 tiles, odd should have 8
            bool isEven = i % 2 == 0;
            int numTiles = isEven ? 7 : 8;
            int numInColumn = letterTiles[i].Count;
            for (int j = 0; j < numTiles; j++)
            {
                float x = letterBaseX + (letterDeltaX * i);
                float y = isEven ? letterBaseYEven + (letterDeltaY * j) : letterBaseYOdd + (letterDeltaY * j);
                if (j >= numInColumn)
                {
                    // need to create a new tile
                    LetterTile newTile = Instantiate(letterTilePrefab, new Vector3(x, y, 0), Quaternion.identity, transform);
                    newTile.Initialize(NextLetter(), i, j, LetterTile.TileType.Normal);
                    letterTiles[i].Add(newTile);
                }
                else
                {
                    // need to tell existing tile what its position is
                    letterTiles[i][j].SetPosition(x, y, i, j);
                    letterTiles[i][j].SetIsSelected(false);
                }
            }
        }
    }

    /// <summary>
    /// Helper function to calculate the current word from the <c>selectedTiles</c> and return the score if applicable.
    /// </summary>
    /// <returns>Tuple with the current word string and its score int, or -1 if not a valid word.</returns>
    private (string, int) GetCurrentWord()
    {
        StringBuilder sb = new StringBuilder();
        foreach ((int col, int row) in selectedTiles)
        {
            char letter = letterTiles[col][row].GetLetter();
            sb.Append(letter == 'Q' ? "QU" : letter);
        }
        string word = sb.ToString().ToLower();

        try
        {
            Word wordDetails = csvReader.wordList.FindWord(word);
            return (word, wordDetails.points);
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
