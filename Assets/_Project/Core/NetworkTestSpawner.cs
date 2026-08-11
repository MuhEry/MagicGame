using UnityEngine;

[RequireComponent(typeof(Alteruna.Multiplayer.Unity.Spawner))]
public class NetworkTestSpawner : MonoBehaviour
{
    [SerializeField] private GameObject testPrefab;
    [SerializeField] private Transform spawnPoint;

    private Alteruna.Multiplayer.Unity.Spawner spawner;
    private AlterunaComponents.MultiplayerManager multiplayerManager;
    private bool hasSpawned;

    private void Awake()
    {
        spawner = GetComponent<Alteruna.Multiplayer.Unity.Spawner>();
        multiplayerManager = GetComponent<AlterunaComponents.MultiplayerManager>();
    }

    private void Start()
    {
        if (testPrefab == null)
        {
            Debug.LogError("[NetTestSpawn] Test Prefab atanmadı.", this);
            return;
        }

        spawner.SpawnableObjects.Clear();
        spawner.SpawnableObjects.Add(testPrefab);

        Debug.Log("[NetTestSpawn] Spawner hazır. Listeye test prefabı eklendi.", this);
    }

    [ContextMenu("Debug/Spawn Test Object")]
    public void SpawnTestObject()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[NetTestSpawn] Önce Play moduna gir.", this);
            return;
        }

        if (multiplayerManager == null || !multiplayerManager.IsConnected)
        {
            Debug.LogWarning("[NetTestSpawn] Sunucuya bağlı değilsin.", this);
            return;
        }

        // Geçici tanı testi: önce Alteruna Spawner'ın odaya yayılımını doğruluyoruz.
        // Host yetkisi, ortak vardiya yöneticisi eklendiğinde yeniden zorunlu kılınacak.
        if (!multiplayerManager.Me.IsHost)
            Debug.LogWarning("[NetTestSpawn] Host olmayan istemciden test spawn isteği gönderiliyor.", this);

        if (hasSpawned)
        {
            Debug.Log("[NetTestSpawn] Test nesnesi zaten üretildi.", this);
            return;
        }

        Transform point = spawnPoint != null ? spawnPoint : transform;
        GameObject spawned = spawner.Spawn(0, point.position, point.rotation);

        hasSpawned = spawned != null;
        Debug.Log($"[NetTestSpawn] Ağ nesnesi üretildi: {spawned?.name}", this);
    }
}
