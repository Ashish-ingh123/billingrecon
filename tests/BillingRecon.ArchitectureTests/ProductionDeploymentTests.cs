using System.Text.RegularExpressions;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// The files of the production deployment hold no secret and run a named release (ADR 0055): a secret is a file of the
/// host, an image is pulled by its tag, and nothing is built on the server.
/// </summary>
public sealed class ProductionDeploymentTests
{
    private static readonly string Compose = File.ReadAllText(RepositoryFiles.PathOf("docker-compose.prod.yml"));

    private static readonly string[] SecretSettings =
        ["MSSQL_SA_PASSWORD:", "Jwt__SigningKey:", "TwoStep__EncryptionKey:", "Mail__Password:", "MAIL_PASSWORD", "Database__ConnectionString:", "MIGRATOR_PASSWORD:", "APP_PASSWORD:", "Password="];

    [Fact]
    public void The_production_compose_file_sets_no_secret_in_the_environment_of_a_service()
    {
        foreach (var setting in SecretSettings)
        {
            Compose.ShouldNotContain(setting, Case.Sensitive);
        }
    }

    [Fact]
    public void Every_secret_of_the_production_compose_file_is_a_file_under_the_hosts_secret_directory()
    {
        var files = Regex.Matches(Compose, @"^\s+file:\s*""(?<path>[^""]+)""", RegexOptions.Multiline)
            .Select(match => match.Groups["path"].Value)
            .ToList();

        files.Count.ShouldBe(9);
        files.ShouldAllBe(path => path.StartsWith("${SECRETS_DIR:-/etc/billingrecon/secrets}/", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_image_of_the_production_compose_file_has_a_tag_that_is_not_latest_and_none_is_built()
    {
        var images = Regex.Matches(Compose, @"^\s+image:\s*""?(?<image>[^""\s]+)""?", RegexOptions.Multiline)
            .Select(match => match.Groups["image"].Value)
            .ToList();

        images.Count.ShouldBe(9);
        foreach (var image in images)
        {
            var tag = image[(image.LastIndexOf(':') + 1)..];
            image.ShouldContain(":");
            tag.ShouldNotBe("latest");
            tag.ShouldNotBeNullOrWhiteSpace();
        }

        Compose.ShouldNotContain("build:");
    }

    [Fact]
    public void The_applications_own_images_run_the_tag_of_the_release()
    {
        foreach (var name in new[] { "billingrecon-api", "billingrecon-migrate", "billingrecon-frontend" })
        {
            Compose.ShouldContain($"/{name}:${{IMAGE_TAG");
        }
    }

    [Fact]
    public void The_example_of_the_environment_file_names_the_secret_files_and_holds_no_value_for_one()
    {
        var example = File.ReadAllLines(RepositoryFiles.PathOf("deploy/prod/env.example"));

        example.Where(line => !line.StartsWith('#') && line.Contains("PASSWORD=", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
        example.Where(line => !line.StartsWith('#') && line.Contains("KEY=", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
        string.Join('\n', example).ShouldContain("jwt_signing_key");
        string.Join('\n', example).ShouldContain("mail_password");
        string.Join('\n', example).ShouldContain("seq_api_key");
    }

    [Fact]
    public void The_database_and_the_logs_are_not_published_to_the_network()
    {
        // Only nginx publishes ports to the world; Seq is bound to the server itself.
        var ports = Regex.Matches(Compose, @"^\s+- ""(?<port>[\d.:]+)""\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["port"].Value)
            .ToList();

        ports.ShouldBe(["127.0.0.1:5341:80", "443:443", "80:80"]);
    }
}
