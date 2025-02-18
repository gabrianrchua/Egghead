using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class LetterTile : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text letterText;

    private char letter;

    public void Initialize(char letter)
    {
        this.letter = letter; // TODO: special case for Q
        letterText.text = letter.ToString();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("clicked me");
    }
}
