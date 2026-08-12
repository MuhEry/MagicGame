using AlterunaComponents;
using UnityEngine;

[RequireComponent(typeof(Alteruna.Multiplayer.Unity.Spawner))]
[RequireComponent(typeof(MultiplayerManager))]
public sealed class NetworkTestSpawner : MonoBehaviour
{
    [SerializeField] private GameObject testPrefab;
    [SerializeField] private Transform spawnPoint;

    private Alteruna.Multiplayer.Unity.Spawner spawner;
    private MultiplayerManager multiplayerManager;
    private bool hasSpawned;

    private void Awake()
    {
        spawner = GetComponent<Alteruna.Multiplayer.Unity.Spawner>();
        multiplayerManager = GetComponent<MultiplayerManager>();
    }

    private void Start()
    {
        if (testPrefab == null)
        {
            Debug.LogError("[NetTestSpawn] Test prefab atanmadi.", this);
            return;
        }

        spawner.SpawnableObjects.Clear();
        spawner.SpawnableObjects.Add(testPrefab);
        spawner.ForceSync = true;

        Debug.Log("[NetTestSpawn] Alteruna Spawner hazir; ForceSync acik.", this);
    }

    [ContextMenu("Debug/Spawn Test Object")]
    public void SpawnTestObject()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[NetTestSpawn] Once Play moduna gir.", this);
            return;
        }

        // LAN host, yerel odasini kurdugunda InRoom=true olur; IsConnected ise
        // uzak bir endpoint baglantisini ifade ettigi icin host tarafinda false
        // kalabilir. Spawn icin oda ve host yetkisi yeterlidir.
        if (multiplayerManager == null || !multiplayerManager.InRoom)
        {
            Debug.LogWarning("[NetTestSpawn] Bir LAN odasina bagli degilsin.", this);
            return;
        }

        if (!multiplayerManager.IsHost())
        {
            Debug.LogWarning("[NetTestSpawn] Yalnizca host ag nesnesi uretebilir.", this);
            return;
        }

        if (hasSpawned)
        {
            Debug.Log("[NetTestSpawn] Test nesnesi zaten uretildi.", this);
            return;
        }

        Transform point = spawnPoint != null ? spawnPoint : transform;
        GameObject spawned = spawner.Spawn(0, point.position, point.rotation);

        hasSpawned = spawned != null;
        Debug.Log($"[NetTestSpawn] Host ag nesnesi uretti: {spawned?.name}", this);
    }
}
