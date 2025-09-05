record RepoMetrics(
    string Repo,
    int MergedCount,
    int Approved,
    int ChangesRequested,
    int CommentedOnly,
    int NoReview)
{
    public int ReviewedAny => Approved + ChangesRequested + CommentedOnly;
    public double Pct(int x) => MergedCount == 0 ? 0 : (double)x / MergedCount * 100.0;
}