using System.Net;
using BillingRecon.Api.Payments;
using BillingRecon.Application.Common;
using BillingRecon.Application.Payments;
using BillingRecon.Domain.Payments;

namespace BillingRecon.IntegrationTests.Support;

/// <summary>
/// The shifts of the cash counter as a test asks for them (ADR 0060). A cash payment needs the open shift of the
/// user who records it, and a cash refund that of the user who drafted it (BR-311): a test that moves cash opens the
/// shift of its user first, through the API, as a cashier does.
/// </summary>
internal static class ShiftsApi
{
    public static readonly Uri Shifts = new("/api/v1/cashier-shifts", UriKind.Relative);

    public static Uri Shift(Guid id) => new($"/api/v1/cashier-shifts/{id}", UriKind.Relative);

    public static Uri Shift(Guid id, string action) => new($"/api/v1/cashier-shifts/{id}/{action}", UriKind.Relative);

    /// <summary>
    /// The open shift of the user of <paramref name="client"/>: the one there is, or a new one opened now with
    /// <paramref name="openingFloat"/>. Its business date is the date of the host's clock at its opening.
    /// </summary>
    public static async Task<CashierShiftDto> EnsureShiftAsync(this HttpClient client, decimal openingFloat = 0m)
    {
        using var mine = await client.GetAsync(new Uri("/api/v1/cashier-shifts?mine=true&status=Open", UriKind.Relative));
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());

        return (await mine.ReadAsAsync<PageOf<CashierShiftDto>>()).Items.SingleOrDefault() ?? await client.OpenShiftAsync(openingFloat);
    }

    /// <summary>
    /// A shift of the user of <paramref name="client"/> opened now: the open one there may be is counted to exactly its
    /// expected cash and closed first. For a scenario that moves its clock through the days.
    /// </summary>
    public static async Task<CashierShiftDto> FreshShiftAsync(this HttpClient client, decimal openingFloat = 0m)
    {
        using var mine = await client.GetAsync(new Uri("/api/v1/cashier-shifts?mine=true&status=Open", UriKind.Relative));
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync());

        if ((await mine.ReadAsAsync<PageOf<CashierShiftDto>>()).Items.SingleOrDefault() is { } open)
        {
            (await client.CloseShiftAsync(open, ExactCount(open.ExpectedCash))).Status.ShouldBe(CashierShiftStatus.Closed);
        }

        return await client.OpenShiftAsync(openingFloat);
    }

    /// <summary>Closes <paramref name="shift"/> with <paramref name="count"/>: closed (200) or waiting for approval (202).</summary>
    public static async Task<CashierShiftDto> CloseShiftAsync(this HttpClient client, CashierShiftDto shift, CloseCashierShiftRequest count)
    {
        using var response = await client.SendWithIfMatchAsync(HttpMethod.Post, Shift(shift.Id, "close"), count, PayersApi.ETag(shift.Version));

        response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Accepted], await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CashierShiftDto>();
    }

    /// <summary>The count that comes to exactly <paramref name="amount"/>: the fewest notes and coins, and the paise as loose change.</summary>
    public static CloseCashierShiftRequest ExactCount(decimal amount, string? reason = null)
    {
        amount.ShouldBeGreaterThanOrEqualTo(0m, "a drawer cannot hold less than nothing");

        var rupees = (int)decimal.Truncate(amount);
        var lines = new List<CashCountLineRequest>();
        foreach (var denomination in CashCount.Denominations)
        {
            if (rupees / denomination is > 0 and var pieces)
            {
                lines.Add(new CashCountLineRequest { Denomination = denomination, Pieces = pieces });
                rupees -= pieces * denomination;
            }
        }

        return new CloseCashierShiftRequest { Count = lines, LooseChange = amount - decimal.Truncate(amount), Reason = reason };
    }

    /// <summary>Opens a shift for the user of <paramref name="client"/>, who has none.</summary>
    public static async Task<CashierShiftDto> OpenShiftAsync(this HttpClient client, decimal openingFloat = 0m)
    {
        using var response = await client.PostJsonAsync(Shifts, new OpenCashierShiftRequest { OpeningFloat = openingFloat });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return await response.ReadAsAsync<CashierShiftDto>();
    }
}
