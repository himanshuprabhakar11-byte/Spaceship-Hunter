using UnityEngine;

public class CollisionHandler : MonoBehaviour
{
    [SerializeField] GameObject destroyedVFX;

    GameSceneManager gameSceneManager;

    private void Start()
    {
        gameSceneManager = FindFirstObjectByType<GameSceneManager>();
    }

    private void OnTriggerEnter(Collider other)
    {
        gameSceneManager.ReloadLevel();
        Instantiate(destroyedVFX, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }
}



















