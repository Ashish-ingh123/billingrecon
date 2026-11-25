namespace BillingRecon.Application.Common;

/// <summary>One row of a <see cref="CsvTable"/>: the line of the file it started on, counted from 1, and its fields as written.</summary>
public sealed record CsvRow(int Line, IReadOnlyList<string> Fields);
