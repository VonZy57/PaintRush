using UnityEngine;
using DG.Tweening;

public class SmokeGrenade : GrenadeBase
{
    [Header("Duman Ayarları")]
    public float smokeDuration = 8f;
    public float expandDuration = 1f; // Dumanın büyüme süresi

    [Header("Görsel Efektler")]
    public GameObject smokeVolumePrefab;
    public AudioClip deploySound;
    [Range(0f, 1f)] public float deployVolume = 1f; // Fıslama/Açılma sesi seviyesi

    protected override void OnExplode(Vector3 position)
    {
        // Duman yalnızca görsel — server'da uygulanacak oyun etkisi yok
    }

    protected override void OnExplodeVisual(Vector3 position)
    {
        if (smokeVolumePrefab != null)
        {
            GameObject smokeObj = Instantiate(smokeVolumePrefab, position, Quaternion.identity);
            smokeObj.transform.localScale = Vector3.one * 0.01f;

            // DOTween ile hızlıca 10 katına büyüt
            smokeObj.transform.DOScale(12f, expandDuration).SetEase(Ease.OutCubic);

            // Süre bitince yavaşça küçült ve dünyadan sil
            smokeObj.transform.DOScale(0.01f, expandDuration).SetDelay(smokeDuration).SetEase(Ease.InCubic)
                .OnComplete(() => Destroy(smokeObj));
        }

        // Sesi 40 metreye kadar duyulur
        Play3DSound(deploySound, position, 40f, deployVolume);
    }
}
