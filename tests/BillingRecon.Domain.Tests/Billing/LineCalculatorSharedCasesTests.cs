using System.Globalization;
using System.Text.Json;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Tests.Billing;

/// <summary>
/// The browser's estimate of a bill line and the server's figures are one arithmetic (BR-202, BR-203, ADR 0040 point 3):
/// the cases of <c>frontend/src/bills/lineCases.json</c> hold the frontend's <c>lineMath.ts</c>, and here they hold
/// <see cref="LineCalculator"/>. A case that one side computes differently fails on that side.
/// </summary>
public sealed class LineCalculatorSharedCasesTests
{
    public static TheoryData<string, string, string, string, string, string, string> Cases()
    {
        var path = Path.Combine(RepositoryRoot(), "frontend", "src", "bills", "lineCases.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new TheoryData<string, string, string, string, string, string, string>();
        foreach (var line in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string Text(string name) => line.GetProperty(name).GetString()!;

            cases.Add(
                Text("quantity"),
                Text("unitPrice"),
                Text("discountAmount"),
                Text("taxRatePercent"),
                Text("lineAmount"),
                Text("taxAmount"),
                Text("lineTotal"));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BR202_the_server_computes_every_shared_case_as_the_browser_does(
        string quantity,
        string unitPrice,
        string discountAmount,
        string taxRatePercent,
        string lineAmount,
        string taxAmount,
        string lineTotal)
    {
        var amounts = LineCalculator.Calculate(
            Quantity.Of(Number(quantity)),
            UnitPrice.Of(Number(unitPrice)),
            Money.Of(Number(discountAmount)),
            Percent.Of(Number(taxRatePercent)));

        amounts.LineAmount.ShouldBe(Money.Of(Number(lineAmount)));
        amounts.TaxAmount.ShouldBe(Money.Of(Number(taxAmount)));
        amounts.LineTotal.ShouldBe(Money.Of(Number(lineTotal)));
    }

    [Fact]
    public void The_shared_cases_are_read()
    {
        Cases().Count.ShouldBeGreaterThan(10);
    }

    private static decimal Number(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BillingRecon.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"BillingRecon.sln was not found above {AppContext.BaseDirectory}. This test reads a file of the frontend and must run inside the repository.");
    }
}
