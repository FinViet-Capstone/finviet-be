import {
  AlignmentType, Document, HeadingLevel, Packer, Paragraph, ShadingType,
  Table, TableCell, TableRow, TextRun, WidthType,
} from 'docx';
import { writeFileSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';
import { meta, functions, cases, deferredCases, gaps, counts } from './unit-testcases.data.mjs';

const outputDirectory = dirname(fileURLToPath(import.meta.url));
const outputPath = join(outputDirectory, 'Unit_BusinessLogic_TestCases.docx');
const green = '2E7D5B';
const white = 'FFFFFF';
const planned = 'EAF3ED';
const deferred = 'FFF3C2';
const c = counts();

const textCell = (value, { bold = false, fill, color, alignment } = {}) => new TableCell({
  shading: fill ? { type: ShadingType.CLEAR, fill, color: 'auto' } : undefined,
  margins: { top: 55, bottom: 55, left: 75, right: 75 },
  children: [new Paragraph({
    alignment,
    children: [new TextRun({ text: String(value ?? ''), bold, color, size: 16 })],
  })],
});

const table = (headers, rows, statusColumn = -1) => new Table({
  width: { size: 100, type: WidthType.PERCENTAGE },
  rows: [
    new TableRow({ tableHeader: true, children: headers.map(header => textCell(header, { bold: true, fill: green, color: white })) }),
    ...rows.map(row => new TableRow({
      children: row.map((value, index) => textCell(value, index === statusColumn
        ? { bold: true, fill: value === 'Deferred' ? deferred : planned }
        : {})),
    })),
  ],
});

const H = (text, level = HeadingLevel.HEADING_2) => new Paragraph({
  text, heading: level, spacing: { before: 250, after: 120 },
});
const P = text => new Paragraph({ children: [new TextRun(text)], spacing: { after: 90 } });

const document = new Document({
  styles: { default: { document: { run: { font: 'Calibri', size: 19 } } } },
  sections: [{
    properties: { page: { size: { orientation: 'landscape' } } },
    children: [
      new Paragraph({ text: meta.title, heading: HeadingLevel.TITLE }),
      new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ text: meta.subtitle, italics: true })] }),
      H('Summary'),
      table(['Field', 'Value'], [
        ...meta.rows,
        ['Function groups', c.functionGroups],
        ['Cataloged functions', c.functions],
        ['Executable cases', c.executable],
        ['Passed executable cases', c.passed],
        ['Deferred DB/API/provider-only cases', c.deferred],
        ['Known code gaps', c.gaps],
        ['Total cataloged cases', c.total],
      ]),
      H('Function Catalog'),
      table(['Feature Group', 'Function', 'Responsibility / Entry Point', 'Isolated Test Target', 'Coverage Focus'], functions),
      H('Unit Test Matrix'),
      P('All cases in this matrix passed in the isolated FinViet.Application.UnitTests run on 2026-07-31. The full suite result was 136 passed, 0 failed, 0 skipped.'),
      table(['ID', 'Feature Group', 'Function', 'Preconditions / Doubles', 'Test Action', 'Expected Isolated Assertion', 'Status', 'Notes'], cases, 6),
      H('Deferred Cases'),
      P('These cases deliberately require database concurrency, HTTP middleware, external-provider contracts, or approved policy changes. They are not represented as passing unit tests.'),
      table(['ID', 'Feature Group', 'Function / Boundary', 'Deferred Scenario', 'Why Deferred', 'Required Test Layer', 'Status'], deferredCases, 6),
      H('Code Gaps'),
      table(['ID', 'Severity', 'Area', 'Confirmed Gap', 'Evidence / Impact', 'Recommended Next Action'], gaps),
    ],
  }],
});

writeFileSync(outputPath, await Packer.toBuffer(document));
console.log(JSON.stringify({ outputPath, sections: ['Summary', 'Function Catalog', 'Unit Test Matrix', 'Deferred Cases', 'Code Gaps'], counts: c }));
