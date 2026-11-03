"""Deterministic local installation ZIP; never uploads or publishes a release."""
import argparse, datetime, hashlib, json
from pathlib import Path
import zipfile

ROOT=Path(__file__).resolve().parents[1]
VERSION='0.1.0.0'
GUID='89654c7e-bf44-4e9f-8adf-2219fe61fa36'
p=argparse.ArgumentParser()
p.add_argument('--build-dir',type=Path,default=ROOT/'src/Jellyfin.Plugin.SidecarBridge/bin/Release/net10.0')
p.add_argument('--output',type=Path,default=ROOT/'artifacts')
p.add_argument('--source-url',help='An actual publicly available ZIP URL; omit until publication is separately authorized')
a=p.parse_args()
a.output.mkdir(parents=True,exist_ok=True)
meta={'category':'General','guid':GUID,'name':'Sidecar Bridge','overview':'Nested audio and subtitles, connected with hardlinks.',
    'description':'Connect external audio and subtitles during ordinary Jellyfin library scans.',
    'owner':'Dark-V','targetAbi':'12.4.0.0','version':VERSION,'timestamp':'2026-11-04T00:00:00Z',
    'status':'Active','autoUpdate':False,'imagePath':'icon.png','assemblies':['SidecarBridge.Core.dll','Jellyfin.Plugin.SidecarBridge.dll']}
payload={name:(a.build_dir/name).read_bytes() for name in ['Jellyfin.Plugin.SidecarBridge.dll','SidecarBridge.Core.dll']}
payload['meta.json']=(json.dumps(meta,ensure_ascii=False,indent=2)+'\n').encode()
payload['icon.png']=(ROOT/'docs/assets/icon.png').read_bytes()
payload['LICENSE']=(ROOT/'LICENSE').read_bytes()
payload['INSTALL.txt']=b'Jellyfin 12.4 only. Stop Jellyfin. Extract into plugins/SidecarBridge_0.1.0.0/. Start and enable libraries in plugin settings. See repository docs/installation.md.\n'
target=a.output/f'SidecarBridge_{VERSION}.zip'
with zipfile.ZipFile(target,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
    for name,data in sorted(payload.items()):
        item=zipfile.ZipInfo(name,date_time=(2026,11,4,0,0,0)); item.compress_type=zipfile.ZIP_DEFLATED; item.external_attr=0o100644<<16
        archive.writestr(item,data,compress_type=zipfile.ZIP_DEFLATED,compresslevel=9)
md5=hashlib.md5(target.read_bytes(),usedforsecurity=False).hexdigest()
sha=hashlib.sha256(target.read_bytes()).hexdigest()
(a.output/(target.name+'.sha256')).write_text(sha+'  '+target.name+'\n',encoding='utf-8')
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
if a.source_url:
    if not a.source_url.startswith('https://'):raise SystemExit('--source-url must be an actual HTTPS download URL')
    manifest[0]['versions']=[{'version':VERSION,'changelog':'Initial Sidecar Bridge build.','targetAbi':'12.4.0.0',
        'sourceUrl':a.source_url,'checksum':md5,'timestamp':meta['timestamp']}]
(a.output/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(target)
print('SHA256 '+sha)
print('Installable catalog version generated.' if a.source_url else 'No public version advertised: release publication has not been authorized.')
