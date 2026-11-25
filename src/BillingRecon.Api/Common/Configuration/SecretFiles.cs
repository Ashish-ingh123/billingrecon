namespace BillingRecon.Api.Common.Configuration;

/// <summary>
/// Secrets read from files, one file a setting (ADR 0055): the host's secret store mounts them, as Docker does under
/// <c>/run/secrets</c>, so that no secret is in git, in an image or in the environment of a container. A file is named
/// as its setting, with two underscores for the colon (<c>Database__ConnectionString</c>, <c>Jwt__SigningKey</c>), and
/// its content is the value, the last line break left out. A setting read from a file wins over the same setting from
/// appsettings or an environment variable.
/// </summary>
public static class SecretFiles
{
    /// <summary>The variable that names the directory, for a host that mounts its secrets elsewhere.</summary>
    public const string DirectoryVariable = "BILLINGRECON_SECRETS_DIR";

    /// <summary>Where Docker mounts the secrets of a service.</summary>
    public const string DefaultDirectory = "/run/secrets";

    /// <summary>Adds the files of <paramref name="directory"/> as settings; a directory that is not there adds nothing.</summary>
    public static IConfigurationBuilder AddSecretFiles(this IConfigurationBuilder configuration, string? directory)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var path = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory;

        if (!Directory.Exists(path))
        {
            return configuration;
        }

        // Read once, at startup: a rotated secret is taken up by a restart (docs/runbook.md section 19).
        var settings = Directory.EnumerateFiles(path).ToDictionary(
            file => Path.GetFileName(file).Replace("__", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal),
            file => (string?)File.ReadAllText(file).TrimEnd('\r', '\n'),
            StringComparer.OrdinalIgnoreCase);

        return configuration.AddInMemoryCollection(settings);
    }
}
