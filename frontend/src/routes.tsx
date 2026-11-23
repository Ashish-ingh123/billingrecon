import type { ReactNode } from 'react';
import type { RouteObject } from 'react-router';
import { AdjustmentPage } from './adjustments/AdjustmentPage';
import { EditAdjustmentPage, NewAdjustmentPage } from './adjustments/AdjustmentFormPage';
import { AdjustmentsPage } from './adjustments/AdjustmentsPage';
import { ApprovalsPage } from './approvals/ApprovalsPage';
import { RequireAuth } from './auth/RequireAuth';
import { EditBillPage, NewBillPage } from './bills/BillFormPage';
import { BillPage } from './bills/BillPage';
import { BillsPage } from './bills/BillsPage';
import type { Policy } from './auth/permissions';
import { SignInPage } from './auth/SignInPage';
import { ApplyCreditPage } from './creditNotes/ApplyCreditPage';
import { EditCreditNotePage, NewCreditNotePage } from './creditNotes/CreditNoteFormPage';
import { CreditNotePage } from './creditNotes/CreditNotePage';
import { CreditNotesPage } from './creditNotes/CreditNotesPage';
import { EditPatientPage, NewPatientPage } from './patients/PatientFormPage';
import { PatientPage } from './patients/PatientPage';
import { PatientsPage } from './patients/PatientsPage';
import { EditPayerPage, NewPayerPage } from './payers/PayerFormPage';
import { PayerPage } from './payers/PayerPage';
import { PayersPage } from './payers/PayersPage';
import { StatementPage } from './payers/StatementPage';
import { AllocatePage } from './payments/AllocatePage';
import { PaymentPage } from './payments/PaymentPage';
import { PaymentsPage } from './payments/PaymentsPage';
import { RecordPaymentPage } from './payments/RecordPaymentPage';
import { EditRefundPage, NewRefundPage } from './refunds/RefundFormPage';
import { AvailableCreditPage } from './reports/AvailableCreditPage';
import { BillRegisterPage } from './reports/BillRegisterPage';
import { DailyCollectionPage } from './reports/DailyCollectionPage';
import { OutstandingPage } from './reports/OutstandingPage';
import { PaymentRegisterPage } from './reports/PaymentRegisterPage';
import { PendingApprovalsReportPage } from './reports/PendingApprovalsReportPage';
import { ReconciliationPage } from './reports/ReconciliationPage';
import { ReportsIndex } from './reports/ReportsIndex';
import { UnappliedCashPage } from './reports/UnappliedCashPage';
import { RefundPage } from './refunds/RefundPage';
import { RefundsPage } from './refunds/RefundsPage';
import { EditServiceItemPage, NewServiceItemPage } from './serviceItems/ServiceItemFormPage';
import { ServiceItemPage } from './serviceItems/ServiceItemPage';
import { ServiceItemsPage } from './serviceItems/ServiceItemsPage';
import { AppShell } from './shell/AppShell';
import { sections } from './shell/navigation';
import { HomeRedirect, NoAccessPage, NotFoundPage, RequirePolicy, SectionPage } from './shell/pages';

/** The screens of a resource: list and record for the readers, create and correct for the writers (ADR 0039 point 3). */
function screens(
  path: string,
  policies: { read: Policy; write: Policy },
  pages: { List: () => ReactNode; One: () => ReactNode; New: () => ReactNode; Edit: () => ReactNode },
  /** The last part of the path of the change of a record: "edit", or "allocate" for a payment. */
  change = 'edit',
): RouteObject[] {
  const { List, One, New, Edit } = pages;
  const read = (page: ReactNode) => <RequirePolicy policy={policies.read}>{page}</RequirePolicy>;
  const write = (page: ReactNode) => <RequirePolicy policy={policies.write}>{page}</RequirePolicy>;

  return [
    { path, element: read(<List />) },
    { path: `${path}/new`, element: write(<New />) },
    { path: `${path}/:id`, element: read(<One />) },
    { path: `${path}/:id/${change}`, element: write(<Edit />) },
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
  '/credit-notes',
  '/adjustments',
  '/approvals',
  '/reports',
]);

const referenceData = { read: 'ReadReferenceData', write: 'WriteReferenceData' } as const;

/** The app's pages: the sign-in, and behind it the shell with the screens of each section (ADR 0038, ADR 0039). */
export const routes: RouteObject[] = [
  { path: '/sign-in', element: <SignInPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <HomeRedirect /> },
          ...screens('/patients', referenceData, {
            List: PatientsPage,
            One: PatientPage,
            New: NewPatientPage,
            Edit: EditPatientPage,
          }),
          ...screens('/payers', referenceData, {
            List: PayersPage,
            One: PayerPage,
            New: NewPayerPage,
            Edit: EditPayerPage,
          }),
          ...screens('/service-items', referenceData, {
            List: ServiceItemsPage,
            One: ServiceItemPage,
            New: NewServiceItemPage,
            Edit: EditServiceItemPage,
          }),
          ...screens(
            '/bills',
            { read: 'ReadBills', write: 'WriteBills' },
            { List: BillsPage, One: BillPage, New: NewBillPage, Edit: EditBillPage },
          ),
          {
            path: '/payers/:id/statement',
            element: (
              <RequirePolicy policy="Reports">
                <StatementPage />
              </RequirePolicy>
            ),
          },
          ...screens(
            '/payments',
            { read: 'ReadPayments', write: 'RecordPayments' },
            { List: PaymentsPage, One: PaymentPage, New: RecordPaymentPage, Edit: AllocatePage },
            'allocate',
          ),
          ...screens(
            '/refunds',
            { read: 'ReadRefunds', write: 'DraftRefunds' },
            { List: RefundsPage, One: RefundPage, New: NewRefundPage, Edit: EditRefundPage },
          ),
          ...screens(
            '/credit-notes',
            { read: 'ReadCreditNotes', write: 'DraftCreditNotesAndAdjustments' },
            { List: CreditNotesPage, One: CreditNotePage, New: NewCreditNotePage, Edit: EditCreditNotePage },
          ),
          {
            path: '/credit-notes/:id/apply',
            element: (
              <RequirePolicy policy="DraftCreditNotesAndAdjustments">
                <ApplyCreditPage />
              </RequirePolicy>
            ),
          },
          ...screens(
            '/adjustments',
            { read: 'ReadAdjustments', write: 'DraftCreditNotesAndAdjustments' },
            { List: AdjustmentsPage, One: AdjustmentPage, New: NewAdjustmentPage, Edit: EditAdjustmentPage },
          ),
          {
            path: '/approvals',
            element: (
              <RequirePolicy policy="Decide">
                <ApprovalsPage />
              </RequirePolicy>
            ),
          },
          ...(
            [
              ['/reports', ReportsIndex],
              ['/reports/bills', BillRegisterPage],
              ['/reports/payments', PaymentRegisterPage],
              ['/reports/collection', DailyCollectionPage],
              ['/reports/outstanding', OutstandingPage],
              ['/reports/unapplied-cash', UnappliedCashPage],
              ['/reports/available-credit', AvailableCreditPage],
              ['/reports/pending-approvals', PendingApprovalsReportPage],
              ['/reports/reconciliation', ReconciliationPage],
            ] as const
          ).map(([path, Page]) => ({
            path,
            element: (
              <RequirePolicy policy="Reports">
                <Page />
              </RequirePolicy>
            ),
          })),
          ...sections
            .filter((section) => !built.has(section.path))
            .map((section) => ({ path: section.path, element: <SectionPage section={section} /> })),
          { path: '/no-access', element: <NoAccessPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];
