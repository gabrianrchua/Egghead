using UnityEngine;
using UnityEngine.InputSystem;

public class TouchscreenHandler : MonoBehaviour
{
    [SerializeField] private InputAction press, screenPosition;
    [SerializeField] private Camera mainCamera;

    private Vector2 currentScreenPosition;
    private LetterTile currentTile;

    private void OnEnable()
    {
        screenPosition.performed += OnScreenPositionPerformed;
        press.canceled += OnPressCanceled;

        screenPosition.Enable();
        press.Enable();
    }

    private void OnDisable()
    {
        screenPosition.performed -= OnScreenPositionPerformed;
        press.canceled -= OnPressCanceled;

        screenPosition.Disable();
        press.Disable();

        currentTile = null;
    }

    private void OnScreenPositionPerformed(InputAction.CallbackContext context)
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }
        }

        currentScreenPosition = context.ReadValue<Vector2>();
        //Debug.Log(currentScreenPosition);
        //mainCamera.ScreenToWorldPoint(currentScreenPosition);
        Ray ray = mainCamera.ScreenPointToRay(currentScreenPosition);
        /*if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Debug.Log("hit " + hit.collider.name);
        }*/
        RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction);
        if (hit.collider != null)
        {
            if (hit.collider.TryGetComponent<LetterTile>(out LetterTile tile))
            {
                if (currentTile != tile)
                {
                    currentTile = tile;
                    tile.OnPointerClick();
                }
            }
        }
    }

    /*press.performed += _ =>
        {
            Debug.Log("press performed");
        };*/

    private void OnPressCanceled(InputAction.CallbackContext context)
    {
        currentTile = null;
    }
}
