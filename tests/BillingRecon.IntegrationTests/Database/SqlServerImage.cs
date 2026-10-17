using System.Text.RegularExpressions;

namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The SQL Server image the tests run: the one <c>docker-compose.yml</c> names, read from the file, so that the tests
/// and local development can never drift apart (the compose file asks for exactly that).
/// </summary>
internal static partial class SqlServerImage
{
    /// <summary>The image tag of the <c>sql</c> service in the repository's <c>docker-compose.yml</c>.</summary>
    public static string FromDockerCompose()
    {
        var composeFile = Path.Combine(RepositoryRoot(), "docker-compose.yml");
        var match = ImageLine().Match(File.ReadAllText(composeFile));

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"{composeFile} names no mcr.microsoft.com/mssql/server image; the database tests take their image from it.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BillingRecon.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"BillingRecon.sln was not found above {AppContext.BaseDirectory}. The database tests read docker-compose.yml and must run inside the repository.");
    }

    [GeneratedRegex(@"^\s*image:\s*(mcr\.microsoft\.com/mssql/server:[^\s#]+)", RegexOptions.Multiline)]
    private static partial Regex ImageLine();
}
