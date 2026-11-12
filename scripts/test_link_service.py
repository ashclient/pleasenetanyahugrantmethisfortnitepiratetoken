import urllib.request,json,uuid,secrets,time,os
base=os.environ.get('TREASURE_LEDGER_SYNC_URL')
if not base: raise SystemExit('Set TREASURE_LEDGER_SYNC_URL to the deployed Worker origin followed by /api/v1/')
def call(path,b,auth=None):
 r=urllib.request.Request(base+path,json.dumps(b).encode(),{'User-Agent':'TreasureLedger/0.5.0','Content-Type':'application/json',**({'Authorization':'Bearer '+auth} if auth else {})})
 with urllib.request.urlopen(r,timeout=25) as x:return json.load(x)
a,b=uuid.uuid4().hex,uuid.uuid4().hex;p,q=secrets.token_urlsafe(32),secrets.token_urlsafe(32)
def snap(n,r):return {'chests':n,'rewards':{'Gizmo':2},'revision':r}
s=call('groups/create',{'memberId':a,'proof':p,'name':'Synthetic A','groupName':'Temporary integration test','snapshot':snap(10,1)})
id=s['groupId'];admin=s['adminToken'];join=s['inviteCode'].split('.')[2]
try:
 s=call(f'groups/{id}/join',{'memberId':b,'proof':q,'name':'Synthetic B','snapshot':snap(20,1)},join)
 assert sum(m['chests'] for m in s['members'])==30
 time.sleep(4)
 s=call(f'groups/{id}/sync',{'memberId':a,'name':'Synthetic A','snapshot':snap(12,2)},p)
 assert sum(m['chests'] for m in s['members'])==32
 time.sleep(4)
 s=call(f'groups/{id}/sync',{'memberId':a,'name':'Synthetic A','snapshot':snap(12,3)},p)
 assert sum(m['chests'] for m in s['members'])==32
 time.sleep(4)
 s=call(f'groups/{id}/sync',{'memberId':a,'name':'Synthetic A','snapshot':snap(999,2)},p)
 assert sum(m['chests'] for m in s['members'])==32
 try:call(f'groups/{id}/sync',{'memberId':a,'name':'Intruder','snapshot':snap(99,9)},secrets.token_urlsafe(32));raise AssertionError('unauthorized accepted')
 except urllib.error.HTTPError as e:assert e.code==403
 s=call(f'groups/{id}/remove',{'memberId':b},admin);assert len(s['members'])==1
 print('PASS live service: two members, saved totals, replacement sync, restart/idempotence, stale revision, unauthorized rejection, removal')
finally:
 call(f'groups/{id}/close',{},admin);print('Temporary test group deleted')
