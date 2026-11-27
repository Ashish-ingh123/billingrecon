namespace BillingRecon.Application.Reporting;

/// <summary>
/// The day-end report of one business date (US-48; BR-903; ADR 0060): every shift of that date, the cash that moved
/// outside any shift, and their sum.
/// </summary>
/// <param name="Date">The business date in India.</param>
/// <param name="Shifts">The shifts of the date, in the order they were opened.</param>
/// <param name="ShiftsTotal">The cash of the shifts together.</param>
/// <param name="OutsideShifts">
/// The cash of the date that no shift holds: a cash payment reversed after its shift was closed, and cash recorded
/// before shifts existed.
/// </param>
/// <param name="Cash">
/// The two together. Its <c>Net</c> equals the net of the Cash row of the daily collection of the date, always.
/// </param>
/// <param name="Difference">The differences of the counted shifts together: above zero an excess, below zero a shortage.</param>
/// <param name="UnclosedShifts">How many shifts of the date are still open or wait for approval.</param>
public sealed record DayEndDto(
    DateOnly Date,
    IReadOnlyList<DayEndShiftDto> Shifts,
    DayEndCashDto ShiftsTotal,
    DayEndCashDto OutsideShifts,
    DayEndCashDto Cash,
    decimal Difference,
    int UnclosedShifts);
