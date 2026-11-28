using BillingRecon.Domain.Common;

namespace BillingRecon.Domain.Reconciliation;

/// <summary>
/// A match was made (BR-1006, BR-1007): its channel, kind, source, reason, how many lines and items it joins and their
/// total. The audit's action is "Made". It carries no id of the match, which is assigned when the match is added.
/// </summary>
public sealed record MatchMade(SettlementChannelId ChannelId, MatchItemType Kind, MatchSource Source, string Reason, int Lines, int Items, Money Total) : IDomainEvent;
