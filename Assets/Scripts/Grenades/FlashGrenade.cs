using Unity.Netcode;
using UnityEngine;

public class FlashGrenade : GrenadeBase
{
    [Header("Flash Ayarları")]
    public float flashRadius = 100f;
    public float flashDuration = 5f;
    public LayerMask playerLayer;
    public LayerMask obstacleLayer = Physics.DefaultRaycastLayers; // Duvar arkasını algılamak için

    [Header("Ses Efektleri")]
    public AudioClip flashSound;
    [Range(0f, 1f)] public float flashVolume = 1f; // Patlama sesi seviyesi

    protected override void OnExplode(Vector3 position)
    {
        Collider[] colliders = Physics.OverlapSphere(position, flashRadius, playerLayer);

        foreach (var col in colliders)
        {
            PlayerHealth health = col.GetComponentInParent<PlayerHealth>();
            if (health == null || health.isDead.Value) continue;

            // Yerden seken bombanın ışınının anında zemine çarpmasını önlemek için 1 birim yukarıdan başlatıyoruz
            Vector3 startPos = position + Vector3.up * 0.3f;
            Vector3 targetPos = col.bounds.center; // Doğrudan oyuncunun merkezini hedef al
            Vector3 dir = targetPos - startPos;
            float distance = dir.magnitude;

            bool isBlocked = false;
            
            // RaycastAll ile aradaki bütün objeleri tarıyoruz
            RaycastHit[] hits = Physics.RaycastAll(startPos, dir.normalized, distance, obstacleLayer, QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                // Eğer ışın bombanın kendi fiziksel gövdesine (Collider) çarptıysa bunu bir duvar olarak sayma, görmezden gel
                if (hit.collider.transform.root == this.transform.root) continue;

                PlayerHealth hitHealth = hit.collider.GetComponentInParent<PlayerHealth>();
                // Eğer ışın hedef oyuncuya değil de başka bir şeye (duvara vb.) çarptıysa engellenmiştir
                if (hitHealth != health)
                {
                    isBlocked = true;
                    break; 
                }
            }

            if (isBlocked) continue; // Araya duvar girdiyse kör etme

            // Sadece etkilenen oyuncunun client'ına flash gönder
            ulong clientId = health.OwnerClientId;
            
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // Eğer kör olan kişi bizsek (Host/Test oyuncusu), RPC kuyruğunu atla ve anında çalıştır
                if (GameUIManager.Instance != null) GameUIManager.Instance.ShowFlash(flashDuration);
            }
            else
            {
                // Etkilenen kişi ağdaki başka biriyse ona hedefli RPC gönder
                ApplyFlashToClientRpc(flashDuration, RpcTarget.Single(clientId, RpcTargetUse.Temp));
            }
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ApplyFlashToClientRpc(float duration, RpcParams rpcParams = default)
    {
        if (GameUIManager.Instance != null)
            GameUIManager.Instance.ShowFlash(duration);
    }

    protected override void OnExplodeVisual(Vector3 position)
    {
        // Sesi 50 metreye kadar duyulur
        Play3DSound(flashSound, position, 50f, flashVolume);
    }
}
