using System.Net;
using BillingRecon.Api.Billing;
using BillingRecon.Api.Payers;
using BillingRecon.Application.Billing;
using BillingRecon.Application.Payers;
using BillingRecon.Domain.Payers;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The addresses of the Billing endpoints and the calls the API tests make to them. The helpers every API test shares
/// (JSON bodies, If-Match, ETags, query strings) are in <see cref="PayersApi"/>.
/// </summary>
internal static class BillingApi
{
    public static readonly Uri ServiceItems = new("/api/v1/service-items", UriKind.Relative);

    public static Uri ServiceItem(Guid id) => new($"/api/v1/service-items/{id}", UriKind.Relative);

    public static Uri ServiceItem(Guid id, string action) => new($"/api/v1/service-items/{id}/{action}", UriKind.Relative);

    public static readonly Uri Bills = new("/api/v1/bills", UriKind.Relative);

    public static Uri Bill(Guid id) => new($"/api/v1/bills/{id}", UriKind.Relative);

    public static Uri Bill(Guid id, string action) => new($"/api/v1/bills/{id}/{action}", UriKind.Relative);

    /// <summary>
    /// Registers a patient and creates their self-pay payer: the two parties a bill needs. Both come from
    /// <see cref="TestData"/> and are active.
    /// </summary>
    public static async Task<(PatientDto Patient, PayerDto Payer)> NewBillPartiesAsync(this HttpClient client)
    {
        var patient = await client.RegisterPatientAsync();
        var payer = await client.CreatePayerAsync(new CreatePayerRequest { Kind = PayerKind.SelfPay, PatientId = patient.Id, Name = patient.FullName });

        return (patient, payer);
    }

    /// <summary>Creates a draft bill and returns it; fails unless the answer is 201.</summary>
    public static async Task<BillDto> CreateBillAsync(this HttpClient client, CreateBillRequest request)
    {
        using var response = await client.PostJsonAsync(Bills, request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<BillDto>();
    }

    /// <summary>A free-text line: <paramref name="quantity"/> units at <paramref name="unitPrice"/>, taxed at <paramref name="taxRatePercent"/>.</summary>
    public static BillLineRequest FreeTextLine(string description, decimal quantity, decimal unitPrice, decimal taxRatePercent)
        => new() { Description = description, Quantity = quantity, UnitPrice = unitPrice, TaxRatePercent = taxRatePercent };

    /// <summary>Adds a service item, by default a new one from <see cref="TestData"/>, and returns it; fails unless the answer is 201.</summary>
    public static async Task<ServiceItemDto> CreateServiceItemAsync(this HttpClient client, CreateServiceItemRequest? request = null)
    {
        using var response = await client.PostJsonAsync(ServiceItems, request ?? TestData.NewServiceItem());

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<ServiceItemDto>();
    }
}
