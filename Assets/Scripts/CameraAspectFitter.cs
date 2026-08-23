using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraAspectFitter : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float desiredAspectRatio;
    [SerializeField] private float baseOrthographicSize = 5f;

    private float lastAspectRatio;
    private bool hasAppliedAspectRatio;

    private void Awake()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
        }

        Refresh(Screen.width, Screen.height);
    }

    private void Update()
    {
        Refresh(Screen.width, Screen.height);
    }

    private bool Refresh(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 ||
            screenHeight <= 0 ||
            cam == null ||
            desiredAspectRatio <= 0f ||
            baseOrthographicSize <= 0f)
        {
            return false;
        }

        float currentAspectRatio = (float)screenWidth / screenHeight;
        if (hasAppliedAspectRatio && currentAspectRatio == lastAspectRatio)
        {
            return false;
        }

        float multiplier = Mathf.Max(1f, desiredAspectRatio / currentAspectRatio);
        cam.orthographicSize = baseOrthographicSize * multiplier;

        lastAspectRatio = currentAspectRatio;
        hasAppliedAspectRatio = true;
        return true;
    }
}
