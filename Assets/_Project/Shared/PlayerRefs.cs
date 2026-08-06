using UnityEngine;

public class PlayerRefs : MonoBehaviour
{
    public static PlayerRefs Instance { get; private set; }

    [SerializeField] private Camera mainCamera;

    public Camera MainCamera => mainCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }
}
