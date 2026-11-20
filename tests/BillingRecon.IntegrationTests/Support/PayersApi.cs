using System.Net;
using System.Net.Http.Json;
using System.Text;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Payers;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>The addresses of the Payers endpoints and the calls the API tests make to them.</summary>
internal static class PayersApi
{
    public static readonly Uri Patients = new("/api/v1/patients", UriKind.Relative);

    public static readonly Uri Payers = new("/api/v1/payers", UriKind.Relative);

    public static Uri Patient(Guid id) => new($"/api/v1/patients/{id}", UriKind.Relative);

    public static Uri Patient(Guid id, string action) => new($"/api/v1/patients/{id}/{action}", UriKind.Relative);

    public static Uri Payer(Guid id) => new($"/api/v1/payers/{id}", UriKind.Relative);

    public static Uri Payer(Guid id, string action) => new($"/api/v1/payers/{id}/{action}", UriKind.Relative);

    /// <summary><paramref name="resource"/> with a query string.</summary>
    public static Uri WithQuery(this Uri resource, string query) => new($"{resource}?{query}", UriKind.Relative);

    /// <summary>The ETag of a version, as the API writes it and a client sends it back.</summary>
    public static string ETag(string version) => $"\"{version}\"";

    /// <summary>A JSON body exactly as written, for bodies the request records cannot express.</summary>
    public static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>The request header of the idempotency contract (BR-803).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>
    /// POSTs <paramref name="body"/> as JSON, enums by name, as a well-behaved client does: with an
    /// <c>Idempotency-Key</c>, a new one for every operation unless <paramref name="idempotencyKey"/> names one. An
    /// endpoint that needs no key ignores the header.
    /// </summary>
    public static async Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, Uri uri, object body, Guid? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body, body.GetType(), options: TestJson.Options) };
        request.Headers.Add(IdempotencyKeyHeader, (idempotencyKey ?? Guid.NewGuid()).ToString());

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Sends <paramref name="body"/> (if any) with the If-Match header <paramref name="ifMatch"/> (if any), written as
    /// given. A POST carries an <c>Idempotency-Key</c> as well, a new one unless <paramref name="idempotencyKey"/>
    /// names one.
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithIfMatchAsync(this HttpClient client, HttpMethod method, Uri uri, object? body, string? ifMatch, Guid? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (method == HttpMethod.Post)
        {
            request.Headers.Add(IdempotencyKeyHeader, (idempotencyKey ?? Guid.NewGuid()).ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: TestJson.Options);
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch).ShouldBeTrue();
        }

        return await client.SendAsync(request);
    }

    /// <summary>Registers a patient, by default a new one from <see cref="TestData"/>, and returns it; fails unless the answer is 201.</summary>
    public static async Task<PatientDto> RegisterPatientAsync(this HttpClient client, RegisterPatientRequest? request = null)
    {
        using var response = await client.PostJsonAsync(Patients, request ?? TestData.NewPatient());

        return await response.ShouldBeCreatedAsync<PatientDto>();
    }

    /// <summary>Creates a payer and returns it; fails unless the answer is 201.</summary>
    public static async Task<PayerDto> CreatePayerAsync(this HttpClient client, CreatePayerRequest request)
    {
        using var response = await client.PostJsonAsync(Payers, request);

        return await response.ShouldBeCreatedAsync<PayerDto>();
    }

    /// <summary>Reads the body of the response.</summary>
    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response)
        where T : class
        => (await response.Content.ReadFromJsonAsync<T>(TestJson.Options)).ShouldNotBeNull();

    private static async Task<T> ShouldBeCreatedAsync<T>(this HttpResponseMessage response)
        where T : class
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<T>();
    }
}
