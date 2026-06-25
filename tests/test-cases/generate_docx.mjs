import {
  Document, Packer, Paragraph, TextRun, HeadingLevel,
  Table, TableRow, TableCell, WidthType, AlignmentType, ShadingType, BorderStyle,
} from 'docx';
import { writeFileSync } from 'fs';
import { meta, cases, defects, counts } from './testcases.data.mjs';

const GREEN = '2E7D5B', WHITE = 'FFFFFF';
const C_PASS = 'D7F5DD', C_BUG = 'FFE3C2', C_PART = 'FFF3C2', C_FAIL = 'FAD4D4';

const statusColor = (s) =>
  s === 'Pass' ? C_PASS : /BUG|ISSUE/.test(s) ? C_BUG : /Partial/.test(s) ? C_PART : C_FAIL;

const cell = (text, { bold = false, fill, color, align } = {}) =>
  new TableCell({
    shading: fill ? { type: ShadingType.CLEAR, fill, color: 'auto' } : undefined,
    margins: { top: 40, bottom: 40, left: 80, right: 80 },
    children: [new Paragraph({
      alignment: align,
      children: [new TextRun({ text: String(text ?? ''), bold, color, size: 18 })],
    })],
  });

const headerRow = (labels) => new TableRow({
  tableHeader: true,
  children: labels.map((l) => cell(l, { bold: true, fill: GREEN, color: WHITE })),
});

function buildTable(headers, rows, widths, statusCol = -1) {
  return new Table({
    width: { size: 100, type: WidthType.PERCENTAGE },
    columnWidths: widths,
    rows: [
      headerRow(headers),
      ...rows.map((r) => new TableRow({
        children: r.map((v, i) =>
          cell(v, i === statusCol ? { bold: true, fill: statusColor(v) } : {})),
      })),
    ],
  });
}

const H = (text, level = HeadingLevel.HEADING_2) =>
  new Paragraph({ text, heading: level, spacing: { before: 240, after: 120 } });

const P = (runs) => new Paragraph({ children: runs, spacing: { after: 80 } });

const c = counts();

const doc = new Document({
  styles: { default: { document: { run: { font: 'Calibri', size: 20 } } } },
  sections: [{
    properties: { page: { size: { orientation: 'landscape' } } },
    children: [
      new Paragraph({ text: meta.title, heading: HeadingLevel.TITLE }),

      H('1. Overview'),
      buildTable(['Field', 'Value'],
        meta.rows.concat([
          ['Total test cases', String(c.total)],
          ['Passed', String(c.pass)],
          ['Known bug / config issue', String(c.bug)],
          ['Not implemented / partial', String(c.notImpl)],
        ]),
        [2200, 7800]),

      H('2. Test cases'),
      P([new TextRun({ text: 'Status legend: Pass (green), BUG/ISSUE (orange), Partial (yellow), Not Implemented (red).', italics: true, size: 18 })]),
      buildTable(
        ['ID', 'Module', 'Precondition', 'Steps', 'Expected', 'Status', 'Notes'],
        cases,
        [1100, 1300, 1500, 2600, 2400, 1300, 2400],
        5),

      H('3. Defect summary'),
      buildTable(
        ['#', 'Severity', 'Area', 'Symptom', 'Root cause', 'Suggested fix'],
        defects,
        [500, 1100, 2000, 2800, 3600, 2800]),

      H('4. Notes / deviations'),
      P([new TextRun('• Tech stack: Capstone registered as Spring Boot (Java) + OpenAI; implementation is ASP.NET Core + Gemini. Architecture still maps (REST + PostgreSQL + Monday WeeklyReportScheduler).')]),
      P([new TextRun('• Re-run: start the API (dotnet run --project src/FinViet.Api), then dotnet test tests/FinViet.Api.IntegrationTests. Override target/credentials via FINVIET_TEST_BASEURL, FINVIET_TEST_CUST_EMAIL, etc.')]),
    ],
  }],
});

const out = 'FinViet_TestCases.docx';
const buffer = await Packer.toBuffer(doc);
writeFileSync(out, buffer);
console.log('Wrote', out, `(${cases.length} cases, ${defects.length} defects)`);
