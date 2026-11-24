namespace BillingRecon.Infrastructure.Identity;

/// <summary>
/// The demo of the one-command local run (ADR 0045): when <see cref="Enabled"/>, the API makes sure at startup that the
/// hospital <c>demo</c> and its administrator exist, with <see cref="AdminPassword"/>; everything else of the demo is
/// entered through the API by the demo seed script. Bound from the <c>Demo</c> section; off unless asked for. The password
/// is a secret and comes from the environment, never from a file in git.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "Demo";

    /// <summary>The code of the demo hospital, typed at sign-in.</summary>
    public const string TenantCode = "demo";

    /// <summary>The name of the demo hospital.</summary>
    public const string TenantName = "City Care Hospital (demo)";

    /// <summary>The address of the demo administrator.</summary>
    public const string AdminEmail = "admin@demo.local";

    /// <summary>The display name of the demo administrator.</summary>
    public const string AdminDisplayName = "Demo administrator";

    /// <summary>Why the API does not start with the demo on and no usable password.</summary>
    public const string PasswordMessage =
        "Demo:AdminPassword is required when Demo:Enabled is true: 12 to 128 characters (set it in .env as DEMO_ADMIN_PASSWORD).";

    /// <summary>The fixed id of the demo hospital, so that a second start finds it.</summary>
    public static readonly Guid TenantId = new("d3e40000-0000-4000-8000-000000000001");

    /// <summary>The fixed id of the demo administrator.</summary>
    public static readonly Guid AdminUserId = new("d3e40000-0000-4000-8000-000000000002");

    /// <summary>The environment the one-command demo runs in: like Production in all else (ADR 0052).</summary>
    public const string EnvironmentName = "Demo";

    /// <summary>The message of a demo asked for in another environment than Demo or Development.</summary>
    public const string EnvironmentMessage =
        "Demo:Enabled is refused outside the Demo and Development environments: a hospital in production is onboarded by the operator's command (ADR 0052).";

    /// <summary>Whether the API makes sure the demo hospital and its administrator exist.</summary>
    public bool Enabled { get; set; }

    /// <summary>The administrator's password, set when the row is first made; a later change of it changes nothing.</summary>
    public string? AdminPassword { get; set; }

    /// <summary>True when the demo is off, or on with a password of 12 to 128 characters (ADR 0030).</summary>
    public static bool IsUsable(DemoOptions options) =>
        !options.Enabled || options.AdminPassword is { Length: >= 12 and <= 128 };
}
