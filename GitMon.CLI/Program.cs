using Microsoft.Extensions.Configuration;
using Octokit;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // 1) Config & client setup (kept minimal)
            var (client, org) = BuildGitHubClient();

            // 2) Demo inputs (feel free to promote these to args/config later)
            //var testRepo = "RootApp.Shared";
            //var start = DateTimeOffset.UtcNow.AddDays(-30);

            // 3) Run demos (each block is an isolated, re-runnable step)
            // await DemoListOrgReposAsync(client, org, take: 10);

            // 4
            // await DemoRepoMetricsAsync(client, org, testRepo, start);


            //var prs = await DemoMergedPrsAsync(client, org, testRepo, start, take: 10);
            // await DemoReviewsAsync(client, org, testRepo, prs, take: 5);
            // await DemoClassificationAsync(client, org, testRepo, prs, take: 5);

            Console.WriteLine("Running weekly org report…");
            await RunWeeklyOrgReportAsync(client, org);
            Console.WriteLine("Done.");

            return 0;
        }
        catch (AuthorizationException)
        {
            Console.Error.WriteLine("Auth failed: check PAT scopes and that it has access to the org/repos.");
            return 2;
        }
        catch (RateLimitExceededException ex)
        {
            Console.Error.WriteLine($"Rate limit exceeded. Resets at: {ex.Reset.ToLocalTime()}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.GetType().Name} - {ex.Message}");
            return 4;
        }
    }

    private static async Task RunWeeklyOrgReportAsync(GitHubClient client, string org)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-7);
        var repoNames = await GetAllRepoNamesAsync(client, org);

        var rows = new List<(string Repo, int Merged, int Approved, int Changes, int Commented, int None)>();

        Console.WriteLine($"Generating 7-day report for org '{org}' across {repoNames.Count} repos…");

        foreach (var repo in repoNames.OrderBy(n => n))
        {
            var m = await ComputeRepoMetricsAsync(client, org, repo, start);

            rows.Add((repo, m.MergedCount, m.Approved, m.ChangesRequested, m.CommentedOnly, m.NoReview));

            // Brief console line per repo
            var reviewedAny = m.ReviewedAny;
            var pct = m.Pct(reviewedAny);

            if (m.MergedCount > 0)
            {
                Console.WriteLine($"{repo,-40} merged:{m.MergedCount,4}  reviewed-any:{reviewedAny,4}  ({pct,5:F1}%)  no-review:{m.NoReview,3}");
            }

            await Task.Delay(5000); // delay to reduce chance of rate-limits
        }

        // Write CSV
        var path = Path.Combine(Directory.GetCurrentDirectory(), "by_repo_7d.csv");
        using (var sw = new StreamWriter(path))
        {
            await sw.WriteLineAsync("repo,merged_count,reviewed_any,reviewed_any_pct,approved,changes_requested,commented_only,no_review");
            foreach (var r in rows)
            {
                var merged = r.Merged;
                var reviewedAny = r.Approved + r.Changes + r.Commented;
                var pct = merged == 0 ? 0.0 : (double)reviewedAny / merged * 100.0;

                await sw.WriteLineAsync(
                    $"{Escape(r.Repo)},{merged},{reviewedAny},{pct:F1},{r.Approved},{r.Changes},{r.Commented},{r.None}");
            }
        }

        Console.WriteLine($"Wrote CSV: {path}");
    }

    private static async Task<T> TryWithRateLimitAsync<T>(Func<Task<T>> action, string opLabel)
    {
        while (true)
        {
            try
            {
                return await action();
            }
            catch (RateLimitExceededException ex)
            {
                var resetLocal = ex.Reset.ToLocalTime();
                var wait = resetLocal - DateTimeOffset.Now + TimeSpan.FromSeconds(2); // small buffer
                if (wait < TimeSpan.Zero) wait = TimeSpan.FromSeconds(5);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[rate-limit] {opLabel} hit limit. Waiting {wait.TotalSeconds:F0}s until {resetLocal:t}...");
                Console.ResetColor();

                await Task.Delay(wait);
                // loop and retry
            }
        }
    }


    private static string Escape(string s)
    {
        // basic CSV escaping
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    private static async Task<IReadOnlyList<string>> GetAllRepoNamesAsync(GitHubClient client, string org)
    {
        var names = new List<string>();
        var page = 1;
        const int perPage = 100;

        while (true)
        {
            var repos = await client.Repository.GetAllForOrg(org, new ApiOptions
            {
                PageCount = 1,
                PageSize = perPage,
                StartPage = page
            });

            if (repos.Count == 0) break;

            // Skip archived and forks
            names.AddRange(repos
                .Where(r => !r.Archived && !r.Fork)
                .Select(r => r.Name));

            page++;
        }

        return names;
    }

    private static (GitHubClient client, string org) BuildGitHubClient()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<Program>()
            .Build();

        var token = config["GitHub:Token"];
        var org = config["GitHub:Org"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(org))
            throw new InvalidOperationException("GitHub:Token or GitHub:Org missing. Use `dotnet user-secrets set`.");

        var client = new GitHubClient(new ProductHeaderValue("GitMon"))
        {
            Credentials = new Credentials(token)
        };

        return (client, org);
    }

    // Demo runners 
    private static async Task DemoRepoMetricsAsync(
    GitHubClient client,
    string org,
    string repo,
    DateTimeOffset start)
    {
        var metrics = await ComputeRepoMetricsAsync(client, org, repo, start);

        Console.WriteLine($"Repo: {metrics.Repo} (last 30d)");
        Console.WriteLine($"Merged: {metrics.MergedCount}");
        Console.WriteLine($"Reviewed Any: {metrics.ReviewedAny} ({metrics.Pct(metrics.ReviewedAny):F1}%)");
        Console.WriteLine($"  Approved:          {metrics.Approved} ({metrics.Pct(metrics.Approved):F1}%)");
        Console.WriteLine($"  Changes Requested: {metrics.ChangesRequested} ({metrics.Pct(metrics.ChangesRequested):F1}%)");
        Console.WriteLine($"  Commented Only:    {metrics.CommentedOnly} ({metrics.Pct(metrics.CommentedOnly):F1}%)");
        Console.WriteLine($"No Review:           {metrics.NoReview} ({metrics.Pct(metrics.NoReview):F1}%)");
    }


    private static async Task DemoListOrgReposAsync(GitHubClient client, string org, int take = 10)
    {
        var page = 1;
        var perPage = 50;
        var all = new List<Repository>();

        while (true)
        {
            var repos = await client.Repository.GetAllForOrg(org, new ApiOptions
            {
                PageCount = 1,
                PageSize = perPage,
                StartPage = page
            });

            if (repos.Count == 0) break;
            all.AddRange(repos);
            page++;
        }

        Console.WriteLine($"Org: {org} — Repositories found: {all.Count}");
        foreach (var r in all.OrderByDescending(r => r.UpdatedAt).Take(take))
            Console.WriteLine($"- {r.Name} (private: {r.Private}, updated: {r.UpdatedAt:O})");
    }

    private static async Task<List<PullRequest>> DemoMergedPrsAsync(
        GitHubClient client, string org, string repo, DateTimeOffset start, int take = 10)
    {
        var prs = (await FetchMergedPrsAsync(client, org, repo, start)).ToList();

        Console.WriteLine($"{repo} — merged PRs in last 30d: {prs.Count}");
        foreach (var pr in prs.OrderByDescending(p => p.MergedAt).Take(take))
            Console.WriteLine($"#{pr.Number} by {pr.User.Login} — merged {pr.MergedAt:O} — {pr.Title}");

        return prs;
    }

    private static async Task DemoReviewsAsync(
        GitHubClient client, string org, string repo, List<PullRequest> prs, int take = 5)
    {
        foreach (var pr in prs.OrderByDescending(p => p.MergedAt).Take(take))
        {
            var reviews = await FetchReviewsAsync(client, org, repo, pr.Number, pr.MergedAt!.Value);
            Console.WriteLine($"PR #{pr.Number} — {reviews.Count} review(s):");
            foreach (var r in reviews)
                Console.WriteLine($"  {r.User.Login}: {r.State} at {r.SubmittedAt:O}");
        }
    }

    private static async Task DemoClassificationAsync(
        GitHubClient client, string org, string repo, List<PullRequest> prs, int take = 5)
    {
        foreach (var pr in prs.OrderByDescending(p => p.MergedAt).Take(take))
        {
            var reviews = await FetchReviewsAsync(client, org, repo, pr.Number, pr.MergedAt!.Value);
            var status = ClassifyReviewStatus(pr, reviews);
            Console.WriteLine($"PR #{pr.Number} — {status} — {reviews.Count} review(s)");
        }
    }

    static async Task<IReadOnlyList<PullRequest>> FetchMergedPrsAsync(
    GitHubClient client,
    string org,
    string repo,
    DateTimeOffset startInclusive,
    DateTimeOffset? endInclusive = null,
    int pageSize = 50)
    {
        // GitHub Search qualifier expects dates in yyyy-MM-dd (UTC)
        string startStr = startInclusive.UtcDateTime.ToString("yyyy-MM-dd");
        string endStr = (endInclusive ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd");

        // Search: repo:ORG/REPO is:pr is:merged merged:START..END
        // (Search returns Issues; we then hydrate each PR to get full details like MergedAt)
        var query = $"repo:{org}/{repo} is:pr is:merged merged:{startStr}..{endStr}";
        var results = new List<PullRequest>();

        int page = 1;
        while (true)
        {
            var searchReq = new SearchIssuesRequest(query)
            {
                PerPage = pageSize,
                Page = page
            };

            var searchRes = await client.Search.SearchIssues(searchReq);

            if (searchRes.Items.Count == 0)
                break;

            // Hydrate each PR
            foreach (var item in searchRes.Items)
            {
                // item.Number is the PR number
                var pr = await client.PullRequest.Get(org, repo, item.Number);
                // Guard: ensure mergedAt inside our window (search should ensure this, but be strict)
                if (pr.Merged == true && pr.MergedAt.HasValue &&
                    pr.MergedAt.Value >= startInclusive &&
                    pr.MergedAt.Value <= (endInclusive ?? DateTimeOffset.UtcNow))
                {
                    results.Add(pr);
                }
            }

            // Stop if we've retrieved all results
            int fetchedSoFar = page * pageSize;
            if (fetchedSoFar >= searchRes.TotalCount) break;
            page++;
        }

        return results;
    }

    static async Task<IReadOnlyList<PullRequestReview>> FetchReviewsAsync(
    GitHubClient client,
    string org,
    string repo,
    int prNumber,
    DateTimeOffset mergedAt,
    int pageSize = 50)
    {
        var results = new List<PullRequestReview>();
        int page = 1;

        while (true)
        {
            var reviews = await client.PullRequest.Review.GetAll(org, repo, prNumber, new ApiOptions
            {
                PageSize = pageSize,
                PageCount = 1,
                StartPage = page
            });

            if (reviews.Count == 0)
                break;

            results.AddRange(reviews.Where(r => r.SubmittedAt < mergedAt));

            if (reviews.Count < pageSize)
                break;
            page++;
        }

        return results;
    }

    static string ClassifyReviewStatus(
    PullRequest pr,
    IReadOnlyList<PullRequestReview> reviews)
    {
        if (pr.Draft)
            return "DRAFT_EXCLUDED";

        // Exclude self-reviews
        var nonSelfReviews = reviews
            .Where(r => !string.Equals(r.User.Login, pr.User.Login, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (nonSelfReviews.Count == 0)
            return "NO_REVIEW";

        // Use the enum on r.State.Value
        if (nonSelfReviews.Any(r => r.State.Value == PullRequestReviewState.Approved))
            return "APPROVED";

        if (nonSelfReviews.Any(r => r.State.Value == PullRequestReviewState.ChangesRequested))
            return "CHANGES_REQUESTED";

        if (nonSelfReviews.Any(r => r.State.Value == PullRequestReviewState.Commented))
            return "COMMENTED";

        return "NO_REVIEW";
    }

    static async Task<RepoMetrics> ComputeRepoMetricsAsync(
    GitHubClient client, string org, string repo, DateTimeOffset start, DateTimeOffset? end = null)
    {
        var prs = await FetchMergedPrsAsync(client, org, repo, start, end);

        // Exclude drafts per your policy
        prs = prs.Where(pr => !pr.Draft).ToList();

        int approved = 0, changes = 0, commented = 0, none = 0;

        foreach (var pr in prs)
        {
            if (!pr.Merged || !pr.MergedAt.HasValue) continue;

            var reviews = await FetchReviewsAsync(client, org, repo, pr.Number, pr.MergedAt.Value);
            var status = ClassifyReviewStatus(pr, reviews);

            switch (status)
            {
                case "APPROVED": approved++; break;
                case "CHANGES_REQUESTED": changes++; break;
                case "COMMENTED": commented++; break;
                case "NO_REVIEW": none++; break;
                    // DRAFT_EXCLUDED won’t appear because we filtered drafts above
            }
        }

        return new RepoMetrics(
            Repo: repo,
            MergedCount: approved + changes + commented + none,
            Approved: approved,
            ChangesRequested: changes,
            CommentedOnly: commented,
            NoReview: none
        );
    }



}
