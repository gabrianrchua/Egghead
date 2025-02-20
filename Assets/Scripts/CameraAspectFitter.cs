using UnityEngine;

[RequireComponent (typeof(Camera))]
public class CameraAspectFitter : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float desiredAspectRatio;

    private void Awake()
    {
        // only shrink for tall devices, never grow for short devices to prevent board from flowing off screen
        float currentRatio = (float)Screen.width / Screen.height;
        float multiplier = desiredAspectRatio / currentRatio;
        if (multiplier >= 1f)
        {
            cam.orthographicSize = 5 * multiplier;
        }
    }
}
