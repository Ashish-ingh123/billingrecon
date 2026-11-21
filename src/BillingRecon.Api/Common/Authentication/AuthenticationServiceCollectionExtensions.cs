using BillingRecon.Application.Common;
using BillingRecon.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BillingRecon.Api.Common.Authentication;

/// <summary>
/// Who a request acts for (ADR 0031): the key and the issuer of tokens, the bearer scheme that validates them, the
/// identity of the request read from its principal, the sign-in's step into that identity and, in Development, the
/// development identity for a request without a token. Every endpoint needs an authenticated user unless it is marked
/// anonymous (a fallback policy): deny by default.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    /// <summary>The scheme of Development that sends a request to the bearer scheme or to the development identity.</summary>
    public const string DevelopmentSelectorScheme = "DevelopmentOrBearer";

    /// <summary>Adds the authentication and authorisation of requests.</summary>
    /// <param name="services">The service collection of the host.</param>
    /// <param name="environment">The environment of the host; Development adds the development identity.</param>
    public static IServiceCollection AddRequestIdentity(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(JwtOptions.HasValidKey, JwtOptions.InvalidKeyMessage)
            .Validate<IHostEnvironment>(
                (options, host) => host.IsDevelopment() || JwtOptions.IsNotTheDevelopmentKey(options),
                JwtOptions.DevelopmentKeyMessage)
            .ValidateOnStart();

        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services.AddHttpContextAccessor();
        services.AddScoped<RequestPrincipalIdentity>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestPrincipalIdentity>());
        services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<RequestPrincipalIdentity>());
        services.AddScoped<ISignInScope, HttpSignInScope>();

        var development = environment.IsDevelopment();
        var authentication = services
            .AddAuthentication(development ? DevelopmentSelectorScheme : JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>, IClock>(ConfigureBearer);

        if (development)
        {
            authentication
                .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, configureOptions: null)
                .AddPolicyScheme(DevelopmentSelectorScheme, displayName: null, options => options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.Count > 0 ? JwtBearerDefaults.AuthenticationScheme : DevelopmentAuthenticationHandler.SchemeName);
        }

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    private static void ConfigureBearer(JwtBearerOptions bearer, IOptions<JwtOptions> jwt, IClock clock)
    {
        // The claims keep the names the token gives them ("sub", "tenant_id", "role"), not the long names of .NET.
        bearer.MapInboundClaims = false;

        // A refused token gets a bare "Bearer" challenge: why it was refused is not told to whoever sent it.
        bearer.IncludeErrorDetails = false;

        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = JwtOptions.Issuer,
            ValidAudience = JwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.ReadKey()),
            ValidateIssuerSigningKey = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = JwtOptions.ClockSkew,

            // In place of the library's check, which reads the system clock: the clock that issued the token.
            LifetimeValidator = (notBefore, expires, _, parameters) => TokenLifetime.IsCurrent(notBefore, expires, clock.UtcNow, parameters.ClockSkew),
            NameClaimType = TokenClaims.UserIdClaim,
            RoleClaimType = TokenClaims.RoleClaim,
        };

        bearer.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (AnonymousEndpoints.IsAnonymous(context.HttpContext))
                {
                    context.NoResult();
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (context.Principal is null || !TokenClaims.TryRead(context.Principal, out _, out _))
                {
                    context.Fail("The token carries no tenant or no user that is an id.");
                }

                return Task.CompletedTask;
            },
        };
    }
}
