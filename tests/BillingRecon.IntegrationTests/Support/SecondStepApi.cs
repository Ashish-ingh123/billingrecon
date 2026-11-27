using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BillingRecon.Api.Identity;
using BillingRecon.Application.Identity;
using BillingRecon.Domain.Identity;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The second step of a sign-in as a test gives it (ADR 0061): it enrols as a user does, through the API, and
/// computes the code an authenticator app would show from the secret the API answered and the instant of the host's
/// clock. Nothing here reaches around the API.
/// </summary>
internal static class SecondStepApi
{
    public static readonly Uri SecondStep = new("/api/v1/auth/second-step", UriKind.Relative);
    public static readonly Uri Enrolment = new("/api/v1/auth/second-step/enrolment", UriKind.Relative);
    public static readonly Uri Confirmation = new("/api/v1/auth/second-step/enrolment/confirm", UriKind.Relative);

    /// <summary>The bytes of a secret the API answered in base32.</summary>
    public static byte[] FromBase32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in text)
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. bytes];
    }

    /// <summary>The code an authenticator app shows for <paramref name="secret"/> at <paramref name="now"/>, or a number of steps away.</summary>
    public static string CodeAt(string secret, DateTimeOffset now, int stepsAway = 0)
        => Totp.CodeOf(FromBase32(secret), Totp.StepOf(now) + stepsAway);

    /// <summary>A POST with a bearer token and an optional body.</summary>
    public static async Task<HttpResponseMessage> PostWithTokenAsync(this HttpClient client, Uri uri, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Begins an enrolment with <paramref name="token"/> and answers the secret.</summary>
    public static async Task<SecondStepEnrolmentDto> BeginEnrolmentAsync(this HttpClient client, string token)
    {
        using var begun = await client.PostWithTokenAsync(Enrolment, token);

        begun.StatusCode.ShouldBe(HttpStatusCode.OK, await begun.Content.ReadAsStringAsync());

        return await begun.ReadAsAsync<SecondStepEnrolmentDto>();
    }

    /// <summary>
    /// Enrols a second step for the user of <paramref name="token"/>, as that user would: begin, read the secret,
    /// confirm with the code of <paramref name="now"/>. Answers the secret, the recovery codes and the new sign-in.
    /// </summary>
    public static async Task<(string Secret, SecondStepEnrolledDto Enrolled)> EnrolAsync(this HttpClient client, string token, DateTimeOffset now)
    {
        var begun = await client.BeginEnrolmentAsync(token);
        using var confirmed = await client.PostWithTokenAsync(Confirmation, token, new SecondStepCodeRequest { Code = CodeAt(begun.Secret, now) });

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());

        return (begun.Secret, await confirmed.ReadAsAsync<SecondStepEnrolledDto>());
    }
}
