const fs=require("fs");
const load=p=>{ const r=JSON.parse(fs.readFileSync(p,"utf8")); return r.result? r.result.tools : r; };
const raw=load(process.argv[2]), fix=load(process.argv[3]);
const byName=Object.fromEntries(fix.map(t=>[t.name,t]));
const countProps=(s)=> s&&s.properties? Object.keys(s.properties).length : 0;
console.log("tool                 props(raw->fix)  required  addlProps");
for(const t of raw){
  const f=byName[t.name];
  const a=countProps(t.inputSchema), b=countProps(f.inputSchema);
  const rq=(s)=>Array.isArray(s&&s.required)? s.required.length : 0;
  const ap=(s)=>{ const v=s&&s.additionalProperties; return v===undefined?"-":JSON.stringify(v); };
  console.log(`${t.name.padEnd(20)} ${String(a).padStart(3)} -> ${String(b).padStart(3)}        ${rq(t.inputSchema)} -> ${rq(f.inputSchema)}   ${ap(t.inputSchema)} -> ${ap(f.inputSchema)}`);
}
console.log("\ndescription 是否保留:");
let lost=0;
for(const t of raw){ const f=byName[t.name]; if((t.description||"")!==(f.description||"")) { lost++; console.log("  CHANGED", t.name); } }
console.log(lost? `  ${lost} 个变了`:"  全部原样保留");
console.log("\n=== fixed unity_editor ===");
console.log(JSON.stringify(byName["unity_editor"].inputSchema,null,1).slice(0,1200));
console.log("\n=== fixed unity_menu.properties.parameters ===");
console.log(JSON.stringify(byName["unity_menu"].inputSchema.properties.parameters,null,1).slice(0,500));
