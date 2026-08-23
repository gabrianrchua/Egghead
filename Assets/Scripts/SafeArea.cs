using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    [SerializeField] private RectTransform rectTransform;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Rect lastSafeArea;
    private bool hasAppliedGeometry;

    private void Awake()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        Refresh(Screen.width, Screen.height, Screen.safeArea);
    }

    private void Update()
    {
        Refresh(Screen.width, Screen.height, Screen.safeArea);
    }

    private bool Refresh(int screenWidth, int screenHeight, Rect safeArea)
    {
        if (screenWidth <= 0 || screenHeight <= 0 || rectTransform == null)
        {
            return false;
        }

        if (hasAppliedGeometry &&
            screenWidth == lastScreenWidth &&
            screenHeight == lastScreenHeight &&
            safeArea == lastSafeArea)
        {
            return false;
        }

        Vector2 min = safeArea.position;
        Vector2 max = safeArea.position + safeArea.size;

        min.x /= screenWidth;
        min.y /= screenHeight;
        max.x /= screenWidth;
        max.y /= screenHeight;

        rectTransform.anchorMin = min;
        rectTransform.anchorMax = max;

        lastScreenWidth = screenWidth;
        lastScreenHeight = screenHeight;
        lastSafeArea = safeArea;
        hasAppliedGeometry = true;
        return true;
    }
}
