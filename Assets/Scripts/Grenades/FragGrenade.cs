using Unity.Netcode;
using UnityEngine;

public class FragGrenade : GrenadeBase
{
    [Header("Patlama Ayarları")]
    public float explosionRadius = 7f;
    public int maxDamage = 150;
    public LayerMask playerLayer;
    public LayerMask obstacleLayer = Physics.DefaultRaycastLayers; // Duvarları algılamak için

    [Header("Görsel Efektler")]
    public GameObject decalPrefab;
    public LayerMask decalPlacementLayer = Physics.DefaultRaycastLayers; // Decal'ın yapışabileceği yüzeyler
    public float decalLifetime = 10f;
    public AudioClip explosionSound;
    [Range(0f, 1f)] public float explosionVolume = 1f; // Patlama sesi seviyesi

    [Header("Hata Ayıklama (Debug)")]
    public bool showDebugSphere = true; // Oyun içinde alanı görmek için
    public float debugSphereLifetime = 2f; // Kürenin ekranda kalma süresi

    protected override void OnExplode(Vector3 position)
    {
        Collider[] colliders = Physics.OverlapSphere(position, explosionRadius, playerLayer);

        foreach (var col in colliders)
        {
            PlayerHealth health = col.GetComponentInParent<PlayerHealth>();
            if (health == null || health.isDead.Value) continue;

            Vector3 targetPoint = col.bounds.center;
            Vector3 direction = targetPoint - position;
            float distance = direction.magnitude;

            // Duvar kontrolü: Patlama noktasından oyuncuya bir ışın gönder.
            // Işın oyuncuya ulaşmadan bir engele (duvara) çarparsa, hasar verme.
            RaycastHit[] hits = Physics.RaycastAll(position, direction.normalized, distance, obstacleLayer, QueryTriggerInteraction.Ignore);
            
            // Işının çarptığı objeleri mesafeye göre sırala (en yakından en uzağa)
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            bool isBlocked = false;
            if (hits.Length > 0)
            {
                // Sıralanmış dizideki ilk eleman, patlamaya en yakın olan objedir.
                // Eğer bu obje, hasar alacak oyuncunun kendisi değilse, arada bir duvar var demektir.
                if (hits[0].collider.GetComponentInParent<PlayerHealth>() != health)
                {
                    isBlocked = true;
                }
            }

            if (isBlocked) continue; // Duvar arkasındaysa hasar verme, döngüde bir sonraki oyuncuya geç.

            float damageFactor = 1f - Mathf.Clamp01(distance / explosionRadius); // Uzaklığa göre hasar azaltma
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(maxDamage * damageFactor)); // Hasarı hesapla
            health.TakeDamage(finalDamage);
        }
    }

    protected override void OnExplodeVisual(Vector3 position)
    {
        // Yeni 3D metodumuzu kullanıyoruz (Sesi 70 metreye kadar duyulur)
        Play3DSound(explosionSound, position, 70f, explosionVolume);

        if (decalPrefab == null) return;

        // Patladığı yüzeyi (zemini) bul ve decal'ı yüzey normaline hizala
        // Bombanın havada patlama ihtimaline karşı ışın (raycast) mesafesini 3 metreye çıkarıyoruz
        if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 3.0f, decalPlacementLayer))
        {
            Vector3 spawnPos = hit.point + hit.normal * 0.001f;
            // Tıpkı mermi deliklerinde (WeaponBase) olduğu gibi: Yüzeyin içine doğru yansıtıp (-hit.normal),
            // kendi etrafında rastgele döndürüyoruz (Z ekseninde Roll) ki zemine tam otursun
            Quaternion spawnRot = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            GameObject decal = Instantiate(decalPrefab, spawnPos, spawnRot);
            Destroy(decal, decalLifetime);
        }
        else
        {
            // Zemin bulunamazsa direkt aşağı bakacak şekilde yerleştir ve rastgele döndür
            Quaternion spawnRot = Quaternion.LookRotation(Vector3.down) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            GameObject decal = Instantiate(decalPrefab, position, spawnRot);
            Destroy(decal, decalLifetime);
        }

        // Oyun içinde patlama alanını gösteren Kırmızı Debug Küresi
        if (showDebugSphere)
        {
            GameObject debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            debugSphere.transform.position = position;
            debugSphere.transform.localScale = Vector3.one * (explosionRadius * 2f); // Çap = Yarıçap x 2
            Destroy(debugSphere.GetComponent<Collider>()); // Mermileri veya oyuncuları engellememesi için
            
            Renderer rend = debugSphere.GetComponent<Renderer>();
            if (rend != null) rend.material.color = new Color(1f, 0f, 0f, 0.4f); // Yarı şeffaf kırmızı
            
            Destroy(debugSphere, debugSphereLifetime);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}
