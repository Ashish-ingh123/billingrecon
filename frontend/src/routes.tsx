import type { ReactNode } from 'react';
import type { RouteObject } from 'react-router';
import { RequireAuth } from './auth/RequireAuth';
import { EditBillPage, NewBillPage } from './bills/BillFormPage';
import { BillPage } from './bills/BillPage';
import { BillsPage } from './bills/BillsPage';
import type { Policy } from './auth/permissions';
import { SignInPage } from './auth/SignInPage';
import { EditPatientPage, NewPatientPage } from './patients/PatientFormPage';
import { PatientPage } from './patients/PatientPage';
import { PatientsPage } from './patients/PatientsPage';
import { EditPayerPage, NewPayerPage } from './payers/PayerFormPage';
import { PayerPage } from './payers/PayerPage';
import { PayersPage } from './payers/PayersPage';
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
): RouteObject[] {
  const { List, One, New, Edit } = pages;
  const read = (page: ReactNode) => <RequirePolicy policy={policies.read}>{page}</RequirePolicy>;
  const write = (page: ReactNode) => <RequirePolicy policy={policies.write}>{page}</RequirePolicy>;

  return [
    { path, element: read(<List />) },
    { path: `${path}/new`, element: write(<New />) },
    { path: `${path}/:id`, element: read(<One />) },
    { path: `${path}/:id/edit`, element: write(<Edit />) },
  ];
}

/** The sections whose screens are built; every other section is a placeholder naming the prompt that builds it. */
const built = new Set(['/patients', '/payers', '/service-items', '/bills']);

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
