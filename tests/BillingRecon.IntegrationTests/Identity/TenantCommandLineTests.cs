using BillingRecon.Api.Identity;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BillingRecon.IntegrationTests.Identity;

/// <summary>
/// The operator's command line (ADR 0052) without a database: what it reads from its arguments, what it prints, its exit
/// codes, and its refusal in the Demo environment. What the operations do on a database is
/// <c>TenantOnboardingOnSqlServerTests</c>.
/// </summary>
public sealed class TenantCommandLineTests
{
    private static readonly string[] Create =
    [
        "tenant", "create", "--code", "city-hospital", "--name", "City Hospital", "--admin-email", "admin@city.example",
        "--admin-name", "A. Rao", "--backdate-days", "3", "--due-days", "15", "--threshold", "100000.50",
        "--prefixes", "Bill=INV, CreditNote=CRN",
    ];

    [Fact]
    public void ADR0052_only_arguments_that_begin_with_tenant_are_a_command()
    {
        TenantCommandLine.IsCommand(["tenant", "create"]).ShouldBeTrue();
        TenantCommandLine.IsCommand([]).ShouldBeFalse();
        TenantCommandLine.IsCommand(["--urls", "http://+:8080"]).ShouldBeFalse();
    }

    [Fact]
    public async Task ADR0052_create_reads_every_option_and_prints_the_one_time_password_once()
    {
        var operations = new RecordingOperations();
        using var output = new StringWriter();

        var exit = await RunAsync(operations, Environments.Production, Create, output);

        exit.ShouldBe(0);
        var command = operations.Onboarded.ShouldNotBeNull();
        (command.Code, command.Name, command.AdminEmail, command.AdminDisplayName).ShouldBe(("city-hospital", "City Hospital", "admin@city.example", "A. Rao"));
        (command.PostingApprovalThreshold, command.MaxBackdateDays, command.DefaultDueDays).ShouldBe((100_000.50m, 3, 15));
        command.Prefixes.ShouldBe(new Dictionary<DocumentType, string> { [DocumentType.Bill] = "INV", [DocumentType.CreditNote] = "CRN" });
        var printed = output.ToString();
        printed.Split(RecordingOperations.Password).Length.ShouldBe(2, "the password is printed exactly once");
        printed.ShouldContain("must choose their own");
    }

    [Fact]
    public async Task ADR0052_without_a_threshold_or_with_none_no_bill_needs_approval()
    {
        var operations = new RecordingOperations();

        (await RunAsync(operations, Environments.Production, [.. Create[..^4], "--threshold", "none"], TextWriter.Null)).ShouldBe(0);

        operations.Onboarded.ShouldNotBeNull().PostingApprovalThreshold.ShouldBeNull();
    }

    [Theory]
    [InlineData("--due-days", "fifteen")]
    [InlineData("--threshold", "1,00,000")]
    [InlineData("--prefixes", "Invoice=INV")]
    [InlineData("--prefixes", "Bill=INV,Bill=BIL")]
    public async Task ADR0052_an_option_that_cannot_be_read_is_refused_before_anything_is_done(string option, string value)
    {
        var operations = new RecordingOperations();
        var args = Create.ToArray();
        var index = Array.IndexOf(args, option);
        args[index + 1] = value;

        (await RunAsync(operations, Environments.Production, args, TextWriter.Null)).ShouldBe(2);

        operations.Onboarded.ShouldBeNull();
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("tenant delete --code city-hospital")]
    [InlineData("tenant suspend --code")]
    [InlineData("tenant suspend code city-hospital")]
    public async Task ADR0052_a_command_it_does_not_know_prints_the_usage(string line)
    {
        using var output = new StringWriter();

        (await RunAsync(new RecordingOperations(), Environments.Production, line.Split(' '), output)).ShouldBe(2);

        output.ToString().ShouldContain("Usage:");
    }

    [Fact]
    public async Task ADR0052_a_refusal_is_printed_with_its_code_and_the_option_at_fault_and_exits_with_one()
    {
        var operations = new RecordingOperations
        {
            Refusal = IdentityErrors.TenantInvalid(new Dictionary<string, string[]> { ["adminEmail"] = ["Give the address."] }),
        };
        using var output = new StringWriter();

        (await RunAsync(operations, Environments.Production, Create, output)).ShouldBe(1);

        output.ToString().ShouldContain("Refused (tenant_invalid)");
        output.ToString().ShouldContain("--admin-email: Give the address.");
    }

    [Fact]
    public async Task ADR0052_suspend_and_resume_pass_the_code_and_the_reason()
    {
        var operations = new RecordingOperations();

        (await RunAsync(operations, Environments.Production, ["tenant", "suspend", "--code", "city-hospital", "--reason", "Contract ended"], TextWriter.Null)).ShouldBe(0);
        (await RunAsync(operations, Environments.Production, ["tenant", "resume", "--code", "city-hospital", "--reason", "Renewed"], TextWriter.Null)).ShouldBe(0);

        operations.StatusChanges.ShouldBe([("suspend", "city-hospital", "Contract ended"), ("resume", "city-hospital", "Renewed")]);
    }

    [Fact]
    public async Task ADR0052_the_commands_are_refused_in_the_demo_environment()
    {
        var operations = new RecordingOperations();
        using var output = new StringWriter();

        (await RunAsync(operations, "Demo", Create, output)).ShouldBe(2);

        operations.Onboarded.ShouldBeNull();
        output.ToString().ShouldContain("refused in the Demo environment");
    }

    private static async Task<int> RunAsync(RecordingOperations operations, string environment, string[] args, TextWriter output)
    {
        await using var services = new ServiceCollection().AddScoped<ITenantOperations>(_ => operations).BuildServiceProvider();

        return await TenantCommandLine.RunAsync(services, new Host(environment), args, output, default);
    }

    private sealed class Host(string environment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;

        public string ApplicationName { get; set; } = "BillingRecon.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class RecordingOperations : ITenantOperations
    {
        public const string Password = "Abcdefghjkmnpqrstuvw";

        public OnboardTenant? Onboarded { get; private set; }

        public Error? Refusal { get; init; }

        public List<(string Action, string Code, string Reason)> StatusChanges { get; } = [];

        public Task<Result<OnboardedTenant>> OnboardAsync(OnboardTenant command, CancellationToken ct)
        {
            Onboarded = command;
            Result<OnboardedTenant> result = Refusal is { } refusal
                ? refusal
                : new OnboardedTenant(new TenantId(Guid.NewGuid()), new UserId(Guid.NewGuid()), Password);

            return Task.FromResult(result);
        }

        public Task<Result> SuspendAsync(string code, string reason, CancellationToken ct)
        {
            StatusChanges.Add(("suspend", code, reason));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> ResumeAsync(string code, string reason, CancellationToken ct)
        {
            StatusChanges.Add(("resume", code, reason));
            return Task.FromResult(Result.Success());
        }
    }
}
