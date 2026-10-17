# Database

- **Status:** R1 schema designed 2026-09-29. Implemented incrementally by EF Core migrations in
  `src/BillingRecon.Infrastructure`; this document is the target the migrations converge on.
- **Engine:** SQL Server 2022 (Docker locally, Testcontainers in tests). One database, one schema
  (`dbo`), one application login.
- **Rules cited:** BR-nnn from `requirements.md`; ADRs 0003–0010.

---

## 1. Conventions

| Concern | Convention |
|---|---|
| Names | Tables plural PascalCase (`Bills`, `BillLines`). Columns PascalCase. `PK_<Table>`, `FK_<Table>_<Column>`, `IX_<Table>_<Cols>`, `UX_` unique, `CK_<Table>_<Rule>`. |
| Primary keys | Aggregates and documents: `Id uniqueidentifier`, generated client-side by EF's sequential GUID generator, clustered. Append-only tables (`LedgerEntries`, `AuditRecords`): `Id bigint IDENTITY`, clustered, so insertion order is the physical order. Junction and cache tables: composite or borrowed keys. |
| Tenancy | Every tenant-scoped table has `TenantId uniqueidentifier NOT NULL` with an FK to `Tenants`. It is the **first column of every non-clustered index** and every unique index. EF applies a global query filter `TenantId == current`, a save interceptor sets it on inserts and rejects mismatches, and `TenantId` is part of the WHERE clause of every UPDATE and DELETE (BR-002, ADR 0009, ADR 0015). |
| Money | `decimal(18,2)` for every amount (BR-001). `decimal(19,4)` unit prices, `decimal(9,3)` quantities, `decimal(5,2)` percentages. EF configures precision and scale explicitly on every property; a decimal without them fails the EF model, which stops the API at startup, the tests and the EF tools (ADR 0015). |
| Instants | `datetimeoffset(3)`, always stored with offset `+00:00`; the `…AtUtc` suffix says so. Read from `IClock.UtcNow` only (BR-003, ADR 0010). |
| Business dates | `date`, IST calendar dates; suffix `…Date` (BR-003). Fiscal year as `char(7)` label `2026-27` (BR-004). |
| Enums and codes | `varchar(n)` with a `CHECK (col IN (...))`. Readable in SQL and in exports; EF maps enums as strings. For an enum the constraint `CK_<Table>_<Column>` is generated from the enum's members, so adding a value is a migration that replaces the CHECK (ADR 0015). |
| Text | `nvarchar(n)` for user-entered text; `varchar(n)` for ASCII codes and references. A text column without a length fails the EF model, like an unsized decimal. No `nvarchar(max)` except JSON and response bodies, which are marked `IsUnbounded()` in their configuration (ADR 0015). |
| Concurrency | `RowVersion rowversion NOT NULL` on every mutable aggregate root, mapped as an EF concurrency token (BR-804, ADR 0007). Child rows (`BillLines`) and caches updated set-based (`PayerBalances`) carry none. |
| Deletes | No `ON DELETE CASCADE` anywhere. The application login has no `DELETE` permission on any table except `IdempotencyKeys` (BR-005, §7). Reference data is deactivated by `Status`/`IsActive`. |
| Append-only | `LedgerPostings`, `LedgerEntries`, `AuditRecords`: `INSERT`/`SELECT` only for the application login (BR-402, BR-702). |
| Nulls | Nullable only where "absent" is meaningful (unposted number, optional reference). Every other column `NOT NULL`. |
| Defaults | Only for `RowVersion`, `bit` flags, zero balances and identity columns. Application code sets everything else explicitly: where a table below says "initially", the value is set by the aggregate's factory method, not by the database. |
| Audit columns | **Stamp** (who created the row, when) on every table that has them, **Touch** (who changed it last, when) on mutable tables only; append-only tables have Stamp only. Both are filled by a save interceptor, never by application code; the user is null for system work (seeding, migrations, background jobs). They are not the audit trail, which is `AuditRecords` (ADR 0015). |

---

## 2. Entity–relationship diagram

```mermaid
erDiagram
    Tenants ||--o{ Users : has
    Users }o--o{ Roles : "UserRoles"
    Tenants ||--o{ Patients : registers
    Tenants ||--o{ Payers : has
    Patients |o--o| Payers : "is self-pay payer"
    Payers ||--|| PayerBalances : "cached in"
    Patients ||--o{ Bills : "billed for"
    Payers ||--o{ Bills : owes
    Bills ||--|{ BillLines : contains
    ServiceItems |o--o{ BillLines : prices
    Bills ||--o{ CreditNotes : "credited by"
    CreditNotes ||--|{ CreditNoteLines : contains
    BillLines |o--o{ CreditNoteLines : references
    CreditNotes ||--o{ CreditNoteAllocations : applies
    Bills ||--o{ CreditNoteAllocations : receives
    Payers ||--o{ Payments : pays
    Payments ||--o{ PaymentAllocations : applies
    Bills ||--o{ PaymentAllocations : receives
    Bills ||--o{ Adjustments : "adjusted by"
    Payments |o--o{ Refunds : "refunded from"
    CreditNotes |o--o{ Refunds : "refunded from"
    Users ||--o{ ApprovalRequests : "maker / checker"
    Payers ||--o{ LedgerPostings : "posted for"
    LedgerPostings ||--|{ LedgerEntries : contains
    LedgerPostings |o--o{ LedgerPostings : reverses
    Tenants ||--o{ NumberSeries : numbers
    Tenants ||--o{ IdempotencyKeys : scopes
    Tenants ||--o{ AuditRecords : records

    Bills {
        uniqueidentifier Id PK
        varchar Number "null until posted"
        varchar Status
        decimal Total
        decimal BalanceDue "cache of Receivable(bill)"
        rowversion RowVersion
    }
    Payments {
        uniqueidentifier Id PK
        varchar Number
        decimal Amount
        decimal UnappliedAmount "cache of UnappliedCash(payment)"
        varchar Status
    }
    CreditNotes {
        uniqueidentifier Id PK
        varchar Number "null until posted"
        decimal Total
        decimal AvailableAmount "cache of CreditAvailable(cn)"
    }
    LedgerEntries {
        bigint Id PK
        varchar Account
        varchar SubjectType "Bill | Payment | CreditNote"
        uniqueidentifier SubjectId
        decimal Debit
        decimal Credit
    }
    PayerBalances {
        uniqueidentifier PayerId PK
        decimal Receivable
        decimal UnappliedCash
        decimal CreditAvailable
        decimal NetOutstanding "computed, persisted"
    }
```

---

## 3. Tables

Column tables list `Type`, `Null` (`•` = nullable) and notes. Common columns are abbreviated:

- **T** = `TenantId uniqueidentifier NOT NULL`, FK `Tenants(Id)`.
- **Stamp** = `CreatedAtUtc datetimeoffset(3) NOT NULL`, `CreatedByUserId uniqueidentifier •` FK `Users(Id)`; null when no user did it (ADR 0015).
- **Touch** = `UpdatedAtUtc datetimeoffset(3) NOT NULL`, `UpdatedByUserId uniqueidentifier •` FK `Users(Id)`; equal to Stamp on a new row. Every mutable table that has Stamp also has Touch, added in the migration that creates the table, even where the table below does not list it yet.
- **RV** = `RowVersion rowversion NOT NULL`.
- **Posted** = `PostedAtUtc datetimeoffset(3) •`, `PostedByUserId uniqueidentifier •` FK `Users(Id)`.
- **Voided** = `VoidedAtUtc datetimeoffset(3) •`, `VoidedByUserId uniqueidentifier •`, `VoidReason nvarchar(500) •`.
- **Doc#** = `Number varchar(30) •`, `FiscalYear char(7) •`, `SequenceNumber int •` — all three null until posted, all three set at posting; `Number = Prefix + '/' + <short fiscal year, e.g. 26-27> + '/' + RIGHT('000000' + SequenceNumber, 6)`, at most 16 characters (ADR 0008).

### 3.1 Identity

**Tenants** — the row *is* the tenant: a tenant reads, updates and deletes its own row only, system work (no
tenant in scope) sees every row (ADR 0015 §3). Settings per BR-607. Migration `0001_Tenants`.
No Stamp or Touch: a tenant is created by platform onboarding before any of its users exists, so the
table has `CreatedAtUtc` only, set by `Tenant.Create`; changes to its settings are in `AuditRecords`.

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| Code | varchar(20) | | `UX_Tenants_Code`; slug for logs and URLs: lower-case letters, digits and single hyphens (checked by `Tenant.Create`) |
| Name | nvarchar(200) | | |
| Status | varchar(20) | | `CK_Tenants_Status`: `Active`, `Suspended` |
| PostingApprovalThreshold | decimal(18,2) | • | null = bills never need approval (BR-207) |
| MaxBackdateDays | int | | initially 7; `CK_Tenants_MaxBackdateDays` 0–365 (BR-205) |
| DefaultDueDays | int | | initially 0; `CK_Tenants_DefaultDueDays` 0–365 (BR-205) |
| BillPrefix, CreditNotePrefix, PaymentPrefix, AdjustmentPrefix, RefundPrefix | varchar(3) | | initially `BIL`, `CN`, `PAY`, `ADJ`, `REF`; one CHECK per column (`CK_Tenants_BillPrefix`, …): 1–3 characters `[A-Z0-9]`, compared with a binary collation so that lower-case letters are refused (BR-802, ADR 0008) |
| CreatedAtUtc | datetimeoffset(3) | | |
| RowVersion | rowversion | | |

**Users**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Email | nvarchar(256) | | as entered |
| NormalizedEmail | nvarchar(256) | | upper-cased; `UX_Users_TenantId_NormalizedEmail` (BR-601) |
| DisplayName | nvarchar(200) | | |
| PasswordHash | nvarchar(500) | | PBKDF2 via the static `Rfc2898DeriveBytes.Pbkdf2` (BCL; the class's constructors are obsolete from .NET 10); format `alg$iterations$salt$hash` |
| Status | varchar(20) | | CK `Active`, `Disabled` |
| CreatedAtUtc | datetimeoffset(3) | | |
| LastSignInAtUtc | datetimeoffset(3) | • | |
| RowVersion | rowversion | | |

**Roles** — global, seeded by migration: `TenantAdmin`, `Supervisor`, `BillingClerk`, `Cashier`, `Viewer`.

| Column | Type | Null | Notes |
|---|---|---|---|
| Code | varchar(30) | | PK |
| Name | nvarchar(100) | | |

**UserRoles**

| Column | Type | Null | Notes |
|---|---|---|---|
| UserId | uniqueidentifier | | PK part, FK `Users` |
| RoleCode | varchar(30) | | PK part, FK `Roles` |
| T | | | denormalised so the tenant filter applies |
| GrantedAtUtc | datetimeoffset(3) | | |
| GrantedByUserId | uniqueidentifier | | FK `Users` |

### 3.2 Payers

**Patients**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Mrn | nvarchar(30) | | `UX_Patients_TenantId_Mrn` (BR-101) |
| FullName | nvarchar(200) | | `IX_Patients_TenantId_FullName` |
| DateOfBirth | date | • | |
| Sex | varchar(10) | • | CK `Male`, `Female`, `Other`, `Unknown` |
| Phone | nvarchar(20) | • | |
| Email | nvarchar(256) | • | |
| AddressLine1, AddressLine2 | nvarchar(200) | • | |
| City, State | nvarchar(100) | • | |
| PinCode | varchar(10) | • | |
| Status | varchar(20) | | CK `Active`, `Inactive` (BR-102) |
| Stamp, RV | | | |

**Payers**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Kind | varchar(20) | | CK `SelfPay` in R1; R4 migration adds `Insurer`, `Corporate` (BR-103) |
| Name | nvarchar(200) | | `IX_Payers_TenantId_Name` |
| PatientId | uniqueidentifier | • | FK `Patients`; `UX_Payers_TenantId_PatientId WHERE PatientId IS NOT NULL` (BR-104) |
| Phone | nvarchar(20) | • | |
| Email | nvarchar(256) | • | |
| Status | varchar(20) | | CK `Active`, `Inactive` (BR-105) |
| Stamp, RV | | | |

### 3.3 Billing

**ServiceItems** — chargemaster, reference data.

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Code | varchar(30) | | `UX_ServiceItems_TenantId_Code` |
| Name | nvarchar(200) | | |
| Category | nvarchar(50) | • | |
| DefaultUnitPrice | decimal(19,4) | | CK ≥ 0 |
| TaxRatePercent | decimal(5,2) | | CK 0–100 |
| IsActive | bit | | default 1 |
| Stamp, RV | | | |

**Bills**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Doc# | | • | `UX_Bills_TenantId_Number WHERE Number IS NOT NULL`; `UX_Bills_TenantId_FiscalYear_SequenceNumber WHERE SequenceNumber IS NOT NULL` (BR-801, gapless check in §4.3) |
| PatientId | uniqueidentifier | | FK `Patients`; `IX_Bills_TenantId_PatientId` |
| PayerId | uniqueidentifier | | FK `Payers`; `IX_Bills_TenantId_PayerId_Status INCLUDE (BalanceDue, Total)` |
| BillDate | date | | `IX_Bills_TenantId_BillDate` |
| DueDate | date | | CK `DueDate >= BillDate` (BR-205) |
| EncounterReference | nvarchar(50) | • | HIS visit / admission id, free text |
| Notes | nvarchar(1000) | • | |
| Status | varchar(20) | | CK `Draft`, `PendingApproval`, `Posted`, `PartiallyPaid`, `Settled`, `Voided`; `IX_Bills_TenantId_Status WHERE Status = 'PendingApproval'` |
| SubTotal, DiscountTotal, TaxTotal, Total | decimal(18,2) | | CK each ≥ 0; CK `Total = SubTotal - DiscountTotal + TaxTotal` (BR-203) |
| BalanceDue | decimal(18,2) | | CK `BalanceDue BETWEEN 0 AND Total` (BR-405). Drafts carry `BalanceDue = Total`. Cache of `Receivable(bill)` (§5.5). |
| UpdatedAtUtc | datetimeoffset(3) | | set on every command, so `RowVersion` changes even when only a line changed (ADR 0007) |
| Stamp, Posted, Voided, RV | | | |

Additional checks:

- `CK_Bills_NumberIffPosted`: `(Status IN ('Draft','PendingApproval','Voided') AND Number IS NULL AND PostedAtUtc IS NULL) OR (Status IN ('Posted','PartiallyPaid','Settled') AND Number IS NOT NULL AND PostedAtUtc IS NOT NULL)` (BR-206).
- `CK_Bills_StatusMatchesBalance`: `(Status <> 'Posted' OR (BalanceDue = Total AND Total > 0)) AND (Status <> 'PartiallyPaid' OR (BalanceDue > 0 AND BalanceDue < Total)) AND (Status <> 'Settled' OR BalanceDue = 0)` (BR-208).
- `CK_Bills_VoidedIffVoided`: `(Status = 'Voided') = (VoidedAtUtc IS NOT NULL)`.

**BillLines** — owned by `Bills`; no rowversion (the bill's covers it).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| BillId | uniqueidentifier | | FK `Bills` |
| LineNo | int | | `UX_BillLines_BillId_LineNo` |
| ServiceItemId | uniqueidentifier | • | FK `ServiceItems` |
| Description | nvarchar(300) | | |
| Quantity | decimal(9,3) | | CK > 0 (BR-201) |
| UnitPrice | decimal(19,4) | | CK ≥ 0 |
| LineAmount | decimal(18,2) | | CK ≥ 0; = round(Quantity × UnitPrice) in code (BR-202) |
| DiscountAmount | decimal(18,2) | | CK `DiscountAmount BETWEEN 0 AND LineAmount` |
| TaxRatePercent | decimal(5,2) | | CK 0–100 |
| TaxAmount | decimal(18,2) | | CK ≥ 0 |
| LineTotal | decimal(18,2) | | CK `LineTotal = LineAmount - DiscountAmount + TaxAmount` |

The multiplication and rounding (`LineAmount`, `TaxAmount`) are not CHECKed: SQL Server's `ROUND`
semantics differ from `MidpointRounding.AwayFromZero` in edge cases and the domain is the single
implementation of BR-202. The additive identities are CHECKed.

**CreditNotes**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Doc# | | • | `UX_CreditNotes_TenantId_Number` and `UX_CreditNotes_TenantId_FiscalYear_SequenceNumber`, both filtered |
| BillId | uniqueidentifier | | FK `Bills`; `IX_CreditNotes_TenantId_BillId_Status INCLUDE (Total)` (BR-210 cap query) |
| PayerId | uniqueidentifier | | FK `Payers`; must equal the bill's payer (checked in code); `IX_CreditNotes_TenantId_PayerId_Status INCLUDE (AvailableAmount)` |
| PatientId | uniqueidentifier | | FK `Patients` |
| CreditNoteDate | date | | CK against bill date is in code (BR-214) |
| Reason | nvarchar(500) | | |
| Status | varchar(20) | | CK `Draft`, `PendingApproval`, `Posted`, `Voided` |
| SubTotal, DiscountTotal, TaxTotal, Total | decimal(18,2) | | same CKs as Bills |
| AvailableAmount | decimal(18,2) | | CK `AvailableAmount BETWEEN 0 AND Total` (BR-213). Cache of `CreditAvailable(creditNote)`. Drafts: `= Total`. |
| UpdatedAtUtc | datetimeoffset(3) | | as for Bills (ADR 0007) |
| Stamp, Posted, Voided, RV | | | `CK_CreditNotes_NumberIffPosted` as for Bills with `Status = 'Posted'` |

**CreditNoteLines** — as `BillLines` with `CreditNoteId` FK `CreditNotes`, `UX_CreditNoteLines_CreditNoteId_LineNo`, plus:

| Column | Type | Null | Notes |
|---|---|---|---|
| BillLineId | uniqueidentifier | • | FK `BillLines`; `IX_CreditNoteLines_TenantId_BillLineId INCLUDE (LineTotal)` for the BR-211 cap |

**CreditNoteAllocations** — application of a credit note's available credit to a bill (BR-212, BR-213).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| CreditNoteId | uniqueidentifier | | FK `CreditNotes`; `IX_CreditNoteAllocations_TenantId_CreditNoteId` |
| BillId | uniqueidentifier | | FK `Bills`; `IX_CreditNoteAllocations_TenantId_BillId INCLUDE (Amount, Status)` |
| Amount | decimal(18,2) | | CK > 0 |
| Status | varchar(20) | | CK `Active`, `PendingReversal`, `Reversed` |
| AllocatedAtUtc | datetimeoffset(3) | | |
| AllocatedByUserId | uniqueidentifier | | FK `Users` |
| PostingId | uniqueidentifier | | FK `LedgerPostings`: the paired entry |
| ReversedAtUtc | datetimeoffset(3) | • | CK `(Status = 'Reversed') = (ReversedAtUtc IS NOT NULL)` |
| ReversedByUserId | uniqueidentifier | • | the checker |
| ReversalReason | nvarchar(500) | • | |
| ReversalPostingId | uniqueidentifier | • | FK `LedgerPostings`: the mirror entry |

### 3.4 Payments

**Payments** — posted at creation, so `Number`, `FiscalYear`, `SequenceNumber` are `NOT NULL` (BR-302).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Number | varchar(30) | | `UX_Payments_TenantId_Number` |
| FiscalYear | char(7) | | |
| SequenceNumber | int | | `UX_Payments_TenantId_FiscalYear_SequenceNumber` |
| PayerId | uniqueidentifier | | FK `Payers`; `IX_Payments_TenantId_PayerId_Status INCLUDE (UnappliedAmount)` |
| PaymentDate | date | | `IX_Payments_TenantId_PaymentDate INCLUDE (Method, Amount, Status)` (daily collection, BR-901) |
| Method | varchar(20) | | CK `Cash`, `Card`, `UPI`, `BankTransfer`, `Cheque` (BR-301) |
| Reference | nvarchar(100) | • | CK `Method = 'Cash' OR Reference IS NOT NULL` (BR-301) |
| Amount | decimal(18,2) | | CK > 0 |
| UnappliedAmount | decimal(18,2) | | CK `UnappliedAmount BETWEEN 0 AND Amount` (BR-405). Cache of `UnappliedCash(payment)`. |
| Status | varchar(20) | | CK `Posted`, `PendingReversal`, `Reversed`; CK `Status <> 'Reversed' OR UnappliedAmount = 0` (BR-306) |
| Notes | nvarchar(500) | • | |
| PostingId | uniqueidentifier | | FK `LedgerPostings` |
| ReversedAtUtc | datetimeoffset(3) | • | CK `(Status = 'Reversed') = (ReversedAtUtc IS NOT NULL)` |
| ReversedByUserId | uniqueidentifier | • | the checker |
| ReversalReason | nvarchar(500) | • | |
| ReversalPostingId | uniqueidentifier | • | FK `LedgerPostings` |
| Stamp, RV | | | `CreatedAtUtc` is the posting instant |

**PaymentAllocations** — same shape as `CreditNoteAllocations` with `PaymentId` FK `Payments`
(`IX_PaymentAllocations_TenantId_PaymentId`, `IX_PaymentAllocations_TenantId_BillId INCLUDE (Amount, Status)`).
The rule "payment and bill share a payer" (BR-303) is cross-table and enforced in code; the
reconciliation query in §5.6 also detects a violation.

**Refunds**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Doc# | | • | filtered unique indexes as for Bills |
| PayerId | uniqueidentifier | | FK `Payers`; `IX_Refunds_TenantId_PayerId` |
| SourceKind | varchar(20) | | CK `Payment`, `CreditNote` (BR-307) |
| SourcePaymentId | uniqueidentifier | • | FK `Payments`; `IX_Refunds_TenantId_SourcePaymentId INCLUDE (Amount, Status)` |
| SourceCreditNoteId | uniqueidentifier | • | FK `CreditNotes`; `IX_Refunds_TenantId_SourceCreditNoteId INCLUDE (Amount, Status)` |
| RefundDate | date | | |
| Amount | decimal(18,2) | | CK > 0 |
| Method | varchar(20) | | CK as Payments |
| Reference | nvarchar(100) | • | CK `Method = 'Cash' OR Reference IS NOT NULL` |
| Reason | nvarchar(500) | | |
| Status | varchar(20) | | CK `Draft`, `PendingApproval`, `Posted`, `Voided` |
| PostingId | uniqueidentifier | • | FK `LedgerPostings`; set at posting |
| Stamp, Posted, Voided, RV | | | |

`CK_Refunds_OneSource`: `(SourceKind = 'Payment' AND SourcePaymentId IS NOT NULL AND SourceCreditNoteId IS NULL) OR (SourceKind = 'CreditNote' AND SourceCreditNoteId IS NOT NULL AND SourcePaymentId IS NULL)`.

### 3.5 Adjustments

**Adjustments**

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| Doc# | | • | filtered unique indexes as for Bills |
| BillId | uniqueidentifier | | FK `Bills`; `IX_Adjustments_TenantId_BillId_Status INCLUDE (Amount)` |
| PayerId | uniqueidentifier | | FK `Payers` |
| AdjustmentDate | date | | |
| Kind | varchar(20) | | CK `Discount`, `WriteOff`, `Rounding` (BR-501) |
| Amount | decimal(18,2) | | CK `Amount > 0 AND (Kind <> 'Rounding' OR Amount < 1.00)` |
| Reason | nvarchar(500) | | NOT NULL (BR-502) |
| Status | varchar(20) | | CK `Draft`, `PendingApproval`, `Posted`, `Voided` |
| PostingId | uniqueidentifier | • | FK `LedgerPostings` |
| Stamp, Posted, Voided, RV | | | |

### 3.6 Common: approvals, number series, idempotency

**ApprovalRequests** (BR-603 – BR-606)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| DocumentType | varchar(30) | | CK `Bill`, `CreditNote`, `Adjustment`, `Refund`, `Payment`, `PaymentAllocation`, `CreditNoteAllocation`, `NumberSeries` |
| DocumentId | uniqueidentifier | | no FK (polymorphic); `UX_ApprovalRequests_TenantId_DocumentType_DocumentId WHERE Status = 'Pending'` (BR-605) |
| Action | varchar(30) | | CK `Post`, `Reverse`, `ChangePrefix` |
| MakerUserId | uniqueidentifier | | FK `Users` |
| MakerComment | nvarchar(500) | • | |
| CheckerUserId | uniqueidentifier | • | FK `Users`; CK `CheckerUserId IS NULL OR CheckerUserId <> MakerUserId` (BR-603) |
| CheckerComment | nvarchar(500) | • | |
| Status | varchar(20) | | CK `Pending`, `Approved`, `Rejected`; CK `(Status = 'Pending') = (CheckerUserId IS NULL AND DecidedAtUtc IS NULL)`; `IX_ApprovalRequests_TenantId_Status_RequestedAtUtc` |
| RequestedAtUtc | datetimeoffset(3) | | |
| DecidedAtUtc | datetimeoffset(3) | • | |
| CorrelationId | uniqueidentifier | | of the request that opened it |
| RowVersion | rowversion | | |

**NumberSeries** (BR-801, BR-802, ADR 0008)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK |
| T | | | |
| DocumentType | varchar(20) | | CK `Bill`, `CreditNote`, `Payment`, `Adjustment`, `Refund` |
| FiscalYear | char(7) | | `UX_NumberSeries_TenantId_DocumentType_FiscalYear` — the row that is locked |
| Prefix | varchar(3) | | CK 1–3 characters `[A-Z0-9]` (ADR 0008) |
| NextNumber | int | | CK ≥ 1; never decreased |
| CreatedAtUtc, UpdatedAtUtc | datetimeoffset(3) | | |
| RowVersion | rowversion | | for prefix edits only; the number path uses the lock in §4 |

**IdempotencyKeys** (BR-803, ADR 0006) — clustered on the lookup key; the only table the application login may `DELETE` from (expiry sweep).

| Column | Type | Null | Notes |
|---|---|---|---|
| TenantId | uniqueidentifier | | PK part |
| Key | uniqueidentifier | | PK part |
| UserId | uniqueidentifier | | |
| Method | varchar(8) | | |
| Path | nvarchar(400) | | |
| RequestHash | binary(32) | | SHA-256 of method + path + body bytes |
| Status | varchar(12) | | CK `InProgress`, `Completed` |
| StatusCode | int | • | set at completion |
| ResponseContentType | varchar(100) | • | |
| ResponseHeaders | nvarchar(1000) | • | JSON: `Location`, `ETag` |
| ResponseBody | nvarchar(max) | • | |
| CreatedAtUtc | datetimeoffset(3) | | |
| CompletedAtUtc | datetimeoffset(3) | • | |
| ExpiresAtUtc | datetimeoffset(3) | | `IX_IdempotencyKeys_ExpiresAtUtc`; 30 days |

### 3.7 Receivables

**LedgerPostings** — one row per financial event; append-only (BR-401, BR-402).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | uniqueidentifier | | PK; generated by the application so document rows can reference it in the same transaction |
| T | | | |
| PostedAtUtc | datetimeoffset(3) | | |
| BusinessDate | date | | the document's date (BR-406); `IX_LedgerPostings_TenantId_BusinessDate` |
| Kind | varchar(30) | | CK `BillPosted`, `PaymentRecorded`, `PaymentAllocated`, `AllocationReversed`, `PaymentReversed`, `CreditNotePosted`, `CreditApplied`, `CreditApplicationReversed`, `AdjustmentPosted`, `RefundPosted` |
| DocumentType | varchar(30) | | CK `Bill`, `Payment`, `PaymentAllocation`, `CreditNote`, `CreditNoteAllocation`, `Adjustment`, `Refund` |
| DocumentId | uniqueidentifier | | `IX_LedgerPostings_TenantId_DocumentType_DocumentId` |
| PayerId | uniqueidentifier | | FK `Payers`; `IX_LedgerPostings_TenantId_PayerId` |
| ReversalOfPostingId | uniqueidentifier | • | FK `LedgerPostings`; set on the four `…Reversed` kinds |
| Description | nvarchar(300) | | e.g. `Bill BIL/26-27/000123 posted` |
| PostedByUserId | uniqueidentifier | | FK `Users` |
| CorrelationId | uniqueidentifier | | |

**LedgerEntries** — the lines; append-only.

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | bigint IDENTITY | | PK clustered |
| T | | | |
| PostingId | uniqueidentifier | | FK `LedgerPostings` |
| LineNo | smallint | | `UX_LedgerEntries_PostingId_LineNo` |
| Account | varchar(20) | | CK `Receivable`, `UnappliedCash`, `CreditAvailable`, `Revenue`, `Cash`, `Adjustments` (BR-403) |
| SubjectType | varchar(20) | • | CK `Bill`, `Payment`, `CreditNote` |
| SubjectId | uniqueidentifier | • | `IX_LedgerEntries_TenantId_SubjectType_SubjectId INCLUDE (Account, Debit, Credit)` |
| PayerId | uniqueidentifier | • | FK `Payers`; `IX_LedgerEntries_TenantId_PayerId_Account INCLUDE (Debit, Credit)` |
| Debit | decimal(18,2) | | default 0; CK ≥ 0 |
| Credit | decimal(18,2) | | default 0; CK ≥ 0 |

Checks:

- `CK_LedgerEntries_OneSide`: `(Debit > 0 AND Credit = 0) OR (Debit = 0 AND Credit > 0)`.
- `CK_LedgerEntries_Subject`:
  `(Account = 'Receivable' AND SubjectType = 'Bill' AND SubjectId IS NOT NULL AND PayerId IS NOT NULL)
  OR (Account = 'UnappliedCash' AND SubjectType = 'Payment' AND SubjectId IS NOT NULL AND PayerId IS NOT NULL)
  OR (Account = 'CreditAvailable' AND SubjectType = 'CreditNote' AND SubjectId IS NOT NULL AND PayerId IS NOT NULL)
  OR (Account IN ('Revenue','Cash','Adjustments') AND SubjectType IS NULL AND SubjectId IS NULL AND PayerId IS NULL)` (BR-403).

"Debits equal credits per posting" spans rows and is not a CHECK. It is asserted by
`IReceivablesLedger` before the insert and by the reconciliation query in §5.6; there are no triggers.

**PayerBalances** — cache row per payer, created with the payer (BR-404).

| Column | Type | Null | Notes |
|---|---|---|---|
| PayerId | uniqueidentifier | | PK, FK `Payers` |
| T | | | |
| Receivable | decimal(18,2) | | default 0; CK ≥ 0 — Σ `BalanceDue` of the payer's bills |
| UnappliedCash | decimal(18,2) | | default 0; CK ≥ 0 — Σ `UnappliedAmount` of the payer's payments |
| CreditAvailable | decimal(18,2) | | default 0; CK ≥ 0 — Σ `AvailableAmount` of the payer's credit notes |
| NetOutstanding | AS (Receivable - UnappliedCash - CreditAvailable) PERSISTED | | what the payer owes net of money we hold for them |
| LastPostingId | uniqueidentifier | • | FK `LedgerPostings` |
| UpdatedAtUtc | datetimeoffset(3) | | |

No rowversion: updated with `UPDATE PayerBalances SET Receivable += @d … WHERE PayerId = @p` inside
the posting transaction, so concurrent postings for one payer queue on the row lock instead of
failing with 409 (see `domain-model.md` §6).

### 3.8 Audit

**AuditRecords** — insert-only (BR-701, BR-702).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | bigint IDENTITY | | PK clustered |
| T | | | |
| OccurredAtUtc | datetimeoffset(3) | | `IX_AuditRecords_TenantId_OccurredAtUtc` |
| UserId | uniqueidentifier | • | null for system actions (migrations, expiry sweep) |
| CorrelationId | uniqueidentifier | | `IX_AuditRecords_TenantId_CorrelationId` (BR-703) |
| EntityType | varchar(50) | | e.g. `Bill`, `Payment`, `User`, `Tenant` |
| EntityId | uniqueidentifier | | `IX_AuditRecords_TenantId_EntityType_EntityId_Id` |
| DocumentNumber | varchar(30) | • | for human lookup |
| Action | varchar(40) | | e.g. `Created`, `Posted`, `Voided`, `Allocated`, `ApprovalGranted`, `RolesChanged` |
| BeforeJson | nvarchar(max) | • | null on create |
| AfterJson | nvarchar(max) | • | null never (state after the change) |
| Reason | nvarchar(500) | • | void, reversal, rejection reason |

---

## 4. Gapless number series (ADR 0008)

### 4.1 Allocation inside the posting transaction

Run by `INumberSeries.NextAsync` on the **same connection and transaction** as the document insert,
after all validation, as the last step before writing the document (BR-801):

```sql
-- @tenant, @type ('Bill'), @fy ('2026-27'), @defaultPrefix from Tenants

DECLARE @next int, @prefix varchar(3);

SELECT @next = NextNumber, @prefix = Prefix
FROM   dbo.NumberSeries WITH (UPDLOCK, HOLDLOCK)
WHERE  TenantId = @tenant AND DocumentType = @type AND FiscalYear = @fy;

IF @next IS NULL
BEGIN
    -- first document of this type in this fiscal year: create the row under the same range lock
    INSERT dbo.NumberSeries (Id, TenantId, DocumentType, FiscalYear, Prefix, NextNumber, CreatedAtUtc, UpdatedAtUtc)
    VALUES (NEWID(), @tenant, @type, @fy, @defaultPrefix, 1, @now, @now);
    SET @next = 1; SET @prefix = @defaultPrefix;
END

UPDATE dbo.NumberSeries
SET    NextNumber = @next + 1, UpdatedAtUtc = @now
WHERE  TenantId = @tenant AND DocumentType = @type AND FiscalYear = @fy;

SELECT @next AS SequenceNumber,
       @prefix + '/' + SUBSTRING(@fy, 3, 5) + '/' + RIGHT('000000' + CAST(@next AS varchar(6)), 6) AS Number;
       -- '2026-27' -> '26-27': BIL/26-27/000123 is 16 characters (ADR 0008)
```

- `UPDLOCK` takes an update lock on the row so two posting transactions for the same series
  serialise; the second waits, then reads the incremented value.
- `HOLDLOCK` (serializable) holds a **range** lock when the row does not exist yet, so two
  first-of-year postings cannot both insert; the loser blocks and then finds the row.
- The lock is released only at commit or rollback. A rollback leaves `NextNumber` unchanged, so a
  failed posting consumes no number.
- An equivalent single-statement form, `UPDATE … SET NextNumber += 1 OUTPUT deleted.NextNumber`,
  is acceptable for the existing-row path; the create-on-first-use path still needs the range lock.

### 4.2 Fiscal year

`@fy` comes from `FiscalYear.FromDate(document business date)` (BR-004), not from today's date. A
bill dated 31 March 2027 posted on 2 April 2027 takes the next `2026-27` number.

### 4.3 Gapless verification query

Run in integration tests after every scenario and available as a report:

```sql
SELECT TenantId, FiscalYear, COUNT(*) AS Documents, MAX(SequenceNumber) AS MaxSeq
FROM   dbo.Bills
WHERE  SequenceNumber IS NOT NULL
GROUP  BY TenantId, FiscalYear
HAVING COUNT(*) <> MAX(SequenceNumber);   -- any row = a gap or a duplicate
```

The same query runs against `CreditNotes`, `Payments`, `Adjustments` and `Refunds`.

---

## 5. Receivables ledger

### 5.1 Scope

A **subledger**, not a general ledger (ADR 0005). It answers "what does each payer owe, on which
bill, and what do we hold for them?" with double-entry discipline, and it is the source of truth for
every balance in the system. Revenue, Cash and Adjustments are **control accounts** that make every
posting balance and let R5 export journals to an external accounting system; they carry no subject.

### 5.2 Accounts and sign convention

| Account | Normal side | Subject | Balance formula | Meaning of a positive balance |
|---|---|---|---|---|
| `Receivable` | Debit | Bill | Σ Debit − Σ Credit | the payer still owes this on the bill |
| `UnappliedCash` | Credit | Payment | Σ Credit − Σ Debit | money received, not yet applied to a bill |
| `CreditAvailable` | Credit | Credit note | Σ Credit − Σ Debit | credit granted, not yet applied or refunded |
| `Revenue` | Credit | — | Σ Credit − Σ Debit | net billed (bills less credit notes) |
| `Cash` | Debit | — | Σ Debit − Σ Credit | net money received (payments less refunds and reversals) |
| `Adjustments` | Debit | — | Σ Debit − Σ Credit | balances given up without money (discounts, write-offs, rounding) |

Every entry has one positive `Debit` or one positive `Credit`, never both, never negative
(`CK_LedgerEntries_OneSide`). "Increase what the payer owes" is always a **debit to `Receivable`**;
"increase what we hold for the payer" is always a **credit to `UnappliedCash` or `CreditAvailable`**.

### 5.3 Posting templates

`Dr` / `Cr` amounts are `decimal(18,2)` already rounded at the document (BR-001). Each row is one
`LedgerPosting`; a use case may create more than one posting (credit note posting creates two).

| Kind | Trigger | Entries | Cache deltas |
|---|---|---|---|
| `BillPosted` | Bill posted, total *T* | Dr `Receivable(bill)` T · Cr `Revenue` T | `Bill.BalanceDue = T`; `Payer.Receivable += T` |
| `PaymentRecorded` | Payment recorded, amount *P* | Dr `Cash` P · Cr `UnappliedCash(payment)` P | `Payment.UnappliedAmount = P`; `Payer.UnappliedCash += P` |
| `PaymentAllocated` | Allocation, amount *A* | **Dr `UnappliedCash(payment)` A · Cr `Receivable(bill)` A** | `Payment.UnappliedAmount −= A`; `Bill.BalanceDue −= A`; `Payer.UnappliedCash −= A`; `Payer.Receivable −= A` |
| `AllocationReversed` | Allocation reversal approved | Dr `Receivable(bill)` A · Cr `UnappliedCash(payment)` A; `ReversalOfPostingId` = the allocation's posting | the mirror of the above |
| `PaymentReversed` | Payment reversal approved, amount *P* | one `AllocationReversed` per active allocation, then Dr `UnappliedCash(payment)` P · Cr `Cash` P | `Payment.UnappliedAmount = 0`; `Payer.UnappliedCash −= P` |
| `CreditNotePosted` | Credit note posted, total *C* | Dr `Revenue` C · Cr `CreditAvailable(creditNote)` C | `CreditNote.AvailableAmount = C`; `Payer.CreditAvailable += C` |
| `CreditApplied` | Auto-apply at posting or explicit apply, amount *X* | **Dr `CreditAvailable(creditNote)` X · Cr `Receivable(bill)` X** | `CreditNote.AvailableAmount −= X`; `Bill.BalanceDue −= X`; `Payer.CreditAvailable −= X`; `Payer.Receivable −= X` |
| `CreditApplicationReversed` | Application reversal approved | Dr `Receivable(bill)` X · Cr `CreditAvailable(creditNote)` X | the mirror |
| `AdjustmentPosted` | Adjustment posted, amount *D* | Dr `Adjustments` D · Cr `Receivable(bill)` D | `Bill.BalanceDue −= D`; `Payer.Receivable −= D` |
| `RefundPosted` | Refund posted, amount *R* | Dr `UnappliedCash(payment)` **or** `CreditAvailable(creditNote)` R · Cr `Cash` R | source cache `−= R`; `Payer.UnappliedCash` or `Payer.CreditAvailable` `−= R` |

### 5.4 The paired-entry treatment of allocations

An allocation moves value between two subject accounts of the **same payer** and touches no control
account: `Dr UnappliedCash(payment) / Cr Receivable(bill)`. Consequences:

- The payer's `NetOutstanding` is unchanged by an allocation (receivable falls by *A*, unapplied
  cash falls by *A*). Allocation is bookkeeping about *which bill* the money settles, not about
  *whether* the payer paid. This is why a payment with unapplied cash already reduces what the
  payer owes net.
- Both subject balances change by the same amount in one posting, which is why the use case must
  update both `Payment.UnappliedAmount` and `Bill.BalanceDue` in one transaction
  (`domain-model.md` §6). Splitting them would break identity 4 and 5 below between commits.
- Reversal is the mirror pair with `ReversalOfPostingId` set; the original posting is never edited.
- Worked example. Bill `BIL/26-27/000042` for ₹5,000; payment `PAY/26-27/000017` of ₹3,000 by UPI;
  allocate ₹3,000: `Receivable(bill)` 5,000 → 2,000, `UnappliedCash(payment)` 3,000 → 0, bill becomes
  `PartiallyPaid`, payer net outstanding stays ₹2,000 before and after the allocation.

### 5.5 Balance identities (BR-404)

These hold after **every** committed transaction. Integration tests assert all of them after each
scenario; the reconciliation report evaluates them on demand.

| # | Identity |
|---|---|
| 1 | For every `LedgerPostings` row: `Σ Debit = Σ Credit` over its entries, and the posting has ≥ 2 entries. |
| 2 | Every posted document (`Bills` with `Number`, `Payments`, posted `CreditNotes`, `Adjustments`, `Refunds`, and every allocation row) has exactly one `LedgerPostings` row with its `DocumentType`/`DocumentId` (plus one reversal posting if reversed); every `LedgerPostings` row has such a document. |
| 3 | `Bills.BalanceDue = Σ(Debit − Credit)` over `LedgerEntries` where `Account = 'Receivable' AND SubjectId = Bills.Id`, for every posted bill. Equivalently: `Total − Σ active PaymentAllocations − Σ active CreditNoteAllocations − Σ posted Adjustments`. |
| 4 | `Payments.UnappliedAmount = Σ(Credit − Debit)` over `UnappliedCash` entries for the payment. Equivalently: `Amount − Σ active allocations − Σ posted refunds from it`, and `0` if reversed. |
| 5 | `CreditNotes.AvailableAmount = Σ(Credit − Debit)` over `CreditAvailable` entries for the credit note. Equivalently: `Total − Σ active applications − Σ posted refunds from it`. |
| 6 | `PayerBalances.Receivable = Σ Bills.BalanceDue` for the payer's posted bills `= Σ(Debit − Credit)` over the payer's `Receivable` entries. |
| 7 | `PayerBalances.UnappliedCash = Σ Payments.UnappliedAmount` for the payer `= Σ(Credit − Debit)` over the payer's `UnappliedCash` entries. |
| 8 | `PayerBalances.CreditAvailable = Σ CreditNotes.AvailableAmount` for the payer `= Σ(Credit − Debit)` over the payer's `CreditAvailable` entries. |
| 9 | Tenant trial balance: `Receivable + Cash + Adjustments (debit balances) = Revenue + UnappliedCash + CreditAvailable (credit balances)`, summed over all entries of the tenant. |
| 10 | No subject balance is negative (identities 3, 4, 5 each ≥ 0) and `BalanceDue ≤ Total`, `UnappliedAmount ≤ Amount`, `AvailableAmount ≤ Total` (BR-405). |
| 11 | Gapless: for each (tenant, document type, fiscal year), `COUNT(*) = MAX(SequenceNumber)` and `NumberSeries.NextNumber = MAX(SequenceNumber) + 1` (§4.3). |

### 5.6 Reconciliation queries

Each query returns **zero rows** when the identity holds. They run per tenant.

```sql
-- Identity 3: bill balance versus ledger
SELECT b.Id, b.Number, b.BalanceDue, ISNULL(l.Balance, 0) AS LedgerBalance
FROM   dbo.Bills b
LEFT JOIN (
    SELECT SubjectId, SUM(Debit - Credit) AS Balance
    FROM   dbo.LedgerEntries
    WHERE  TenantId = @tenant AND Account = 'Receivable'
    GROUP  BY SubjectId) l ON l.SubjectId = b.Id
WHERE  b.TenantId = @tenant AND b.Number IS NOT NULL
  AND  b.BalanceDue <> ISNULL(l.Balance, 0);

-- Identity 4: payment unapplied versus ledger
SELECT p.Id, p.Number, p.UnappliedAmount, ISNULL(l.Balance, 0) AS LedgerBalance
FROM   dbo.Payments p
LEFT JOIN (
    SELECT SubjectId, SUM(Credit - Debit) AS Balance
    FROM   dbo.LedgerEntries
    WHERE  TenantId = @tenant AND Account = 'UnappliedCash'
    GROUP  BY SubjectId) l ON l.SubjectId = p.Id
WHERE  p.TenantId = @tenant AND p.UnappliedAmount <> ISNULL(l.Balance, 0);

-- Identity 6–8: payer cache versus ledger
SELECT pb.PayerId, pb.Receivable, pb.UnappliedCash, pb.CreditAvailable,
       l.Receivable AS LedgerReceivable, l.UnappliedCash AS LedgerUnapplied, l.CreditAvailable AS LedgerCredit
FROM   dbo.PayerBalances pb
LEFT JOIN (
    SELECT PayerId,
           SUM(CASE WHEN Account = 'Receivable'      THEN Debit - Credit ELSE 0 END) AS Receivable,
           SUM(CASE WHEN Account = 'UnappliedCash'   THEN Credit - Debit ELSE 0 END) AS UnappliedCash,
           SUM(CASE WHEN Account = 'CreditAvailable' THEN Credit - Debit ELSE 0 END) AS CreditAvailable
    FROM   dbo.LedgerEntries
    WHERE  TenantId = @tenant AND PayerId IS NOT NULL
    GROUP  BY PayerId) l ON l.PayerId = pb.PayerId
WHERE  pb.TenantId = @tenant
  AND (pb.Receivable <> ISNULL(l.Receivable, 0)
    OR pb.UnappliedCash <> ISNULL(l.UnappliedCash, 0)
    OR pb.CreditAvailable <> ISNULL(l.CreditAvailable, 0));

-- Identity 1: unbalanced postings
SELECT PostingId, SUM(Debit) AS Debits, SUM(Credit) AS Credits, COUNT(*) AS Lines
FROM   dbo.LedgerEntries
WHERE  TenantId = @tenant
GROUP  BY PostingId
HAVING SUM(Debit) <> SUM(Credit) OR COUNT(*) < 2;

-- Identity 2 (one direction): posted bills without a posting
SELECT b.Id, b.Number
FROM   dbo.Bills b
WHERE  b.TenantId = @tenant AND b.Number IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.LedgerPostings lp
                  WHERE lp.TenantId = @tenant AND lp.DocumentType = 'Bill' AND lp.DocumentId = b.Id AND lp.Kind = 'BillPosted');

-- BR-303 cross-table rule: allocation between different payers
SELECT a.Id
FROM   dbo.PaymentAllocations a
JOIN   dbo.Payments p ON p.Id = a.PaymentId
JOIN   dbo.Bills    b ON b.Id = a.BillId
WHERE  a.TenantId = @tenant AND p.PayerId <> b.PayerId;
```

A non-empty result is a **Sev-1 defect**: the runbook says stop postings for the tenant, capture the
rows and the correlation ids from `AuditRecords`, and fix the code path. Caches are never
"corrected" by hand; if a fix requires rebuilding a cache, it is rebuilt from the ledger by a
migration that is itself audited.

---

## 6. Idempotency store (ADR 0006)

Lifecycle of a row in `IdempotencyKeys` for a financial POST:

1. **Reserve** in its own short transaction: `INSERT … Status = 'InProgress'`. A primary-key
   violation means a row exists: compare `RequestHash`; if equal and `Completed` → replay the stored
   response; if equal and `InProgress` → 409 `idempotency_in_progress`; if different → 422
   `idempotency_key_reuse`.
2. **Process** the use case in the business transaction.
3. **Complete** inside that same business transaction: `UPDATE … SET Status = 'Completed',
   StatusCode, ResponseBody, ResponseHeaders, CompletedAtUtc`. Commit. The stored response and the
   document are therefore visible together or not at all.
4. On failure before commit, the reservation is **deleted** in a compensating transaction so the
   client can retry with the same key. If the process crashes between 1 and 4, the `InProgress` row
   expires after 30 days (`ExpiresAtUtc`) or is cleared by the runbook procedure; a retry before that
   gets 409 and the operator resolves it.

Only 2xx and 4xx responses are stored; a 5xx leaves no `Completed` row.

---

## 7. Database permissions

Two logins. Migrations run as `billingrecon_migrator` (ddl_admin, db_datawriter). The API runs as
`billingrecon_app` with:

| Tables | Permissions |
|---|---|
| All tables not listed below | `SELECT`, `INSERT`, `UPDATE` |
| `LedgerPostings`, `LedgerEntries`, `AuditRecords` | `SELECT`, `INSERT` (BR-402, BR-702) |
| `IdempotencyKeys` | `SELECT`, `INSERT`, `UPDATE`, `DELETE` (expiry sweep and reservation cleanup) |
| `Roles` | `SELECT` |
| `__EFMigrationsHistory` | `SELECT`. The readiness check compares the migrations this table lists with the ones in the build; without the grant the API can never report ready (ADR 0015). EF Core creates the table, so its grant is not part of any table's migration: it belongs to the step that creates the logins. |

There is no `DELETE` grant on any financial table; an EF `Remove` on one would fail at the database
even if an architecture test missed it (BR-005).

Neither login exists yet: development and the tests run as `sa`, and no migration contains a
`GRANT`. Until the logins and their grants are created, nothing at the database stops a delete or an
update of an append-only table (CLAUDE.md, section 9).

---

## 8. Migrations

- EF Core migrations in `src/BillingRecon.Infrastructure/Common/Migrations`, one per vertical slice,
  named `NNNN_<Slice>`. They are scaffolded with the local `dotnet-ef` tool; the commands are in
  `docs/runbook.md` section 3.

  | Migration | Contents | Status |
  |---|---|---|
  | `0001_Tenants` | `Tenants` (§3.1) | In the repository: `20261003192738_0001_Tenants`, scaffolded with EF Core 10.0.12 and verified against §3.1 on 2026-10-17. Not yet applied to SQL Server 2022 (`docs/runbook.md` §2, step 7). |
  | `0002_…` onward | Users and roles, then one per slice in the order of §3 | Not started. |

- CHECK constraints, unique and filtered indexes and computed columns are declared in the EF model
  (the entity configuration, or a convention of the context), so a scaffolded migration needs no
  hand editing and the model snapshot knows them. `migrationBuilder.Sql` is for what EF Core cannot
  express, such as the permission grants of §7, added in the migration that creates the table
  (ADR 0015).
- A test without a database fails when the model has a change that no migration contains, and
  another compares the script of the migrations with §3.1.
- Applied automatically when the API starts **in Development only**; everywhere else they are a
  deployment step run as `billingrecon_migrator` (§7). `/health/ready` reports "not ready" while a
  migration is unapplied.
- The database tests apply all migrations to a SQL Server 2022 that Testcontainers starts once per test
  run, and Respawn empties the tables between tests without touching the schema or
  `__EFMigrationsHistory` (nor `Roles`, once that reference table exists). A second database, created
  from the test model, holds the test-only probe tables (ADR 0016).
- Widening an enum CHECK (for example adding `Insurer` to `Payers.Kind` in R4) is a migration that
  drops and recreates the constraint; narrowing one is never done.
- Data fixes are migrations too, and they write `AuditRecords` with `UserId = NULL` and
  `Action = 'Migration'`.
