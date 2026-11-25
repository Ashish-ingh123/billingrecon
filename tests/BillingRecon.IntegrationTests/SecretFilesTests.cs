using BillingRecon.Api.Common.Configuration;
using Microsoft.Extensions.Configuration;

namespace BillingRecon.IntegrationTests;

/// <summary>Secrets come from the files the host mounts, one file a setting (ADR 0055).</summary>
public sealed class SecretFilesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("billingrecon-secrets-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_file_is_a_setting_named_with_two_underscores_and_its_last_line_break_is_left_out()
    {
        File.WriteAllText(Path.Combine(_directory, "Jwt__SigningKey"), "key-from-a-file\n");
        File.WriteAllText(Path.Combine(_directory, "Database__ConnectionString"), "Server=sql;Database=BillingRecon");

        var configuration = new ConfigurationBuilder().AddSecretFiles(_directory).Build();

        configuration["Jwt:SigningKey"].ShouldBe("key-from-a-file");
        configuration["Database:ConnectionString"].ShouldBe("Server=sql;Database=BillingRecon");
    }

    [Fact]
    public void A_setting_from_a_file_wins_over_the_same_setting_from_an_earlier_source()
    {
        File.WriteAllText(Path.Combine(_directory, "Jwt__SigningKey"), "from-the-file");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Jwt:SigningKey", "from-the-environment"), new("Seq:ServerUrl", "http://seq")])
            .AddSecretFiles(_directory)
            .Build();

        configuration["Jwt:SigningKey"].ShouldBe("from-the-file");
        configuration["Seq:ServerUrl"].ShouldBe("http://seq");
    }

    [Fact]
    public void A_directory_that_is_not_there_adds_nothing_and_does_not_fail()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Jwt:SigningKey", "kept")])
            .AddSecretFiles(Path.Combine(_directory, "missing"))
            .Build();

        configuration["Jwt:SigningKey"].ShouldBe("kept");
    }

    [Fact]
    public void An_empty_secret_file_is_an_empty_setting_which_the_start_then_refuses()
    {
        File.WriteAllText(Path.Combine(_directory, "Jwt__SigningKey"), "\n");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Jwt:SigningKey", "an-older-key")])
            .AddSecretFiles(_directory)
            .Build();

        // The file wins with nothing in it; StartupValidationTests shows that the API refuses an empty key.
        configuration["Jwt:SigningKey"].ShouldBeNullOrEmpty();
    }
}
