"""Disposable REAL Jellyfin scan test. FFmpeg is used only to generate test fixtures.
Never points to an existing server; all server state and media live below --work.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request

p = argparse.ArgumentParser()
p.add_argument('--server', required=True, type=Path, help='Published jellyfin.dll')
p.add_argument('--plugin', required=True, type=Path, help='Directory containing built plugin DLLs')
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--ffmpeg', default='ffmpeg')
p.add_argument('--work', required=True, type=Path, help='A new, empty disposable directory')
a = p.parse_args()
root = a.work.resolve()
root.mkdir(parents=True, exist_ok=True)
if any(root.iterdir()):
    raise SystemExit('--work must be empty; nothing was deleted')
media = root / 'media' / 'Bridge Film (2026)'
audio = media / 'Rus Sound' / 'NDA Studio'
subs = media / 'Rus Subs' / 'SubsGroup'
audio.mkdir(parents=True)
subs.mkdir(parents=True)
stem = 'Bridge Film (2026)'
video = media / (stem + '.mkv')
track = audio / (stem + '.mka')
sub = subs / (stem + '.srt')
def ff(*args):
    subprocess.run([a.ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', *map(str,args)], check=True)
ff('-f','lavfi','-i','color=c=black:s=160x90:r=10','-t','1','-c:v','mpeg4',video)
ff('-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','1','-c:a','flac',track)
sub.write_text('1\n00:00:00,000 --> 00:00:00,900\nSidecar Bridge test\n', encoding='utf-8')
reserved=media/'Rus Sound'/'Default Studio'/(stem+'.mka')
reserved.parent.mkdir(parents=True); shutil.copy2(track,reserved)
signs=media/'Subtitles'/'Signs'/(stem+'.srt')
signs.parent.mkdir(parents=True); shutil.copy2(sub,signs)
flagged=media/'Subtitles'/'Caption Team'/(stem+'.eng.default.forced.sdh.srt')
flagged.parent.mkdir(parents=True); shutil.copy2(sub,flagged)
multi=media/'Audio'/'Multi Studio'/(stem+'.rus.default.forced.mka')
multi.parent.mkdir(parents=True)
ff('-f','lavfi','-i','sine=frequency=220:sample_rate=48000','-f','lavfi','-i','sine=frequency=330:sample_rate=48000',
   '-t','1','-map','0:a','-map','1:a','-c:a','flac','-metadata:s:a:0','language=jpn','-metadata:s:a:0','title=Japanese Original',
   '-metadata:s:a:1','language=eng','-metadata:s:a:1','title=English Original','-disposition:a:0','0','-disposition:a:1','0',multi)
# Fixture files are complete; age gates should not turn this into a second-scan test.
fixture_files=(video,track,sub,reserved,signs,flagged,multi)
for file in fixture_files:
    os.utime(file, (time.time()-120,time.time()-120))
originals = {str(f):hashlib.sha256(f.read_bytes()).hexdigest() for f in fixture_files}
data = root / 'data'
config = root / 'config'
plugin = data / 'plugins' / 'SidecarBridge_0.1.0.0'
plugin.mkdir(parents=True)
config.mkdir()
for name in ('Jellyfin.Plugin.SidecarBridge.dll','SidecarBridge.Core.dll'):
    shutil.copy2(a.plugin.resolve()/name, plugin/name)
(plugin/'meta.json').write_text(json.dumps({'name':'Sidecar Bridge','guid':'89654c7e-bf44-4e9f-8adf-2219fe61fa36',
    'version':'0.1.0.0','targetAbi':'12.4.0.0','status':'Active','autoUpdate':False}),encoding='utf-8')
with socket.socket() as sock:
    sock.bind(('127.0.0.1',0))
    port = sock.getsockname()[1]
(config/'network.xml').write_text(f'''<NetworkConfiguration><InternalHttpPort>{port}</InternalHttpPort>
<PublicHttpPort>{port}</PublicHttpPort><EnableRemoteAccess>false</EnableRemoteAccess>
<EnableIPv6>false</EnableIPv6><EnableUPnP>false</EnableUPnP><EnableSSDP>false</EnableSSDP>
<LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses></NetworkConfiguration>''',encoding='utf-8')
base = f'http://127.0.0.1:{port}'
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
auth = 'MediaBrowser Client="Sidecar Bridge Tests", Device="Integration", DeviceId="sidecar-test", Version="0.1"'
def api(method,path,body=None,authenticated=True):
    headers={'Content-Type':'application/json'}
    if authenticated: headers['Authorization']=auth
    data_bytes = json.dumps(body).encode() if body is not None else (b'' if method=='POST' else None)
    req=urllib.request.Request(base+path,data=data_bytes,headers=headers,method=method)
    try:
        with opener.open(req,timeout=30) as response:
            raw=response.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as ex:
        raise AssertionError(f'{method} {path}: {ex.code} {ex.read().decode()[:500]}') from ex
log = (root/'server.log').open('w',encoding='utf-8')
env=dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1')
server=subprocess.Popen([a.dotnet,str(a.server.resolve()),'--datadir',str(data),'--configdir',str(config),
    '--cachedir',str(root/'cache'),'--logdir',str(root/'logs'),'--nowebclient','--nonetchange',
    '--ffmpeg',str(Path(shutil.which(a.ffmpeg) or a.ffmpeg).resolve())],stdout=log,stderr=subprocess.STDOUT,env=env)
try:
    for _ in range(120):
        if server.poll() is not None: raise AssertionError('Jellyfin exited; inspect server.log')
        try:
            info=api('GET','/System/Info/Public',authenticated=False)
            if info.get('Version'): break
            time.sleep(0.5)
        except Exception: time.sleep(0.5)
    else: raise AssertionError('Jellyfin startup timeout; inspect server.log')
    assert info['Version'].startswith('12.4.'),info
    api('GET','/Startup/User')
    password=secrets.token_urlsafe(24)
    api('POST','/Startup/User',{'Name':'bridge-test','Password':password})
    api('POST','/Startup/Complete')
    login=api('POST','/Users/AuthenticateByName',{'Username':'bridge-test','Pw':password})
    auth += ', Token="'+login['AccessToken']+'"'
    user=login['User']['Id']
    api('POST','/Library/VirtualFolders?'+urllib.parse.urlencode({'name':'Bridge Test','collectionType':'movies','paths':str(root/'media')}),
        {'LibraryOptions':{'EnableRealtimeMonitor':False,'EnableInternetProviders':False,'EnableAutomaticSeriesGrouping':False,
         'AutomaticRefreshIntervalDays':0,'EnableChapterImageExtraction':False,'EnableTrickplayImageExtraction':False,
         'TypeOptions':[{'Type':'Movie','MetadataFetchers':[],'ImageFetchers':[]}]}})
    folders=api('GET','/Library/VirtualFolders')
    library=next(f for f in folders if f['Name']=='Bridge Test')
    config_body=api('GET','/Plugins/89654c7ebf444e9f8adf2219fe61fa36/Configuration')
    config_body['EnabledLibraryIds']=[library['ItemId']]
    api('POST','/Plugins/89654c7ebf444e9f8adf2219fe61fa36/Configuration',config_body)
    tasks=api('GET','/ScheduledTasks')
    scan=next(t for t in tasks if t['Key']=='RefreshLibrary')
    def run_scan():
        # One request to the ordinary library scan; no per-item refresh calls.
        before=api('GET','/ScheduledTasks/'+scan['Id']).get('LastExecutionResult')
        api('POST','/ScheduledTasks/Running/'+scan['Id'])
        for _ in range(240):
            task=api('GET','/ScheduledTasks/'+scan['Id'])
            if task['State']=='Idle' and task.get('LastExecutionResult') != before:
                assert task['LastExecutionResult']['Status']=='Completed',task
                return
            time.sleep(0.25)
        raise AssertionError('Scan timeout')
    def streams():
        items=api('GET','/Items?'+urllib.parse.urlencode({'userId':user,'Recursive':'true','IncludeItemTypes':'Movie','Fields':'MediaStreams,Path'}))['Items']
        item=next(i for i in items if i.get('Path')==str(video))
        return item,[s for s in item.get('MediaStreams',[]) if s.get('IsExternal')]
    run_scan()
    item,external=streams()
    assert any(s['Type']=='Audio' and s.get('Language')=='rus' for s in external), external
    assert any(s['Type']=='Subtitle' and s.get('Language')=='rus' for s in external), external
    reserved_stream=next(s for s in external if '%44efault Studio' in s.get('Path',''))
    assert not reserved_stream.get('IsDefault') and not reserved_stream.get('IsForced'),reserved_stream
    signs_stream=next(s for s in external if '.Signs.' in s.get('Path',''))
    assert not signs_stream.get('IsForced') and not signs_stream.get('Language'),signs_stream
    flagged_stream=next(s for s in external if '.Caption Team.' in s.get('Path',''))
    assert flagged_stream.get('Language')=='eng' and flagged_stream.get('IsDefault') and flagged_stream.get('IsForced') and flagged_stream.get('IsHearingImpaired'),flagged_stream
    multi_streams=[s for s in external if '.Multi Studio.' in s.get('Path','')]
    assert len(multi_streams)==2,multi_streams
    assert {s.get('Language') for s in multi_streams}=={'jpn','eng'},multi_streams
    assert not any(s.get('IsForced') or s.get('IsDefault') for s in multi_streams),multi_streams
    assert {s.get('Title') for s in multi_streams}=={'Japanese Original','English Original'},multi_streams
    initial_audio=sum(s['Type']=='Audio' for s in external)
    links=[Path(s['Path']) for s in external]
    assert any(os.path.samefile(f,track) for f in links)
    assert any(os.path.samefile(f,sub) for f in links)
    proof={'jellyfin':info['Version'],'platform':os.name,'A':{'scanRequests':1,'streams':external}}
    later=media/'Audio'/'Second Studio'
    later.mkdir(parents=True)
    later_track=later/(stem+'.eng.mka')
    shutil.copy2(track,later_track)
    run_scan()
    _,external2=streams()
    assert sum(s['Type']=='Audio' for s in external2)==initial_audio+1,external2
    assert any(s['Type']=='Audio' and s.get('Language')=='eng' for s in external2),external2
    assert any(os.path.samefile(Path(s['Path']),later_track) for s in external2 if s['Type']=='Audio')
    proof['B']={'scanRequests':1,'streams':external2,'videoUnchanged':hashlib.sha256(video.read_bytes()).hexdigest()==originals[str(video)]}
    # Source replacement at the same path must repair only the owned link and re-probe.
    old_identity=os.stat(later_track).st_ino
    later_track.rename(later_track.with_suffix('.old'))
    ff('-f','lavfi','-i','sine=frequency=880:sample_rate=48000','-t','1','-c:a','flac',later_track)
    os.utime(later_track,(time.time()-120,time.time()-120))
    assert os.stat(later_track).st_ino != old_identity
    run_scan()
    _,external3=streams()
    assert any(os.path.samefile(Path(s['Path']),later_track) for s in external3 if s['Type']=='Audio')
    registry=data/'data'/'sidecar-bridge'/'links.json'
    if not registry.exists(): registry=next(data.rglob('links.json'))
    registry_bytes=registry.read_bytes()
    run_scan()
    assert registry.read_bytes()==registry_bytes,'Idempotent scan rewrote registry'
    proof['C']={'sourceReplacement':True,'idempotentRegistry':True,'streams':external3}
    # Admin-only diagnostic endpoints and read-only preview.
    report=api('GET','/SidecarBridge/Report')
    assert any(e['Status']=='Replaced' for e in report),report
    preview=api('POST','/SidecarBridge/Preview',{'LibraryId':library['ItemId'],'VideoPath':str(video)})
    assert not preview['ChangedPaths'] and registry.read_bytes()==registry_bytes
    req=urllib.request.Request(base+'/SidecarBridge/Report')
    try: opener.open(req); raise AssertionError('Anonymous diagnostics were accepted')
    except urllib.error.HTTPError as ex: assert ex.code in (401,403),ex.code
    proof['D']={'anonymousDiagnosticsDenied':True,'previewReadOnly':True}
    proof['E']={'reservedStudioNotAFlag':True,'signsNotForced':True,'explicitSubtitleFlags':True,'multiStreamMetadataPreserved':True}
    for path,digest in originals.items(): assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest
    (root/'proof.json').write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding='utf-8')
    print('PASS A: first scan registers hardlinked audio + subtitle in MediaStreams')
    print('PASS B: one ordinary scan registers new audio on unchanged video')
    print('PASS original file hashes and hardlink identities')
    print('PASS source replacement, idempotent registry, preview and anonymous access denial')
finally:
    server.terminate()
    try: server.wait(timeout=15)
    except subprocess.TimeoutExpired: server.kill(); server.wait()
    log.close()
