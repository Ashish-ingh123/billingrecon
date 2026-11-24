import { useEffect, useRef } from 'react';
import { useLocation } from 'react-router';
import { sections } from './navigation';

const product = 'BillingRecon';

/**
 * What a page change tells the user (ADR 0048): the tab's title names the section ("Bills · BillingRecon"), and after a
 * navigation, not on the first page, the page's heading takes the focus, so a screen reader starts at the new page and
 * the keyboard does not have to cross the navigation again.
 */
export function usePageFocus() {
  const location = useLocation();
  const first = useRef(true);

  useEffect(() => {
    const section = sections.find(
      (candidate) => location.pathname === candidate.path || location.pathname.startsWith(`${candidate.path}/`),
    );
    document.title = section === undefined ? product : `${section.label} · ${product}`;

    if (first.current) {
      first.current = false;
      return;
    }

    // The page may still be loading its heading: look for it on the next frames, for a moment.
    let tries = 0;
    let frame = 0;
    const focus = () => {
      const heading = document.querySelector<HTMLElement>('#content h1');
      if (heading !== null) {
        heading.setAttribute('tabindex', '-1');
        heading.focus({ preventScroll: false });
        return;
      }

      tries += 1;
      if (tries < 30) {
        frame = window.requestAnimationFrame(focus);
      }
    };

    frame = window.requestAnimationFrame(focus);
    return () => window.cancelAnimationFrame(frame);
  }, [location.pathname]);
}
