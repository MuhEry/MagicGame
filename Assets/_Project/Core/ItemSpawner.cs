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
    [SerializeField] private int seed = 12345;
    [SerializeField] private List<ItemSpawnEntry> itemPrefabs = new List<ItemSpawnEntry>();

    [Header("Sahne")]
    [SerializeField] private Transform spawnPoint;

    private readonly List<ItemSpawnEntry> spawnQueue = new List<ItemSpawnEntry>();
    private System.Random random;
    private int nextQueueIndex;

    public bool IsSpawning { get; private set; }
    public GameObject CurrentSpawnedItem { get; private set; }
    public int Seed => seed;

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
        random = new System.Random(seed);
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

        ItemSpawnEntry entry = spawnQueue[nextQueueIndex++];
        Transform point = spawnPoint != null ? spawnPoint : transform;

        // Projedeki Instantiate çağrısı yalnızca bu dosyada tutulur.
        CurrentSpawnedItem = Instantiate(entry.prefab, point.position, point.rotation);
        CurrentSpawnedItem.name = entry.prefab.name + "_" + entry.itemId;
        return CurrentSpawnedItem;
    }

    public void StopSpawning()
    {
        IsSpawning = false;
    }
}
