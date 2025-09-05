using Microsoft.Extensions.Configuration;
using Octokit;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<Program>()
            .Build();

        var token = config["GitHub:Token"];
        var org = config["GitHub:Org"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(org))
        {
            Console.Error.WriteLine("Error: GitHub:Token or GitHub:Org missing. Set with `dotnet user-secrets set`.");
            return 1;
        }

        var productHeader = new ProductHeaderValue("GitMon");
        var client = new GitHubClient(productHeader)
        {
            Credentials = new Credentials(token)
        };

        var testRepo = "RootApp.Shared"; // e.g., "RootApp.Shared"
        var start = DateTimeOffset.UtcNow.AddDays(-30);
        var prs = await FetchMergedPrsAsync(client, org, testRepo, start);

        Console.WriteLine($"{testRepo} — merged PRs in last 30d: {prs.Count}");
        foreach (var pr in prs.OrderByDescending(p => p.MergedAt).Take(10))
        {
            Console.WriteLine($"#{pr.Number} by {pr.User.Login} — merged {pr.MergedAt:O} — {pr.Title}");
        }

        foreach (var pr in prs.OrderByDescending(p => p.MergedAt).Take(5))
        {
            var reviews = await FetchReviewsAsync(client, org, testRepo, pr.Number, pr.MergedAt.Value);

            Console.WriteLine($"PR #{pr.Number} — {reviews.Count} review(s):");
            foreach (var r in reviews)
            {
                Console.WriteLine($"  {r.User.Login}: {r.State} at {r.SubmittedAt:O}");
            }
        }

        foreach (var pr in prs.OrderByDescending(p => p.MergedAt).Take(5))
        {
            var reviews = await FetchReviewsAsync(client, org, testRepo, pr.Number, pr.MergedAt.Value);
            var status = ClassifyReviewStatus(pr, reviews);

            Console.WriteLine($"PR #{pr.Number} — {status} — {reviews.Count} review(s)");
        }

        try
        {
            // 3) Fetch org repos (simple pagination)
            var request = new RepositoryRequest { Type = RepositoryType.All, Sort = RepositorySort.Updated };
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

            // 4) Print a quick summary
            Console.WriteLine($"Org: {org} — Repositories found: {all.Count}");
            foreach (var r in all.OrderByDescending(r => r.UpdatedAt).Take(10))
            {
                Console.WriteLine($"- {r.Name} (private: {r.Private}, updated: {r.UpdatedAt:O})");
            }

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


}
