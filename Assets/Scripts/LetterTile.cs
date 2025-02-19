using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class LetterTile : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text letterText;
    [SerializeField] private GameObject normalSprite;
    [SerializeField] private GameObject normalSelectedSprite;

    private char letter;
    private int column; // y; outer index
    private int row; // x; inner index
    private TileType tileType;
    private bool isSelected;

    public enum TileType { Normal, Fire, Bonus, Gold, Diamond }

    public void Initialize(char letter, int column, int row, TileType type)
    {
        this.letter = letter; // TODO: special case for Q
        letterText.text = letter.ToString();
        this.column = column;
        this.row = row;
        tileType = type;
        isSelected = false;
        ApplySprite();
    }

    // apply the correct sprite based on current state
    private void ApplySprite()
    {
        // disable everything
        // TODO: implement all other tile types
        normalSprite.SetActive(false);
        normalSelectedSprite.SetActive(false);

        switch (tileType)
        {
            case TileType.Normal:
                if (isSelected)
                {
                    normalSelectedSprite.SetActive(true);
                } else
                {
                    normalSprite.SetActive(true);
                }
                break;
        }
    }

    public char GetLetter()
    {
        return letter;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        GameManager.instance.OnTileClick(column, row);
    }

    public void SetIsSelected(bool isSelected)
    {
        this.isSelected = isSelected;
        ApplySprite();
    }
}
