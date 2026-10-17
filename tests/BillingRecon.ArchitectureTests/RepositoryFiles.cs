namespace BillingRecon.ArchitectureTests;

/// <summary>Finds files of the repository from the directory the tests run in.</summary>
internal static class RepositoryFiles
{
    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    /// <summary>The directory that holds <c>BillingRecon.sln</c>.</summary>
    public static string Root => RootDirectory.Value;

    /// <summary>The absolute path of a file given relative to the repository root.</summary>
    public static string PathOf(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Files under <paramref name="relativeDirectory"/> that match <paramref name="pattern"/>, without build output, as paths relative to the root with forward slashes.</summary>
    public static IEnumerable<string> Find(string relativeDirectory, string pattern)
        => Directory.EnumerateFiles(PathOf(relativeDirectory), pattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BillingRecon.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"BillingRecon.sln was not found above {AppContext.BaseDirectory}. These tests read source files and must run inside the repository.");
    }
}
