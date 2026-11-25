using System.Globalization;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;

namespace BillingRecon.Api.Identity;

/// <summary>
/// The operator's commands on a hospital (ADR 0052), run on the server with the API's own image and configuration,
/// never reachable from the internet: <c>tenant create</c>, <c>tenant suspend</c> and <c>tenant resume</c>. The API
/// runs one when its first argument is <c>tenant</c>, prints the outcome and exits instead of serving. Refused in the
/// Demo environment, whose hospital the demo makes.
/// </summary>
public static class TenantCommandLine
{
    /// <summary>The environment in which the commands are refused (the demo's, ADR 0052).</summary>
    public const string DemoEnvironment = "Demo";

    /// <summary>The usage, printed for a command it does not know.</summary>
    public const string Usage = """
        Usage:
          tenant create --code <code> --name <name> --admin-email <address> --admin-name <name>
                        --backdate-days <0-365> --due-days <0-365> [--threshold <amount>|none]
                        [--prefixes Bill=BIL,CreditNote=CN,Payment=PAY,Adjustment=ADJ,Refund=REF]
          tenant suspend --code <code> --reason <text>
          tenant resume  --code <code> --reason <text>
        """;

    /// <summary>True when the arguments ask for an operator's command rather than the API.</summary>
    public static bool IsCommand(string[] args) => args is ["tenant", ..];

    /// <summary>Runs the command in a scope of <paramref name="services"/>; answers the process's exit code.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, string[] args, TextWriter output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (environment.IsEnvironment(DemoEnvironment))
        {
            await output.WriteLineAsync("The tenant commands are refused in the Demo environment: the demo makes its own hospital.");
            return 2;
        }

        if (args.Length < 2 || !TryReadOptions(args.AsSpan(2), out var options))
        {
            await output.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<ITenantOperations>();

        switch (args[1])
        {
            case "create":
                return await CreateAsync(operations, options, output, ct);
            case "suspend":
                return await ReportAsync(await operations.SuspendAsync(Option(options, "code"), Option(options, "reason"), ct), "Suspended", output);
            case "resume":
                return await ReportAsync(await operations.ResumeAsync(Option(options, "code"), Option(options, "reason"), ct), "Resumed", output);
            default:
                await output.WriteLineAsync(Usage);
                return 2;
        }
    }

    private static async Task<int> CreateAsync(ITenantOperations operations, Dictionary<string, string> options, TextWriter output, CancellationToken ct)
    {
        if (!TryReadCommand(options, out var command, out var problem))
        {
            await output.WriteLineAsync(problem);
            return 2;
        }

        var onboarded = await operations.OnboardAsync(command, ct);
        if (onboarded.IsFailure)
        {
            return await ReportAsync(onboarded.Error, output);
        }

        await output.WriteLineAsync($"Created hospital {command.Code} ({onboarded.Value.TenantId.Value}).");
        await output.WriteLineAsync($"Its administrator {command.AdminEmail.Trim()} ({onboarded.Value.AdminUserId.Value}) signs in with this one-time password,");
        await output.WriteLineAsync("shown once and stored nowhere; they must choose their own at the first sign-in:");
        await output.WriteLineAsync();
        await output.WriteLineAsync($"    {onboarded.Value.OneTimePassword}");
        await output.WriteLineAsync();
        return 0;
    }

    private static async Task<int> ReportAsync(Result result, string done, TextWriter output)
    {
        if (result.IsFailure)
        {
            return await ReportAsync(result.Error, output);
        }

        await output.WriteLineAsync($"{done}.");
        return 0;
    }

    private static async Task<int> ReportAsync(Error error, TextWriter output)
    {
        await output.WriteLineAsync($"Refused ({error.Code}): {error.Message}");
        foreach (var (field, messages) in error.FieldErrors)
        {
            await output.WriteLineAsync($"  --{ToOptionName(field)}: {string.Join(" ", messages)}");
        }

        return 1;
    }

    private static string ToOptionName(string field) => field switch
    {
        "adminEmail" => "admin-email",
        "adminName" => "admin-name",
        "backdateDays" => "backdate-days",
        "dueDays" => "due-days",
        _ => field,
    };

    private static string Option(Dictionary<string, string> options, string name) => options.GetValueOrDefault(name, string.Empty);

    private static bool TryReadOptions(ReadOnlySpan<string> args, out Dictionary<string, string> options)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length || !options.TryAdd(args[index][2..], args[index + 1]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadCommand(Dictionary<string, string> options, out OnboardTenant command, out string problem)
    {
        command = null!;
        problem = string.Empty;

        if (!int.TryParse(Option(options, "backdate-days"), NumberStyles.None, CultureInfo.InvariantCulture, out var backdate)
            || !int.TryParse(Option(options, "due-days"), NumberStyles.None, CultureInfo.InvariantCulture, out var due))
        {
            problem = "Give --backdate-days and --due-days as whole numbers of days.";
            return false;
        }

        decimal? threshold = null;
        var givenThreshold = Option(options, "threshold");
        if (givenThreshold is not ("" or "none"))
        {
            if (!decimal.TryParse(givenThreshold, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            {
                problem = "Give --threshold as an amount such as 100000 or 2500.50, or none.";
                return false;
            }

            threshold = amount;
        }

        var prefixes = new Dictionary<DocumentType, string>();
        foreach (var pair in Option(options, "prefixes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !Enum.TryParse<DocumentType>(parts[0], ignoreCase: false, out var type)
                || !Enum.IsDefined(type)
                || !Tenant.HasPrefixSetting(type)
                || !prefixes.TryAdd(type, parts[1]))
            {
                problem = "Give --prefixes as Type=PREFIX pairs separated by commas, each type once: Bill, CreditNote, Payment, Adjustment, Refund.";
                return false;
            }
        }

        command = new OnboardTenant
        {
            Code = Option(options, "code"),
            Name = Option(options, "name"),
            AdminEmail = Option(options, "admin-email"),
            AdminDisplayName = Option(options, "admin-name"),
            PostingApprovalThreshold = threshold,
            MaxBackdateDays = backdate,
            DefaultDueDays = due,
            Prefixes = prefixes,
        };
        return true;
    }
}
