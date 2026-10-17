namespace BillingRecon.IntegrationTests.Database;

/// <summary>
/// The tests that need the SQL Server of the test run. xUnit gives every class of the collection the same
/// <see cref="SqlServerFixture"/> and runs the collection's tests one after another, which is what sharing two
/// databases requires; the other collections still run in parallel with it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollectionDefinition : ICollectionFixture<SqlServerFixture>
{
    /// <summary>The name to put on a test class: <c>[Collection(DatabaseCollectionDefinition.Name)]</c>.</summary>
    public const string Name = "Database";
}
