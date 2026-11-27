/**
 * A QR code of a short text, drawn in the page with no package (ADR 0061): the address an authenticator app reads when
 * a second step is enrolled. Byte mode, error correction level M, versions 1 to 12 (up to 287 bytes), the mask chosen
 * by the penalty rules of ISO/IEC 18004. The key is always shown as text beside the code, so a code that some phone
 * will not read never stops an enrolment.
 */

/** A drawn code: `size` modules a side, `dark[y][x]` true for a dark module. The quiet zone is the drawer's to add. */
export interface QrCode {
  version: number;
  size: number;
  dark: boolean[][];
}

/** For level M, by version: the error correction codewords of a block, and the blocks as [count, data codewords]. */
const blocksM: readonly (readonly [number, readonly (readonly [number, number])[]])[] = [
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
  [
    30,
    [
      [1, 50],
      [4, 51],
    ],
  ],
  [
    22,
    [
      [6, 36],
      [2, 37],
    ],
  ],
];

/** The centres of the alignment patterns, by version. */
const alignment: readonly (readonly number[])[] = [
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
  [6, 30, 54],
  [6, 32, 58],
];

// The field of 256 elements the codewords are computed in: x^8 + x^4 + x^3 + x^2 + 1.
const exp = new Uint8Array(512);
const log = new Uint8Array(256);
for (let power = 0, value = 1; power < 255; power += 1) {
  exp[power] = value;
  log[value] = power;
  value <<= 1;
  if (value & 0x100) {
    value ^= 0x11d;
  }
}
for (let power = 255; power < 512; power += 1) {
  exp[power] = exp[power - 255]!;
}

function multiply(a: number, b: number): number {
  return a === 0 || b === 0 ? 0 : exp[log[a]! + log[b]!]!;
}

/** The error correction codewords of a block of data: the remainder of its division by the generator of that degree. */
export function errorCorrection(data: readonly number[], count: number): number[] {
  let generator = [1];
  for (let degree = 0; degree < count; degree += 1) {
    const next = new Array<number>(generator.length + 1).fill(0);
    for (let index = 0; index < generator.length; index += 1) {
      next[index] = next[index]! ^ generator[index]!;
      next[index + 1] = next[index + 1]! ^ multiply(generator[index]!, exp[degree]!);
    }
    generator = next;
  }

  const remainder = new Array<number>(count).fill(0);
  for (const value of data) {
    const factor = value ^ remainder[0]!;
    remainder.shift();
    remainder.push(0);
    for (let index = 0; index < count; index += 1) {
      remainder[index] = remainder[index]! ^ multiply(generator[index + 1]!, factor);
    }
  }

  return remainder;
}

/** The 15 bits of the format of level M with a mask: five bits of data and the ten of their BCH code, masked. */
export function formatBits(mask: number): number {
  const data = mask; // Level M is 00, so the five bits are the mask's three.
  let remainder = data << 10;
  for (let bit = 14; bit >= 10; bit -= 1) {
    if ((remainder >> bit) & 1) {
      remainder ^= 0x537 << (bit - 10);
    }
  }

  return ((data << 10) | remainder) ^ 0x5412;
}

/** The 18 bits of a version from 7 on: six bits of data and the twelve of their BCH code. */
export function versionBits(version: number): number {
  let remainder = version << 12;
  for (let bit = 17; bit >= 12; bit -= 1) {
    if ((remainder >> bit) & 1) {
      remainder ^= 0x1f25 << (bit - 12);
    }
  }

  return (version << 12) | remainder;
}

function dataCapacity(version: number): number {
  return blocksM[version - 1]![1].reduce((sum, [count, size]) => sum + count * size, 0);
}

/** The codewords of the text for a version: the mode, the count, the bytes, the terminator and the padding. */
function dataCodewords(bytes: Uint8Array, version: number): number[] {
  const capacity = dataCapacity(version);
  const bits: number[] = [];
  const push = (value: number, length: number) => {
    for (let bit = length - 1; bit >= 0; bit -= 1) {
      bits.push((value >> bit) & 1);
    }
  };

  push(0b0100, 4);
  push(bytes.length, version < 10 ? 8 : 16);
  for (const byte of bytes) {
    push(byte, 8);
  }

  push(0, Math.min(4, capacity * 8 - bits.length));
  while (bits.length % 8 !== 0) {
    bits.push(0);
  }

  const codewords: number[] = [];
  for (let index = 0; index < bits.length; index += 8) {
    codewords.push(bits.slice(index, index + 8).reduce((value, bit) => (value << 1) | bit, 0));
  }

  for (let pad = 0; codewords.length < capacity; pad += 1) {
    codewords.push(pad % 2 === 0 ? 0xec : 0x11);
  }

  return codewords;
}

/** The data and its error correction, block by block and then interleaved, as the symbol carries them. */
function interleaved(data: readonly number[], version: number): number[] {
  const [ecCount, groups] = blocksM[version - 1]!;
  const blocks: number[][] = [];
  const corrections: number[][] = [];
  let offset = 0;
  for (const [count, size] of groups) {
    for (let block = 0; block < count; block += 1) {
      const part = data.slice(offset, offset + size);
      offset += size;
      blocks.push(part);
      corrections.push(errorCorrection(part, ecCount));
    }
  }

  const result: number[] = [];
  const longest = Math.max(...blocks.map((block) => block.length));
  for (let index = 0; index < longest; index += 1) {
    for (const block of blocks) {
      if (index < block.length) {
        result.push(block[index]!);
      }
    }
  }

  for (let index = 0; index < ecCount; index += 1) {
    for (const correction of corrections) {
      result.push(correction[index]!);
    }
  }

  return result;
}

const masks: readonly ((x: number, y: number) => boolean)[] = [
  (x, y) => (x + y) % 2 === 0,
  (_x, y) => y % 2 === 0,
  (x) => x % 3 === 0,
  (x, y) => (x + y) % 3 === 0,
  (x, y) => (Math.floor(y / 2) + Math.floor(x / 3)) % 2 === 0,
  (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
];

/** The four penalties of ISO/IEC 18004 section 7.8.3 for a finished symbol: the lower, the easier to read. */
function penalty(dark: boolean[][]): number {
  const size = dark.length;
  let score = 0;

  // 1: runs of five or more modules of one colour in a row or a column.
  for (let a = 0; a < size; a += 1) {
    for (const line of [dark[a]!, dark.map((row) => row[a]!)]) {
      let run = 1;
      for (let b = 1; b <= size; b += 1) {
        if (b < size && line[b] === line[b - 1]) {
          run += 1;
        } else {
          if (run >= 5) {
            score += run - 2;
          }
          run = 1;
        }
      }
    }
  }

  // 2: blocks of two by two modules of one colour.
  for (let y = 0; y < size - 1; y += 1) {
    for (let x = 0; x < size - 1; x += 1) {
      const colour = dark[y]![x];
      if (dark[y]![x + 1] === colour && dark[y + 1]![x] === colour && dark[y + 1]![x + 1] === colour) {
        score += 3;
      }
    }
  }

  // 3: the pattern of a finder (1:1:3:1:1) with four light modules on either side, in a row or a column.
  const finder = [true, false, true, true, true, false, true];
  const matches = (line: readonly boolean[], start: number) => {
    for (let index = 0; index < 7; index += 1) {
      if (line[start + index] !== finder[index]) {
        return false;
      }
    }

    const light = (from: number) => [0, 1, 2, 3].every((step) => line[from + step] === false);
    return (start >= 4 && light(start - 4)) || (start + 11 <= line.length && light(start + 7));
  };
  for (let a = 0; a < size; a += 1) {
    for (const line of [dark[a]!, dark.map((row) => row[a]!)]) {
      for (let start = 0; start + 7 <= size; start += 1) {
        if (matches(line, start)) {
          score += 40;
        }
      }
    }
  }

  // 4: how far the share of dark modules is from a half.
  const darkCount = dark.reduce((sum, row) => sum + row.filter(Boolean).length, 0);
  score += Math.floor(Math.abs((darkCount * 100) / (size * size) - 50) / 5) * 10;

  return score;
}

/**
 * The QR code of `text` in UTF-8. Throws when the text is longer than version 12 holds at level M (287 bytes): the
 * caller shows the key as text alone then.
 */
export function qrCode(text: string): QrCode {
  const bytes = new TextEncoder().encode(text);
  let version = 1;
  while (version <= 12 && bytes.length + (version < 10 ? 2 : 3) > dataCapacity(version)) {
    version += 1;
  }

  if (version > 12) {
    throw new RangeError('The text is too long for a QR code of version 12 at level M.');
  }

  const size = 17 + 4 * version;
  const dark = Array.from({ length: size }, () => new Array<boolean>(size).fill(false));
  const fixed = Array.from({ length: size }, () => new Array<boolean>(size).fill(false));
  const set = (x: number, y: number, value: boolean) => {
    dark[y]![x] = value;
    fixed[y]![x] = true;
  };

  // The three finder patterns with their separators.
  for (const [left, top] of [
    [0, 0],
    [size - 7, 0],
    [0, size - 7],
  ] as const) {
    for (let dy = -1; dy <= 7; dy += 1) {
      for (let dx = -1; dx <= 7; dx += 1) {
        const x = left + dx;
        const y = top + dy;
        if (x < 0 || y < 0 || x >= size || y >= size) {
          continue;
        }

        const ring = Math.max(Math.abs(dx - 3), Math.abs(dy - 3));
        set(x, y, ring !== 2 && ring <= 3);
      }
    }
  }

  // The alignment patterns, where no finder is.
  const centres = alignment[version - 1]!;
  for (const cy of centres) {
    for (const cx of centres) {
      if (fixed[cy]![cx]) {
        continue;
      }

      for (let dy = -2; dy <= 2; dy += 1) {
        for (let dx = -2; dx <= 2; dx += 1) {
          set(cx + dx, cy + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
        }
      }
    }
  }

  // The timing patterns and the dark module.
  for (let index = 8; index < size - 8; index += 1) {
    if (!fixed[6]![index]) {
      set(index, 6, index % 2 === 0);
    }

    if (!fixed[index]![6]) {
      set(6, index, index % 2 === 0);
    }
  }
  set(8, size - 8, true);

  // The places of the format and, from version 7 on, of the version: kept free of data, written below.
  for (let index = 0; index < 9; index += 1) {
    if (!fixed[8]![index]) {
      set(index, 8, false);
    }

    if (!fixed[index]![8]) {
      set(8, index, false);
    }
  }
  for (let index = 0; index < 8; index += 1) {
    set(size - 1 - index, 8, false);
    if (!fixed[size - 1 - index]![8]) {
      set(8, size - 1 - index, false);
    }
  }
  if (version >= 7) {
    const bits = versionBits(version);
    for (let index = 0; index < 18; index += 1) {
      const value = ((bits >> index) & 1) === 1;
      const a = Math.floor(index / 3);
      const b = size - 11 + (index % 3);
      set(a, b, value);
      set(b, a, value);
    }
  }

  // The codewords, from the bottom right upwards in columns of two, skipping the vertical timing pattern.
  const codewords = interleaved(dataCodewords(bytes, version), version);
  const stream: number[] = [];
  for (const codeword of codewords) {
    for (let bit = 7; bit >= 0; bit -= 1) {
      stream.push((codeword >> bit) & 1);
    }
  }

  const data: [number, number][] = [];
  let next = 0;
  let upwards = true;
  for (let right = size - 1; right > 0; right -= 2) {
    if (right === 6) {
      right = 5;
    }

    for (let step = 0; step < size; step += 1) {
      const y = upwards ? size - 1 - step : step;
      for (const x of [right, right - 1]) {
        if (!fixed[y]![x]) {
          dark[y]![x] = (stream[next] ?? 0) === 1;
          next += 1;
          data.push([x, y]);
        }
      }
    }
    upwards = !upwards;
  }

  // Each mask in turn, with its format; the one with the lowest penalty is kept.
  const writeFormat = (target: boolean[][], mask: number) => {
    const bits = formatBits(mask);
    const bit = (index: number) => ((bits >> index) & 1) === 1;
    for (let index = 0; index <= 5; index += 1) {
      target[index]![8] = bit(index);
    }
    target[7]![8] = bit(6);
    target[8]![8] = bit(7);
    target[8]![7] = bit(8);
    for (let index = 9; index <= 14; index += 1) {
      target[8]![14 - index] = bit(index);
    }
    for (let index = 0; index <= 7; index += 1) {
      target[8]![size - 1 - index] = bit(index);
    }
    for (let index = 8; index <= 14; index += 1) {
      target[size - 15 + index]![8] = bit(index);
    }
  };

  let best: boolean[][] | null = null;
  let lowest = Number.POSITIVE_INFINITY;
  for (let mask = 0; mask < masks.length; mask += 1) {
    const candidate = dark.map((row) => [...row]);
    for (const [x, y] of data) {
      if (masks[mask]!(x, y)) {
        candidate[y]![x] = !candidate[y]![x];
      }
    }
    writeFormat(candidate, mask);
    const score = penalty(candidate);
    if (score < lowest) {
      lowest = score;
      best = candidate;
    }
  }

  return { version, size, dark: best! };
}

/** The path of the dark modules for an SVG whose view box is the code with a quiet zone of four modules around it. */
export function qrPath(code: QrCode): string {
  const parts: string[] = [];
  for (let y = 0; y < code.size; y += 1) {
    for (let x = 0; x < code.size; x += 1) {
      if (code.dark[y]![x]) {
        parts.push(`M${String(x + 4)},${String(y + 4)}h1v1h-1z`);
      }
    }
  }

  return parts.join('');
}
