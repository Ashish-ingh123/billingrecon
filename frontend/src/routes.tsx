import type { ReactNode } from 'react';
import type { RouteObject } from 'react-router';
import { RequireAuth } from './auth/RequireAuth';
import type { Policy } from './auth/permissions';
import { SignInPage } from './auth/SignInPage';
import { sections } from './shell/navigation';
import { HomeRedirect, NoAccessPage, NotFoundPage, RequirePolicy, SectionPage } from './shell/pages';

/** A page's component, loaded when its route is first visited (Prompt 9.4b, backlog B21). */
type Load = () => Promise<() => ReactNode>;

/**
 * A route whose page is loaded on demand and shown to the roles of a policy (ADR 0039 point 3): the module is read the
 * first time the route is visited, so a first visit to the sign-in page reads none of the screens (ADR 0050, amended).
 */
function guarded(path: string, policy: Policy, load: Load): RouteObject {
  return {
    path,
    lazy: async () => {
      const Page = await load();
      return {
        element: (
          <RequirePolicy policy={policy}>
            <Page />
          </RequirePolicy>
        ),
      };
    },
  };
}

/** The screens of a resource: list and record for the readers, create and correct for the writers (ADR 0039 point 3). */
function screens(
  path: string,
  policies: { read: Policy; write: Policy },
  pages: { List: Load; One: Load; New: Load; Edit: Load },
  /** The last part of the path of the change of a record: "edit", or "allocate" for a payment. */
  change = 'edit',
): RouteObject[] {
  return [
    guarded(path, policies.read, pages.List),
    guarded(`${path}/new`, policies.write, pages.New),
    guarded(`${path}/:id`, policies.read, pages.One),
    guarded(`${path}/:id/${change}`, policies.write, pages.Edit),
  ];
}

/** The sections whose screens are built; every other section is a placeholder naming the prompt that builds it. */
const built = new Set([
  '/patients',
  '/payers',
  '/service-items',
  '/bills',
  '/payments',
  '/refunds',
  '/shifts',
  '/credit-notes',
  '/adjustments',
  '/approvals',
  '/reports',
  '/dashboard',
  '/administration',
  '/reconciliation',
]);

const referenceData = { read: 'ReadReferenceData', write: 'WriteReferenceData' } as const;

/** The app's pages: the sign-in, and behind it the shell with the screens of each section (ADR 0038, ADR 0039). */
export const routes: RouteObject[] = [
  { path: '/sign-in', element: <SignInPage /> },
  // A forgotten password, before any sign-in (ADR 0061, amended): asking for the link, and the page the link opens.
  {
    path: '/forgot-password',
    lazy: async () => {
      const { ForgotPasswordPage } = await import('./auth/ForgotPasswordPage');
      return { element: <ForgotPasswordPage /> };
    },
  },
  {
    path: '/reset-password',
    lazy: async () => {
      const { ResetPasswordPage } = await import('./auth/ResetPasswordPage');
      return { element: <ResetPasswordPage /> };
    },
  },
  {
    element: <RequireAuth />,
    children: [
      // A user whose password an administrator set changes it here first, outside the shell (ADR 0051).
      {
        path: '/change-password',
        lazy: async () => {
          const { ChangePasswordPage } = await import('./auth/ChangePasswordPage');
          return { element: <ChangePasswordPage /> };
        },
      },
      // The second step of a sign-in and its enrolment, outside the shell like the change of a password (ADR 0061).
      {
        path: '/second-step',
        lazy: async () => {
          const { SecondStepPage } = await import('./auth/SecondStepPage');
          return { element: <SecondStepPage /> };
        },
      },
      {
        path: '/second-step/enrol',
        lazy: async () => {
          const { EnrolSecondStepPage } = await import('./auth/EnrolSecondStepPage');
          return { element: <EnrolSecondStepPage /> };
        },
      },
      {
        // The shell is read with the first page behind the sign-in, not by the sign-in page (Prompt 9.4b, backlog B21).
        lazy: async () => {
          const { AppShell } = await import('./shell/AppShell');
          return { element: <AppShell /> };
        },
        children: [
          { index: true, element: <HomeRedirect /> },
          ...screens('/patients', referenceData, {
            List: async () => (await import('./patients/PatientsPage')).PatientsPage,
            One: async () => (await import('./patients/PatientPage')).PatientPage,
            New: async () => (await import('./patients/PatientFormPage')).NewPatientPage,
            Edit: async () => (await import('./patients/PatientFormPage')).EditPatientPage,
          }),
          ...screens('/payers', referenceData, {
            List: async () => (await import('./payers/PayersPage')).PayersPage,
            One: async () => (await import('./payers/PayerPage')).PayerPage,
            New: async () => (await import('./payers/PayerFormPage')).NewPayerPage,
            Edit: async () => (await import('./payers/PayerFormPage')).EditPayerPage,
          }),
          ...screens('/service-items', referenceData, {
            List: async () => (await import('./serviceItems/ServiceItemsPage')).ServiceItemsPage,
            One: async () => (await import('./serviceItems/ServiceItemPage')).ServiceItemPage,
            New: async () => (await import('./serviceItems/ServiceItemFormPage')).NewServiceItemPage,
            Edit: async () => (await import('./serviceItems/ServiceItemFormPage')).EditServiceItemPage,
          }),
          ...screens(
            '/bills',
            { read: 'ReadBills', write: 'WriteBills' },
            {
              List: async () => (await import('./bills/BillsPage')).BillsPage,
              One: async () => (await import('./bills/BillPage')).BillPage,
              New: async () => (await import('./bills/BillFormPage')).NewBillPage,
              Edit: async () => (await import('./bills/BillFormPage')).EditBillPage,
            },
          ),
          guarded(
            '/payers/:id/statement',
            'Reports',
            async () => (await import('./payers/StatementPage')).StatementPage,
          ),
          ...screens(
            '/payments',
            { read: 'ReadPayments', write: 'RecordPayments' },
            {
              List: async () => (await import('./payments/PaymentsPage')).PaymentsPage,
              One: async () => (await import('./payments/PaymentPage')).PaymentPage,
              New: async () => (await import('./payments/RecordPaymentPage')).RecordPaymentPage,
              Edit: async () => (await import('./payments/AllocatePage')).AllocatePage,
            },
            'allocate',
          ),
          ...screens(
            '/refunds',
            { read: 'ReadRefunds', write: 'DraftRefunds' },
            {
              List: async () => (await import('./refunds/RefundsPage')).RefundsPage,
              One: async () => (await import('./refunds/RefundPage')).RefundPage,
              New: async () => (await import('./refunds/RefundFormPage')).NewRefundPage,
              Edit: async () => (await import('./refunds/RefundFormPage')).EditRefundPage,
            },
          ),
          ...screens(
            '/credit-notes',
            { read: 'ReadCreditNotes', write: 'DraftCreditNotesAndAdjustments' },
            {
              List: async () => (await import('./creditNotes/CreditNotesPage')).CreditNotesPage,
              One: async () => (await import('./creditNotes/CreditNotePage')).CreditNotePage,
              New: async () => (await import('./creditNotes/CreditNoteFormPage')).NewCreditNotePage,
              Edit: async () => (await import('./creditNotes/CreditNoteFormPage')).EditCreditNotePage,
            },
          ),
          guarded(
            '/credit-notes/:id/apply',
            'DraftCreditNotesAndAdjustments',
            async () => (await import('./creditNotes/ApplyCreditPage')).ApplyCreditPage,
          ),
          ...screens(
            '/adjustments',
            { read: 'ReadAdjustments', write: 'DraftCreditNotesAndAdjustments' },
            {
              List: async () => (await import('./adjustments/AdjustmentsPage')).AdjustmentsPage,
              One: async () => (await import('./adjustments/AdjustmentPage')).AdjustmentPage,
              New: async () => (await import('./adjustments/AdjustmentFormPage')).NewAdjustmentPage,
              Edit: async () => (await import('./adjustments/AdjustmentFormPage')).EditAdjustmentPage,
            },
          ),
          guarded('/approvals', 'Decide', async () => (await import('./approvals/ApprovalsPage')).ApprovalsPage),
          // The dashboard and its chart library are read only when the dashboard is opened (ADR 0044 point 5).
          guarded('/dashboard', 'Dashboard', async () => (await import('./dashboard/DashboardPage')).DashboardPage),
          // Users and settings, for the hospital's administrator (Prompt 10.1, ADR 0051).
          guarded(
            '/administration',
            'ManageTenant',
            async () => (await import('./administration/UsersPage')).UsersPage,
          ),
          guarded(
            '/administration/users/new',
            'ManageTenant',
            async () => (await import('./administration/UserFormPage')).NewUserPage,
          ),
          guarded(
            '/administration/users/:id',
            'ManageTenant',
            async () => (await import('./administration/UserPage')).UserPage,
          ),
          guarded(
            '/administration/users/:id/edit',
            'ManageTenant',
            async () => (await import('./administration/UserFormPage')).EditUserPage,
          ),
          guarded(
            '/administration/settings',
            'ManageTenant',
            async () => (await import('./administration/SettingsPage')).SettingsPage,
          ),
          // The shifts of the cash counter (Prompt 11.2b, ADR 0060): read by all, a cashier's own; opened and closed
          // by whoever may record a payment. The closing slip is printed by whoever may read the shift.
          ...screens(
            '/shifts',
            { read: 'ReadShifts', write: 'WorkShift' },
            {
              List: async () => (await import('./shifts/ShiftsPage')).ShiftsPage,
              One: async () => (await import('./shifts/ShiftPage')).ShiftPage,
              New: async () => (await import('./shifts/OpenShiftPage')).OpenShiftPage,
              Edit: async () => (await import('./shifts/CloseShiftPage')).CloseShiftPage,
            },
            'close',
          ),
          guarded(
            '/shifts/:id/print',
            'ReadShifts',
            async () => (await import('./printing/PrintedShiftSlipPage')).PrintedShiftSlipPage,
          ),
          // The printed documents (Prompt 10.4b, ADR 0054): whoever may read a document may print it.
          guarded(
            '/bills/:id/print',
            'ReadBills',
            async () => (await import('./printing/PrintedBillPage')).PrintedBillPage,
          ),
          guarded(
            '/payments/:id/print',
            'ReadPayments',
            async () => (await import('./printing/PrintedReceiptPage')).PrintedReceiptPage,
          ),
          guarded(
            '/refunds/:id/print',
            'ReadRefunds',
            async () => (await import('./printing/PrintedRefundPage')).PrintedRefundPage,
          ),
          // How the hospital's documents are printed (Prompt 10.4c, ADR 0059).
          guarded(
            '/administration/print',
            'ManageTenant',
            async () => (await import('./administration/PrintSettingsPage')).PrintSettingsPage,
          ),
          // The names of the accountant's ledgers (Prompt 11.4b, ADR 0062).
          guarded(
            '/administration/accounting',
            'ManageTenant',
            async () => (await import('./administration/AccountingLedgersPage')).AccountingLedgersPage,
          ),
          // Bringing in a hospital's records and opening balances (Prompt 10.3b, ADR 0053).
          // R2 Reconciliation (Prompt 13.1c, ADR 0065): the channels are set up by a TenantAdmin, under Users and settings;
          // the statements are read by whoever reads the reconciliation and imported by a TenantAdmin, Supervisor or Accountant.
          guarded(
            '/administration/channels',
            'ManageTenant',
            async () => (await import('./reconciliation/ChannelsPage')).ChannelsPage,
          ),
          guarded(
            '/administration/channels/new',
            'ManageTenant',
            async () => (await import('./reconciliation/ChannelFormPage')).NewChannelPage,
          ),
          guarded(
            '/administration/channels/:id',
            'ManageTenant',
            async () => (await import('./reconciliation/ChannelFormPage')).ChannelPage,
          ),
          guarded(
            '/reconciliation',
            'ReadReconciliation',
            async () => (await import('./reconciliation/StatementsPage')).StatementsPage,
          ),
          guarded(
            '/reconciliation/import',
            'ImportStatements',
            async () => (await import('./reconciliation/StatementImportPage')).StatementImportPage,
          ),
          guarded(
            '/administration/import',
            'ManageTenant',
            async () => (await import('./imports/ImportIndexPage')).ImportIndexPage,
          ),
          guarded(
            '/administration/import/patients',
            'ManageTenant',
            async () => (await import('./imports/PartiesImportPage')).PartiesImportPage,
          ),
          guarded(
            '/administration/import/opening-balances',
            'ManageTenant',
            async () => (await import('./imports/OpeningBalancesPage')).OpeningBalancesPage,
          ),
          guarded(
            '/administration/import/batches/:id',
            'ManageTenant',
            async () => (await import('./imports/OpeningBatchPage')).OpeningBatchPage,
          ),
          guarded('/reports', 'Reports', async () => (await import('./reports/ReportsIndex')).ReportsIndex),
          guarded(
            '/reports/bills',
            'Reports',
            async () => (await import('./reports/BillRegisterPage')).BillRegisterPage,
          ),
          guarded(
            '/reports/payments',
            'Reports',
            async () => (await import('./reports/PaymentRegisterPage')).PaymentRegisterPage,
          ),
          guarded(
            '/reports/collection',
            'Reports',
            async () => (await import('./reports/DailyCollectionPage')).DailyCollectionPage,
          ),
          guarded('/reports/day-end', 'DayEnd', async () => (await import('./reports/DayEndPage')).DayEndPage),
          // The accountant's export (Prompt 11.4b, ADR 0062): for whoever runs or audits the hospital.
          guarded(
            '/reports/accounting-export',
            'AccountingExport',
            async () => (await import('./reports/AccountingExportPage')).AccountingExportPage,
          ),
          guarded(
            '/reports/outstanding',
            'Reports',
            async () => (await import('./reports/OutstandingPage')).OutstandingPage,
          ),
          guarded(
            '/reports/unapplied-cash',
            'Reports',
            async () => (await import('./reports/UnappliedCashPage')).UnappliedCashPage,
          ),
          guarded(
            '/reports/available-credit',
            'Reports',
            async () => (await import('./reports/AvailableCreditPage')).AvailableCreditPage,
          ),
          guarded(
            '/reports/pending-approvals',
            'Reports',
            async () => (await import('./reports/PendingApprovalsReportPage')).PendingApprovalsReportPage,
          ),
          guarded(
            '/reports/reconciliation',
            'Reports',
            async () => (await import('./reports/ReconciliationPage')).ReconciliationPage,
          ),
          ...sections
            .filter((section) => !built.has(section.path))
            .map((section) => ({ path: section.path, element: <SectionPage section={section} /> })),
          // The specimen of the shared components, in Development builds only (ADR 0048): left out of a production build.
          ...(import.meta.env.DEV
            ? [
                {
                  path: '/design',
                  lazy: async () => {
                    const { DesignPage } = await import('./design/DesignPage');
                    return { element: <DesignPage /> };
                  },
                },
              ]
            : []),
          { path: '/no-access', element: <NoAccessPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];
