import type { RouteObject } from 'react-router';
import { RequireAuth } from './auth/RequireAuth';
import { SignInPage } from './auth/SignInPage';
import { AppShell } from './shell/AppShell';
import { sections } from './shell/navigation';
import { HomeRedirect, NoAccessPage, NotFoundPage, SectionPage } from './shell/pages';

/** The app's pages: the sign-in, and behind it the shell with one page per section (ADR 0038). */
export const routes: RouteObject[] = [
  { path: '/sign-in', element: <SignInPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <HomeRedirect /> },
          ...sections.map((section) => ({ path: section.path, element: <SectionPage section={section} /> })),
          { path: '/no-access', element: <NoAccessPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
];
