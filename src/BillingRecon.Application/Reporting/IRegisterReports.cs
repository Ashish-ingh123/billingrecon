namespace BillingRecon.Application.Reporting;

/// <summary>The bill and payment registers (BR-901), read without tracking and filtered by the current tenant.</summary>
public interface IRegisterReports
{
    /// <summary>One page of the posted bills of <paramref name="period"/> that match <paramref name="query"/>, with the totals of all of them.</summary>
    public Task<ReportPage<BillRegisterRowDto, BillRegisterTotalsDto>> BillRegisterAsync(ReportPeriodDto period, BillRegisterQuery query, CancellationToken ct);

    /// <summary>One page of the payments of <paramref name="period"/> that match <paramref name="query"/>, with the totals of all of them.</summary>
    public Task<ReportPage<PaymentRegisterRowDto, PaymentRegisterTotalsDto>> PaymentRegisterAsync(ReportPeriodDto period, PaymentRegisterQuery query, CancellationToken ct);
}
