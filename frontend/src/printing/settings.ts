import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';

/** How the hospital prints one kind of document (Prompt 10.4c, ADR 0059). */
export type PrintSettings = components['schemas']['PrintSettingsDto'];
export type PrintedDocumentType = PrintSettings['documentType'];
export type PaperSize = PrintSettings['paper'];

/**
 * The layout of a sheet: the settings without the kind of document and the version, with every measure a number (the
 * API's document lets a whole number travel as text).
 */
export interface PrintLayout {
  paper: PaperSize;
  marginTopMm: number;
  marginRightMm: number;
  marginBottomMm: number;
  marginLeftMm: number;
  logoPosition: PrintSettings['logoPosition'];
  logoSize: PrintSettings['logoSize'];
  prePrintedStationery: boolean;
  stationeryTopMm: number;
  showPatientContact: boolean;
  showPayer: boolean;
  showSacColumn: boolean;
  showTaxSplit: boolean;
  showAmountInWords: boolean;
  showSignatureBlock: boolean;
  showCashierName: boolean;
  footerLine: string | null;
  terms: string | null;
}

/** The settings of one kind of document as a sheet and a form use them: the layout, and the version a save sends. */
export interface DocumentSettings {
  documentType: PrintedDocumentType;
  layout: PrintLayout;
  version: string;
}

export const printSettingsKey = ['print-settings'] as const;

/** The version of settings a hospital never saved, as the API answers it: what their first save sends in If-Match. */
export const noVersion = 'AAAAAAAAAAA=';

/**
 * The sheet as it was before there were settings (ADR 0054 as amended): A4 with the margins of the print styles, the
 * logo at the left, and what the three sheets showed. The API answers the same for a kind that was never saved; this
 * copy is what a preview starts from, and a test holds it to the sheet of today.
 */
export const defaultLayout: PrintLayout = {
  paper: 'A4',
  marginTopMm: 14,
  marginRightMm: 12,
  marginBottomMm: 14,
  marginLeftMm: 12,
  logoPosition: 'Left',
  logoSize: 'Medium',
  prePrintedStationery: false,
  stationeryTopMm: 0,
  showPatientContact: false,
  showPayer: true,
  showSacColumn: true,
  showTaxSplit: true,
  showAmountInWords: true,
  showSignatureBlock: true,
  showCashierName: false,
  footerLine: null,
  terms: null,
};

/** The limits of a layout, as the API holds them. */
export const layoutLimits = { marginMinMm: 5, marginMaxMm: 25, stationeryTopMaxMm: 80, footerLine: 200, terms: 1000 };

/** The papers a kind of document may be printed on: a roll is for a receipt or a refund voucher, never for a bill. */
export function papersOf(documentType: PrintedDocumentType): readonly PaperSize[] {
  return documentType === 'Bill' ? ['A4', 'A5'] : ['A4', 'A5', 'Roll80'];
}

export const paperLabels: Record<PaperSize, string> = { A4: 'A4', A5: 'A5', Roll80: 'Roll, 80 mm' };
export const documentLabels: Record<PrintedDocumentType, string> = {
  Bill: 'Bill',
  Payment: 'Receipt',
  Refund: 'Refund voucher',
};

/** How every kind of document is printed: three answers, a kind never saved answering the defaults. */
export function usePrintSettings() {
  const client = useApiClient();

  return useQuery({
    queryKey: printSettingsKey,
    queryFn: ({ signal }) => answer(client.GET('/api/v1/settings/print', { signal })),
  });
}

/** The layout an answer of the API holds. */
export function layoutOf(settings: PrintSettings): PrintLayout {
  return {
    paper: settings.paper,
    marginTopMm: Number(settings.marginTopMm),
    marginRightMm: Number(settings.marginRightMm),
    marginBottomMm: Number(settings.marginBottomMm),
    marginLeftMm: Number(settings.marginLeftMm),
    logoPosition: settings.logoPosition,
    logoSize: settings.logoSize,
    prePrintedStationery: settings.prePrintedStationery,
    stationeryTopMm: Number(settings.stationeryTopMm),
    showPatientContact: settings.showPatientContact,
    showPayer: settings.showPayer,
    showSacColumn: settings.showSacColumn,
    showTaxSplit: settings.showTaxSplit,
    showAmountInWords: settings.showAmountInWords,
    showSignatureBlock: settings.showSignatureBlock,
    showCashierName: settings.showCashierName,
    footerLine: settings.footerLine,
    terms: settings.terms,
  };
}

/** The settings of one kind among the three the API answers; the defaults, never saved, should it answer none. */
export function settingsOf(all: readonly PrintSettings[], documentType: PrintedDocumentType): DocumentSettings {
  const one = all.find((candidate) => candidate.documentType === documentType);
  return one === undefined
    ? { documentType, layout: defaultLayout, version: noVersion }
    : { documentType, layout: layoutOf(one), version: one.version };
}

const paperSizes: Record<PaperSize, string> = { A4: 'A4', A5: 'A5', Roll80: '80mm 297mm' };

/** A whole number of millimetres within its limits; what is no number at all is the least the limits allow. */
function millimetres(value: number, least: number, most: number): string {
  const whole = Number.isFinite(value) ? Math.round(value) : least;
  return `${String(Math.min(most, Math.max(least, whole)))}mm`;
}

/** The four margins as CSS writes them, from whole millimetres within the limits. */
function margins(layout: PrintLayout): string {
  return [layout.marginTopMm, layout.marginRightMm, layout.marginBottomMm, layout.marginLeftMm]
    .map((value) => millimetres(value, layoutLimits.marginMinMm, layoutLimits.marginMaxMm))
    .join(' ');
}

function stationeryTop(layout: PrintLayout): string {
  return millimetres(layout.stationeryTopMm, 0, layoutLimits.stationeryTopMaxMm);
}

/**
 * The rules a layout adds to the page, as one stylesheet text: the paper and its margins for the printer, and the space
 * pre-printed stationery takes. Built from numbers that were rounded and held within their limits and from three fixed
 * words, so nothing a user typed is ever written into a rule. The preview takes its margins as the sheet's padding and
 * gives the printer no paper: it is a picture of a sheet on a screen.
 */
export function layoutRules(layout: PrintLayout, mode: 'print' | 'preview'): string {
  const space = `.print-stationery-space { height: ${stationeryTop(layout)}; }`;

  return mode === 'print'
    ? `@page { size: ${paperSizes[layout.paper]}; margin: ${margins(layout)}; }\n${space}`
    : `.print-sheet-preview { padding: ${margins(layout)}; }\n${space}`;
}

/**
 * Puts the rules of a layout on the page while a sheet is shown, as a stylesheet object of the page (never a `style`
 * attribute and never a `<style>` element, which the content security policy of the page refuses), and takes them off
 * when the sheet goes. A browser without such stylesheets prints by the print styles' own A4.
 */
export function useLayoutRules(layout: PrintLayout, mode: 'print' | 'preview') {
  const rules = layoutRules(layout, mode);

  useEffect(() => {
    if (typeof CSSStyleSheet === 'undefined' || !('adoptedStyleSheets' in document)) {
      return;
    }

    const sheet = new CSSStyleSheet();
    sheet.replaceSync(rules);
    document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];

    return () => {
      document.adoptedStyleSheets = document.adoptedStyleSheets.filter((adopted) => adopted !== sheet);
    };
  }, [rules]);
}
