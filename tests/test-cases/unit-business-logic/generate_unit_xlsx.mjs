import ExcelJS from 'exceljs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';
import { meta, functions, cases, deferredCases, gaps, counts } from './unit-testcases.data.mjs';

const outputDirectory = dirname(fileURLToPath(import.meta.url));
const outputPath = join(outputDirectory, 'Unit_BusinessLogic_TestCases.xlsx');
const workbook = new ExcelJS.Workbook();
workbook.creator = 'FinViet QA';
workbook.created = new Date();
workbook.properties.title = meta.title;

const green = 'FF2E7D5B';
const white = 'FFFFFFFF';
const planned = 'FFEAF3ED';
const deferred = 'FFFFF3C2';
const c = counts();

function styleHeader(sheet) {
  const header = sheet.getRow(1);
  header.font = { bold: true, color: { argb: white } };
  header.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: green } };
  header.alignment = { vertical: 'middle', wrapText: true };
  header.height = 28;
  sheet.views = [{ state: 'frozen', ySplit: 1 }];
}

function styleRows(sheet, statusColumn) {
  sheet.eachRow((row, index) => {
    if (index === 1) return;
    row.alignment = { vertical: 'top', wrapText: true };
    if (statusColumn) {
      const cell = row.getCell(statusColumn);
      cell.font = { bold: true };
      cell.fill = {
        type: 'pattern', pattern: 'solid',
        fgColor: { argb: cell.value === 'Deferred' ? deferred : planned },
      };
    }
  });
  sheet.autoFilter = `A1:${sheet.getColumn(sheet.columnCount).letter}1`;
}

const summary = workbook.addWorksheet('Summary');
summary.columns = [{ width: 30 }, { width: 100 }];
summary.addRow([meta.title, '']);
summary.getRow(1).font = { bold: true, size: 16, color: { argb: white } };
summary.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: green } };
summary.mergeCells('A1:B1');
for (const row of meta.rows) summary.addRow(row);
summary.addRow([]);
[
  ['Function groups', c.functionGroups],
  ['Cataloged functions', c.functions],
  ['Executable cases', c.executable],
  ['Passed executable cases', c.passed],
  ['Deferred DB/API/provider-only cases', c.deferred],
  ['Known code gaps', c.gaps],
  ['Total cataloged cases', c.total],
].forEach(row => summary.addRow(row));
summary.eachRow((row, index) => {
  row.alignment = { vertical: 'top', wrapText: true };
  if (index > 1) row.getCell(1).font = { bold: true };
});

const catalog = workbook.addWorksheet('Function Catalog');
catalog.columns = [
  { header: 'Feature Group', width: 16 }, { header: 'Function', width: 52 },
  { header: 'Responsibility / Entry Point', width: 44 }, { header: 'Isolated Test Target', width: 42 },
  { header: 'Coverage Focus', width: 58 },
];
functions.forEach(row => catalog.addRow(row));
styleHeader(catalog); styleRows(catalog);

const matrix = workbook.addWorksheet('Unit Test Matrix');
matrix.columns = [
  { header: 'ID', width: 14 }, { header: 'Feature Group', width: 15 }, { header: 'Function', width: 42 },
  { header: 'Preconditions / Doubles', width: 36 }, { header: 'Test Action', width: 43 },
  { header: 'Expected Isolated Assertion', width: 58 }, { header: 'Status', width: 14 }, { header: 'Notes', width: 34 },
];
cases.forEach(row => matrix.addRow(row));
styleHeader(matrix); styleRows(matrix, 7);

const deferredSheet = workbook.addWorksheet('Deferred Cases');
deferredSheet.columns = [
  { header: 'ID', width: 14 }, { header: 'Feature Group', width: 15 }, { header: 'Function / Boundary', width: 42 },
  { header: 'Deferred Scenario', width: 54 }, { header: 'Why Deferred', width: 52 },
  { header: 'Required Test Layer', width: 36 }, { header: 'Status', width: 14 },
];
deferredCases.forEach(row => deferredSheet.addRow(row));
styleHeader(deferredSheet); styleRows(deferredSheet, 7);

const gapSheet = workbook.addWorksheet('Code Gaps');
gapSheet.columns = [
  { header: 'ID', width: 16 }, { header: 'Severity', width: 14 }, { header: 'Area', width: 26 },
  { header: 'Confirmed Gap', width: 54 }, { header: 'Evidence / Impact', width: 62 }, { header: 'Recommended Next Action', width: 58 },
];
gaps.forEach(row => gapSheet.addRow(row));
styleHeader(gapSheet); styleRows(gapSheet);

await workbook.xlsx.writeFile(outputPath);
console.log(JSON.stringify({ outputPath, sheets: workbook.worksheets.map(s => s.name), counts: c }));
