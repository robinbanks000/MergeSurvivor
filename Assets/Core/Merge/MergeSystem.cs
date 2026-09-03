namespace MergeSurvivor.Core.Merge
{
    /// <summary>
    /// The game's namesake rule, kept deliberately tiny and pure: two weapons of the same
    /// tier become one of the next tier. Everything about presentation — animation, which
    /// slot the result lands in, what it looks like — belongs to the Unity shell.
    /// </summary>
    public static class MergeSystem
    {
        public static MergeResult Merge(Weapon a, Weapon b)
        {
            if (a.Tier != b.Tier)
            {
                return MergeResult.Fail(MergeFailure.TierMismatch);
            }

            if (a.IsMaxTier)
            {
                return MergeResult.Fail(MergeFailure.AtMaxTier);
            }

            return MergeResult.Ok(new Weapon(a.Tier + 1));
        }

        /// <summary>
        /// True when the pair can merge. Lets the UI grey out an illegal drop target
        /// without performing the merge or duplicating the rule.
        /// </summary>
        public static bool CanMerge(Weapon a, Weapon b) => Merge(a, b).Success;
    }
}
