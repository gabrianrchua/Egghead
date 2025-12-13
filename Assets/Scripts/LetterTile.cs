using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class LetterTile : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private TMP_Text letterText;
    [SerializeField] private GameObject normalSprite;
    [SerializeField] private GameObject normalSelectedSprite;
    [SerializeField] private GameObject fireSprite;
    [SerializeField] private GameObject fireSelectedSprite;
    [SerializeField] private GameObject bonusSprite;
    [SerializeField] private GameObject bonusSelectedSprite;
    [SerializeField] private GameObject goldSprite;
    [SerializeField] private GameObject goldSelectedSprite;
    [SerializeField] private GameObject diamondSprite;
    [SerializeField] private GameObject diamondSelectedSprite;

    private const float dropAnimationDuration = 0.5f;

    private char letter;
    private TileType tileType;
    private int column; // y; outer index
    private int row; // x; inner index
    private bool isSelected;
    private bool isAnimating; // if animation is playing, disable touches

    public TileType GetTileType()
    {
        return tileType;
    }

    public enum TileType { Normal, Fire, Bonus, Gold, Diamond }
    public enum TileDestroyReason { Selected, Fire, Shuffled };

    public struct LetterTileData
    {
        public char letter;
        public int column;
        public int row;
        public int tileType;
    }

    /// <summary>
    /// Initialize tile using raw values
    /// </summary>
    /// <param name="letter">Letter character</param>
    /// <param name="column">Column (outer index)</param>
    /// <param name="row">Row (inner index)</param>
    /// <param name="type">Which <c>TileType</c></param>
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
    /// Initialize tile from JSON
    /// </summary>
    /// <param name="letterTileData">JSON string containing a <c>LetterTileData</c> object</param>
    public void Initialize(string letterTileData)
    {
        LetterTileData data = JsonUtility.FromJson<LetterTileData>(letterTileData);
        Initialize(data.letter, data.column, data.row, (TileType)data.tileType);
    }

    public LetterTileData ToLetterTileData()
    {
        return new LetterTileData()
        {
            letter = letter,
            column = column,
            row = row,
            tileType = (int)tileType
        };
    }

    /// <summary>
    /// Apply the correct sprite based on current state and type
    /// </summary>
    private void ApplySprite()
    {
        // disable everything first
        normalSprite.SetActive(false);
        normalSelectedSprite.SetActive(false);
        fireSprite.SetActive(false);
        fireSelectedSprite.SetActive(false);
        bonusSprite.SetActive(false);
        bonusSelectedSprite.SetActive(false);
        goldSprite.SetActive(false);
        goldSelectedSprite.SetActive(false);
        diamondSprite.SetActive(false);
        diamondSelectedSprite.SetActive(false);

        // enable the proper type
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
            case TileType.Fire:
                if (isSelected)
                {
                    fireSelectedSprite.SetActive(true);
                }
                else
                {
                    fireSprite.SetActive(true);
                }
                break;
            case TileType.Bonus:
                if (isSelected)
                {
                    bonusSelectedSprite.SetActive(true);
                }
                else
                {
                    bonusSprite.SetActive(true);
                }
                break;
            case TileType.Gold:
                if (isSelected)
                {
                    goldSelectedSprite.SetActive(true);
                }
                else
                {
                    goldSprite.SetActive(true);
                }
                break;
            case TileType.Diamond:
                if (isSelected)
                {
                    diamondSelectedSprite.SetActive(true);
                }
                else
                {
                    diamondSprite.SetActive(true);
                }
                break;
            default:
                Debug.LogWarning("This LetterTile had an invalid TileType: " + tileType.ToString());
                break;
        }
    }

    public char GetLetter()
    {
        return letter;
    }

    //public void OnPointerClick(PointerEventData _)
    public void OnPointerClick()
    {
        if (isAnimating) return;
        GameManager.Instance.OnTileClick(new TilePos(column, row));
    }

    public void SetIsSelected(bool isSelected)
    {
        this.isSelected = isSelected;
        ApplySprite();
    }

    public void DestroyTile(TileDestroyReason reason)
    {
        float waitTime = 0f;
        if (reason == TileDestroyReason.Selected)
        {
            // animation is 30 frames at 60fps
            EnableAnimator();
            animator.SetTrigger("DestroySelected");
            waitTime = 0.5f;
        }
        else if (reason == TileDestroyReason.Fire)
        {
            // animation is 50 frames at 60fps
            EnableAnimator();
            animator.SetTrigger("DestroyFire");
            waitTime = 0.84f;
        }
        else if (reason == TileDestroyReason.Shuffled)
        {
            // animation is 30 frames at 60fps
            EnableAnimator();
            animator.SetTrigger("DestroyShuffle");
            waitTime = 0.5f;
        }
        StartCoroutine(WaitThenDestroySelf(waitTime));
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
        isAnimating = true;
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
        isAnimating = false;
    }

    private IEnumerator WaitThenDestroySelf(float duration)
    {
        yield return new WaitForSeconds(duration);
        Destroy(gameObject);
    }

#pragma warning disable IDE0051 // (Remove unused private members) Used by animation
    private void DisableAnimator()
#pragma warning restore IDE0051 // (Remove unused private members) Used by animation
    {
        animator.enabled = false;
    }

    private void EnableAnimator()
    {
        animator.enabled = true;
    }
}
