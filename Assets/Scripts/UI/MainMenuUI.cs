using System.Collections;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuUI : MonoBehaviour
{
    [Header("Paneller")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject lobbyPanel;

    [Header("Menu")]
    [SerializeField] private TMP_InputField playerNameInputField; // Oyuncunun ismini gireceği alan
    [SerializeField] private Button hostButton;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private Button joinButton;

    [Header("Lobby")]
    [SerializeField] private TMP_Text playerCountText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text lobbyCodeText;
    [SerializeField] private TMP_Text warningText;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button renkliTeamButton;
    [SerializeField] private Button renksizTeamButton;
    [SerializeField] private TMP_Text playerListText;

    [Header("Oyun sahnesi")]
    [SerializeField] private string gameSceneName = "GameScene";

    private NetworkBootstrap _bootstrap;

    private void Awake()
    {
        // Artık Singleton olduğu için doğrudan Instance üzerinden alabiliriz (veya Find kullanmaya devam edebiliriz).
        _bootstrap = NetworkBootstrap.Instance ?? FindFirstObjectByType<NetworkBootstrap>();
    }

    private void Start()
    {
        // İmleci görünür ve serbest yap (Oyundan lobiye dönüldüğünde kilitli kalmaması için)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Kayıtlı bir oyuncu ismi varsa yükle, yoksa rastgele bir isim ata
        if (playerNameInputField != null)
        {
            playerNameInputField.text = PlayerPrefs.GetString("PlayerName", "Oyuncu " + Random.Range(1000, 9999));
            playerNameInputField.onValueChanged.AddListener((val) => PlayerPrefs.SetString("PlayerName", val));
        }

        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        disconnectButton.onClick.AddListener(OnDisconnectClicked);
        startGameButton.onClick.AddListener(OnStartGameClicked);
        
        if (renkliTeamButton) renkliTeamButton.onClick.AddListener(() => OnTeamClicked(1));
        if (renksizTeamButton) renksizTeamButton.onClick.AddListener(() => OnTeamClicked(2));
        
        // Eğer lobiye maçtan dönüldüyse (zaten bağlıysak) Host/Join yerine doğrudan lobiyi göster
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            ShowLobby();

            // Oyundan lobiye dönüldüğünde OnClientConnected tekrar çalışmaz.
            // Bu yüzden UI listesini güncel tutacak olayı (event) buradan yeniden dinlemeye başlamalıyız.
            if (NetworkLobbyManager.Instance != null)
            {
                NetworkLobbyManager.Instance.LobbyPlayers.OnListChanged += OnLobbyPlayersChanged;
            }

            UpdatePlayerListUI();
            if (NetworkManager.Singleton.IsServer) UpdatePlayerCount();
            if (startGameButton) startGameButton.gameObject.SetActive(NetworkManager.Singleton.IsServer);
        }
        else
        {
            ShowMenu();
        }
    }

    private void OnEnable()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        // Sahne değişirken (oyuna geçerken) hafıza sızıntısı ve hata olmaması için
        // UI objesi silinirken listeyi dinlemeyi bırakıyoruz.
        if (NetworkLobbyManager.Instance != null)
        {
            NetworkLobbyManager.Instance.LobbyPlayers.OnListChanged -= OnLobbyPlayersChanged;
        }
    }

    // ── Buton işlemleri ───────────────────────────────────────────────────

    private async void OnHostClicked()
    {
        SetButtons(false);
        SetStatus("Bağlanıyor...");

        try
        {
            string code = await _bootstrap.StartHostAsync();
            
            if (string.IsNullOrEmpty(code))
            {
                SetWarning("Host başlatılamadı. Lütfen tekrar deneyin.");
                SetButtons(true);
                return;
            }

            ShowLobby();
            SetLobbyCode($"Kod: {code}");
            UpdatePlayerCount();
            startGameButton.gameObject.SetActive(true);
        }
        catch (System.Exception e)
        {
            SetWarning($"Hata: {e.Message}");
            SetButtons(true);
        }
    }

    private async void OnJoinClicked()
    {
        string code = joinCodeInputField.text.Trim().ToUpper();
        
        // Eğer oyuncu yanlışlıkla "KOD: ABCD12" şeklinde kopyaladıysa "KOD:" kısmını temizle
        if (code.StartsWith("KOD:")) code = code.Substring(4).Trim();
        else if (code.StartsWith("KOD")) code = code.Substring(3).Trim();

        if (string.IsNullOrEmpty(code))
        {
            SetWarning("Join kodu gir!");
            return;
        }

        SetButtons(false);
        SetStatus($"Bağlanıyor... ({code})");

        try
        {
            await _bootstrap.StartClientAsync(code);
            
            // Bağlantı başladığında (CustomMessagingManager aktifleştiğinde) mesajları dinlemeye başla
            if (NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler("PlayerCountUpdate", ReceivePlayerCountUpdate);
            }

            StartCoroutine(ConnectionTimeout(8f));
        }
        catch (System.Exception e)
        {
            SetWarning($"Hata: {e.Message}");
            SetButtons(true);
        }
    }

    private IEnumerator ConnectionTimeout(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (!NetworkManager.Singleton.IsConnectedClient)
        {
            _bootstrap.Disconnect();
            yield return new WaitUntil(() => !NetworkManager.Singleton.IsListening);
            SetWarning("Bağlantı başarısız. Kodu kontrol et.");
            SetButtons(true);
        }
    }

    private void OnTeamClicked(int teamId)
    {
        if (NetworkLobbyManager.Instance != null)
        {
            NetworkLobbyManager.Instance.SelectTeamRpc(teamId);
            string teamName = teamId == 1 ? "Renkli Takım" : "Renksiz Takım";
            SetStatus($"{teamName} seçildi.");
        }
    }

    private void OnStartGameClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        
        if (NetworkLobbyManager.Instance != null && !NetworkLobbyManager.Instance.CanStartGame(out string errorMessage))
        {
            SetWarning(errorMessage);
            return;
        }
        
        if (NetworkManager.Singleton.SceneManager != null)
        {
            // Karmaşık index hesaplaması yerine yukarıda tanımlanan Inspector değişkenini kullanıyoruz.
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.LogError("[Menu] NetworkManager üzerinde 'Enable Scene Management' aktif değil!");
        }
    }

    private void OnDisconnectClicked()
    {
        // Eğer Client isek dinlemeyi bırak
        if (!NetworkManager.Singleton.IsHost && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("PlayerCountUpdate");
        }
        _bootstrap.Disconnect();
        StartCoroutine(WaitForShutdownThenMenu(""));
    }

    // ── Network olayları ──────────────────────────────────────────────────

    private void OnClientConnected(ulong clientId)
    {
        // Eğer Host isek, yeni gelen kişiyi say ve herkese yeni sayıyı bildir
        if (NetworkManager.Singleton.IsServer)
        {
            UpdatePlayerCount();
        }

        // Biz (kendi bilgisayarımız) bağlandığında
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (NetworkLobbyManager.Instance != null)
            {
                // Sunucuya ismimizi iletiyoruz
                string myName = playerNameInputField != null && !string.IsNullOrWhiteSpace(playerNameInputField.text) 
                    ? playerNameInputField.text 
                    : "Gizemli Oyuncu";
                NetworkLobbyManager.Instance.SetPlayerNameRpc(myName);

                NetworkLobbyManager.Instance.LobbyPlayers.OnListChanged += OnLobbyPlayersChanged;
                UpdatePlayerListUI(); // Bağlanır bağlanmaz listeyi bir kez çiz
            }

            if (!NetworkManager.Singleton.IsHost)
            {
                ShowLobby();
                startGameButton.gameObject.SetActive(false);
                SetWarning("Host oyunu başlatana kadar bekle...");
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Biri koptuğunda (Host isek) güncel sayıyı herkese yeniden gönder
        if (NetworkManager.Singleton.IsServer)
        {
            UpdatePlayerCount();
        }

        // Biz koptuysak (Hem Host hem Client için geçerli)
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            if (NetworkLobbyManager.Instance != null)
            {
                NetworkLobbyManager.Instance.LobbyPlayers.OnListChanged -= OnLobbyPlayersChanged;
            }

            if (!NetworkManager.Singleton.IsHost)
            {
                if (NetworkManager.Singleton.CustomMessagingManager != null)
                {
                    NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("PlayerCountUpdate");
                }
                _bootstrap.Disconnect();
                StartCoroutine(WaitForShutdownThenMenu("Sunucu bağlantısı kesildi."));
            }
        }
    }

    // Shutdown tamamlanmadan menüye dönme — re-host sorununu çözer
    private IEnumerator WaitForShutdownThenMenu(string msg)
    {
        yield return new WaitUntil(() => !NetworkManager.Singleton.IsListening);
        ShowMenu();
        SetStatus(msg);
        SetButtons(true);
    }

    // ── Yardımcı ─────────────────────────────────────────────────────────

    private void UpdatePlayerCount()
    {
        if (!playerCountText) return;
        
        if (NetworkManager.Singleton.IsServer)
        {
            int count = NetworkManager.Singleton.ConnectedClients.Count;
            SetPlayerCountUI(count);

            // İstemcilere (Client) güncel sayıyı bildir
            if (NetworkManager.Singleton.CustomMessagingManager != null)
            {
                using var writer = new FastBufferWriter(4, Allocator.Temp);
                writer.WriteValueSafe(count);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll("PlayerCountUpdate", writer);
            }
        }
    }

    private void ReceivePlayerCountUpdate(ulong senderClientId, FastBufferReader messagePayload)
    {
        messagePayload.ReadValueSafe(out int count);
        SetPlayerCountUI(count);
    }

    private void SetPlayerCountUI(int count)
    {
        if (playerCountText)
        {
            playerCountText.text = $"Oyuncular: {count} / {NetworkBootstrap.MaxConnections}";
        }
    }

    // ── Lobi Listesi UI ───────────────────────────────────────────────────

    private void OnLobbyPlayersChanged(NetworkListEvent<LobbyPlayerState> changeEvent)
    {
        UpdatePlayerListUI();

        if (changeEvent.Type == NetworkListEvent<LobbyPlayerState>.EventType.Add)
        {
            SetStatus($"{changeEvent.Value.PlayerName} bağlandı.");
        }
        else if (changeEvent.Type == NetworkListEvent<LobbyPlayerState>.EventType.Remove)
        {
            SetStatus($"{changeEvent.Value.PlayerName} ayrıldı.");
        }
    }

    private void UpdatePlayerListUI()
    {
        if (playerListText == null || NetworkLobbyManager.Instance == null) return;

        string list = "Oyuncu Listesi:\n\n";
        foreach (var player in NetworkLobbyManager.Instance.LobbyPlayers)
        {
            string teamName = player.TeamId == 1 ? "<color=#FF5555>Renkli Takım</color>" : (player.TeamId == 2 ? "<color=#5555FF>Renksiz Takım</color>" : "<color=#AAAAAA>Seçim Yapmadı</color>");
            list += $"{player.PlayerName} - {teamName}\n";
        }
        playerListText.text = list;
    }

    private void ShowMenu()
    {
        menuPanel.SetActive(true);
        lobbyPanel.SetActive(false);
        if (playerListText) playerListText.text = "";
    }

    private void ShowLobby()
    {
        menuPanel.SetActive(false);
        lobbyPanel.SetActive(true);
    }

    private void SetStatus(string msg)
    {
        if (statusText) statusText.text = msg;
        if (!string.IsNullOrEmpty(msg)) Debug.Log($"[Menu] {msg}");
    }

    private void SetWarning(string msg)
    {
        if (warningText) warningText.text = msg;
        if (!string.IsNullOrEmpty(msg)) Debug.LogWarning($"[Menu] {msg}");
    }

    private void SetLobbyCode(string code)
    {
        if (lobbyCodeText) lobbyCodeText.text = $"{code}";
    }

    private void SetButtons(bool interactable)
    {
        hostButton.interactable = interactable;
        joinButton.interactable = interactable;
    }
}
