using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null)
        {
            Debug.LogWarning($"More than one {GetType().Name} in the scene! This one will be disabled.");
            enabled = false;
            return;
        }
        Instance = this as T;
    }
}
