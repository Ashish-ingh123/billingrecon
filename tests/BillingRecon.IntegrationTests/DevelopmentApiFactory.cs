using Microsoft.Extensions.Hosting;

namespace BillingRecon.IntegrationTests;

/// <summary>Hosts the API in the Development environment, for behaviour that exists only there.</summary>
public sealed class DevelopmentApiFactory : ApiFactory
{
    protected override string EnvironmentName => Environments.Development;
}
