using UnityEngine;
using UnityEngine.InputSystem;

public class TouchscreenHandler : MonoBehaviour
{
    [SerializeField] private InputAction press, screenPosition;
    [SerializeField] private Camera mainCamera;

    private Vector2 currentScreenPosition;
    private LetterTile currentTile;

    private void Awake()
    {
        screenPosition.Enable();
        press.Enable();
        screenPosition.performed += context =>
        {
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
        };
        /*press.performed += _ =>
        {
            Debug.Log("press performed");
        };*/
        press.canceled += _ =>
        {
            currentTile = null;
        };

    }
}
