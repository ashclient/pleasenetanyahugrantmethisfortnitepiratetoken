interface Env { DB: any }
const json=(data:unknown,status=200)=>Response.json(data,{status,headers:{'Cache-Control':'no-store'}});
const fail=(message:string,status=400)=>{throw Object.assign(new Error(message),{status});};
const token=()=>{const b=crypto.getRandomValues(new Uint8Array(32));return btoa(String.fromCharCode(...b)).replaceAll('+','-').replaceAll('/','_').replaceAll('=','');};
const hash=async(s:string)=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(s)))).map(x=>x.toString(16).padStart(2,'0')).join('');
const text=(v:unknown,n=40)=>typeof v==='string'&&v.trim().length>0&&v.length<=n&&!/[\x00-\x1f]/.test(v)?v.trim():fail('Invalid text');
const memberId=(v:unknown)=>typeof v==='string'&&/^[a-f0-9]{32}$/.test(v)?v:fail('Invalid member identity');
const secret=(v:unknown)=>typeof v==='string'&&/^[A-Za-z0-9_-]{43}$/.test(v)?v:fail('Invalid credential',401);
const bearer=(r:Request)=>secret(r.headers.get('Authorization')?.replace(/^Bearer /,''));
const rewardKinds=new Set(['Gizmo','Loading screen','Jam track','Pickaxe','Back bling','Vehicle / decal','Pirate shop access','Gold outfit']);
function snapshot(v:any){
 if(!v || !Number.isSafeInteger(v.chests)||v.chests<0||v.chests>2147483647||!Number.isSafeInteger(v.revision)||v.revision<1||v.revision>1e12)fail('Invalid snapshot');
 const rewards:Record<string,number>={};for(const [k,n]of Object.entries(v.rewards??{})){if(!rewardKinds.has(k)||!Number.isSafeInteger(n)||Number(n)<0||Number(n)>2147483647)fail('Invalid reward total');rewards[k]=Number(n);}
 return {chests:v.chests,revision:v.revision,rewards:JSON.stringify(rewards)};
}
async function run(r:Request,env:Env){
 if(!new URL(r.url).pathname.startsWith("/api/v1/")) return new Response(null,{status:404});
 const db=env.DB!;if(!db) return json({error:'Storage unavailable'},503);
 const path=new URL(r.url).pathname.split('/').filter(Boolean).slice(2);const now=Math.floor(Date.now()/1000);
 if(r.method==='GET'&&path.join('/')==='health') return json({ok:true,protocol:1});
 if(r.method!=='POST')return json({error:'Not found'},404);
 if(Number(r.headers.get('Content-Length')??0)>16384)fail('Request too large',413);
 const raw=await r.text();if(raw.length>16384)fail('Request too large',413);let b:any;try{b=JSON.parse(raw)}catch{fail('Invalid JSON');}
 const limited=async(label:string,max:number)=>{const key=label+':'+Math.floor(now/86400)+':'+await hash(r.headers.get('CF-Connecting-IP')??'unknown');const row:any=await db.prepare('INSERT INTO limits(key,count) VALUES(?,1) ON CONFLICT(key) DO UPDATE SET count=count+1 RETURNING count').bind(key).first();if(row.count>max)fail('Please try again later',429);};
 const state=async(id:string)=>{const room:any=await db.prepare('SELECT id,name FROM rooms WHERE id=?').bind(id).first();if(!room)fail('Group no longer exists',404);const rows:any=await db.prepare('SELECT id,name,chests,rewards,revision,updated,seen FROM members WHERE room=? AND active=1 ORDER BY name,id').bind(id).all();return {groupId:room.id,groupName:room.name,members:rows.results.map((m:any)=>({...m,rewards:JSON.parse(m.rewards)})),serverTime:now};};
 if(path.join('/')==='groups/create'){
  await limited('create',10);const id=token().slice(0,22),join=token(),admin=token(),proof=secret(b.proof),mid=memberId(b.memberId),name=text(b.name,32),group=text(b.groupName,40),s=snapshot(b.snapshot);
  await db.batch([db.prepare('INSERT INTO rooms(id,name,join_hash,admin_hash,created) VALUES(?,?,?,?,?)').bind(id,group,await hash(join),await hash(admin),now),db.prepare('INSERT INTO members(room,id,proof,name,chests,rewards,revision,updated,seen,active) VALUES(?,?,?,?,?,?,?,?,?,1)').bind(id,mid,await hash(proof),name,s.chests,s.rewards,s.revision,now,now)]);
  return json({...await state(id),inviteCode:`TL1.${id}.${join}`,adminToken:admin});
 }
 const id=path[1];if(path[0]!=='groups'||!id||!/^[A-Za-z0-9_-]{22}$/.test(id))return json({error:'Not found'},404);
 const room:any=await db.prepare('SELECT * FROM rooms WHERE id=?').bind(id).first();if(!room)fail('Group no longer exists',404);
 const action=path[2],auth=await hash(bearer(r));
 if(action==='join'){
  await limited('join',150);if(auth!==room.join_hash)fail('Invite code is no longer valid',403);
  const mid=memberId(b.memberId),proof=await hash(secret(b.proof)),name=text(b.name,32),s=snapshot(b.snapshot);
  const old:any=await db.prepare('SELECT proof FROM members WHERE room=? AND id=?').bind(id,mid).first();
  if(old&&old.proof!==proof)fail('Member identity does not match this installation',403);
  const count:any=await db.prepare('SELECT COUNT(*) AS n FROM members WHERE room=? AND active=1').bind(id).first();if(!old&&count.n>=32)fail('Group is full',409);
  await db.prepare('INSERT INTO members(room,id,proof,name,chests,rewards,revision,updated,seen,active) VALUES(?,?,?,?,?,?,?,?,?,1) ON CONFLICT(room,id) DO UPDATE SET active=1,name=excluded.name,seen=excluded.seen').bind(id,mid,proof,name,s.chests,s.rewards,s.revision,now,now).run();
  return json({...await state(id),inviteCode:`TL1.${id}.${bearer(r)}`});
 }
 if(action==='remove'||action==='close'){
  if(auth!==room.admin_hash)fail('Only the group creator can do this',403);
  if(action==='close'){await db.batch([db.prepare('DELETE FROM members WHERE room=?').bind(id),db.prepare('DELETE FROM rooms WHERE id=?').bind(id)]);return json({closed:true});}
  const target=memberId(b.memberId),join=token();await db.batch([db.prepare('UPDATE members SET active=0 WHERE room=? AND id=?').bind(id,target),db.prepare('UPDATE rooms SET join_hash=? WHERE id=?').bind(await hash(join),id)]);return json({...await state(id),inviteCode:`TL1.${id}.${join}`});
 }
 const mid=memberId(b.memberId);const me:any=await db.prepare('SELECT proof,active,revision,seen FROM members WHERE room=? AND id=?').bind(id,mid).first();if(!me||!me.active||me.proof!==auth)fail('Membership is no longer active',403);
 if(action==='leave'){await db.prepare('UPDATE members SET active=0 WHERE room=? AND id=?').bind(id,mid).run();return json({left:true});}
 if(action==='sync'){
  if(now-me.seen<3) return json({error:'Sync is too frequent'},429);
  const name=text(b.name,32);if(b.snapshot){const s=snapshot(b.snapshot);await db.prepare('UPDATE members SET chests=?,rewards=?,revision=?,updated=? WHERE room=? AND id=? AND revision<?').bind(s.chests,s.rewards,s.revision,now,id,mid,s.revision).run();}
  await db.prepare('UPDATE members SET name=?,seen=? WHERE room=? AND id=?').bind(name,now,id,mid).run();return json(await state(id));
 }
 return json({error:'Not found'},404);
}
async function handle(r:Request,env:Env){try{return await run(r,env)}catch(e:any){return json({error:e.status?e.message:'Sync service error'},e.status??500)}}
export default { fetch:handle };
