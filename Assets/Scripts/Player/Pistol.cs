using UnityEngine;

public class Pistol : WeaponBase
{
    private void Awake()
    {
        weaponName = "Tabanca";
        damage = 30;
        range = 50f;
        fireRate = 5f; // Saniyede 5 mermi (oyuncu hızlı tıklarsa)
        isAutomatic = false; // Sadece her tıklamada 1 kez sıkar
        verticalRecoil = 1.5f;
        horizontalRecoil = 0.3f;

        // Mermi sistemi
        maxAmmoPerMag = 12;
        currentAmmo = 12;
        maxReserveAmmo = 24;
        reserveAmmo = 24; // 2 Yedek Şarjör
        reloadTime = 1.5f;
    }

    // İleride buraya tabancaya özel hızlı çekme veya koşarken sekme özellikleri eklenebilir.
}