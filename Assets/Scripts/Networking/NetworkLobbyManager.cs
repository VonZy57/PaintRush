using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// 1. Oyuncu Verisi (Ağ üzerinden taşınacak paket)
public struct LobbyPlayerState : INetworkSerializable, IEquatable<LobbyPlayerState>
{
    public ulong ClientId;
    public FixedString32Bytes PlayerName; // Ağ dostu string tipi
    public int TeamId; // 0: Tarafsız, 1: Renkli Takım, 2: Renksiz Takım

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref TeamId);
    }

    public bool Equals(LobbyPlayerState other)
    {
        return ClientId == other.ClientId && 
               PlayerName == other.PlayerName && 
               TeamId == other.TeamId;
    }
}

public class NetworkLobbyManager : NetworkBehaviour
{
    public static NetworkLobbyManager Instance { get; private set; }

    // 2. Senkronize Liste (İçinde bir değişiklik olduğunda otomatik olarak tüm Client'lara yansır)
    public NetworkList<LobbyPlayerState> LobbyPlayers;

    // Benzersiz oyuncu isimleri oluşturmak için bir sayaç
    private int _playerJoinedCounter = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Awake içinde listeyi mutlaka oluşturmalıyız
        LobbyPlayers = new NetworkList<LobbyPlayerState>();

        // Sahne değiştiğinde lobi verilerinin kaybolmaması (silinmemesi) için bu kodu ekliyoruz.
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // 1. SORUNUN ÇÖZÜMÜ: Eski oturumdan kalan oyuncu listesini temizle
            LobbyPlayers.Clear();
            _playerJoinedCounter = 0;

            // Yeni oyuncular katıldıkça veya koptukça listeyi güncellemek için dinleyiciler
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            // Host'un kendisi (veya önceden bağlanmış olanlar) için listeye ekleme yap
            foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                AddPlayerToList(clientId);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId) => AddPlayerToList(clientId);

    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == clientId)
            {
                LobbyPlayers.RemoveAt(i);
                break;
            }
        }
    }

    private void AddPlayerToList(ulong clientId)
    {
        // Eğer bu oyuncu (ClientId) zaten listedeyse, mükerrer eklemeyi engelle
        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == clientId)
                return;
        }

        // 3. Sıraya göre isimlendirme (Player 1, Player 2 vb.)
        _playerJoinedCounter++;
            
        LobbyPlayers.Add(new LobbyPlayerState
        {
            ClientId = clientId,
            PlayerName = $"Player {_playerJoinedCounter}",
            TeamId = 0 // Başlangıçta tarafsız
        });
    }

    // 4. İsim Belirleme (Client'lar bağlandığında kendi belirledikleri ismi sunucuya iletir)
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetPlayerNameRpc(string playerName, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == senderClientId)
            {
                var updatedPlayer = LobbyPlayers[i];
                updatedPlayer.PlayerName = playerName;
                LobbyPlayers[i] = updatedPlayer; // Struct'ı güncelle
                break;
            }
        }
    }

    // 5. Takım Seçimi (Client'lar Host'a takım seçtiklerini bu metodla bildirir)
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SelectTeamRpc(int teamId, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == senderClientId)
            {
                var updatedPlayer = LobbyPlayers[i];
                updatedPlayer.TeamId = teamId;
                LobbyPlayers[i] = updatedPlayer; // Struct'ı güncelle
                break;
            }
        }
    }

    // 6. Başlatma Kontrolü (Herkes takım seçti mi ve takımlar eşit mi?)
    public bool CanStartGame(out string errorMessage)
    {
        errorMessage = "";
        if (LobbyPlayers.Count == 0) return false;

        int renkliCount = 0;
        int renksizCount = 0;

        foreach (var player in LobbyPlayers)
        {
            if (player.TeamId == 0)
            {
                errorMessage = "Tüm oyuncular takım seçimi yapmalı!";
                return false;
            }
            
            if (player.TeamId == 1) renkliCount++;
            else if (player.TeamId == 2) renksizCount++;
        }

        if (renkliCount != renksizCount)
        {
            errorMessage = "Takımlar eşit sayıda olmalı!";
            return false;
        }
        return true;
    }
}
