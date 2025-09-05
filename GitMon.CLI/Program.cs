using Microsoft.Extensions.Configuration;
using Octokit;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // 1) Load config (user-secrets + optional appsettings.json)
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

        // 2) Create GitHub client
        var productHeader = new ProductHeaderValue("GitMon");
        var client = new GitHubClient(productHeader)
        {
            Credentials = new Credentials(token)
        };

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
}
