using System.Collections;
using UnityEngine;

public class SelfDeleter : MonoBehaviour
{
    public void DeleteSelf()
    {
        Destroy(gameObject);
    }

    public void DisableSelf()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Disables self after one frame. Used when calling as an animation event
    /// to disable self safely without throwing errors, especially with
    /// <c>TMP_InputField</c> on children components.
    /// </summary>
    public void DisableSelfSafely()
    {
        StartCoroutine(DisableNextFrame());
    }

    private IEnumerator DisableNextFrame()
    {
        yield return null;
        DisableSelf();
    }
}
