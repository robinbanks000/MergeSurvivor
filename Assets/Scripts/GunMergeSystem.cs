using UnityEngine;

public class GunMergeSystem : MonoBehaviour
{
    public int gunLevel = 1;

    public void UpgradeGun()
    {
        gunLevel++;
        Debug.Log("Gun upgraded to level " + gunLevel);
        // Her kunne vi ændre bullet damage, fire rate, osv.
    }
}
