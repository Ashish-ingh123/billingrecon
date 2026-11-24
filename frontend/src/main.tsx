import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter } from 'react-router';
import { RouterProvider } from 'react-router/dom';
import { Providers } from './Providers';
import { routes } from './routes';
// The typeface, self-hosted: the package's files are bundled and served from this origin (ADR 0047).
import '@fontsource-variable/inter';
import './theme/tokens.css';
import './theme/base.css';

const router = createBrowserRouter(routes);

const root = document.getElementById('root');
if (root === null) {
  throw new Error('index.html has no #root element.');
}

createRoot(root).render(
  <StrictMode>
    <Providers>
      <RouterProvider router={router} />
    </Providers>
  </StrictMode>,
);
