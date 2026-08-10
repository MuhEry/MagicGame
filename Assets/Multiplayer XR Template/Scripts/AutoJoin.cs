using System.Collections;
using Alteruna.Multiplayer.Unity;
using Alteruna.Multiplayer.Unity.EventArgument;
using UnityEngine;

namespace Alteruna
{
	/// <summary>
	/// Quest sahnesini agdan bagimsiz baslatir; Alteruna baglantisini ilk kareden
	/// sonra kurar ve baglanti kurulunca tek bir matchmaking istegi gonderir.
	/// </summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(-9000)]
	public sealed class AutoJoin : CommunicationBridge
	{
		public enum BootstrapState
		{
			WaitingForManager,
			WaitingForService,
			WaitingBeforeConnect,
			Connecting,
			Connected,
			Matchmaking,
			InRoom,
			Offline,
		}

		[SerializeField, Min(0f)] float initialConnectDelay = 1f;
		[SerializeField, Min(1f)] float connectionTimeout = 20f;
		[SerializeField, Min(1f)] float reconnectDelay = 5f;
		[SerializeField, Min(5f)] float matchmakingWarningDelay = 30f;

		bool listenersAttached;
		bool matchmakingRequested;
		bool connectionTimeoutReported;
		float connectStartedAt;
		float matchmakingStartedAt;
		float nextConnectAttempt;

		public BootstrapState State { get; private set; } = BootstrapState.WaitingForManager;
		public string Status { get; private set; } = "Alteruna manager bekleniyor";

		IEnumerator Start()
		{
			while (Multiplayer == null)
				yield return null;

			AttachListeners();
			State = BootstrapState.WaitingForService;
			Status = "Alteruna servisi hazirlaniyor";
			Debug.Log("[Multiplayer] Yerel XR hazir; Alteruna servis baslangici bekleniyor.", this);

			// SDK'nin resmi ornekleri baglanti/oda islemlerinden once Started
			// durumunu bekler. Lisans ve proje kimligi dogrulamasi tamamlanmadan
			// Connect cagirmak Quest'te belirsiz bir IsConnecting durumuna yol acabilir.
			while (Multiplayer != null && !Multiplayer.Started)
				yield return null;

			if (Multiplayer == null)
				yield break;

			// MultiplayerManager.Start ve diger sahne Start metodlari tamamlansin.
			// Yerel XR rig bu sirada agdan bagimsiz olarak goruntu vermeye baslar.
			State = BootstrapState.WaitingBeforeConnect;
			Status = "VR sahnesi hazirlanıyor";
			yield return null;

			if (initialConnectDelay > 0f)
				yield return new WaitForSecondsRealtime(initialConnectDelay);

			if (Multiplayer.IsConnected)
				HandleConnectedInternal();
			else
				AttemptConnect();
		}

		void Update()
		{
			if (Multiplayer == null)
				return;

			if (!Multiplayer.Started)
			{
				State = BootstrapState.WaitingForService;
				Status = "Alteruna servisi hazirlaniyor";
				return;
			}

			if (Multiplayer.InRoom)
			{
				State = BootstrapState.InRoom;
				return;
			}

			if (Multiplayer.IsConnected)
			{
				if (!matchmakingRequested)
					RequestMatchmaking();
				else if (Time.realtimeSinceStartup - matchmakingStartedAt >= matchmakingWarningDelay &&
				         State == BootstrapState.Matchmaking)
				{
					Status = "Matchmaking oyuncu bekliyor";
				}

				return;
			}

			if (Multiplayer.IsConnecting)
			{
				State = BootstrapState.Connecting;
				Status = "Alteruna sunucusuna baglaniliyor";

				if (!connectionTimeoutReported &&
				    Time.realtimeSinceStartup - connectStartedAt >= connectionTimeout)
				{
					connectionTimeoutReported = true;
					Status = "Baglanti zaman asimi; yeniden denenecek";
					Debug.LogWarning(
						$"[Multiplayer] Alteruna baglantisi {connectionTimeout:0} saniyede tamamlanmadi. " +
						"Baglanti sifirlanacak; XR ve cevrimdisi oyun calismaya devam edecek.", this);
					Multiplayer.Disconnect();
					nextConnectAttempt = Time.realtimeSinceStartup + reconnectDelay;
				}

				return;
			}

			State = BootstrapState.Offline;
			Status = "Cevrimdisi; yeniden baglanti bekleniyor";

			if (Time.realtimeSinceStartup >= nextConnectAttempt)
				AttemptConnect();
		}

		void OnDestroy()
		{
			DetachListeners();
		}

		void AttachListeners()
		{
			if (listenersAttached || Multiplayer == null)
				return;

			Multiplayer.OnConnected.AddListener(HandleConnected);
			Multiplayer.OnDisconnected.AddListener(HandleDisconnected);
			Multiplayer.OnRoomJoined.AddListener(HandleRoomJoined);
			Multiplayer.OnRoomLeft.AddListener(HandleRoomLeft);
			listenersAttached = true;
		}

		void DetachListeners()
		{
			if (!listenersAttached || Multiplayer == null)
				return;

			Multiplayer.OnConnected.RemoveListener(HandleConnected);
			Multiplayer.OnDisconnected.RemoveListener(HandleDisconnected);
			Multiplayer.OnRoomJoined.RemoveListener(HandleRoomJoined);
			Multiplayer.OnRoomLeft.RemoveListener(HandleRoomLeft);
			listenersAttached = false;
		}

		void AttemptConnect()
		{
			if (Multiplayer == null || Multiplayer.IsConnected || Multiplayer.IsConnecting)
				return;

			State = BootstrapState.Connecting;
			Status = "Alteruna baglantisi baslatiliyor";
			connectStartedAt = Time.realtimeSinceStartup;
			nextConnectAttempt = connectStartedAt + reconnectDelay;
			connectionTimeoutReported = false;

			Debug.Log("[Multiplayer] VR sahnesi hazir; Alteruna baglantisi baslatiliyor.", this);
			Multiplayer.Connect();
		}

		void HandleConnected(ConnectedEvent _)
		{
			HandleConnectedInternal();
		}

		void HandleConnectedInternal()
		{
			connectionTimeoutReported = false;
			State = BootstrapState.Connected;
			Status = "Alteruna baglandi";
			Debug.Log("[Multiplayer] Alteruna sunucusuna baglanildi.", this);
			RequestMatchmaking();
		}

		void HandleDisconnected(DisconnectedEvent _)
		{
			matchmakingRequested = false;
			State = BootstrapState.Offline;
			Status = "Baglanti kesildi; yeniden denenecek";
			nextConnectAttempt = Time.realtimeSinceStartup + reconnectDelay;
			Debug.LogWarning("[Multiplayer] Alteruna baglantisi kesildi; yeniden denenecek.", this);
		}

		void HandleRoomJoined(RoomJoinedEvent _)
		{
			matchmakingRequested = false;
			State = BootstrapState.InRoom;
			Status = "Iki oyunculu odada";
			Debug.Log("[Multiplayer] Matchmaking odasina katilindi.", this);
		}

		void HandleRoomLeft(RoomLeftEvent _)
		{
			matchmakingRequested = false;
			State = Multiplayer != null && Multiplayer.IsConnected
				? BootstrapState.Connected
				: BootstrapState.Offline;
			Status = "Odadan cikildi";

			if (Multiplayer != null && Multiplayer.IsConnected)
				RequestMatchmaking();
		}

		void RequestMatchmaking()
		{
			if (matchmakingRequested || Multiplayer == null ||
			    !Multiplayer.IsConnected || Multiplayer.InRoom)
				return;

			matchmakingRequested = true;
			matchmakingStartedAt = Time.realtimeSinceStartup;
			State = BootstrapState.Matchmaking;
			Status = "Ikinci oyuncu aranıyor";
			Multiplayer.JoinMatchmaking();
			Debug.Log("[Multiplayer] Iki oyunculu matchmaking baslatildi.", this);
		}
	}
}
