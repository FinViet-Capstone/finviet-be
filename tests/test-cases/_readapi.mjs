import ExcelJS from 'exceljs';
const wb = new ExcelJS.Workbook();
await wb.xlsx.readFile('C:/Users/MSI/Desktop/finviet/api_list.xlsx');
wb.eachSheet(ws=>{
  console.log('\n##### SHEET:', ws.name, '(rows='+ws.rowCount+', cols='+ws.columnCount+') #####');
  ws.eachRow({includeEmpty:false},(row,ri)=>{
    const vals = [];
    row.eachCell({includeEmpty:true},(cell)=>{ let v=cell.value; if(v&&typeof v==='object'){v=v.text||v.result||v.hyperlink||JSON.stringify(v);} vals.push(v==null?'':String(v).replace(/\s+/g,' ').trim()); });
    console.log(ri+': '+vals.join(' | '));
  });
});
