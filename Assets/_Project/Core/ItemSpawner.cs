using System;
using System.Collections.Generic;
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

    [Header("Varyasyon")]
    [Tooltip("Ayni gorunur prefab her dogusta Sesli, Parlak veya Agir olabilir. Gorunus kategori ipucu vermez.")]
    [SerializeField] private bool randomizeCategoryPerSpawn = true;

    [Tooltip("Sesli secilen prefabin kendi klibi yoksa kullanilir.")]
    [SerializeField] private AudioClip fallbackRattleClip;

    private readonly List<ItemSpawnEntry> spawnQueue = new List<ItemSpawnEntry>();
    private System.Random random;
    private int nextQueueIndex;
    private int lastSpawnedItemId = int.MinValue;

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
        lastSpawnedItemId = int.MinValue;
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

        // 9 prefab ilk turla sinirli degil: vardiya bitene kadar kuyrugu
        // ayni seedli random kaynakla yeniden karistirip devam ederiz.
        if (spawnQueue.Count > 0 && nextQueueIndex >= spawnQueue.Count)
            RefillQueue();

        if (nextQueueIndex >= spawnQueue.Count)
        {
            Debug.Log("ItemSpawner: Bu vardiya için tanımlı tüm eşyalar üretildi.", this);
            return null;
        }

        ItemSpawnEntry entry = spawnQueue[nextQueueIndex++];
        Transform point = spawnPoint != null ? spawnPoint : transform;

        // Projedeki Instantiate çağrısı yalnızca bu dosyada tutulur.
        CurrentSpawnedItem = Instantiate(entry.prefab, point.position, point.rotation);
        CurrentSpawnedItem.name = entry.prefab.name + "_" + entry.itemId;

        if (randomizeCategoryPerSpawn)
            ApplyRuntimeCategory(CurrentSpawnedItem, entry.itemId);

        lastSpawnedItemId = entry.itemId;

        ItemSpawned?.Invoke(CurrentSpawnedItem);

        return CurrentSpawnedItem;
    }

    public void StopSpawning()
    {
        IsSpawning = false;
    }

    void RefillQueue()
    {
        for (int index = spawnQueue.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (spawnQueue[index], spawnQueue[swapIndex]) = (spawnQueue[swapIndex], spawnQueue[index]);
        }

        // Kuyruk sinirinda ayni gorunen prefab iki kez arka arkaya gelmesin.
        if (spawnQueue.Count > 1 && spawnQueue[0].itemId == lastSpawnedItemId)
            (spawnQueue[0], spawnQueue[1]) = (spawnQueue[1], spawnQueue[0]);

        nextQueueIndex = 0;
    }

    void ApplyRuntimeCategory(GameObject item, int itemId)
    {
        var identity = item.GetComponentInChildren<ItemIdentity>();
        if (identity == null || identity.ItemData == null)
            return;

        // Kaynak ItemData bir assettir. Onu degistirmek tum prefablarin
        // kategorisini degistirir; bu kopya yalnizca dogan nesneye aittir.
        ItemData runtimeData = Instantiate(identity.ItemData);
        runtimeData.id = itemId;
        runtimeData.category = (ItemCategory)random.Next(0, 3);

        switch (runtimeData.category)
        {
            case ItemCategory.Sesli:
                runtimeData.mass = 1f;
                if (runtimeData.rattleClip == null)
                    runtimeData.rattleClip = FindFallbackRattleClip();
                runtimeData.glowColor = Color.black;
                break;

            case ItemCategory.Parlak:
                runtimeData.mass = 1f;
                runtimeData.rattleClip = null;
                runtimeData.glowColor = GetGlowColor();
                break;

            case ItemCategory.Agir:
                runtimeData.mass = 8f;
                runtimeData.rattleClip = null;
                runtimeData.glowColor = Color.black;
                break;
        }

        identity.SetRuntimeItemData(runtimeData);

        var body = item.GetComponentInChildren<Rigidbody>();
        if (body != null)
            body.mass = runtimeData.mass;

        ApplyNonCategoryColor(item);
    }

    AudioClip FindFallbackRattleClip()
    {
        if (fallbackRattleClip != null)
            return fallbackRattleClip;

        foreach (ItemSpawnEntry entry in itemPrefabs)
        {
            var identity = entry?.prefab != null ? entry.prefab.GetComponentInChildren<ItemIdentity>() : null;
            if (identity != null && identity.ItemData != null && identity.ItemData.rattleClip != null)
                return identity.ItemData.rattleClip;
        }

        return null;
    }

    Color GetGlowColor()
    {
        // HDR renk, URP emissive materyalde gozle gorulur bir parlama verir.
        Color baseColor = Color.HSVToRGB((float)random.NextDouble(), 0.65f, 1f);
        return baseColor * 4f;
    }

    void ApplyNonCategoryColor(GameObject item)
    {
        var renderer = item.GetComponentInChildren<Renderer>();
        if (renderer == null)
            return;

        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_BaseColor", Color.HSVToRGB((float)random.NextDouble(), 0.25f, 0.8f));
        renderer.SetPropertyBlock(block);
    }
}
