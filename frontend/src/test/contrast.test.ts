import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The tokens meet WCAG 2.2 AA in both themes (ADR 0038, point 7): text at least 4.5:1 against every surface it may stand
 * on (1.4.3), and borders, the focus ring and the chart series at least 3:1 (1.4.11). Computed from tokens.css itself.
 */
const tokensCss = readFileSync(join(process.cwd(), 'src', 'theme', 'tokens.css'), 'utf8');

function tokensOf(selector: string): Record<string, string> {
  const start = tokensCss.indexOf(selector);
  const block = tokensCss.slice(tokensCss.indexOf('{', start) + 1, tokensCss.indexOf('}', start));
  const tokens: Record<string, string> = {};
  for (const [, name, value] of block.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-f]{6});/gi)) {
    if (name !== undefined && value !== undefined) {
      tokens[name] = value;
    }
  }

  return tokens;
}

function luminance(hex: string): number {
  const channel = (offset: number) => {
    const value = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };

  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

export function contrast(first: string, second: string): number {
  const [lighter, darker] = [luminance(first), luminance(second)].sort((a, b) => b - a) as [number, number];
  return (lighter + 0.05) / (darker + 0.05);
}

const themes = { light: tokensOf(":root[data-theme='light']"), dark: tokensOf(":root[data-theme='dark']") };
const surfaces = ['surface', 'surface-raised', 'surface-sunken'];
const texts = ['text', 'text-muted', 'accent', 'success', 'warning', 'danger'];
const marks = ['border', 'focus'];
const series = ['series-1', 'series-2', 'series-3', 'series-4', 'series-5', 'series-6'];

describe.each(Object.entries(themes))('the %s theme', (_, tokens) => {
  const pairs = (foregrounds: string[], backgrounds: string[]) =>
    foregrounds.flatMap((foreground) => backgrounds.map((background) => [foreground, background] as const));

  it('defines every token', () => {
    for (const name of [...surfaces, ...texts, ...marks, ...series, 'on-accent']) {
      expect(tokens[name], `--${name}`).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });

  it.each(pairs(texts, surfaces))('%s text on %s is at least 4.5:1', (foreground, background) => {
    expect(contrast(tokens[foreground] ?? '', tokens[background] ?? '')).toBeGreaterThanOrEqual(4.5);
  });

  it('on-accent text on the accent is at least 4.5:1', () => {
    expect(contrast(tokens['on-accent'] ?? '', tokens.accent ?? '')).toBeGreaterThanOrEqual(4.5);
  });

  it.each(pairs(marks, surfaces))('%s against %s is at least 3:1', (foreground, background) => {
    expect(contrast(tokens[foreground] ?? '', tokens[background] ?? '')).toBeGreaterThanOrEqual(3);
  });

  // Since Prompt 9.3 (ADR 0049): text and its meaning on the soft grounds of a banner, a meter and a changed total.
  it.each([
    ['text', 'accent-soft'],
    ['text-muted', 'accent-soft'],
    ['accent', 'accent-soft'],
    ['text', 'success-soft'],
    ['success', 'success-soft'],
    ['text', 'warning-soft'],
    ['warning', 'warning-soft'],
    ['text', 'danger-soft'],
    ['danger', 'danger-soft'],
  ])('%s text on %s is at least 4.5:1', (foreground, background) => {
    expect(tokens[background], `--${background}`).toMatch(/^#[0-9a-f]{6}$/i);
    expect(contrast(tokens[foreground] ?? '', tokens[background] ?? '')).toBeGreaterThanOrEqual(4.5);
  });

  // Since Prompt 9.4 (ADR 0050): a tooltip's text on its ground, and its edge against every surface it floats over.
  it.each(['text', 'text-muted', 'accent', 'success', 'danger', 'warning'])(
    '%s text on surface-popover is at least 4.5:1',
    (foreground) => {
      expect(tokens['surface-popover'], '--surface-popover').toMatch(/^#[0-9a-f]{6}$/i);
      expect(contrast(tokens[foreground] ?? '', tokens['surface-popover'] ?? '')).toBeGreaterThanOrEqual(4.5);
    },
  );

  it.each([...surfaces, 'surface-popover'])('border-strong against %s is at least 3:1', (background) => {
    expect(contrast(tokens['border-strong'] ?? '', tokens[background] ?? '')).toBeGreaterThanOrEqual(3);
  });

  it.each(pairs(series, ['surface', 'surface-raised']))('%s against %s is at least 3:1', (foreground, background) => {
    expect(contrast(tokens[foreground] ?? '', tokens[background] ?? '')).toBeGreaterThanOrEqual(3);
  });
});

// Since Prompt 9.1 (ADR 0047): the pairs the sign-in page and the restyled controls added.
describe.each(Object.entries(themes))('the %s theme, since the sign-in page of 9.1', (_, tokens) => {
  const ratio = (foreground: string, background: string) =>
    contrast(tokens[foreground] ?? '', tokens[background] ?? '');

  it('the placeholder of a field is at least 4.5:1 on the field', () => {
    expect(ratio('placeholder', 'surface-raised')).toBeGreaterThanOrEqual(4.5);
  });

  it('text on the pressed accent is at least 4.5:1', () => {
    expect(ratio('on-accent', 'accent-strong')).toBeGreaterThanOrEqual(4.5);
  });

  // The tint is darker than its waves wherever text stands on it, so the waves are the lightest ground under white text.
  it.each(['signin-wave-front', 'signin-wave-back'])(
    'white text on the blue of the brand (%s) is at least 4.5:1',
    (ground) => {
      expect(ratio('brand-on-tint', ground)).toBeGreaterThanOrEqual(4.5);
    },
  );

  it('the outline icons on the blue of the brand are at least 3:1', () => {
    expect(ratio('brand-icon-on-tint', 'signin-wave-back')).toBeGreaterThanOrEqual(3);
  });

  it.each(['brand-pulse', 'brand-label-icon', 'brand-cross'])('%s on the card is at least 3:1', (mark) => {
    expect(ratio(mark, 'surface-raised')).toBeGreaterThanOrEqual(3);
  });
});

describe('the plate of the company, the same in both themes', () => {
  const company = tokensOf(':root {');

  it.each(['company-ink', 'company-accent'])('%s on the plate is at least 4.5:1', (text) => {
    expect(contrast(company[text] ?? '', company['company-plate'] ?? '')).toBeGreaterThanOrEqual(4.5);
  });
});

describe('the contrast formula', () => {
  it('gives the known ratios', () => {
    expect(contrast('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrast('#777777', '#ffffff')).toBeCloseTo(4.48, 2);
    expect(contrast('#ffffff', '#ffffff')).toBe(1);
  });
});

// Since Prompt 10.4c (ADR 0059): the paper on which the letterhead's images are shown, the same in both themes.
describe.each(Object.entries(themes))('the %s theme, since the letterhead images of 10.4c', (_, tokens) => {
  it('what is written on paper is at least 4.5:1 against it', () => {
    expect(contrast(tokens['paper-ink'] ?? '', tokens.paper ?? '')).toBeGreaterThanOrEqual(4.5);
  });

  // The sheet is told from the card it lies on by its own ground, or, where both are white, by its edge.
  it('the paper is told from the card it lies on, by its ground or by its edge, at 3:1', () => {
    const byGround = contrast(tokens.paper ?? '', tokens['surface-raised'] ?? '');
    const byEdge = contrast(tokens['border-strong'] ?? '', tokens.paper ?? '');

    expect(Math.max(byGround, byEdge)).toBeGreaterThanOrEqual(3);
  });

  it('paper is white, as a printed sheet is', () => {
    expect(tokens.paper).toBe('#ffffff');
  });
});

// Since Prompt 10.4c (ADR 0059): a sheet previewed on the screen is paper, in either theme.
describe('a sheet of paper on the screen', () => {
  const onPaper = tokensOf('[data-paper]');
  const printed = tokensOf('@media print');

  it('takes the very tokens of the print styles, so that the preview is what prints', () => {
    expect(Object.keys(onPaper).length).toBeGreaterThanOrEqual(8);
    for (const [name, value] of Object.entries(onPaper)) {
      expect(printed[name], name).toBe(value);
    }
  });

  it.each(['text', 'text-muted'])('%s is at least 4.5:1 on the sheet and on what is sunk into it', (text) => {
    for (const ground of ['surface', 'surface-raised', 'surface-sunken']) {
      expect(contrast(onPaper[text] ?? '', onPaper[ground] ?? '')).toBeGreaterThanOrEqual(4.5);
    }
  });

  it('the lines that carry meaning are at least 3:1 on the sheet', () => {
    for (const line of ['border', 'border-strong']) {
      expect(contrast(onPaper[line] ?? '', onPaper['surface-raised'] ?? '')).toBeGreaterThanOrEqual(3);
    }
  });
});
