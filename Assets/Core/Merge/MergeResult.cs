namespace MergeSurvivor.Core.Merge
{
    /// <summary>Why a merge was refused. <see cref="None"/> means it succeeded.</summary>
    public enum MergeFailure
    {
        None = 0,
        TierMismatch = 1,
        AtMaxTier = 2,
    }

    /// <summary>
    /// Merging returns a result rather than throwing or returning null, because refusing
    /// a merge is ordinary gameplay (the player drags the wrong pair constantly), not an
    /// exceptional condition. The caller is forced to look at <see cref="Success"/>.
    /// </summary>
    public readonly struct MergeResult
    {
        private MergeResult(bool success, Weapon merged, MergeFailure failure)
        {
            Success = success;
            Merged = merged;
            Failure = failure;
        }

        public bool Success { get; }

        /// <summary>Only meaningful when <see cref="Success"/> is true.</summary>
        public Weapon Merged { get; }

        public MergeFailure Failure { get; }

        public static MergeResult Ok(Weapon merged) =>
            new MergeResult(true, merged, MergeFailure.None);

        public static MergeResult Fail(MergeFailure failure) =>
            new MergeResult(false, default, failure);
    }
}
