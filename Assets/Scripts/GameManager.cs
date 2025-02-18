using UnityEngine;
using System.Collections.Generic;
using static CSVReader;

public class GameManager : MonoBehaviour
{
    [SerializeField] private CSVReader csvReader;
    [SerializeField] private LetterTile letterTilePrefab;

    public static GameManager instance;

    private List<LetterTile>[] letterTiles;
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
                newTile.Initialize(NextLetter());
                current.Add(newTile);
            }

            letterTiles[i] = current;
        }
    }

    // TODO: pre calculate word score for each word

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
}
