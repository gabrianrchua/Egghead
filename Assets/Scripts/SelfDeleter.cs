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
}
