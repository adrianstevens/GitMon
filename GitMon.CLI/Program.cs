using Microsoft.Extensions.Configuration;

class Program
{
    static void Main(string[] args)
    {
        // Build configuration from user-secrets + appsettings.json (optional)
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<Program>() // This loads the user-secrets
            .Build();

        // Read settings
        var gitHubToken = config["GitHub:Token"];
        var gitHubOrg = config["GitHub:Org"];

        // Quick validation
        if (string.IsNullOrEmpty(gitHubToken) || string.IsNullOrEmpty(gitHubOrg))
        {
            Console.WriteLine("Error: GitHub token or org is not set. Use `dotnet user-secrets set` first.");
            return;
        }

        // Mask the token for display (never log the real token)
        var maskedToken = gitHubToken.Length > 4
            ? new string('*', gitHubToken.Length - 4) + gitHubToken[^4..]
            : "****";

        Console.WriteLine($"GitHub Org: {gitHubOrg}");
        Console.WriteLine($"GitHub Token: {maskedToken}");
    }
}
