using BillingRecon.Domain.Common;
using BillingRecon.Domain.Receivables;

namespace BillingRecon.Domain.Tests.Receivables;

/// <summary>
/// A ledger posting (BR-401 to BR-403, BR-406; docs/database.md, sections 3.7 and 5): two or more entries whose debits
/// equal their credits, each on one side, with a subject on the three subject accounts and none on the control
/// accounts; and what a posting does to the balances of its subjects and of its payer.
/// </summary>
public sealed class LedgerPostingTests
{
    private static readonly Guid Bill = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
    private static readonly Guid Payment = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3302");
    private static readonly PayerId Payer = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3303"));
    private static readonly UserId Clerk = new(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3304"));
    private static readonly Guid Correlation = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3305");
    private static readonly DateOnly BillDate = new(2026, 11, 20);
    private static readonly DateTimeOffset At = new(2026, 11, 20, 6, 30, 0, TimeSpan.Zero);
    private static readonly LedgerSubject BillSubject = new(LedgerSubjectType.Bill, Bill);
    private static readonly LedgerSubject PaymentSubject = new(LedgerSubjectType.Payment, Payment);

    [Fact]
    public void BR401_a_bill_posting_debits_the_receivable_of_the_bill_and_credits_revenue()
    {
        var posting = Posting(
            LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(590m), BillSubject),
            LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(590m)));

        posting.Kind.ShouldBe(LedgerPostingKind.BillPosted);
        posting.DocumentType.ShouldBe(LedgerDocumentType.Bill);
        posting.DocumentId.ShouldBe(Bill);
        posting.PayerId.ShouldBe(Payer);
        posting.BusinessDate.ShouldBe(BillDate);
        posting.PostedAtUtc.ShouldBe(At);
        posting.PostedByUserId.ShouldBe(Clerk);
        posting.CorrelationId.ShouldBe(Correlation);
        posting.Description.ShouldBe("Bill BIL/26-27/000001 posted");
        posting.ReversalOfPostingId.ShouldBeNull();

        posting.Entries.Select(entry => entry.LineNo).ShouldBe([(short)1, (short)2]);
        var receivable = posting.Entries[0];
        receivable.Account.ShouldBe(LedgerAccount.Receivable);
        receivable.Debit.ShouldBe(Money.Of(590m));
        receivable.Credit.ShouldBe(Money.Zero);
        receivable.SubjectType.ShouldBe(LedgerSubjectType.Bill);
        receivable.SubjectId.ShouldBe(Bill);
        receivable.PayerId.ShouldBe(Payer);
        var revenue = posting.Entries[1];
        revenue.Account.ShouldBe(LedgerAccount.Revenue);
        revenue.Credit.ShouldBe(Money.Of(590m));
        revenue.SubjectType.ShouldBeNull("a control account carries no subject (BR-403)");
        revenue.SubjectId.ShouldBeNull();
        revenue.PayerId.ShouldBeNull();
    }

    [Fact]
    public void BR402_a_posting_and_its_entries_are_append_only_and_nothing_public_changes_them()
    {
        typeof(IAppendOnly).IsAssignableFrom(typeof(LedgerPosting)).ShouldBeTrue();
        typeof(IAppendOnly).IsAssignableFrom(typeof(LedgerEntry)).ShouldBeTrue();
        typeof(IHardDeletable).IsAssignableFrom(typeof(LedgerPosting)).ShouldBeFalse();
        typeof(IHardDeletable).IsAssignableFrom(typeof(LedgerEntry)).ShouldBeFalse();

        foreach (var type in new[] { typeof(LedgerPosting), typeof(LedgerEntry) })
        {
            type.GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
        }
    }

    [Fact]
    public void BR401_a_posting_whose_debits_differ_from_its_credits_is_refused()
    {
        var exception = Should.Throw<ArgumentException>(() => Posting(
            LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(590m), BillSubject),
            LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(589.99m))));

        exception.Message.ShouldContain("BR-401");
    }

    [Fact]
    public void BR401_a_posting_with_fewer_than_two_lines_is_refused()
    {
        Should.Throw<ArgumentException>(() => Posting(LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(590m), BillSubject)));
        Should.Throw<ArgumentException>(() => Posting());
    }

    [Fact]
    public void A_line_needs_an_amount_above_zero_on_exactly_one_side()
    {
        var zero = new LedgerLine(LedgerAccount.Revenue, Money.Zero, Money.Zero, null);
        var both = new LedgerLine(LedgerAccount.Revenue, Money.Of(10m), Money.Of(10m), null);
        var negative = new LedgerLine(LedgerAccount.Revenue, Money.Zero, Money.Of(-10m), null);

        foreach (var broken in new[] { zero, both, negative })
        {
            Should.Throw<ArgumentException>(() => Posting(LedgerLine.Dr(LedgerAccount.Cash, Money.Of(10m)), broken));
        }
    }

    [Fact]
    public void BR403_a_subject_account_needs_its_own_kind_of_subject_and_a_control_account_takes_none()
    {
        // Receivable without a subject, with a payment as its subject, and with an empty id.
        Should.Throw<ArgumentException>(() => Posting(LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(10m)), LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(10m))))
            .Message.ShouldContain("BR-403");
        Should.Throw<ArgumentException>(() => Posting(LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(10m), PaymentSubject), LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(10m))));
        Should.Throw<ArgumentException>(() => Posting(
            LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(10m), new LedgerSubject(LedgerSubjectType.Bill, Guid.Empty)),
            LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(10m))));

        // A control account with a subject.
        Should.Throw<ArgumentException>(() => Posting(LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(10m), BillSubject), LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(10m), BillSubject)));
    }

    [Theory]
    [InlineData(LedgerAccount.Receivable, LedgerSubjectType.Bill)]
    [InlineData(LedgerAccount.UnappliedCash, LedgerSubjectType.Payment)]
    [InlineData(LedgerAccount.CreditAvailable, LedgerSubjectType.CreditNote)]
    public void BR403_each_subject_account_is_kept_for_one_kind_of_document(LedgerAccount account, LedgerSubjectType subject)
    {
        LedgerPosting.SubjectTypeOf(account).ShouldBe(subject);
    }

    [Theory]
    [InlineData(LedgerAccount.Revenue)]
    [InlineData(LedgerAccount.Cash)]
    [InlineData(LedgerAccount.Adjustments)]
    public void BR403_a_control_account_has_no_subject(LedgerAccount account)
    {
        LedgerPosting.SubjectTypeOf(account).ShouldBeNull();
    }

    [Fact]
    public void A_posting_without_a_document_a_payer_a_correlation_id_or_a_description_is_a_bug_and_throws()
    {
        IReadOnlyList<LedgerLine> lines = [LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(10m), BillSubject), LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(10m))];

        Should.Throw<ArgumentException>(() => LedgerPosting.Create(LedgerPostingKind.BillPosted, LedgerDocumentType.Bill, Guid.Empty, Payer, BillDate, "Posted", lines, At, Clerk, Correlation));
        Should.Throw<ArgumentException>(() => LedgerPosting.Create(LedgerPostingKind.BillPosted, LedgerDocumentType.Bill, Bill, default, BillDate, "Posted", lines, At, Clerk, Correlation));
        Should.Throw<ArgumentException>(() => LedgerPosting.Create(LedgerPostingKind.BillPosted, LedgerDocumentType.Bill, Bill, Payer, BillDate, "Posted", lines, At, Clerk, Guid.Empty));
        Should.Throw<ArgumentException>(() => LedgerPosting.Create(LedgerPostingKind.BillPosted, LedgerDocumentType.Bill, Bill, Payer, BillDate, " ", lines, At, Clerk, Correlation));
        Should.Throw<ArgumentException>(() => LedgerPosting.Create(LedgerPostingKind.BillPosted, LedgerDocumentType.Bill, Bill, Payer, BillDate, "Posted", lines, At, Clerk, Correlation, new LedgerPostingId(Guid.Empty)));
        Should.Throw<ArgumentException>(() => LedgerPosting.Create((LedgerPostingKind)99, LedgerDocumentType.Bill, Bill, Payer, BillDate, "Posted", lines, At, Clerk, Correlation));
    }

    [Fact]
    public void BR404_a_bill_posting_raises_what_the_payer_owes_and_touches_nothing_else()
    {
        var posting = Posting(
            LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(590m), BillSubject),
            LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(590m)));

        posting.PayerChange().ShouldBe(new PayerBalanceChange(Money.Of(590m), Money.Zero, Money.Zero));
        posting.PayerChange().IsNone.ShouldBeFalse();
        posting.SubjectChanges().ShouldHaveSingleItem().ShouldBe((LedgerAccount.Receivable, BillSubject, Money.Of(590m)));
    }

    [Fact]
    public void BR404_an_allocation_lowers_the_receivable_of_the_bill_and_the_unapplied_cash_of_the_payment_by_the_same_amount()
    {
        // The paired entry of docs/database.md section 5.4: Dr UnappliedCash(payment), Cr Receivable(bill).
        var posting = LedgerPosting.Create(
            LedgerPostingKind.PaymentAllocated,
            LedgerDocumentType.PaymentAllocation,
            Guid.NewGuid(),
            Payer,
            BillDate,
            "Payment allocated",
            [
                LedgerLine.Dr(LedgerAccount.UnappliedCash, Money.Of(300m), PaymentSubject),
                LedgerLine.Cr(LedgerAccount.Receivable, Money.Of(300m), BillSubject),
            ],
            At,
            Clerk,
            Correlation);

        posting.PayerChange().ShouldBe(new PayerBalanceChange(Money.Of(-300m), Money.Of(-300m), Money.Zero));
        posting.SubjectChanges().ShouldBe(
            [(LedgerAccount.UnappliedCash, PaymentSubject, Money.Of(-300m)), (LedgerAccount.Receivable, BillSubject, Money.Of(-300m))],
            ignoreOrder: true);
    }

    [Fact]
    public void A_posting_between_control_accounts_moves_no_balance_of_the_payer()
    {
        var posting = Posting(LedgerLine.Dr(LedgerAccount.Cash, Money.Of(10m)), LedgerLine.Cr(LedgerAccount.Revenue, Money.Of(10m)));

        posting.PayerChange().IsNone.ShouldBeTrue();
        posting.SubjectChanges().ShouldBeEmpty();
    }

    [Fact]
    public void A_reversal_names_the_posting_it_reverses()
    {
        var original = new LedgerPostingId(Guid.NewGuid());

        var reversal = LedgerPosting.Create(
            LedgerPostingKind.AllocationReversed,
            LedgerDocumentType.PaymentAllocation,
            Guid.NewGuid(),
            Payer,
            BillDate,
            "Allocation reversed",
            [
                LedgerLine.Dr(LedgerAccount.Receivable, Money.Of(300m), BillSubject),
                LedgerLine.Cr(LedgerAccount.UnappliedCash, Money.Of(300m), PaymentSubject),
            ],
            At,
            postedBy: null,
            Correlation,
            original);

        reversal.ReversalOfPostingId.ShouldBe(original);
        reversal.PostedByUserId.ShouldBeNull();
    }

    [Fact]
    public void The_balances_of_a_new_payer_are_opened_at_zero()
    {
        var balance = PayerBalance.Open(Payer, At);

        balance.PayerId.ShouldBe(Payer);
        balance.Receivable.ShouldBe(Money.Zero);
        balance.UnappliedCash.ShouldBe(Money.Zero);
        balance.CreditAvailable.ShouldBe(Money.Zero);
        balance.UpdatedAtUtc.ShouldBe(At);
        Should.Throw<ArgumentException>(() => PayerBalance.Open(default, At));

        // Nothing in the application assigns an amount: only the ledger moves them, in the database (BR-404).
        typeof(PayerBalance).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
        typeof(IHasRowVersion).IsAssignableFrom(typeof(PayerBalance)).ShouldBeFalse();
    }

    private static LedgerPosting Posting(params LedgerLine[] lines) => LedgerPosting.Create(
        LedgerPostingKind.BillPosted,
        LedgerDocumentType.Bill,
        Bill,
        Payer,
        BillDate,
        " Bill BIL/26-27/000001 posted ",
        lines,
        At,
        Clerk,
        Correlation);
}
