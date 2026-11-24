import type { ReactNode } from 'react';

/**
 * The icons of the interface (ADR 0047): our own inline SVG on one 24 px grid, drawn with currentColor so that an icon
 * takes the colour of the text it stands in, in both themes, and nothing is loaded for it. An icon is decoration beside
 * a word unless it is given a title, which then names it.
 */
export type IconName =
  | 'building'
  | 'envelope'
  | 'envelope-outline'
  | 'padlock'
  | 'eye'
  | 'eye-off'
  | 'enter'
  | 'help'
  | 'shield-check'
  | 'bar-chart'
  | 'handshake'
  | 'spinner';

const outline = {
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.75,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
} as const;
const solid = { fill: 'currentColor', stroke: 'none' } as const;

const shapes: Record<IconName, ReactNode> = {
  building: (
    <path
      {...solid}
      fillRule="evenodd"
      d="M8 2.5a1 1 0 0 0-1 1V8H4a1 1 0 0 0-1 1v11.5a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V9a1 1 0 0 0-1-1h-3V3.5a1 1 0 0 0-1-1H8Zm3.25 2.25h1.5v1.5h1.5v1.5h-1.5v1.5h-1.5v-1.5h-1.5v-1.5h1.5v-1.5ZM5.5 10.5h2v2h-2v-2Zm0 4h2v2h-2v-2Zm11-4h2v2h-2v-2Zm0 4h2v2h-2v-2Zm-6.5-3h1.5v2H10v-2Zm2.5 0H14v2h-1.5v-2Zm-2 5h3v3h-3v-3Z"
    />
  ),
  envelope: (
    <path
      {...solid}
      d="M3.5 5h17A1.5 1.5 0 0 1 22 6.5v.3l-10 6-10-6v-.3A1.5 1.5 0 0 1 3.5 5ZM2 9.1v8.4A1.5 1.5 0 0 0 3.5 19h17a1.5 1.5 0 0 0 1.5-1.5V9.1l-9.5 5.7a1 1 0 0 1-1 0L2 9.100Z"
    />
  ),
  'envelope-outline': (
    <g {...outline}>
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="m3.5 7 8.5 6 8.5-6" />
    </g>
  ),
  padlock: (
    <path
      {...solid}
      fillRule="evenodd"
      d="M12 2a5 5 0 0 0-5 5v3H6.500A1.5 1.5 0 0 0 5 11.500v8A1.5 1.5 0 0 0 6.5 21h11a1.5 1.5 0 0 0 1.5-1.500v-8a1.5 1.5 0 0 0-1.5-1.500H17V7a5 5 0 0 0-5-5Zm3 8V7a3 3 0 1 0-6 0v3h6Zm-3 3.250a1.5 1.5 0 0 1 .750 2.800V18h-1.500v-1.950a1.5 1.5 0 0 1 .750-2.800Z"
    />
  ),
  eye: (
    <g {...outline}>
      <path d="M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12Z" />
      <circle cx="12" cy="12" r="3" />
    </g>
  ),
  'eye-off': (
    <g {...outline}>
      <path d="M9.9 5.800A8.7 8.7 0 0 1 12 5.500c6 0 9.5 6.5 9.5 6.500a15 15 0 0 1-2.6 3.300M6.6 7.200C4 9 2.5 12 2.5 12S6 18.5 12 18.500a8.6 8.6 0 0 0 4.2-1.1" />
      <path d="M9.9 9.900a3 3 0 0 0 4.2 4.2" />
      <path d="m3.5 3.5 17 17" />
    </g>
  ),
  enter: (
    <g {...outline}>
      <path d="M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4" />
      <path d="M4 12h11" />
      <path d="m11 8 4 4-4 4" />
    </g>
  ),
  help: (
    <g {...outline}>
      <circle cx="12" cy="12" r="9" />
      <path d="M9.5 9.500a2.5 2.5 0 1 1 3.7 2.200c-.800.450-1.2 1-1.2 1.8" />
      <path d="M12 16.800v.100" />
    </g>
  ),
  'shield-check': (
    <g {...outline}>
      <path d="M12 2.5 4 5.500v6c0 5 3.4 8.6 8 10 4.6-1.4 8-5 8-10v-6l-8-3Z" />
      <path d="m8.5 12 2.5 2.5 4.5-5" />
    </g>
  ),
  'bar-chart': (
    <g {...outline}>
      <rect x="3.5" y="13" width="4" height="7.5" rx="1" />
      <rect x="10" y="9" width="4" height="11.5" rx="1" />
      <rect x="16.5" y="5" width="4" height="15.5" rx="1" />
      <path d="M4 9.5 10.5 5l3 1.500L19 2.5" />
    </g>
  ),
  handshake: (
    <g {...outline}>
      <path d="M2.5 9.5 7 7l4 1.5 2.5-1.200L17 7l4.5 2.5" />
      <path d="M2.5 9.5V15l2.5.8 4.800 4a1.700 1.700 0 0 0 2.400-.2" />
      <path d="M21.5 9.5V15L19 16l-4.300 3.800a1.700 1.700 0 0 1-2.500-2.300" />
      <path d="m11 8.5-3.200 3.200a1.600 1.600 0 0 0 2.300 2.300l1.900-1.900 4.800 4.200" />
    </g>
  ),
  spinner: (
    <g {...outline} strokeWidth={2.25}>
      <path d="M12 3a9 9 0 1 0 9 9" />
    </g>
  ),
};

export function Icon({ name, title, className }: { name: IconName; title?: string; className?: string }) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      width="1em"
      height="1em"
      focusable="false"
      role={title === undefined ? undefined : 'img'}
      aria-hidden={title === undefined ? true : undefined}
      aria-label={title}
    >
      {shapes[name]}
    </svg>
  );
}
