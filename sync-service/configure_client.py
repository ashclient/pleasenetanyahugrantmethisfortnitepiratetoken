import json,os,re
from pathlib import Path
from urllib.parse import urlparse
endpoint=os.environ.get('SYNC_ENDPOINT','').strip()
if not endpoint and Path('sync-service/endpoint.json').exists():endpoint=json.loads(Path('sync-service/endpoint.json').read_text())['endpoint']
if endpoint:
 u=urlparse(endpoint)
 if u.scheme!='https' or not u.hostname or not u.hostname.endswith('.workers.dev') or u.username or u.password or u.port or u.query or u.fragment or u.path!='/api/v1/':raise SystemExit('Expected the deployed Cloudflare Worker HTTPS API endpoint')
 p=Path('src/LinkedCounts.cs');source,n=re.subn(r'public const string ServiceUrl="[^"\r\n]+";',lambda _:f'public const string ServiceUrl="{endpoint}";',p.read_text())
 if n!=1:raise SystemExit('Cannot configure native sync endpoint')
 p.write_text(source);print('Native client configured for deployed Cloudflare Worker')
else:print('No Cloudflare endpoint configured; existing source endpoint retained')
