const fs=require("fs");
const src=process.argv[2], dst=process.argv[3];
const lines=fs.readFileSync(src,"utf8").split(/\r?\n/);
for(const l of lines){ const t=l.trim(); if(!t.startsWith("{")) continue;
  try{ const j=JSON.parse(t); if(j.result&&Array.isArray(j.result.tools)){ fs.writeFileSync(dst,t,"utf8"); console.log("captured tools/list:",t.length,"chars"); process.exit(0);} }catch(e){} }
console.log("NOT FOUND"); process.exit(1);
