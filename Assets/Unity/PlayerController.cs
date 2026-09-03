using MergeSurvivor.Core.Combat;
using MergeSurvivor.Core.Player;
using UnityEngine;

namespace MergeSurvivor.Unity
{
    /// <summary>
    /// Adapter over <see cref="PlayerMotor"/>. Reads input and writes the Transform;
    /// every rule about how far the player may move and how often it may fire belongs
    /// to Core, so those rules are covered by tests that run without the editor.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float minX = -8f;
        [SerializeField] private float maxX = 8f;

        [Header("Shooting")]
        [SerializeField] private SimplePool bulletPool;
        [SerializeField] private Transform firePoint;
        [SerializeField] private GunMergeSystem guns;

        private PlayerMotor _motor;

        private void Awake()
        {
            _motor = new PlayerMotor(moveSpeed, minX, maxX, transform.position.x);
        }

        private void Update()
        {
            _motor.Tick(Time.deltaTime, Input.GetAxisRaw("Horizontal"));

            Vector3 position = transform.position;
            position.x = _motor.PositionX;
            transform.position = position;

            // Held rather than pressed: the cooldown in PlayerMotor now governs the
            // cadence, so tapping the key can no longer outpace the weapon's fire rate.
            if (Input.GetKey(KeyCode.Space) && _motor.TryFire(CurrentFireRate()))
            {
                Shoot();
            }
        }

        private float CurrentFireRate() =>
            guns != null ? WeaponStats.FireRateFor(guns.CurrentWeapon) : WeaponStats.BaseFireRate;

        private void Shoot()
        {
            if (bulletPool == null || firePoint == null)
            {
                return;
            }

            bulletPool.Get(firePoint.position);
        }
    }
}
