"""Package exact corresponding source and hash for the independent GPL worker."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
root=Path(__file__).resolve().parents[1]
out=root/'worker-dist'; out.mkdir(exist_ok=True)
exe=out/'TreasureLedger.ReplayWorker.exe'
(out/'worker.sha256').write_text(hashlib.sha256(exe.read_bytes()).hexdigest())
with ZipFile(out/'ReplayWorkerSource.zip','w',ZIP_DEFLATED) as z:
    for folder in ['replay-worker','external/chestreader']:
        for p in (root/folder).rglob('*'):
            if p.is_file() and not any(x in ('.git','bin','obj') for x in p.relative_to(root).parts):
                z.write(p,p.relative_to(root).as_posix())
    z.write(__file__,'scripts/package_replay_worker.py')
