namespace Koan.Packaging.Services;

internal static class NativeCandidateGuard
{
    public static void RequireExact(
        string baseCommit,
        string candidateCommit,
        string headCommit,
        bool baseIsAncestor,
        bool worktreeIsClean)
    {
        if (!string.Equals(candidateCommit, headCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Native admission candidate is {candidateCommit}, but checked-out HEAD is {headCommit}. " +
                "Check out the exact merge candidate before evaluating it.");
        }
        if (!baseIsAncestor)
        {
            throw new InvalidOperationException(
                $"Native admission base {baseCommit} is not an ancestor of candidate {candidateCommit}.");
        }
        if (!worktreeIsClean)
        {
            throw new InvalidOperationException(
                $"Native admission candidate {candidateCommit} is not an exact checkout; tracked or untracked " +
                "working-tree content is present.");
        }
    }
}
