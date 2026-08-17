using UnityEngine;

public class FramerateManager : MonoBehaviour
{
    private const int LowFpsModeFramerate = 30;

    void Start()
    {
        int targetFps = ApplyFpsToggle();
        if (UIManager.Instance != null)
        {
            UIManager.Instance.SetLowFpsToggleValue(targetFps == LowFpsModeFramerate);
        }
    }

    private int GetTargetFps()
    {
        return PlayerPrefs.GetInt("LowFpsMode", 0) == 1
            ? LowFpsModeFramerate : (int)Screen.currentResolution.refreshRateRatio.value;
    }

    private int ApplyFpsToggle()
    {
        int targetFps = GetTargetFps();
        Application.targetFrameRate = targetFps;
        return targetFps;
    }

    /// <summary>
    /// UI dyamic function to be called when the low fps toggle value changed.
    /// </summary>
    /// <param name="value">The new value, true = lock to 30fps</param>
    public void SetLowFpsToggle(bool value)
    {
        PlayerPrefs.SetInt("LowFpsMode", value ? 1 : 0);
        Debug.Log($"LowFpsMode saved as {value}");
        ApplyFpsToggle();
    }
}
