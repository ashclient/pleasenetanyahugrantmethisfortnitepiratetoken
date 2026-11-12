import fs from 'node:fs';
import {execFileSync} from 'node:child_process';
const account=process.env.CLOUDFLARE_ACCOUNT_ID,token=process.env.CLOUDFLARE_API_TOKEN;
if(!account||!/^[a-f0-9]{32}$/i.test(account)||!token)throw new Error('Configure CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN in GitHub Actions secrets.');
async function api(path,method='GET',body){
 const response=await fetch(`https://api.cloudflare.com/client/v4/accounts/${account}/${path}`,{method,headers:{Authorization:`Bearer ${token}`,'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});
 const result=await response.json();if(!response.ok||!result.success)throw new Error(`Cloudflare ${method} request failed (${response.status}); verify token account scope and Workers/D1 permissions.`);return result;
}
let database;
for(let page=1;page<=100;page++){const data=await api(`d1/database?per_page=100&page=${page}`);database=data.result.find(x=>x.name==='treasure-ledger-sync');if(database||data.result.length<100)break;}
if(!database)database=(await api('d1/database','POST',{name:'treasure-ledger-sync'})).result;
if(!database.uuid)throw new Error('Cloudflare did not return a D1 database ID.');
const config=JSON.parse(fs.readFileSync('wrangler.example.jsonc','utf8'));config.d1_databases[0].database_id=database.uuid;fs.writeFileSync('wrangler.jsonc',JSON.stringify(config,null,2));
for(const sql of fs.readFileSync('schema.sql','utf8').split('--> statement-breakpoint').map(x=>x.trim()).filter(Boolean))await api(`d1/database/${database.uuid}/query`,'POST',{sql,params:[]});
const output=execFileSync('npx',['--no-install','wrangler','deploy','--config','wrangler.jsonc'],{encoding:'utf8',maxBuffer:1024*1024});process.stdout.write(output);
const matches=[...output.matchAll(/https:\/\/treasure-ledger-sync\.[a-z0-9-]+\.workers\.dev\b/g)];if(!matches.length)throw new Error('No workers.dev URL returned; enable the Cloudflare account workers.dev subdomain.');
const endpoint=matches.at(-1)[0]+'/api/v1/';
const health=await fetch(endpoint+'health');if(!health.ok||!(await health.json()).ok)throw new Error('Deployed Worker health check failed.');
fs.writeFileSync('endpoint.json',JSON.stringify({endpoint},null,2)+'\n');
if(process.env.GITHUB_OUTPUT)fs.appendFileSync(process.env.GITHUB_OUTPUT,`endpoint=${endpoint}\n`);
if(process.env.GITHUB_STEP_SUMMARY)fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY,`Cloudflare sync API deployed and healthy: ${endpoint}\nNo public web pages. Group access requires pairing credentials.\n`);
