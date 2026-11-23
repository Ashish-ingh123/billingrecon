import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * No raw colour value outside the design tokens (ADR 0038, point 7): every colour of a component comes from a variable
 * of src/theme/tokens.css, so both themes hold everywhere. This scans index.html and every style and script file of
 * src/ but tokens.css and the tests, and fails with the file and line of each colour it finds.
 */
// The tests run in frontend/ (npm test), whose files these are.
const root = process.cwd();

// The CSS named colours (CSS Color Module Level 4), which a style may not use either.
const namedColours = new Set(
  (
    'aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood ' +
    'cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray ' +
    'darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen ' +
    'darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue ' +
    'firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew ' +
    'hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan ' +
    'lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray ' +
    'lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue ' +
    'mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred ' +
    'midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid ' +
    'palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple ' +
    'rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue ' +
    'slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato turquoise violet wheat white ' +
    'whitesmoke yellow yellowgreen canvas canvastext linktext visitedtext activetext buttonface buttontext buttonborder ' +
    'field fieldtext highlight highlighttext mark marktext graytext accentcolor accentcolortext'
  ).split(' '),
);

const colourProperty =
  /^(color|background|background-color|border|border-(top|right|bottom|left|block|inline)(-(start|end))?(-color)?|border-color|outline|outline-color|fill|stroke|box-shadow|text-shadow|text-decoration|text-decoration-color|caret-color|accent-color|column-rule|column-rule-color|stop-color|flood-color|lighting-color)$/;

const hexColour = /#[0-9a-f]{3,8}\b/i;
const colourFunction = /\b(rgba?|hsla?|hwb|lab|lch|oklab|oklch|color)\(/i;
const quotedHex = /['"`]#(?:[0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})['"`]/i;

/** The colours a CSS text declares outside custom properties, as "line: declaration". */
export function coloursInCss(css: string): string[] {
  const found: string[] = [];
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\n]/g, ' '));
  const declaration = /([a-z-]+)\s*:\s*([^;{}]+)/gi;
  for (const match of withoutComments.matchAll(declaration)) {
    const [whole, property = '', value = ''] = match;
    if (property.startsWith('--')) {
      continue;
    }

    const words = value.toLowerCase().match(/[a-z]+/g) ?? [];
    const named = colourProperty.test(property.toLowerCase()) && words.some((word) => namedColours.has(word));
    if (hexColour.test(value) || colourFunction.test(value) || named) {
      const line = withoutComments.slice(0, match.index).split('\n').length;
      found.push(`${line}: ${whole.trim()}`);
    }
  }

  return found;
}

/** The colours a script or HTML text holds as literals, as "line: text". */
export function coloursInCode(code: string): string[] {
  return code
    .split('\n')
    .map((text, index) => [index + 1, text] as const)
    .filter(
      ([, text]) =>
        quotedHex.test(text) || colourFunction.test(text) || /style\s*=\s*["'][^"']*#[0-9a-f]{3}/i.test(text),
    )
    .map(([line, text]) => `${line}: ${text.trim()}`);
}

function sourceFiles(): string[] {
  const src = join(root, 'src');
  const files = (readdirSync(src, { recursive: true }) as string[])
    .map((file) => join(src, file))
    .filter((file) => /\.(css|ts|tsx)$/.test(file))
    .filter((file) => !/\.test\.(ts|tsx)$/.test(file))
    .filter((file) => !file.includes(`${sep}test${sep}`))
    .filter((file) => !file.endsWith(join('theme', 'tokens.css')))
    .filter((file) => !file.endsWith('.d.ts'));

  return [join(root, 'index.html'), ...files];
}

describe('colours come from the tokens only', () => {
  it('finds a colour wherever it is written', () => {
    expect(coloursInCss('a { color: red; }')).toHaveLength(1);
    expect(coloursInCss('a {\n  background: #fff;\n}')).toEqual(['2: background: #fff']);
    expect(coloursInCss('a { fill: rgb(0 0 0); }')).toHaveLength(1);
    expect(coloursInCss('a { border: 1px solid oklch(0.5 0.1 200); }')).toHaveLength(1);
    expect(coloursInCode("const danger = '#b0261d';")).toHaveLength(1);
    expect(coloursInCode('<Line stroke="hsl(10 50% 50%)" />')).toHaveLength(1);
  });

  it('lets a token, a keyword or a mix of tokens through', () => {
    expect(coloursInCss('a { color: var(--text); background: transparent; border-color: currentColor; }')).toEqual([]);
    expect(coloursInCss('a { background: color-mix(in srgb, var(--accent) 20%, var(--surface)); }')).toEqual([]);
    expect(coloursInCss('a { font-family: system-ui; grid-template-areas: "top"; }')).toEqual([]);
    expect(coloursInCss('/* white */ a { color: var(--text); }')).toEqual([]);
    expect(coloursInCode('<a href="#content">Skip</a>')).toEqual([]);
  });

  it('holds in every file but tokens.css', () => {
    const files = sourceFiles();
    expect(files.length).toBeGreaterThan(10);

    const offences = files.flatMap((file) => {
      const text = readFileSync(file, 'utf8');
      const found = file.endsWith('.css') ? coloursInCss(text) : coloursInCode(text);
      return found.map((where) => `${relative(root, file)}:${where}`);
    });

    expect(offences, 'Use a token of src/theme/tokens.css instead.').toEqual([]);
  });
});
