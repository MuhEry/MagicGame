using System;
using System.Collections.Generic;
using AlterunaComponents;
using UnityEngine;

[Serializable]
public class ItemSpawnEntry
{
    [Tooltip("A'nın ItemData varlığıyla aynı kalıcı kimlik.")]
    public int itemId;

    [Tooltip("Üzerinde ItemData ve ItemProbe bulunan A prefabı.")]
    public GameObject prefab;
}

/// <summary>
/// Projedeki tek Instantiate noktasıdır. Aynı seed, aynı prefab sırasını üretir.
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Header("Sıra")]
    [Tooltip("0 = her vardiyada farklı sıra üretilir.\n" +
             "0'dan farklıysa sıra HER ZAMAN aynıdır (hata ayıklama / tekrar üretilebilir test).\n" +
             "Faz 2'de host, SetSeed ile istemciye aynı değeri geçirir.")]
    [SerializeField] private int seed;
    [SerializeField] private List<ItemSpawnEntry> itemPrefabs = new List<ItemSpawnEntry>();

    [Header("Sahne")]
    [SerializeField] private Transform spawnPoint;

    [Header("Ag")]
    [SerializeField] private Alteruna.Multiplayer.Unity.Spawner networkSpawner;
    [SerializeField] private AlterunaComponents.MultiplayerManager multiplayerManager;

    private readonly List<ItemSpawnEntry> spawnQueue = new List<ItemSpawnEntry>();
    private System.Random random;
    private int nextQueueIndex;

    /// <summary>
    /// Bacadan bir esya dustugu anda tetiklenir. Ses/isik gibi sunum efektleri
    /// bu event'e baglanir - kimse Update icinde spawner'i yoklamaz (mimari kural 7).
    /// </summary>
    public event Action<GameObject> ItemSpawned;

    public bool IsSpawning { get; private set; }
    public GameObject CurrentSpawnedItem { get; private set; }

    /// <summary>Bu vardiyada FIILEN kullanilan seed. Faz 2'de host bunu istemciye gonderir.</summary>
    public int Seed { get; private set; }

    private void Awake()
    {
        if (networkSpawner == null)
            networkSpawner = GetComponent<Alteruna.Multiplayer.Unity.Spawner>();
        if (multiplayerManager == null)
            multiplayerManager = FindFirstObjectByType<AlterunaComponents.MultiplayerManager>();

        ConfigureNetworkSpawner();
    }

    /// <summary>
    /// Vardiya BASLAMADAN once cagrilir. Faz 2'de host, istemcilerin ayni sirayi
    /// uretmesi icin kendi seed'ini bu metotla dagitir (mimari kural 4).
    /// </summary>
    public void SetSeed(int value)
    {
        seed = value;
    }

    private void OnValidate()
    {
        if (spawnPoint == null)
            spawnPoint = transform;
    }

    /// <summary>
    /// Her vardiyanın başında çağrılır. Sıra, aynı seed ile deterministik biçimde karıştırılır.
    /// </summary>
    public void BeginShift()
    {
        if (!CanHostSpawn())
            return;

        // seed 0 ise her vardiya farkli bir sira. UnityEngine.Random KULLANILMAZ
        // (mimari kural 4) - tohum Environment.TickCount'tan alinir, uretim yine
        // System.Random ile deterministiktir. Loglanan seed ile ayni tur tekrar oynatilabilir.
        Seed = seed != 0 ? seed : System.Environment.TickCount;
        random = new System.Random(Seed);
        Debug.Log($"[ItemSpawner] Vardiya seed = {Seed} (Inspector'da 0 = her tur farkli)", this);

        spawnQueue.Clear();

        foreach (ItemSpawnEntry entry in itemPrefabs)
        {
            if (entry != null && entry.prefab != null)
                spawnQueue.Add(entry);
        }

        for (int index = spawnQueue.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (spawnQueue[index], spawnQueue[swapIndex]) = (spawnQueue[swapIndex], spawnQueue[index]);
        }

        nextQueueIndex = 0;
        IsSpawning = true;

        if (spawnQueue.Count == 0)
            Debug.LogWarning("ItemSpawner: Inspector'a geçerli bir eşya prefabı eklenmedi.", this);
    }

    public GameObject SpawnNext()
    {
        if (!CanHostSpawn())
            return null;

        if (!IsSpawning)
        {
            Debug.LogWarning("ItemSpawner: Vardiya başlamadan eşya üretilemez.", this);
            return null;
        }

        if (nextQueueIndex >= spawnQueue.Count)
        {
            Debug.Log("ItemSpawner: Bu vardiya için tanımlı tüm eşyalar üretildi.", this);
            return null;
        }

        ItemSpawnEntry entry = spawnQueue[nextQueueIndex++];
        Transform point = spawnPoint != null ? spawnPoint : transform;

        // Projedeki Instantiate çağrısı yalnızca bu dosyada tutulur.
        int prefabIndex = networkSpawner.SpawnableObjects.IndexOf(entry.prefab);
        if (prefabIndex < 0)
        {
            Debug.LogError($"[ItemSpawner] Prefab Spawner listesinde yok: {entry.prefab.name}", this);
            return null;
        }

        CurrentSpawnedItem = networkSpawner.Spawn(prefabIndex, point.position, point.rotation);
        if (CurrentSpawnedItem == null)
            return null;

        CurrentSpawnedItem.name = entry.prefab.name + "_" + entry.itemId;

        ItemSpawned?.Invoke(CurrentSpawnedItem);

        return CurrentSpawnedItem;
    }

    public void StopSpawning()
    {
        IsSpawning = false;
    }

    public void Despawn(GameObject item)
    {
        if (item == null || !CanHostSpawn())
            return;

        networkSpawner.Despawn(item);
        if (CurrentSpawnedItem == item)
            CurrentSpawnedItem = null;
    }

    private void ConfigureNetworkSpawner()
    {
        if (networkSpawner == null)
        {
            Debug.LogError("[ItemSpawner] Alteruna Spawner bulunamadi.", this);
            return;
        }

        networkSpawner.SpawnableObjects.Clear();
        foreach (ItemSpawnEntry entry in itemPrefabs)
        {
            if (entry != null && entry.prefab != null)
                networkSpawner.SpawnableObjects.Add(entry.prefab);
        }

        networkSpawner.ForceSync = true;
    }

    private bool CanHostSpawn()
    {
        if (networkSpawner == null || multiplayerManager == null)
        {
            Debug.LogWarning("[ItemSpawner] Ag bilesenleri hazir degil.", this);
            return false;
        }

        if (!multiplayerManager.InRoom || !multiplayerManager.IsHost())
        {
            Debug.LogWarning("[ItemSpawner] Esyayi yalnizca LAN hostu uretebilir.", this);
            return false;
        }

        return true;
    }
}
