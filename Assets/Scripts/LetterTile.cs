using System.Collections;
using TMPro;
using UnityEngine;
using Egghead.SaveSystem;

[RequireComponent(typeof(Animator))]
public class LetterTile : MonoBehaviour
{
    [System.Serializable]
    private class TileVisuals
    {
        public GameObject normal;
        public GameObject selected;
    }

    [Header("Object References")]
    [SerializeField] private Animator animator;
    [SerializeField] private TMP_Text letterText;
    [SerializeField] private SpriteRenderer fireWarningSpriteRenderer;
    [SerializeField] private GameObject submitHint;

    [Tooltip("Order: Normal, Fire, Bonus, Gold, Diamond"), SerializeField]
    private TileVisuals[] tileVisuals = { new(), new(), new(), new(), new() };

    private const float dropAnimationDuration = 0.5f;

    private static readonly int FireCriticalHash = Animator.StringToHash("FireCritical");
    private static readonly int FireWarningHash = Animator.StringToHash("FireWarning");
    private static readonly int DestroySelectedHash = Animator.StringToHash("DestroySelected");
    private static readonly int DestroyFireHash = Animator.StringToHash("DestroyFire");
    private static readonly int DestroyShuffleHash = Animator.StringToHash("DestroyShuffle");

    private char letter;
    private TileType tileType;
    private int column; // y; outer index
    private int row; // x; inner index
    private bool isSelected;
    private bool isAnimating; // if animation is playing, disable touches
    private GameObject activeSprite;

    private void Awake()
    {
        foreach (TileVisuals visuals in tileVisuals)
        {
            visuals.normal.SetActive(false);
            visuals.selected.SetActive(false);
        }
    }

    public TileType GetTileType()
    {
        return tileType;
    }

    public enum TileType { Normal, Fire, Bonus, Gold, Diamond }
    public enum TileDestroyReason { Selected, Fire, Shuffled };

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

    public SavedLetterTileData ToLetterTileData()
    {
        return new SavedLetterTileData()
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
        int typeIndex = (int)tileType;
        if (typeIndex >= tileVisuals.Length)
        {
            Debug.LogWarning($"This LetterTile had an invalid TileType: {tileType}");
            return;
        }

        TileVisuals visuals = tileVisuals[typeIndex];
        GameObject nextSprite = isSelected ? visuals.selected : visuals.normal;

        if (activeSprite == nextSprite)
        {
            return;
        }

        if (activeSprite != null)
        {
            activeSprite.SetActive(false);
        }

        nextSprite.SetActive(true);
        activeSprite = nextSprite;
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
            animator.SetTrigger(DestroySelectedHash);
            waitTime = 0.5f;
        }
        else if (reason == TileDestroyReason.Fire)
        {
            // animation is 50 frames at 60fps
            EnableAnimator();
            animator.SetTrigger(DestroyFireHash);
            waitTime = 0.84f;
        }
        else if (reason == TileDestroyReason.Shuffled)
        {
            // animation is 30 frames at 60fps
            EnableAnimator();
            animator.SetTrigger(DestroyShuffleHash);
            waitTime = 0.5f;
        }
        StartCoroutine(WaitThenDestroySelf(waitTime));
    }

    public void TriggerFireCritical()
    {
        EnableAnimator();
        animator.SetTrigger(FireCriticalHash);
    }

    public void TriggerFireWarning()
    {
        EnableAnimator();
        animator.SetTrigger(FireWarningHash);
    }

    public void UntriggerFireWarning()
    {
        DisableAnimator();
        fireWarningSpriteRenderer.color = new Color(1f, 0f, 0f, 0f);
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
            float t = Mathf.Clamp01(timeElapsed / duration);
            t = 1f - Mathf.Pow(1f - t, 2f); // ease-out
            //transform.position = new Vector3(originalX, Mathf.Lerp(originalY, destinationY, Mathf.Clamp01(timeElapsed / duration)), 0f); // simple linear
            transform.position = new Vector3(originalX, Mathf.Lerp(originalY, destinationY, t), 0f); // nonlinear
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

    private void DisableAnimator()
    {
        animator.enabled = false;
    }

    private void EnableAnimator()
    {
        animator.enabled = true;
    }

    public void ShowSubmitHint()
    {
        submitHint.SetActive(true);
    }

    public void HideSubmitHint()
    {
        submitHint.SetActive(false);
    }
}
