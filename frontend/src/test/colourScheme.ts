/** A stand-in for the system's colour scheme, which jsdom lacks; `set` changes it and tells the listeners, as a browser does. */
export interface ColourScheme {
  set: (prefersDark: boolean) => void;
}

export function stubColourScheme(prefersDark: boolean): ColourScheme {
  let dark = prefersDark;
  const listeners = new Set<(event: MediaQueryListEvent) => void>();

  window.matchMedia = (query: string) =>
    ({
      get matches() {
        return query === '(prefers-color-scheme: dark)' ? dark : false;
      },
      media: query,
      onchange: null,
      addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) =>
        listeners.delete(listener),
      addListener: () => undefined,
      removeListener: () => undefined,
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList;

  return {
    set(next) {
      dark = next;
      for (const listener of listeners) {
        listener({ matches: next, media: '(prefers-color-scheme: dark)' } as MediaQueryListEvent);
      }
    },
  };
}
