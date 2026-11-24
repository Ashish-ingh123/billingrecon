import { useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';

/**
 * What a Recharts chart needs for its tooltip to behave as every tooltip of the dashboard does (ADR 0050): Recharts
 * opens it on hover and moves it with the arrow keys once the chart has the focus (its accessibility layer); this closes
 * it on Escape and on blur, and makes a second tap close what the first opened.
 */
export function useChartTip() {
  const [hidden, setHidden] = useState(false);
  const tapped = useRef(false);

  return {
    /** Given to the chart's `<Tooltip active>`: undefined lets Recharts decide, false hides it. */
    active: hidden ? false : undefined,
    /** Spread on the element around the chart. */
    handlers: {
      onKeyDown: (event: KeyboardEvent) => {
        if (event.key === 'Escape') {
          setHidden(true);
        } else if (event.key.startsWith('Arrow')) {
          setHidden(false);
        }
      },
      onFocus: () => setHidden(false),
      onBlur: () => {
        tapped.current = false;
        setHidden(true);
      },
      onPointerDown: (event: PointerEvent) => {
        if (event.pointerType === 'mouse') return;
        // The first tap shows the point under the finger; a second one closes the tooltip.
        setHidden(tapped.current);
        tapped.current = !tapped.current;
      },
      onPointerMove: (event: PointerEvent) => {
        if (event.pointerType === 'mouse') setHidden(false);
      },
    },
  };
}
