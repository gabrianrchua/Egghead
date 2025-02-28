using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class LetterTile : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text letterText;
    [SerializeField] private GameObject normalSprite;
    [SerializeField] private GameObject normalSelectedSprite;
    [SerializeField] private float dropAnimationDuration = 0.5f;

    private char letter;
    private int column; // y; outer index
    private int row; // x; inner index
    private TileType tileType;
    private bool isSelected;

    public enum TileType { Normal, Fire, Bonus, Gold, Diamond }

    public void Initialize(char letter, int column, int row, TileType type)
    {
        this.letter = letter;
        if (letter == 'Q')
        {
            // special case to display Q as Qu
            letterText.text = "Qu";
            letterText.fontSize = 6.5f;
        }
        else
        {
            letterText.text = letter.ToString();
            letterText.fontSize = 10f;
        }
        this.column = column;
        this.row = row;
        tileType = type;
        isSelected = false;
        ApplySprite();
    }

    /// <summary>
    /// Apply the correct sprite based on current state and type
    /// </summary>
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
                }
                else
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

    public void DestroyTile()
    {
        // TODO: future: animate destruction
        Destroy(gameObject);
    }

    public void SetPosition(float x, float y, int column, int row)
    {
        //transform.localPosition = new Vector3(x, y, 0);
        if (Mathf.Abs(transform.position.y - y) > 0.1f)
        {
            // we moved, play drop animation
            StartCoroutine(PlayDropAnimation(transform.position.y, y, dropAnimationDuration));
        }

        this.column = column;
        this.row = row;
    }

    private IEnumerator PlayDropAnimation(float originalY, float destinationY, float duration)
    {
        float timeElapsed = 0;
        float originalX = transform.position.x;

        while (timeElapsed < duration)
        {
            timeElapsed += Time.deltaTime;
            //transform.position = new Vector3(originalX, Mathf.Lerp(originalY, destinationY, timeElapsed / duration), 0f); // linear
            transform.position = new Vector3(originalX, Mathf.Lerp(transform.position.y, destinationY, 0.02f), 0f); // nonlinear
            yield return new WaitForEndOfFrame();
        }

        transform.position = new Vector3(originalX, destinationY, 0f);
    }

    private void DestroyAnimator()
    {
        Animator animator = GetComponent<Animator>();
        if (animator != null)
        {
            Destroy(animator);
        }
    }
}
