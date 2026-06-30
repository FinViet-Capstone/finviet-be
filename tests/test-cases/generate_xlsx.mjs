import ExcelJS from 'exceljs';
import { meta, cases, defects, counts } from './testcases.data.mjs';

const wb = new ExcelJS.Workbook();
wb.creator = 'FinViet QA'; wb.created = new Date();
const c = counts();

// ---- Summary sheet ----
const s = wb.addWorksheet('Summary');
s.columns = [{ width: 32 }, { width: 60 }];
const summaryRows = [
  [meta.title, ''],
  ...meta.rows,
  ['', ''],
  ['Total test cases', String(c.total)],
  ['Passed', String(c.pass)],
  ['Known bug / config issue', String(c.bug)],
  ['Not implemented / partial', String(c.notImpl)],
];
summaryRows.forEach(r => s.addRow(r));
s.getRow(1).font = { bold: true, size: 14 };
for (let i = 2; i <= summaryRows.length; i++) s.getCell('A' + i).font = { bold: true };

// ---- Test Cases sheet ----
const ws = wb.addWorksheet('Test Cases');
ws.columns = [
  { header: 'ID', width: 14 }, { header: 'Module', width: 16 }, { header: 'Precondition', width: 20 },
  { header: 'Steps', width: 46 }, { header: 'Expected', width: 42 }, { header: 'Status', width: 16 }, { header: 'Notes', width: 42 },
];
cases.forEach(row => ws.addRow(row));
styleHeader(ws);
ws.eachRow((row, i) => {
  if (i === 1) return;
  row.alignment = { vertical: 'top', wrapText: true };
  const st = row.getCell(6).value;
  const color = st === 'Pass' ? 'FFD7F5DD' : /BUG|ISSUE/.test(st) ? 'FFFFE3C2' : /Partial/.test(st) ? 'FFFFF3C2' : 'FFFAD4D4';
  row.getCell(6).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: color } };
  row.getCell(6).font = { bold: true };
});
ws.autoFilter = 'A1:G1'; ws.views = [{ state: 'frozen', ySplit: 1 }];

// ---- Defects sheet ----
const d = wb.addWorksheet('Defects');
d.columns = [
  { header: '#', width: 5 }, { header: 'Severity', width: 12 }, { header: 'Area', width: 26 },
  { header: 'Symptom', width: 46 }, { header: 'Root cause', width: 60 }, { header: 'Suggested fix', width: 46 },
];
defects.forEach(r => d.addRow(r));
styleHeader(d);
d.eachRow((row, i) => { if (i === 1) return; row.alignment = { vertical: 'top', wrapText: true }; });

function styleHeader(sheet) {
  const h = sheet.getRow(1);
  h.font = { bold: true, color: { argb: 'FFFFFFFF' } };
  h.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF2E7D5B' } };
  h.alignment = { vertical: 'middle' };
  h.height = 20;
}

const out = 'FinViet_TestCases.xlsx';
await wb.xlsx.writeFile(out);
console.log('Wrote', out, `(${cases.length} cases, ${defects.length} defects)`);
