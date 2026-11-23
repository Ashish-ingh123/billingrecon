import { QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { ApiClientProvider } from './api/ApiClientProvider';
import { AuthProvider } from './auth/AuthProvider';
import { newQueryClient } from './queryClient';
import { ThemeProvider } from './theme/ThemeProvider';

/** Everything the pages rely on: the theme, the query cache, the session and the API client. `fetch` is for tests. */
export function Providers({
  children,
  fetch,
  now,
}: {
  children: ReactNode;
  fetch?: (request: Request) => Promise<Response>;
  now?: () => Date;
}) {
  const [queryClient] = useState(newQueryClient);

  return (
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <AuthProvider {...(now === undefined ? {} : { now })}>
          <ApiClientProvider {...(fetch === undefined ? {} : { fetch })}>{children}</ApiClientProvider>
        </AuthProvider>
      </QueryClientProvider>
    </ThemeProvider>
  );
}
