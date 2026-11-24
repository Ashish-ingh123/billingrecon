using System.Text.RegularExpressions;

namespace BillingRecon.ArchitectureTests;

/// <summary>
/// docs/traceability.md is complete and true (Prompt 8.3): every user story of docs/requirements.md section 4 has a row,
/// no row is without a test, and every test a row names exists, as <c>path#name</c>: a file of the repository that
/// contains the name, a C# test method or the title of a frontend test. A new story fails here until a test proves it;
/// a renamed or removed test fails here until the table follows.
/// </summary>
public sealed partial class TraceabilityTests
{
    private static readonly string Requirements = File.ReadAllText(RepositoryFiles.PathOf("docs/requirements.md"));

    private static readonly string Traceability = File.ReadAllText(RepositoryFiles.PathOf("docs/traceability.md"));

    [Fact]
    public void Every_user_story_has_a_row()
    {
        var stories = StoriesOfTheRequirements().ToList();
        stories.Count.ShouldBeGreaterThanOrEqualTo(25);

        var rows = Rows().Select(row => row.Story).ToList();

        stories.Where(story => !rows.Contains(story)).ShouldBeEmpty("user stories without a row in docs/traceability.md");
        rows.Where(story => !stories.Contains(story)).ShouldBeEmpty("rows of docs/traceability.md for no user story");
        rows.Count.ShouldBe(rows.Distinct().Count(), "a story has one row");
    }

    [Fact]
    public void No_row_is_without_a_test()
    {
        Rows().Where(row => row.Tests.Count == 0).Select(row => row.Story).ShouldBeEmpty("rows of docs/traceability.md without a test");
    }

    [Fact]
    public void Every_test_a_row_names_exists()
    {
        var missing = new List<string>();
        foreach (var (story, tests) in Rows())
        {
            foreach (var reference in tests)
            {
                var separator = reference.IndexOf('#', StringComparison.Ordinal);
                var path = reference[..separator];
                var name = reference[(separator + 1)..];
                var file = RepositoryFiles.PathOf(path);
                if (!File.Exists(file) || !File.ReadAllText(file).Contains(name, StringComparison.Ordinal))
                {
                    missing.Add($"{story}: {reference}");
                }
            }
        }

        missing.ShouldBeEmpty("tests named in docs/traceability.md that do not exist");
    }

    // The ids of the rows of the user-story tables of section 4.
    private static IEnumerable<string> StoriesOfTheRequirements()
    {
        var start = Requirements.IndexOf("## 4.", StringComparison.Ordinal);
        var end = Requirements.IndexOf("## 5.", start, StringComparison.Ordinal);

        return StoryRow().Matches(Requirements[start..end]).Select(match => match.Groups["story"].Value);
    }

    // Each row of the table: its story, and the `path#name` references of its last cell.
    private static IEnumerable<(string Story, List<string> Tests)> Rows()
        => StoryRow().Matches(Traceability).Select(match =>
        {
            var line = Traceability[match.Index..Traceability.IndexOf('\n', match.Index)];
            var tests = line[(line.TrimEnd().TrimEnd('|').LastIndexOf('|') + 1)..];

            return (match.Groups["story"].Value, Reference().Matches(tests).Select(reference => reference.Groups["reference"].Value).ToList());
        });

    [GeneratedRegex(@"^\| (?<story>US-\d{2}) \|", RegexOptions.Multiline)]
    private static partial Regex StoryRow();

    [GeneratedRegex(@"`(?<reference>[^`#]+#[^`]+)`")]
    private static partial Regex Reference();
}
