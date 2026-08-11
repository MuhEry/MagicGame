using System;
using System.Collections.Generic;
using Alteruna.Multiplayer.Unity;
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

    [Header("Faz 2")]
    [Tooltip("Alteruna Spawner. Bos ise oyun cevrimdisi calisir; ODADAYKEN zorunludur.\n" +
             "Tools > Gece Vardiyasi > Faz 2 Kurulumunu Uygula bunu baglar.")]
    [SerializeField] private Spawner networkSpawner;

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

        NetworkShiftCoordinator network = NetworkShiftCoordinator.Instance;
        bool inNetworkedRoom = network != null && network.IsInRoom;

        // Odadayken esyayi yalnizca host uretir; istemciye Alteruna kopyasini getirir.
        // Kuyruk indeksini de TUKETMEDEN cikiyoruz ki host degisirse sira kaymasin.
        if (inNetworkedRoom && !network.IsHost)
            return null;

        ItemSpawnEntry entry = spawnQueue[nextQueueIndex++];
        Transform point = spawnPoint != null ? spawnPoint : transform;

        if (inNetworkedRoom)
        {
            CurrentSpawnedItem = SpawnOverNetwork(entry, point);
        }
        else
        {
            // Projedeki yerel Instantiate çağrısı yalnızca bu dosyada tutulur (mimari kural 3).
            CurrentSpawnedItem = Instantiate(entry.prefab, point.position, point.rotation);
        }

        if (CurrentSpawnedItem == null)
            return null;

        CurrentSpawnedItem.name = entry.prefab.name + "_" + entry.itemId;

        ItemSpawned?.Invoke(CurrentSpawnedItem);

        return CurrentSpawnedItem;
    }

    /// <summary>
    /// Alteruna Spawner uzerinden uretim. Basarisiz olursa SESSIZCE yerel
    /// Instantiate'e DUSMEZ: o durumda host esyayi gorur, istemci bos tezgaha
    /// bakar ve kimse sebebini anlamaz. Gorunmez desenkronizasyon yerine gorunur hata.
    /// </summary>
    private GameObject SpawnOverNetwork(ItemSpawnEntry entry, Transform point)
    {
        if (networkSpawner == null)
        {
            Debug.LogError(
                "[ItemSpawner] Odadayiz ama Alteruna Spawner atanmamis; esya uretilmedi.\n" +
                "Tools > Gece Vardiyasi > Faz 2 Kurulumunu Uygula komutunu calistir.", this);
            return null;
        }

        // Indeks Alteruna'nin KENDI listesine gore cozulmeli. itemPrefabs.IndexOf
        // kullanilirsa ve iki liste ayni sirada degilse host bir esyayi, istemci
        // bambaska bir esyayi gorur.
        int prefabIndex = networkSpawner.SpawnableObjects.IndexOf(entry.prefab);
        if (prefabIndex < 0)
        {
            Debug.LogError(
                $"[ItemSpawner] '{entry.prefab.name}' Alteruna Spawner.SpawnableObjects listesinde yok; " +
                "esya uretilmedi. Faz 2 kurulum komutunu tekrar calistir.", this);
            return null;
        }

        return networkSpawner.Spawn(prefabIndex, point.position, point.rotation);
    }

    public void StopSpawning()
    {
        IsSpawning = false;
    }
}
