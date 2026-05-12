using Unity.Netcode;
using UnityEngine;
using DG.Tweening;

public abstract class WeaponBase : NetworkBehaviour
{
    [Header("Temel Silah Ayarları")]
    public string weaponName;
    public int damage;
    public float range;
    public float fireRate; // Saniyede kaç mermi atılabileceği
    public bool isAutomatic; // Otomatik (basılı tutunca sıkan) mi, yoksa tekli mi?

    [Header("Mermi ve Şarjör (Ammo)")]
    public int maxAmmoPerMag;    // Şarjör kapasitesi
    public int currentAmmo;      // Şarjördeki anlık mermi
    public int maxReserveAmmo;   // Maksimum yedek mermi kapasitesi
    public int reserveAmmo;      // Yedekteki toplam mermi
    public float reloadTime;     // Yeniden yükleme süresi
    public bool isReloading;     // Şu an reload yapıyor mu?

    [Header("Geri Tepme (Recoil)")]
    public float verticalRecoil = 2f;
    public float horizontalRecoil = 0.5f;

    [Header("Görsel Efektler")]
    public Transform barrelPoint;
    public SpriteRenderer muzzleFlashRenderer;
    public Sprite[] muzzleFlashSprites;
    public TrailRenderer bulletTrailPrefab;
    public GameObject[] bulletHolePrefabs;

    [Header("Ses Efektleri")]
    public AudioSource weaponAudioSource; // Sesi çalacak kaynak
    public AudioClip shootSound; // Patlama sesi
    [Range(0f, 1f)] public float shootVolume = 1f; // Sesin şiddeti (0: Sessiz, 1: Maksimum)

    protected float nextTimeToFire = 0f;

    private Coroutine _reloadCoroutine;
    private Vector3 _initialLocalRot;

    private void Start()
    {
        _initialLocalRot = transform.localEulerAngles;

        // Silah sesinin otomatik olarak 3D ayarlanması
        if (weaponAudioSource != null)
        {
            if (IsOwner)
            {
                // Kendi silahımızsa 2D (0) yapıyoruz ki sesi her iki kulaktan eşit ve tok duyalım
                weaponAudioSource.spatialBlend = 0f;
            }
            else
            {
                // Başkasının silahıysa tamamen 3D (1) yapıyoruz ki yönünü ve mesafesini anlayalım
                weaponAudioSource.spatialBlend = 1f; 
                weaponAudioSource.rolloffMode = AudioRolloffMode.Linear; 
                weaponAudioSource.minDistance = 3f; 
                weaponAudioSource.maxDistance = 60f; 
            }
        }
    }

    public void Refill()
    {
        CancelReload();
        currentAmmo = maxAmmoPerMag;
        reserveAmmo = maxReserveAmmo;
    }

    private void OnDisable()
    {
        // Silah gizlenirse (değiştirilirse) reload'u güvenli şekilde iptal et
        CancelReload();
    }

    // Oyuncu inputlarına göre ateş etme denemesi yapar
    public virtual void HandleShooting(bool wasPressed, bool isPressed, Transform cameraTransform, FPSController shooter)
    {
        if (isReloading) return; // Reload yapıyorsa ateş edemez

        bool wantToShoot = isAutomatic ? isPressed : wasPressed;

        if (wantToShoot && Time.time >= nextTimeToFire)
        {
            if (currentAmmo > 0)
            {
                nextTimeToFire = Time.time + (1f / fireRate);
                PerformShoot(cameraTransform, shooter);
            }
            else
            {
                // Şarjör boşsa tıklayınca otomatik reload yap
                StartReload();
            }
        }
    }

    public void StartReload()
    {
        // Zaten reload yapıyorsa, şarjör tam doluysa veya yedekte mermi yoksa işlemi yapma
        if (isReloading || currentAmmo >= maxAmmoPerMag || reserveAmmo <= 0) return;
        
        if (_reloadCoroutine != null) StopCoroutine(_reloadCoroutine);
        _reloadCoroutine = StartCoroutine(ReloadRoutine());
    }

    private System.Collections.IEnumerator ReloadRoutine()
    {
        isReloading = true;

        // Sadece lokalde çalışan basit DOTween animasyonu (Silah namlusunu yukarı 60 derece kaldırıp indirir)
        Vector3 reloadRot = _initialLocalRot + new Vector3(-60f, 0, 0);
        transform.DOLocalRotate(reloadRot, reloadTime / 2f).SetEase(Ease.InOutQuad)
            .OnComplete(() => transform.DOLocalRotate(_initialLocalRot, reloadTime / 2f).SetEase(Ease.InOutQuad));

        yield return new WaitForSeconds(reloadTime);

        // Mermi hesaplaması (Taktiksel Reload: Sadece eksik olanı tamamla)
        int bulletsNeeded = maxAmmoPerMag - currentAmmo;
        int bulletsToLoad = Mathf.Min(bulletsNeeded, reserveAmmo);

        currentAmmo += bulletsToLoad;
        reserveAmmo -= bulletsToLoad;
        
        isReloading = false;
        UpdateAmmoUI();
    }

    public void CancelReload()
    {
        if (isReloading)
        {
            if (_reloadCoroutine != null) StopCoroutine(_reloadCoroutine);
            isReloading = false;
            transform.DOKill();
            transform.localEulerAngles = _initialLocalRot; // Silah açısını sıfırla
        }
    }

    private void ShowMuzzleFlash()
    {
        if (muzzleFlashRenderer == null || muzzleFlashSprites == null || muzzleFlashSprites.Length == 0) return;

        muzzleFlashRenderer.sprite = muzzleFlashSprites[Random.Range(0, muzzleFlashSprites.Length)];
        muzzleFlashRenderer.gameObject.SetActive(true);

        Camera cam = Camera.main;
        if (cam != null)
            muzzleFlashRenderer.transform.forward = cam.transform.forward;

        StartCoroutine(HideMuzzleFlash());
    }

    private System.Collections.IEnumerator HideMuzzleFlash()
    {
        yield return new WaitForSeconds(0.05f);
        if (muzzleFlashRenderer != null)
            muzzleFlashRenderer.gameObject.SetActive(false);
    }

    public void UpdateAmmoUI()
    {
        if (IsOwner && GameUIManager.Instance != null)
        {
            GameUIManager.Instance.UpdateAmmoUI(currentAmmo, reserveAmmo);
        }
    }

    // Gerçek mermi atış mantığı (Raycast)
    protected virtual void PerformShoot(Transform cameraTransform, FPSController shooter)
    {
        currentAmmo--;
        UpdateAmmoUI();

        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        
        Vector3 hitPoint = ray.GetPoint(range); // Default olarak menzil sonunu hedefle (havaya sıkılırsa)
        Vector3 hitNormal = -ray.direction;
        bool spawnDecal = false;
        
        // Bütün çarpan objeleri al (Kendi karakterimize çarpıp merminin durmasını engellemek için)
        RaycastHit[] hits = Physics.RaycastAll(ray, range, shooter.shootLayer);
        
        // Çarpan objeleri mesafeye göre yakından uzağa sırala
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            PlayerHealth targetHealth = hit.collider.GetComponentInParent<PlayerHealth>();

            // Eğer mermi KENDİMİZE çarptıysa, görmezden gel ve arkaya gitmeye devam et
            if (targetHealth != null && targetHealth.OwnerClientId == shooter.OwnerClientId)
                continue; 

            // Kendimiz haricinde İLK geçerli objeye çarptık (Duvar veya Düşman)
            hitPoint = hit.point;
            hitNormal = hit.normal;
            spawnDecal = true;

            if (targetHealth != null)
            {
                spawnDecal = false; // Vurduğumuz şey oyuncuysa duvar deliği çıkartma

                // Kendi takım arkadaşımızı vurmayı engelliyoruz
                if (targetHealth.GetTeam() != shooter.GetMyTeam())
                {
                    var targetNetObj = targetHealth.GetComponent<NetworkObject>();
                    if (targetNetObj != null) shooter.HitPlayerRpc(targetNetObj.NetworkObjectId, damage);
                }
            }
            break; // İlk hedefe (duvar veya düşman) çarptıktan sonra mermiyi durdur
        }

        // Editor üzerinde merminin nereye gittiğini görmek için Debug çizgisi (2 saniye ekranda kalır)
        Debug.DrawLine(ray.origin, hitPoint, Color.red, 2f);

        // Görsel efektleri (Muzzle Flash ve Trail) ağdaki herkese göster
        PlayShootVisualsRpc(hitPoint, hitNormal, spawnDecal);

        // Kamerayı sars (Recoil)
        shooter.AddRecoil(verticalRecoil, horizontalRecoil);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    //[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Everyone)]
    private void PlayShootVisualsRpc(Vector3 endPoint, Vector3 hitNormal, bool spawnDecal, RpcParams rpcParams = default)
    {
        if (weaponAudioSource != null && shootSound != null) weaponAudioSource.PlayOneShot(shootSound, shootVolume);

        // Muzzle flash sadece silahın sahibinde (Owner) görünsün
        if (IsOwner)
        {
            ShowMuzzleFlash();
        }

        if (bulletTrailPrefab != null && barrelPoint != null)
        {
            TrailRenderer trail = Instantiate(bulletTrailPrefab, barrelPoint.position, Quaternion.identity);
            trail.transform.DOMove(endPoint, 0.05f).SetEase(Ease.Linear).OnComplete(() => {
                Destroy(trail.gameObject, trail.time);
            });
        }

        // Mermi deliği (Decal) oluşturma
        if (spawnDecal && bulletHolePrefabs != null && bulletHolePrefabs.Length > 0)
        {
            GameObject selectedDecal = bulletHolePrefabs[Random.Range(0, bulletHolePrefabs.Length)];
            if (selectedDecal == null) return;

            // Titremeyi (Z-Fighting) önlemek için yüzeyden çok az (0.001f) öne alıyoruz
            Vector3 spawnPos = endPoint + hitNormal * 0.001f;
            
            // URP Decal Projector'ın yüzeyin içine doğru yansıtması için ters normal (-hitNormal) kullanıyoruz
            // Ayrıca deliklerin hep aynı açıda durmaması için Z ekseninde rastgele döndürüyoruz (Daha gerçekçi görünüm)
            Quaternion spawnRot = Quaternion.LookRotation(-hitNormal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            
            GameObject decal = Instantiate(selectedDecal, spawnPos, spawnRot);
        }
    }
}