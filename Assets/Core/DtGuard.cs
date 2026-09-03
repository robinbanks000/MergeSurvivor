using System;

namespace MergeSurvivor.Core
{
    /// <summary>
    /// The single statement of the dt contract every Core method taking a dt parameter
    /// must enforce: dt must be a finite number greater than or equal to zero. Extracted
    /// so a fourth independent copy of the finite-vs-non-negative guard mismatch (see
    /// PRO-0002 and PRO-0005) cannot be introduced by an author who does not know the
    /// first three existed.
    /// </summary>
    internal static class DtGuard
    {
        /// <summary>
        /// Throws ArgumentOutOfRangeException when <paramref name="dt"/> is not finite
        /// (NaN or either infinity) or is negative. Does nothing otherwise.
        /// </summary>
        internal static void RequireFiniteNonNegative(float dt, string paramName)
        {
            if (!float.IsFinite(dt) || dt < 0f)
            {
                throw new ArgumentOutOfRangeException(paramName, dt, "dt must be a finite number >= 0.");
            }
        }
    }
}
