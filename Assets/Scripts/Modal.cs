using System;
using TMPro;
using UnityEngine;

public class Modal : Singleton<Modal>
{
    [Header("Object references")]
    [SerializeField] private GameObject modalBackgroundObject;
    [SerializeField] private TextMeshProUGUI promptText;
    [SerializeField] private GameObject negativeActionButton;
    [SerializeField] private TextMeshProUGUI negativeActionText;
    [SerializeField] private TextMeshProUGUI positiveActionText;
    [SerializeField] private Animator animator;
    
    private static readonly int OutHash = Animator.StringToHash("Out");

    private Action onNegativeAction;
    private Action onPositiveAction;

    /// <summary>
    /// Initializes and opens the modal, subscribing to events for this instance
    /// </summary>
    /// <param name="negativeActionCallback">Action to invoke when the negative button is clicked.</param>
    /// <param name="positiveActionCallback">Action to invoke when the positive button is clicked</param>
    /// <param name="prompt">The text of the prompt on the modal</param>
    /// <param name="negativeActionLabel">The text of the negative action. Pass in an empty string <c>""</c> to disable the button</param>
    /// <param name="positiveActionLabel">The text of the positive action</param>
    public void OpenModal(
        Action negativeActionCallback,
        Action positiveActionCallback,
        string prompt,
        string negativeActionLabel = "Cancel",
        string positiveActionLabel = "Yes")
    {
        negativeActionButton.SetActive(negativeActionLabel != "");
        promptText.text = prompt;
        negativeActionText.text = negativeActionLabel;
        positiveActionText.text = positiveActionLabel;
        onNegativeAction = negativeActionCallback;
        onPositiveAction = positiveActionCallback;
        modalBackgroundObject.SetActive(true);
    }

    /// <summary>
    /// Action to take when the negative action button is clicked, then closes modal
    /// </summary>
    public void OnNegativeActionClick()
    {
        onNegativeAction?.Invoke();
        ClearActions();
        animator.SetTrigger(OutHash);
    }

    /// <summary>
    /// Action to take when the positive action button is clicked, then closes modal
    /// </summary>
    public void OnPositiveActionClick()
    {
        onPositiveAction?.Invoke();
        ClearActions();
        animator.SetTrigger(OutHash);
    }

    /// <summary>
    /// Private helper to clear actions so that further clicks don't cause duplicate
    /// action invokes / callback calls
    /// </summary>
    private void ClearActions()
    {
        onNegativeAction = null;
        onPositiveAction = null;
    }
}
