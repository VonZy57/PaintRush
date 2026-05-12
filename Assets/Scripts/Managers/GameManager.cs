using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum GameState
{
    WaitingForPlayers, // Lobi/Yükleme ekranı
    PreRound,          // 5 sn hazırlık (Hareket kilitli)
    ObjectivePhase,    // 90 sn Switch açma (Ölünce 5 sn sonra doğma)
    DelayPhase,        // Geçiş öncesi ses ve bekleme aşaması (Hareket kilitli)
    TransitionPhase,   // 3 sn ikincil spawnlara ışınlanma (Hareket kilitli)
    DefusePhase,       // 40 sn Bomba patlama süresi (Ölüm kalıcı)
    RoundEnd,          // Raund bitti, puanlar dağıtıldı
    MatchEnd           // Bir takım 3 puan yaptı, maç bitti
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Oyun Durumu Senkronizasyonu")]
    public NetworkVariable<GameState> CurrentState = new NetworkVariable<GameState>(GameState.WaitingForPlayers);
    public NetworkVariable<float> RoundTimer = new NetworkVariable<float>(0f);
    
    [Header("Süre Ayarları")]
    public float preRoundDuration = 5f;
    public float objectiveDuration = 90f;
    public float delayDuration = 4f; // Delay aşaması süresi
    public float transitionDuration = 3f;
    public float defuseDuration = 40f;
    public float roundEndDuration = 8f; // Patlama görselini izlemek için süreyi uzattık
    public float matchEndDuration = 5f; // Maç bitince lobiye dönmeden önceki bekleme süresi
    public float respawnCooldown = 5f;

    [Header("Görev Ayarları")]
    public NetworkVariable<int> ActivatedSwitches = new NetworkVariable<int>(0);
    public const int TotalSwitchesNeeded = 3;

    public const int ScoreToWin = 3;

    [Header("Skor Tablosu")]
    public NetworkVariable<int> RenkliTeamScore = new NetworkVariable<int>(0);
    public NetworkVariable<int> RenksizTeamScore = new NetworkVariable<int>(0);

    [Header("Ses Ayarları")]
    public AudioSource announcementAudioSource;
    public AudioClip transitionAnnouncement;

    [Header("Harita Patlama Efektleri")]
    public Renderer mapRenderer; // Haritanın ana mesh rendereri
    public Material[] explodedMapMaterials; // Patlama anında sırayla geçilecek 4 materyal
    private Material[] _originalMapMaterials; // Raund başı sıfırlamak için orijinal materyaller

    [Header("Sahne Ayarları")]
    public string lobbySceneName = "MainMenu"; // Lobi sahnenizin tam adı

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Şimdilik "Oyuncular Bekleniyor" aşamasında kal
            CurrentState.Value = GameState.WaitingForPlayers;
            
            // Bütün oyuncuların (Client'ların) sahneyi tam olarak yüklemesini dinle
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnAllClientsLoadedScene;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnAllClientsLoadedScene;
        }
    }

    private void OnAllClientsLoadedScene(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        // Yüklenen sahne bu oyun sahnesiyse maçı güvenle başlatabiliriz
        if (sceneName == gameObject.scene.name)
        {
            Debug.Log($"[GameManager] Tüm oyuncular sahneyi (ve shaderları) yükledi. {clientsCompleted.Count} oyuncu hazır. Maç başlıyor!");
            StartPreRound();
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        // Sadece süreli aşamalarda sayacı çalıştır
        if (CurrentState.Value == GameState.WaitingForPlayers || CurrentState.Value == GameState.MatchEnd)
            return;

        if (RoundTimer.Value > 0)
        {
            RoundTimer.Value -= Time.deltaTime;
            
            // Süre bittiğinde ne olacağına karar ver
            if (RoundTimer.Value <= 0)
            {
                RoundTimer.Value = 0;
                HandleTimerZero();
            }
        }
    }
    
    private void HandleTimerZero()
    {
        switch (CurrentState.Value)
        {
            case GameState.PreRound:
                // 5 sn bitti -> Hareket serbest, Switchleri açma süresi başlar
                ChangeState(GameState.ObjectivePhase, objectiveDuration);
                break;
                
            case GameState.ObjectivePhase:
                // 90 sn bitti ve Renkli takım switchleri açamadı -> Renksiz takım kazandı
                EndRound(2);
                break;
                
            case GameState.DelayPhase:
                // Delay bitti -> TransitionPhase'e geç, ölü/diri herkesi ışınla ve dirilt
                PlayerHealth[] allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
                foreach (var player in allPlayers)
                {
                    if (player != null)
                    {
                        var (pos, rot) = PlayerSpawnManager.Instance.GetRandomSpawnPoint(player.GetTeam());
                        player.Respawn(pos, rot); // isDead'i false yapar, canı 100'ler ve ışınlar
                    }
                }
                ChangeState(GameState.TransitionPhase, transitionDuration);
                break;

            case GameState.TransitionPhase:
                // 3 sn ışınlanma arası bitti -> Bomba sayacı başlar (Renkli takım bombayı kurmuş sayılır)
                ChangeState(GameState.DefusePhase, defuseDuration);
                break;
                
            case GameState.DefusePhase:
                // 40 sn bitti ve Renksiz takım bombayı imha edemedi -> Bomba patlar, Renkli takım kazandı
                TriggerMapExplosionRpc();
                EndRound(1);
                break;
                
            case GameState.RoundEnd:
                // Raund arası bitti -> Yeni raunda geç
                StartPreRound();
                break;
        }
    }

    // ── Aşama Kontrolleri ────────────────────────────────────────────────

    private void ChangeState(GameState newState, float duration)
    {
        CurrentState.Value = newState;
        RoundTimer.Value = duration;
        Debug.Log($"[GameManager] Yeni Aşama: {newState} | Süre: {duration} sn");
    }

    private void StartPreRound()
    {
        ActivatedSwitches.Value = 0;
        
        // Yeni Raund Başlangıcı: Bütün oyuncuları başlangıç noktalarına ışınla (Ölüyse dirilir, canları dolar)
        PlayerHealth[] allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (player != null)
            {
                var (pos, rot) = PlayerSpawnManager.Instance.GetRandomSpawnPoint(player.GetTeam());
                player.Respawn(pos, rot); 
            }
        }

        // Bütün oyuncularda harita görsellerini (materyal ve decallar) sıfırla
        ResetMapVisualsRpc();

        // Şalterleri sıfırla
        InteractableSwitch[] switches = FindObjectsByType<InteractableSwitch>(FindObjectsSortMode.None);
        foreach (var sw in switches)
        {
            if (sw != null) sw.ResetSwitch();
        }

        // Bombayı sıfırla (Renksiz takım önceki raundda imha ettiyse tekrar kurulu hale gelsin)
        BombController[] bombs = FindObjectsByType<BombController>(FindObjectsSortMode.None);
        foreach (var bomb in bombs)
        {
            if (bomb != null) bomb.ResetBomb();
        }

        ChangeState(GameState.PreRound, preRoundDuration);
    }

    public void SwitchActivated()
    {
        if (!IsServer) return;
        
        ActivatedSwitches.Value++;
        Debug.Log($"[GameManager] Şalter açıldı! ({ActivatedSwitches.Value}/{TotalSwitchesNeeded})");

        if (ActivatedSwitches.Value >= TotalSwitchesNeeded)
        {
            TriggerTransitionPhase();
        }
    }

    // Dışarıdan (örneğin InteractableSwitch veya BombController) çağrılacak fonksiyonlar
    public void TriggerTransitionPhase()
    {
        if (CurrentState.Value == GameState.ObjectivePhase)
        {
            PlayAnnouncementRpc();
            ChangeState(GameState.DelayPhase, delayDuration);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void PlayAnnouncementRpc()
    {
        if (announcementAudioSource != null && transitionAnnouncement != null)
        {
            announcementAudioSource.PlayOneShot(transitionAnnouncement);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void TriggerMapExplosionRpc()
    {
        StartCoroutine(MapMaterialSwapRoutine());

        // Haritadaki ana bombayı bul ve sesini çal
        BombController[] bombs = FindObjectsByType<BombController>(FindObjectsSortMode.None);
        foreach (var bomb in bombs)
        {
            if (bomb != null) bomb.PlayExplosionSound();
        }
    }

    private IEnumerator MapMaterialSwapRoutine()
    {
        if (mapRenderer == null || explodedMapMaterials == null || explodedMapMaterials.Length == 0) yield break;

        // Orijinal materyalleri yedekle (ilk çalışmada)
        if (_originalMapMaterials == null || _originalMapMaterials.Length == 0)
            _originalMapMaterials = mapRenderer.sharedMaterials;

        // Değişim yapacağımız materyal dizisini kopyala
        Material[] currentMaterials = mapRenderer.sharedMaterials;

        for (int i = 0; i < explodedMapMaterials.Length; i++)
        {
            if (i < currentMaterials.Length && explodedMapMaterials[i] != null)
            {
                currentMaterials[i] = explodedMapMaterials[i];
                mapRenderer.sharedMaterials = currentMaterials; // Array'i Mesh'e geri ata
            }
            yield return new WaitForSeconds(1f); // Her materyal değişiminde yarım saniye bekle
        }
    }

    [Rpc(SendTo.Everyone)]
    private void ResetMapVisualsRpc()
    {
        // Harita materyallerini orijinal (patlamamış) haline geri döndür
        if (mapRenderer != null && _originalMapMaterials != null && _originalMapMaterials.Length > 0)
        {
            mapRenderer.sharedMaterials = _originalMapMaterials;
        }

        // Haritadaki tüm mermi ve patlama izlerini (Decal) anında temizle
        MapDecal[] allDecals = FindObjectsByType<MapDecal>(FindObjectsSortMode.None);
        foreach (var decal in allDecals)
        {
            if (decal != null) Destroy(decal.gameObject);
        }
    }

    public void EndRound(int winnerTeamId)
    {
        Debug.Log($"[GameManager] Raund bitti. Kazanan Takım: {(winnerTeamId == 1 ? "Renkli Takım" : "Renksiz Takım")}");
        
        if (winnerTeamId == 1) RenkliTeamScore.Value++;
        else if (winnerTeamId == 2) RenksizTeamScore.Value++;

        if (RenkliTeamScore.Value >= ScoreToWin || RenksizTeamScore.Value >= ScoreToWin)
        {
            CurrentState.Value = GameState.MatchEnd;
            StartCoroutine(ReturnToLobbyRoutine());
            Debug.Log("[GameManager] MAÇ BİTTİ!");
        }
        else
        {
            ChangeState(GameState.RoundEnd, roundEndDuration);
        }
    }

    private IEnumerator ReturnToLobbyRoutine()
    {
        yield return new WaitForSeconds(matchEndDuration);
        
        if (IsServer && NetworkManager.Singleton.SceneManager != null)
        {
            // 2. SORUNUN ÇÖZÜMÜ: Lobiye dönmeden önce tüm oyuncu karakterlerini yok et.
            // Karakterler lobi sahnesine taşınmaz ve bir sonraki maçta çiftlenmezler.
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                {
                    client.PlayerObject.Despawn(true);
                }
            }

            NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    // ── Ölüm ve Doğma Sistemi ────────────────────────────────────────────

    public void OnPlayerDied(PlayerHealth victim)
    {
        if (!IsServer) return;

        int teamId = victim.GetTeam();
        Debug.Log($"[GameManager] Oyuncu öldü. Takım: {(teamId == 1 ? "Renkli" : "Renksiz")} | Aşama: {CurrentState.Value}");

        if (CurrentState.Value == GameState.ObjectivePhase)
        {
            // Deathmatch evresi: 5 saniye sonra doğur
            StartCoroutine(RespawnRoutine(victim, respawnCooldown));
        }
        else if (CurrentState.Value == GameState.DefusePhase)
        {
            // Kalıcı ölüm evresi: Renksiz takım tamamen öldü mü kontrol et
            if (teamId == 2) CheckRenksizTeamWipeout();
        }
    }

    private IEnumerator RespawnRoutine(PlayerHealth victim, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (victim == null || !victim.IsSpawned) yield break;

        // Geçiş aşamasında oyuncu topluca (otomatik) diriltildiyse (isDead = false) tekrar ışınlama yapma
        if (!victim.isDead.Value) yield break;

        // Objective veya Transition evresindeysek normal bir şekilde doğmaya izin ver
        if (CurrentState.Value == GameState.ObjectivePhase || CurrentState.Value == GameState.TransitionPhase)
        {
            var (pos, rot) = PlayerSpawnManager.Instance.GetRandomSpawnPoint(victim.GetTeam());
            victim.Respawn(pos, rot);
        }
    }

    private void CheckRenksizTeamWipeout()
    {
        PlayerHealth[] allPlayers = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
        bool isAnyRenksizAlive = false;

        foreach (var player in allPlayers)
        {
            if (player.GetTeam() == 2 && !player.isDead.Value)
            {
                isAnyRenksizAlive = true;
                break;
            }
        }

        if (!isAnyRenksizAlive)
        {
            Debug.Log("[GameManager] Renksiz Takımın tamamı öldü! Renkli Takım kazanıyor.");
            EndRound(1); // 1: Renkli Takım
        }
    }
}