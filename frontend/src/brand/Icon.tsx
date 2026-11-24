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
  | 'spinner'
  | 'sun'
  | 'moon'
  | 'monitor'
  | 'grid'
  | 'user'
  | 'wallet'
  | 'tag'
  | 'receipt'
  | 'card'
  | 'undo'
  | 'receipt-minus'
  | 'sliders'
  | 'check-circle'
  | 'report'
  | 'gear'
  | 'menu'
  | 'chevrons-left'
  | 'chevrons-right'
  | 'close'
  | 'alert'
  | 'inbox'
  | 'dot'
  | 'arrow-left'
  | 'clock'
  | 'pencil'
  | 'ban'
  | 'plus'
  | 'half'
  | 'stamp';

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
  sun: (
    <g {...outline}>
      <circle cx="12" cy="12" r="4" />
      <path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4" />
    </g>
  ),
  moon: (
    <g {...outline}>
      <path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5Z" />
    </g>
  ),
  monitor: (
    <g {...outline}>
      <rect x="3" y="4" width="18" height="12" rx="2" />
      <path d="M8.5 20h7M12 16v4" />
    </g>
  ),
  grid: (
    <g {...outline}>
      <rect x="3.5" y="3.5" width="7" height="7" rx="1.5" />
      <rect x="13.5" y="3.5" width="7" height="7" rx="1.5" />
      <rect x="3.5" y="13.5" width="7" height="7" rx="1.5" />
      <rect x="13.5" y="13.5" width="7" height="7" rx="1.5" />
    </g>
  ),
  user: (
    <g {...outline}>
      <circle cx="12" cy="8" r="4" />
      <path d="M4.5 20.5a7.5 7.5 0 0 1 15 0" />
    </g>
  ),
  wallet: (
    <g {...outline}>
      <path d="M4 7.5V18a2 2 0 0 0 2 2h13a1 1 0 0 0 1-1v-3" />
      <path d="M4 7.5A2.5 2.5 0 0 1 6.5 5H17v2.5" />
      <path d="M20 10.5h-4a2 2 0 0 0 0 4h4a.5.5 0 0 0 .5-.5v-3a.5.5 0 0 0-.5-.5Z" />
      <path d="M4 7.5h15a1 1 0 0 1 1 1v2" />
    </g>
  ),
  tag: (
    <g {...outline}>
      <path d="M3.5 12.6V4.5a1 1 0 0 1 1-1h8.1a1 1 0 0 1 .7.3l7.4 7.4a1 1 0 0 1 0 1.4l-8.1 8.1a1 1 0 0 1-1.4 0l-7.4-7.4a1 1 0 0 1-.3-.7Z" />
      <circle cx="8.5" cy="8.5" r="1.5" />
    </g>
  ),
  receipt: (
    <g {...outline}>
      <path d="M5.5 3h13v18l-2.2-1.5L14.2 21 12 19.5 9.8 21l-2.1-1.5L5.5 21Z" />
      <path d="M9 8h6M9 11.5h6M9 15h3.5" />
    </g>
  ),
  card: (
    <g {...outline}>
      <rect x="2.5" y="5" width="19" height="14" rx="2" />
      <path d="M2.5 9.5h19M6.5 15h4" />
    </g>
  ),
  undo: (
    <g {...outline}>
      <path d="M9 14 4 9l5-5" />
      <path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11" />
    </g>
  ),
  'receipt-minus': (
    <g {...outline}>
      <path d="M5.5 3h13v18l-2.2-1.5L14.2 21 12 19.5 9.8 21l-2.1-1.5L5.5 21Z" />
      <path d="M9 11.5h6" />
    </g>
  ),
  sliders: (
    <g {...outline}>
      <path d="M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1" />
      <circle cx="15" cy="6" r="2" />
      <circle cx="9" cy="12" r="2" />
      <circle cx="17" cy="18" r="2" />
    </g>
  ),
  'check-circle': (
    <g {...outline}>
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12.5 2.8 2.8L16.5 9.5" />
    </g>
  ),
  report: (
    <g {...outline}>
      <path d="M6 2.5h8l4.5 4.5v13a1.5 1.5 0 0 1-1.5 1.5H6A1.5 1.5 0 0 1 4.5 20V4A1.5 1.5 0 0 1 6 2.5Z" />
      <path d="M14 2.5V7h4.5" />
      <path d="M8.5 17v-3M12 17v-6M15.5 17v-4" />
    </g>
  ),
  gear: (
    <g {...outline}>
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5V21a2 2 0 0 1-4 0v-.1a1.6 1.6 0 0 0-1-1.5 1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H3a2 2 0 0 1 0-4h.1a1.6 1.6 0 0 0 1.5-1 1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H9a1.6 1.6 0 0 0 1-1.5V3a2 2 0 0 1 4 0v.1a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V9a1.6 1.6 0 0 0 1.5 1H21a2 2 0 0 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1Z" />
    </g>
  ),
  menu: (
    <g {...outline}>
      <path d="M4 6.5h16M4 12h16M4 17.5h16" />
    </g>
  ),
  'chevrons-left': (
    <g {...outline}>
      <path d="m11.5 17-5-5 5-5M18 17l-5-5 5-5" />
    </g>
  ),
  'chevrons-right': (
    <g {...outline}>
      <path d="m12.5 7 5 5-5 5M6 7l5 5-5 5" />
    </g>
  ),
  close: (
    <g {...outline}>
      <path d="M6 6l12 12M18 6 6 18" />
    </g>
  ),
  alert: (
    <g {...outline}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7.5v5.5M12 16.4v.1" />
    </g>
  ),
  inbox: (
    <g {...outline}>
      <path d="M3.5 13.5 6 5.5a1.5 1.5 0 0 1 1.4-1h9.2a1.5 1.5 0 0 1 1.4 1l2.5 8" />
      <path d="M3.5 13.5V18a2 2 0 0 0 2 2h13a2 2 0 0 0 2-2v-4.5H16l-1.5 2.5h-5L8 13.5Z" />
    </g>
  ),
  dot: <circle {...solid} cx="12" cy="12" r="4" />,
  'arrow-left': (
    <g {...outline}>
      <path d="M19 12H5M11 6l-6 6 6 6" />
    </g>
  ),
  clock: (
    <g {...outline}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3.5 2" />
    </g>
  ),
  pencil: (
    <g {...outline}>
      <path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16v4Z" />
      <path d="m13.5 6.5 4 4" />
    </g>
  ),
  ban: (
    <g {...outline}>
      <circle cx="12" cy="12" r="9" />
      <path d="m5.7 5.7 12.6 12.6" />
    </g>
  ),
  plus: (
    <g {...outline}>
      <path d="M12 5v14M5 12h14" />
    </g>
  ),
  half: (
    <g>
      <circle {...outline} cx="12" cy="12" r="9" />
      <path {...solid} d="M12 3a9 9 0 0 1 0 18V3Z" />
    </g>
  ),
  stamp: (
    <g {...outline}>
      <path d="M9.5 13 9 8.5a3 3 0 1 1 6 0l-.5 4.5" />
      <path d="M5 13h14v4H5zM6.5 20h11" />
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
