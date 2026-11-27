import { describe, expect, it } from 'vitest';
import { errorCorrection, formatBits, qrCode, qrPath, versionBits, type QrCode } from './qr';

/**
 * The QR code drawn for the enrolment of a second step (ADR 0061), held to what ISO/IEC 18004 fixes: the error
 * correction of the standard's worked example, the format and version words of its tables, the fixed patterns of a
 * symbol, and a reading of the drawn symbol back into its text by a reader written here from the standard's rules.
 * What no test here can show is that a phone's camera reads it; the key is shown as text beside every code.
 */

const maskOf = [
  (x: number, y: number) => (x + y) % 2 === 0,
  (_x: number, y: number) => y % 2 === 0,
  (x: number) => x % 3 === 0,
  (x: number, y: number) => (x + y) % 3 === 0,
  (x: number, y: number) => (Math.floor(y / 2) + Math.floor(x / 3)) % 2 === 0,
  (x: number, y: number) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x: number, y: number) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x: number, y: number) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
];
const alignmentOf = [
  [],
  [6, 18],
  [6, 22],
  [6, 26],
  [6, 30],
  [6, 34],
  [6, 22, 38],
  [6, 24, 42],
  [6, 26, 46],
  [6, 28, 50],
];
// Level M, versions 1 to 10: [error correction codewords a block, [blocks, data codewords]...].
const blocksOf: [number, [number, number][]][] = [
  [10, [[1, 16]]],
  [16, [[1, 28]]],
  [26, [[1, 44]]],
  [18, [[2, 32]]],
  [24, [[2, 43]]],
  [16, [[4, 27]]],
  [18, [[4, 31]]],
  [
    22,
    [
      [2, 38],
      [2, 39],
    ],
  ],
  [
    22,
    [
      [3, 36],
      [2, 37],
    ],
  ],
  [
    26,
    [
      [4, 43],
      [1, 44],
    ],
  ],
];

/** Reads a drawn symbol back: the format beside the top-left finder, the mask taken off, the codewords, the bytes. */
function read(code: QrCode): { text: string; level: number; mask: number } {
  const { size, dark, version } = code;
  const at = (x: number, y: number) => (dark[y]![x] ? 1 : 0);

  // The fifteen bits of the format, most significant first, as they stand around the top-left finder.
  const places: [number, number][] = [
    [0, 8],
    [1, 8],
    [2, 8],
    [3, 8],
    [4, 8],
    [5, 8],
    [7, 8],
    [8, 8],
    [8, 7],
    [8, 5],
    [8, 4],
    [8, 3],
    [8, 2],
    [8, 1],
    [8, 0],
  ];
  const format = places.reduce((value, [x, y]) => (value << 1) | at(x, y), 0) ^ 0x5412;
  const level = format >> 13;
  const mask = (format >> 10) & 7;

  // What is no data: the finders with their separators and format, the timing, the alignment, the version.
  const fixed = (x: number, y: number) => {
    if ((x <= 8 && y <= 8) || (x >= size - 8 && y <= 8) || (x <= 8 && y >= size - 8)) return true;
    if (x === 6 || y === 6) return true;
    if (version >= 7 && ((x < 6 && y >= size - 11) || (y < 6 && x >= size - 11))) return true;
    const centres = alignmentOf[version - 1]!;
    return centres.some((cy) =>
      centres.some(
        (cx) =>
          !(cx === 6 && cy === 6) &&
          !(cx === 6 && cy === size - 7) &&
          !(cx === size - 7 && cy === 6) &&
          Math.abs(x - cx) <= 2 &&
          Math.abs(y - cy) <= 2,
      ),
    );
  };

  const bits: number[] = [];
  let upwards = true;
  for (let right = size - 1; right > 0; right -= 2) {
    if (right === 6) right = 5;
    for (let step = 0; step < size; step += 1) {
      const y = upwards ? size - 1 - step : step;
      for (const x of [right, right - 1]) {
        if (!fixed(x, y)) bits.push(at(x, y) ^ (maskOf[mask]!(x, y) ? 1 : 0));
      }
    }
    upwards = !upwards;
  }

  const codewords: number[] = [];
  for (let index = 0; index + 8 <= bits.length; index += 8) {
    codewords.push(bits.slice(index, index + 8).reduce((value, bit) => (value << 1) | bit, 0));
  }

  // The data codewords out of their interleaving.
  const [, groups] = blocksOf[version - 1]!;
  const sizes = groups.flatMap(([count, length]) => new Array<number>(count).fill(length));
  const blocks = sizes.map(() => [] as number[]);
  let next = 0;
  for (let index = 0; index < Math.max(...sizes); index += 1) {
    sizes.forEach((length, block) => {
      if (index < length) blocks[block]!.push(codewords[next++]!);
    });
  }

  const data = blocks.flat();
  const stream = data.flatMap((codeword) => [7, 6, 5, 4, 3, 2, 1, 0].map((bit) => (codeword >> bit) & 1));
  const take = (from: number, length: number) =>
    stream.slice(from, from + length).reduce((value, bit) => (value << 1) | bit, 0);
  expect(take(0, 4), 'byte mode').toBe(0b0100);
  const countBits = version < 10 ? 8 : 16;
  const length = take(4, countBits);
  const bytes = new Uint8Array(length);
  for (let index = 0; index < length; index += 1) {
    bytes[index] = take(4 + countBits + index * 8, 8);
  }

  return { text: new TextDecoder().decode(bytes), level, mask };
}

describe('the QR code of an enrolment', () => {
  it("computes the error correction of the standard's worked example", () => {
    // "HELLO WORLD" at 1-M: the sixteen data codewords and their ten error correction codewords.
    const data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];

    expect(errorCorrection(data, 10)).toEqual([196, 35, 39, 119, 235, 215, 231, 226, 93, 23]);
  });

  it("writes the format and version words of the standard's tables", () => {
    expect(formatBits(0).toString(2).padStart(15, '0')).toBe('101010000010010');
    expect(formatBits(1).toString(2).padStart(15, '0')).toBe('101000100100101');
    expect(formatBits(7).toString(2).padStart(15, '0')).toBe('100101010100000');
    expect(versionBits(7).toString(2).padStart(18, '0')).toBe('000111110010010100');
    expect(versionBits(8).toString(2).padStart(18, '0')).toBe('001000010110111100');
  });

  it('draws the fixed patterns of a symbol: the three finders, the timing and the dark module', () => {
    const code = qrCode('otpauth://totp/BillingRecon:asha%40example.org?secret=JBSWY3DPEHPK3PXP');
    const { size, dark } = code;
    const finder = ['1111111', '1000001', '1011101', '1011101', '1011101', '1000001', '1111111'];
    for (const [left, top] of [
      [0, 0],
      [size - 7, 0],
      [0, size - 7],
    ] as const) {
      const drawn = finder.map((_, y) =>
        dark[top + y]!.slice(left, left + 7)
          .map((on) => (on ? '1' : '0'))
          .join(''),
      );
      expect(drawn).toEqual(finder);
    }

    expect(size).toBe(17 + 4 * code.version);
    for (let index = 8; index < size - 8; index += 1) {
      expect(dark[6]![index]).toBe(index % 2 === 0);
      expect(dark[index]![6]).toBe(index % 2 === 0);
    }
    expect(dark[size - 8]![8]).toBe(true);
    // The separators of the finders are light.
    expect(dark[7]!.slice(0, 8).some(Boolean)).toBe(false);
    expect(dark[size - 8]!.slice(0, 8).some(Boolean)).toBe(false);
  });

  it.each([
    ['a short text', 'BillingRecon'],
    [
      'an enrolment address',
      'otpauth://totp/BillingRecon:asha%40example.org?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=BillingRecon&algorithm=SHA1&digits=6&period=30',
    ],
    [
      'a long address, which needs the version words',
      `otpauth://totp/BillingRecon:${'a.very.long.name.of.a.person.'.repeat(1)}%40cityhospital.example.org?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=BillingRecon&algorithm=SHA1&digits=6&period=30`,
    ],
    ['text beyond ASCII', 'रसीद ₹1,250.50'],
  ])('is read back as the text it was drawn from: %s', (_name, text) => {
    const code = qrCode(text);
    const back = read(code);

    expect(back.text).toBe(text);
    // Level M is 00 in the format.
    expect(back.level).toBe(0);
    expect(back.mask).toBeGreaterThanOrEqual(0);
  });

  it('takes the smallest version that holds the text, and refuses a text no version here holds', () => {
    expect(qrCode('a'.repeat(14)).version).toBe(1);
    expect(qrCode('a'.repeat(15)).version).toBe(2);
    expect(qrCode('a'.repeat(120)).version).toBe(7);
    expect(qrCode('a'.repeat(211)).version).toBe(10);
    expect(qrCode('a'.repeat(287)).version).toBe(12);
    expect(() => qrCode('a'.repeat(288))).toThrow(RangeError);
  });

  it('draws the same symbol for the same text, as a path with a quiet zone of four modules', () => {
    const first = qrCode('BillingRecon');
    const path = qrPath(first);

    expect(qrPath(qrCode('BillingRecon'))).toBe(path);
    // The top-left module of the finder stands four modules in.
    expect(path.startsWith('M4,4h1v1h-1z')).toBe(true);
    expect(path.split('M').length - 1).toBe(first.dark.flat().filter(Boolean).length);
  });
});
