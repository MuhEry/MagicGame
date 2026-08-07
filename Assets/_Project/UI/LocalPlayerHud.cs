using System.Collections;
using System.Collections.Generic;
using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;

/// <summary>
/// Keeps one world-space HUD per player while ensuring each client renders and
/// interacts only with its own panel. The shared cabinet signage stays untouched.
/// </summary>
[DisallowMultipleComponent]
public sealed class LocalPlayerHud : CommunicationBridge
{
    struct BehaviourState
    {
        public Behaviour behaviour;
        public bool initiallyEnabled;
    }

    [SerializeField, Range(0, 1)] int playerSlot;

    readonly List<BehaviourState> controlledBehaviours = new List<BehaviourState>();
    bool subscribed;
    bool isInRoom;
    bool isHost;

    public int PlayerSlot => playerSlot;
    public string ContextLabel => isInRoom
        ? $"Oyuncu {playerSlot + 1} - {(isHost ? "HOST" : "İSTEMCİ")}"
        : $"Oyuncu {playerSlot + 1} - ÇEVRİMDIŞI";

    public void ConfigureSlot(int slot)
    {
        playerSlot = Mathf.Clamp(slot, 0, 1);
    }

    void Awake()
    {
        CacheControlledBehaviours();
        SetVisible(playerSlot == 0);
    }

    IEnumerator Start()
    {
        while (!subscribed && Multiplayer == null)
            yield return null;

        Subscribe();
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void HandleRoomJoined(RoomJoinedEvent args)
    {
        isInRoom = true;
        isHost = args.Controller.Me.Index == args.Controller.LowestUserIndex;
        int localSlot = Mathf.Abs(args.Controller.Me.Index) % 2;
        SetVisible(localSlot == playerSlot);
    }

    void HandleRoomLeft(RoomLeftEvent _)
    {
        isInRoom = false;
        isHost = false;
        SetVisible(playerSlot == 0);
    }

    void HandleOtherUserJoined(OtherUserJoinedEvent args)
    {
        isHost = args.Controller.Me.Index == args.Controller.LowestUserIndex;
        RefreshPresenter();
    }

    void HandleOtherUserLeft(OtherUserLeftEvent args)
    {
        isHost = args.Controller.Me.Index == args.Controller.LowestUserIndex;
        RefreshPresenter();
    }

    void Subscribe()
    {
        if (subscribed || Multiplayer == null)
            return;

        Multiplayer.OnRoomJoined.AddListener(HandleRoomJoined);
        Multiplayer.OnRoomLeft.AddListener(HandleRoomLeft);
        Multiplayer.OnOtherUserJoined.AddListener(HandleOtherUserJoined);
        Multiplayer.OnOtherUserLeft.AddListener(HandleOtherUserLeft);
        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed || Multiplayer == null)
            return;

        Multiplayer.OnRoomJoined.RemoveListener(HandleRoomJoined);
        Multiplayer.OnRoomLeft.RemoveListener(HandleRoomLeft);
        Multiplayer.OnOtherUserJoined.RemoveListener(HandleOtherUserJoined);
        Multiplayer.OnOtherUserLeft.RemoveListener(HandleOtherUserLeft);
        subscribed = false;
    }

    void CacheControlledBehaviours()
    {
        controlledBehaviours.Clear();
        foreach (Behaviour behaviour in GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour == this)
                continue;

            controlledBehaviours.Add(new BehaviourState
            {
                behaviour = behaviour,
                initiallyEnabled = behaviour.enabled
            });
        }
    }

    void SetVisible(bool visible)
    {
        foreach (BehaviourState state in controlledBehaviours)
        {
            if (state.behaviour != null)
                state.behaviour.enabled = visible && state.initiallyEnabled;
        }

        if (visible)
            RefreshPresenter();
    }

    void RefreshPresenter()
    {
        ShiftHudPresenter presenter = GetComponent<ShiftHudPresenter>();
        if (presenter != null && presenter.enabled)
            presenter.RefreshPlayerContext();
    }
}
