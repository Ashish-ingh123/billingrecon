using BillingRecon.Domain.Audit;
using BillingRecon.Domain.Billing;
using BillingRecon.Domain.Common;
using BillingRecon.Domain.Identity;
using BillingRecon.Domain.Payers;
using BillingRecon.Domain.Receivables;
using BillingRecon.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BillingRecon.IntegrationTests.Persistence;

/// <summary>
/// A column that holds the typed id of another entity is a foreign key to that entity's table, by convention of the
/// context (ADR 0021), so that no configuration has to name an entity of another module (ADR 0003). Tested on the
/// model, without a database.
/// </summary>
public sealed class TypedIdForeignKeyTests
{
    [Theory]
    [InlineData(typeof(LedgerPosting), "PayerId", typeof(Payer), "FK_LedgerPostings_PayerId")]
    [InlineData(typeof(LedgerEntry), "PayerId", typeof(Payer), "FK_LedgerEntries_PayerId")]
    [InlineData(typeof(PayerBalance), "PayerId", typeof(Payer), "FK_PayerBalances_PayerId")]
    [InlineData(typeof(LedgerPosting), "ReversalOfPostingId", typeof(LedgerPosting), "FK_LedgerPostings_ReversalOfPostingId")]
    public void A_typed_id_column_is_a_foreign_key_to_the_table_of_its_entity(Type entity, string column, Type target, string name)
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        var foreignKey = ForeignKeyOn(context, entity, column).ShouldNotBeNull($"{entity.Name}.{column} has no foreign key");

        foreignKey.PrincipalEntityType.ClrType.ShouldBe(target);
        foreignKey.GetConstraintName().ShouldBe(name);
        foreignKey.DeleteBehavior.ShouldBe(DeleteBehavior.Restrict, "no foreign key deletes anything (ADR 0015)");
        foreignKey.DependentToPrincipal.ShouldBeNull("the key has no navigation: loading a row never loads the other");
        foreignKey.PrincipalToDependent.ShouldBeNull();
    }

    [Fact]
    public void An_id_that_is_the_whole_key_gives_a_one_to_one_key_and_any_other_a_one_to_many()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        ForeignKeyOn(context, typeof(PayerBalance), "PayerId").ShouldNotBeNull().IsUnique.ShouldBeTrue("one row of balances for one payer");
        ForeignKeyOn(context, typeof(LedgerPosting), "PayerId").ShouldNotBeNull().IsUnique.ShouldBeFalse();
        ForeignKeyOn(context, typeof(LedgerPosting), "ReversalOfPostingId").ShouldNotBeNull().IsRequired.ShouldBeFalse("only a reversal names another posting");
    }

    [Fact]
    public void The_entitys_own_id_and_the_tenant_are_left_to_their_own_conventions()
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        ForeignKeyOn(context, typeof(Payer), "Id").ShouldBeNull();
        ForeignKeyOn(context, typeof(LedgerPosting), "Id").ShouldBeNull();
        ForeignKeyOn(context, typeof(LedgerPosting), "TenantId").ShouldNotBeNull().GetConstraintName().ShouldBe("FK_LedgerPostings_TenantId");
    }

    [Theory]
    [InlineData(typeof(Payer), "PatientId", "FK_Payers_PatientId")]
    [InlineData(typeof(Bill), "PatientId", "FK_Bills_PatientId")]
    [InlineData(typeof(Bill), "PayerId", "FK_Bills_PayerId")]
    [InlineData(typeof(BillLine), "ServiceItemId", "FK_BillLines_ServiceItemId")]
    [InlineData(typeof(BillLine), "BillId", "FK_BillLines_BillId")]
    [InlineData(typeof(LedgerEntry), "PostingId", "FK_LedgerEntries_PostingId")]
    public void A_key_a_configuration_declared_stays_exactly_as_written_and_gets_no_second_one(Type entity, string column, string name)
    {
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());
        var entityType = context.Model.FindEntityType(entity).ShouldNotBeNull();

        var onColumn = entityType.GetForeignKeys().Where(foreignKey => foreignKey.Properties.Any(property => property.Name == column)).ToArray();

        onColumn.ShouldHaveSingleItem().GetConstraintName().ShouldBe(name);
    }

    [Theory]
    [InlineData(typeof(Patient), "CreatedByUserId", "FK_Patients_CreatedByUserId")]
    [InlineData(typeof(Bill), "VoidedByUserId", "FK_Bills_VoidedByUserId")]
    [InlineData(typeof(AuditRecord), "UserId", "FK_AuditRecords_UserId")]
    [InlineData(typeof(IdempotencyKey), "UserId", "FK_IdempotencyKeys_UserId")]
    [InlineData(typeof(LedgerPosting), "PostedByUserId", "FK_LedgerPostings_PostedByUserId")]
    [InlineData(typeof(User), "CreatedByUserId", "FK_Users_CreatedByUserId")]
    public void A_user_column_is_a_key_to_the_users_since_they_exist(Type entity, string column, string name)
    {
        // Until Prompt 5.1 these had no key: the table of users did not exist (ADR 0021). It decided they all get one (ADR 0030).
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        var foreignKey = ForeignKeyOn(context, entity, column).ShouldNotBeNull($"{entity.Name}.{column} has no foreign key");

        foreignKey.PrincipalEntityType.ClrType.ShouldBe(typeof(User));
        foreignKey.GetConstraintName().ShouldBe(name);
        foreignKey.IsRequired.ShouldBeFalse("a row that no user wrote names nobody");
    }

    [Fact]
    public void An_untyped_id_gets_no_key()
    {
        // The subject of a ledger entry is a bill, a payment or a credit note: one column for three tables.
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        ForeignKeyOn(context, typeof(LedgerEntry), "SubjectId").ShouldBeNull();
        ForeignKeyOn(context, typeof(LedgerPosting), "DocumentId").ShouldBeNull();
        ForeignKeyOn(context, typeof(AuditRecord), "EntityId").ShouldBeNull();
    }

    [Fact]
    public void The_keys_of_the_production_model_are_the_ones_this_list_names()
    {
        // A new table that holds the typed id of another gets its key without a line of configuration. This list is
        // where that shows: a key that appears here unplanned is a schema change somebody has to mean.
        using var context = TestContexts.CreateProduction(FixedIdentity.Random());

        var names = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Select(foreignKey => foreignKey.GetConstraintName())
            .Where(name => name is not null && !name.EndsWith("_TenantId", StringComparison.Ordinal));

        names.ShouldBe(
            [
                "FK_Payers_PatientId",
                "FK_Bills_PatientId", "FK_Bills_PayerId", "FK_BillLines_BillId", "FK_BillLines_ServiceItemId",
                "FK_LedgerPostings_PayerId", "FK_LedgerPostings_ReversalOfPostingId",
                "FK_LedgerEntries_PostingId", "FK_LedgerEntries_PayerId",
                "FK_PayerBalances_PayerId",
                "FK_Payments_PayerId", "FK_Payments_PostingId",
                "FK_PaymentAllocations_PaymentId", "FK_PaymentAllocations_BillId", "FK_PaymentAllocations_PostingId",
                "FK_CreditNotes_BillId", "FK_CreditNotes_PayerId", "FK_CreditNotes_PatientId",
                "FK_CreditNoteLines_CreditNoteId", "FK_CreditNoteLines_BillLineId",
                "FK_CreditNoteAllocations_CreditNoteId", "FK_CreditNoteAllocations_BillId", "FK_CreditNoteAllocations_PostingId",
                "FK_Adjustments_BillId", "FK_Adjustments_PayerId", "FK_Adjustments_PostingId",
                "FK_Payments_ReversalPostingId", "FK_PaymentAllocations_ReversalPostingId",
                "FK_CreditNoteAllocations_ReversalPostingId",
                "FK_Refunds_PayerId", "FK_Refunds_SourcePaymentId", "FK_Refunds_SourceCreditNoteId", "FK_Refunds_PostingId",

                // Since Prompt 5.1 (ADR 0030): every column that names a user, and the roles of a user.
                "FK_Patients_CreatedByUserId", "FK_Patients_UpdatedByUserId",
                "FK_Payers_CreatedByUserId", "FK_Payers_UpdatedByUserId",
                "FK_ServiceItems_CreatedByUserId", "FK_ServiceItems_UpdatedByUserId",
                "FK_Bills_CreatedByUserId", "FK_Bills_UpdatedByUserId", "FK_Bills_PostedByUserId", "FK_Bills_VoidedByUserId",
                "FK_CreditNotes_CreatedByUserId", "FK_CreditNotes_UpdatedByUserId", "FK_CreditNotes_PostedByUserId", "FK_CreditNotes_VoidedByUserId",
                "FK_CreditNoteAllocations_AllocatedByUserId", "FK_CreditNoteAllocations_ReversedByUserId",
                "FK_Adjustments_CreatedByUserId", "FK_Adjustments_UpdatedByUserId", "FK_Adjustments_PostedByUserId", "FK_Adjustments_VoidedByUserId",
                "FK_Payments_CreatedByUserId", "FK_Payments_UpdatedByUserId", "FK_Payments_ReversedByUserId",
                "FK_PaymentAllocations_AllocatedByUserId", "FK_PaymentAllocations_ReversedByUserId",
                "FK_Refunds_CreatedByUserId", "FK_Refunds_UpdatedByUserId", "FK_Refunds_PostedByUserId", "FK_Refunds_VoidedByUserId",
                "FK_LedgerPostings_PostedByUserId",
                "FK_ApprovalRequests_MakerUserId", "FK_ApprovalRequests_CheckerUserId",
                "FK_AuditRecords_UserId", "FK_IdempotencyKeys_UserId",
                "FK_Users_CreatedByUserId", "FK_Users_UpdatedByUserId",
                "FK_UserRoles_UserId", "FK_UserRoles_GrantedByUserId", "FK_UserRoles_RoleCode",
                "FK_PrefixChanges_CreatedByUserId", "FK_PrefixChanges_UpdatedByUserId",
                "FK_OpeningBatches_CreatedByUserId", "FK_OpeningBatches_UpdatedByUserId",
                "FK_DocumentPrints_PrintedByUserId",
                "FK_PrintSettings_CreatedByUserId", "FK_PrintSettings_UpdatedByUserId",
            ],
            ignoreOrder: true);
    }

    private static IForeignKey? ForeignKeyOn(DbContext context, Type entity, string column)
        => context.Model.FindEntityType(entity).ShouldNotBeNull()
            .GetForeignKeys()
            .SingleOrDefault(foreignKey => foreignKey.Properties.Count == 1 && foreignKey.Properties[0].Name == column);
}
