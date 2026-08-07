using Alteruna.Multiplayer;
using Alteruna.Multiplayer.Core;
using Alteruna.Multiplayer.Unity;
using UnityEngine;

/// <summary>Replicates the runtime category assigned to a network-spawned visual prefab.</summary>
[DisallowMultipleComponent]
public sealed class NetworkItemState : AttributesSync
{
    bool initialized;

    public void Publish(int itemId, ItemCategory category, float colorHue)
    {
        ApplyState(itemId, (int)category, colorHue);
        InvokeRemoteMethod(nameof(ApplyState), UserId.All, itemId, (int)category, colorHue);
    }

    [SynchronizableMethod]
    void ApplyState(int itemId, int category, float colorHue)
    {
        if (initialized)
            return;

        initialized = true;
        ItemSpawner spawner = FindFirstObjectByType<ItemSpawner>();
        if (spawner != null)
            spawner.ApplySpawnedItemState(gameObject, itemId, (ItemCategory)category, colorHue);
    }
}
