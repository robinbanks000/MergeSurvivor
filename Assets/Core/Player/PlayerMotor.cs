using System;

namespace MergeSurvivor.Core.Player
{
    /// <summary>
    /// Horizontal movement and fire cadence, with no knowledge of Transform, Input or
    /// prefabs. The Unity shell reads the axis and applies the resulting position; this
    /// class owns the rules about how fast the player moves and how often it may fire.
    /// </summary>
    public sealed class PlayerMotor
    {
        private readonly float _moveSpeed;
        private readonly float _minX;
        private readonly float _maxX;

        private float _fireCooldown;

        public PlayerMotor(float moveSpeed, float minX, float maxX, float startX = 0f)
        {
            if (moveSpeed < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(moveSpeed), moveSpeed, "Move speed must be >= 0.");
            }

            if (maxX <= minX)
            {
                throw new ArgumentException($"maxX ({maxX}) must be > minX ({minX}).", nameof(maxX));
            }

            _moveSpeed = moveSpeed;
            _minX = minX;
            _maxX = maxX;
            PositionX = Clamp(startX);
        }

        public float PositionX { get; private set; }

        /// <summary>Seconds until the next shot is allowed. Zero means ready.</summary>
        public float FireCooldown => _fireCooldown;

        /// <param name="moveAxis">-1 to 1. Values outside that range are clamped.</param>
        public void Tick(float dt, float moveAxis)
        {
            if (dt < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt must be >= 0.");
            }

            if (moveAxis > 1f)
            {
                moveAxis = 1f;
            }
            else if (moveAxis < -1f)
            {
                moveAxis = -1f;
            }

            PositionX = Clamp(PositionX + (moveAxis * _moveSpeed * dt));

            _fireCooldown -= dt;
            if (_fireCooldown < 0f)
            {
                _fireCooldown = 0f;
            }
        }

        /// <summary>
        /// Consumes the shot if the weapon is off cooldown.
        /// </summary>
        /// <param name="shotsPerSecond">Usually WeaponStats.FireRateFor(currentWeapon).</param>
        /// <returns>True when a shot actually happens.</returns>
        public bool TryFire(float shotsPerSecond)
        {
            if (shotsPerSecond <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shotsPerSecond), shotsPerSecond, "Fire rate must be > 0.");
            }

            if (_fireCooldown > 0f)
            {
                return false;
            }

            _fireCooldown = 1f / shotsPerSecond;
            return true;
        }

        private float Clamp(float x)
        {
            if (x < _minX)
            {
                return _minX;
            }

            return x > _maxX ? _maxX : x;
        }
    }
}
