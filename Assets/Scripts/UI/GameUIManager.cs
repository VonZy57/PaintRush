using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

public class GameUIManager : MonoBehaviour
{
    public static GameUIManager Instance { get; private set; }

    [Header("UI Elementleri")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text renkliTeamScoreText;
    [SerializeField] private TMP_Text renksizTeamScoreText;
    [SerializeField] private TMP_Text phaseText;
    [SerializeField] private TMP_Text switchesText;
    [SerializeField] private TMP_Text healthText;

    [Header("Silah UI")]
    [SerializeField] private TMP_Text ammoText;

    [Header("Etkileşim (İlerleme) UI")]
    [SerializeField] private GameObject interactionPanel;
    [SerializeField] private Slider interactionProgressBar;
    [SerializeField] private TMP_Text interactionText;

    [Header("Bomba Cooldown UI")]
    [SerializeField] private TMP_Text fragCooldownText;
    [SerializeField] private TMP_Text smokeCooldownText;
    [SerializeField] private TMP_Text flashCooldownText;

    [Header("Flash Bombası UI")]
    [SerializeField] private Image flashOverlay;

    [Header("Hasar Efekti UI")]
    [SerializeField] private Image hitOverlay;

    [Header("Ölüm ve Yeniden Doğma UI")]
    [SerializeField] private GameObject deathPanel;
    [SerializeField] private TMP_Text deathCountdownText;
    private Coroutine _deathCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        // GameManager henüz sahnede yoksa bekle
        if (GameManager.Instance == null) return;

        // 1. Süreyi Güncelle (MM:SS Formatında)
        float time = GameManager.Instance.RoundTimer.Value;
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        
        if (timerText) 
            timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        // 2. Skorları Güncelle
        if (renkliTeamScoreText) 
            renkliTeamScoreText.text = GameManager.Instance.RenkliTeamScore.Value.ToString();
            
        if (renksizTeamScoreText) 
            renksizTeamScoreText.text = GameManager.Instance.RenksizTeamScore.Value.ToString();

        // 3. Aşama (State) İsmini Güncelle
        if (phaseText) 
            phaseText.text = GetPhaseName(GameManager.Instance.CurrentState.Value);

        // 4. Şalter Görevi Durumunu Güncelle
        if (switchesText)
        {
            if (GameManager.Instance.CurrentState.Value == GameState.ObjectivePhase)
                switchesText.text = $"Şalterler: {GameManager.Instance.ActivatedSwitches.Value} / {GameManager.TotalSwitchesNeeded}";
            else
                switchesText.text = ""; // Sadece şalter açma evresinde ekranda göster
        }
    }

    private string GetPhaseName(GameState state)
    {
        return state switch
        {
            GameState.WaitingForPlayers => "Oyuncular Bekleniyor...",
            GameState.PreRound => "Hazırlık Evresi",
            GameState.ObjectivePhase => "Görev: Şalterleri Aç!",
            GameState.DelayPhase => "Delay",
            GameState.TransitionPhase => "Bölge Değişimi",
            GameState.DefusePhase => "Görev: Bombayı İmha Et!",
            GameState.RoundEnd => "Raund Bitti",
            GameState.MatchEnd => "Maç Bitti",
            _ => ""
        };
    }

    public void UpdateAmmoUI(int currentAmmo, int reserveAmmo)
    {
        if (ammoText) ammoText.text = $"{currentAmmo} / {reserveAmmo}";
    }

    public void ShowInteraction(string message, float progress)
    {
        if (interactionPanel && !interactionPanel.activeSelf) interactionPanel.SetActive(true);
        if (interactionText) interactionText.text = message;
        if (interactionProgressBar) interactionProgressBar.value = progress;
    }

    public void HideInteraction()
    {
        if (interactionPanel && interactionPanel.activeSelf) interactionPanel.SetActive(false);
    }

    public void UpdateHealthUI(int currentHealth, int maxHealth = 100)
    {
        if (healthText) healthText.text = $"{currentHealth} Hp";
    }

    public void UpdateGrenadeCooldownUI(float frag, float smoke, float flash)
    {
        SetCooldownText(fragCooldownText, frag);
        SetCooldownText(smokeCooldownText, smoke);
        SetCooldownText(flashCooldownText, flash);
    }

    private static void SetCooldownText(TMP_Text label, float remaining)
    {
        if (label == null) return;
        bool onCooldown = remaining > 0f;
        label.gameObject.SetActive(onCooldown);
        if (onCooldown) label.text = Mathf.CeilToInt(remaining).ToString();
    }

    public void ShowFlash(float duration)
    {
        if (flashOverlay == null) return;
        flashOverlay.DOKill();
        flashOverlay.gameObject.SetActive(true);
        flashOverlay.color = Color.white;
        
        float fadeOutTime = 1f; // Son 1 saniyede yavaşça yok olsun
        float solidTime = Mathf.Max(0f, duration - fadeOutTime); // Geri kalan sürede tam beyaz kalsın

        flashOverlay.DOFade(0f, fadeOutTime).SetDelay(solidTime).SetEase(Ease.InOutQuad)
            .OnComplete(() => flashOverlay.gameObject.SetActive(false));
    }

    public void ShowHitEffect()
    {
        if (hitOverlay == null) return;
        
        hitOverlay.DOKill(); // Eğer önceden çalan bir animasyon varsa durdur (peş peşe hasar yendiğinde sıfırlanması için)
        hitOverlay.gameObject.SetActive(true);
        
        hitOverlay.color = new Color(1f, 1f, 1f, 0.8f); // Görünürlüğü (Alpha) %80'e çek
        hitOverlay.DOFade(0f, 0.4f).SetEase(Ease.OutQuad) // 0.4 saniye içinde yavaşça şeffaflaşıp kaybolsun
            .OnComplete(() => hitOverlay.gameObject.SetActive(false));
    }

    public void ShowRespawnScreen(float duration)
    {
        if (deathPanel) deathPanel.SetActive(true);
        if (_deathCoroutine != null) StopCoroutine(_deathCoroutine);
        _deathCoroutine = StartCoroutine(RespawnCountdownRoutine(duration));
    }

    public void ShowDeadScreen()
    {
        if (deathPanel) deathPanel.SetActive(true);
        if (deathCountdownText) deathCountdownText.text = "Öldün!\n(Bu el yeniden doğamazsın)";
        if (_deathCoroutine != null) StopCoroutine(_deathCoroutine);
    }

    public void HideRespawnScreen()
    {
        if (deathPanel && deathPanel.activeSelf) deathPanel.SetActive(false);
        if (_deathCoroutine != null) StopCoroutine(_deathCoroutine);
    }

    private IEnumerator RespawnCountdownRoutine(float duration)
    {
        int remaining = Mathf.CeilToInt(duration);
        while (remaining > 0)
        {
            if (deathCountdownText)
            {
                deathCountdownText.text = $"Öldün!\nYeniden Doğmana: {remaining}";
                deathCountdownText.transform.DOKill(true); // Önceki animasyonu sıfırla
                deathCountdownText.transform.DOPunchScale(Vector3.one * 0.3f, 0.5f, 2, 0.5f); // Büyüyüp küçülme efekti
            }
            yield return new WaitForSeconds(1f);
            remaining--;
        }
        if (deathCountdownText) deathCountdownText.text = "Doğuluyor...";
    }
}