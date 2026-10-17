using System.Reflection;
using BillingRecon.Api.Common;

namespace BillingRecon.ArchitectureTests;

/// <summary>The assemblies and module names the architecture rules are checked against.</summary>
internal static class Assemblies
{
    public const string RootNamespace = "BillingRecon";

    public static readonly Assembly Domain = typeof(BillingRecon.Domain.AssemblyMarker).Assembly;
    public static readonly Assembly Application = typeof(BillingRecon.Application.AssemblyMarker).Assembly;
    public static readonly Assembly Infrastructure = typeof(BillingRecon.Infrastructure.AssemblyMarker).Assembly;
    public static readonly Assembly Api = typeof(ApiControllerBase).Assembly;

    public static readonly Assembly[] All = [Domain, Application, Infrastructure, Api];

    public static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];

    public static readonly string[] BusinessModules = ["Payers", "Billing", "Payments", "Receivables", "Adjustments", "Reporting"];

    public static readonly string[] CrossCuttingModules = ["Common", "Identity", "Audit"];
}
