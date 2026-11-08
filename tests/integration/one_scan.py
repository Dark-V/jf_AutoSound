"""Disposable REAL Jellyfin scan test. FFmpeg is used only to generate test fixtures.
Never points to an existing server; all server state and media live below --work.
"""
import argparse
from functools import partial
import hashlib
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
from threading import Thread
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--server', required=True, type=Path, help='Published jellyfin.dll')
package=p.add_mutually_exclusive_group(required=True)
package.add_argument('--plugin', type=Path, help='Directory containing built plugin DLLs')
package.add_argument('--plugin-zip', type=Path, help='Install the tested ZIP through a loopback Jellyfin catalog')
package.add_argument('--catalog-url', help='Install from a published Jellyfin catalog')
p.add_argument('--version', help='Expected plugin version for a published catalog')
p.add_argument('--upgrade-from-zip', type=Path, help='Seed an older plugin before installing the catalog version')
p.add_argument('--scheduled-update', action='store_true', help='Enable auto-update on the test seed and run Jellyfin’s update task')
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--ffmpeg', default='ffmpeg')
p.add_argument('--work', required=True, type=Path, help='A new, empty disposable directory')
a = p.parse_args()
if a.catalog_url and not a.version:
    p.error('--catalog-url requires --version')
if a.upgrade_from_zip and a.plugin:
    p.error('--upgrade-from-zip requires a catalog installation')
if a.scheduled_update and not a.upgrade_from_zip:
    p.error('--scheduled-update requires --upgrade-from-zip')
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
config.mkdir()
guid = '89654c7e-bf44-4e9f-8adf-2219fe61fa36'
assemblies = {'Jellyfin.Plugin.SidecarBridge.dll', 'SidecarBridge.Core.dll'}
plugin_config_route = f'/Plugins/{guid}/Configuration'


def zip_meta(archive):
    allowed = assemblies | {'meta.json', 'icon.png', 'LICENSE', 'INSTALL.txt'}
    assert sorted(archive.namelist()) == sorted(allowed), 'Unexpected installation ZIP entries'
    meta = json.loads(archive.read('meta.json'))
    assert meta['guid'] == guid and set(meta['assemblies']) == assemblies, meta
    return meta


old_version = None
if a.upgrade_from_zip:
    with zipfile.ZipFile(a.upgrade_from_zip) as archive:
        seed = zip_meta(archive)
        old_version = seed['version']
        plugin = data / 'plugins' / f'SidecarBridge_{old_version}'
        plugin.mkdir(parents=True)
        for name in archive.namelist():
            (plugin / name).write_bytes(archive.read(name))
        if a.scheduled_update:
            seed['autoUpdate'] = True
            (plugin / 'meta.json').write_text(json.dumps(seed), encoding='utf-8')

catalog_url = a.catalog_url
catalog_server = None
expected_version = a.version
expected_auto_update = True
if a.plugin_zip:
    with zipfile.ZipFile(a.plugin_zip) as archive:
        meta = zip_meta(archive)
    expected_version = meta['version']
    expected_auto_update = meta['autoUpdate']
    staging = root / 'catalog'
    staging.mkdir()
    shutil.copy2(a.plugin_zip, staging / a.plugin_zip.name)
    catalog_server = ThreadingHTTPServer(('127.0.0.1', 0), partial(SimpleHTTPRequestHandler, directory=str(staging)))
    catalog_base = f'http://127.0.0.1:{catalog_server.server_port}'
    catalog_url = catalog_base + '/manifest.json'
    entry = {key: meta[key] for key in ('guid', 'name', 'description', 'overview', 'category', 'owner')}
    entry['versions'] = [{
        **{key: meta[key] for key in ('version', 'targetAbi')},
        'changelog': meta.get('changelog', ''),
        'sourceUrl': f'{catalog_base}/{a.plugin_zip.name}',
        'checksum': hashlib.md5(a.plugin_zip.read_bytes(), usedforsecurity=False).hexdigest(),
    }]
    (staging / 'manifest.json').write_text(json.dumps([entry]), encoding='utf-8')
    Thread(target=catalog_server.serve_forever, daemon=True).start()
elif a.plugin:
    project = Path(__file__).resolve().parents[2] / 'src/Jellyfin.Plugin.SidecarBridge/Jellyfin.Plugin.SidecarBridge.csproj'
    expected_version = ET.parse(project).findtext('./PropertyGroup/Version')
    plugin = data / 'plugins' / f'SidecarBridge_{expected_version}'
    plugin.mkdir(parents=True)
    for name in assemblies:
        shutil.copy2(a.plugin.resolve()/name, plugin/name)
    (plugin/'meta.json').write_text(json.dumps({'name':'Sidecar Bridge','guid':guid,
        'version':expected_version,'targetAbi':'12.4.0.0','status':'Active'}),encoding='utf-8')
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
server = None


def start_server():
    global server
    server = subprocess.Popen([a.dotnet,str(a.server.resolve()),'--datadir',str(data),'--configdir',str(config),
        '--cachedir',str(root/'cache'),'--logdir',str(root/'logs'),'--nowebclient','--nonetchange',
        '--ffmpeg',str(Path(shutil.which(a.ffmpeg) or a.ffmpeg).resolve())],stdout=log,stderr=subprocess.STDOUT,env=env)
    for _ in range(120):
        if server.poll() is not None: raise AssertionError('Jellyfin exited; inspect server.log')
        try:
            info = api('GET', '/System/Info/Public', authenticated=False)
            if info.get('Version'):
                return info
        except (AssertionError, urllib.error.URLError, TimeoutError):
            pass
        time.sleep(0.5)
    raise AssertionError('Jellyfin startup timeout; inspect server.log')


def assert_active(version):
    plugins = [item for item in api('GET', '/Plugins') if item['Id'].replace('-', '') == guid.replace('-', '')]
    active = [item for item in plugins if item['Status'] == 'Active']
    assert len(active) == 1 and active[0]['Version'] == version, plugins
    api('GET', plugin_config_route)


def run_task(key):
    selected = next(task for task in api('GET', '/ScheduledTasks') if task['Key'] == key)
    task_id = selected['Id']
    route = f'/ScheduledTasks/{task_id}'
    for _ in range(120):
        before = api('GET', route)
        if before['State'] == 'Idle':
            break
        time.sleep(0.25)
    else:
        raise AssertionError(f'{key} was already running')
    api('POST', f'/ScheduledTasks/Running/{task_id}')
    for _ in range(240):
        task = api('GET', route)
        if task['State'] == 'Idle' and task.get('LastExecutionResult') != before.get('LastExecutionResult'):
            assert task['LastExecutionResult']['Status'] == 'Completed', task
            return
        time.sleep(0.25)
    raise AssertionError(f'{key} timeout')


def check_linked_episodes(user_id, settings):
    shows = root / 'shows'
    series = shows / 'Bridge Series'
    series.mkdir(parents=True)
    regular = series / 'Bridge Series S01E01.mkv'
    special = series / 'Bridge Series S01E02 Special.mkv'
    # Model an imported special given the regular episode's number by metadata.
    special.with_suffix('.nfo').write_text(
        '<episodedetails><title>Special</title><season>1</season><episode>1</episode></episodedetails>', encoding='utf-8')
    sources = {}
    for episode in (regular, special):
        shutil.copy2(video, episode)
        subtitle = series / 'Rus Subs' / 'Translation' / (episode.stem + '.srt')
        subtitle.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(sub, subtitle)
        sources[episode] = {'Subtitle': subtitle}
    dub = series / 'Rus Sound' / 'Studio' / (regular.stem + '.mka')
    dub.parent.mkdir(parents=True)
    shutil.copy2(track, dub)
    sources[regular]['Audio'] = dub

    query = urllib.parse.urlencode({'name': 'Bridge Shows', 'collectionType': 'tvshows', 'paths': str(shows)})
    api('POST', '/Library/VirtualFolders?' + query, {'LibraryOptions': {
        'EnableRealtimeMonitor': False, 'EnableInternetProviders': False, 'EnableAutomaticSeriesGrouping': False,
        'TypeOptions': [{'Type': kind, 'MetadataFetchers': [], 'ImageFetchers': []} for kind in ('Series', 'Season', 'Episode')],
    }})
    tv_library = next(f for f in api('GET', '/Library/VirtualFolders') if f['Name'] == 'Bridge Shows')
    run_task('RefreshLibrary')
    query = urllib.parse.urlencode({'userId': user_id, 'Recursive': 'true', 'IncludeItemTypes': 'Episode', 'Fields': 'Path'})
    items_url = '/Items?' + query
    items = {i['Path']: i['Id'] for i in api('GET', items_url)['Items'] if i.get('Path')}
    assert all(str(p) in items for p in sources), items
    query = urllib.parse.urlencode({'ids': ','.join(items[str(p)] for p in sources)})
    api('POST', '/Videos/MergeVersions?' + query)
    settings['EnabledLibraryIds'].append(tv_library['ItemId'])
    api('POST', plugin_config_route, settings)
    api('POST', '/System/Shutdown')
    server.wait(timeout=30)
    start_server()
    run_task('RefreshLibrary')

    visible = [i for i in api('GET', items_url)['Items'] if i.get('Path') in items]
    assert len(visible) == 1, visible
    playback = api('GET', f"/Items/{visible[0]['Id']}/PlaybackInfo?userId={user_id}")
    media_sources = {Path(item['Path']): item for item in playback['MediaSources']}
    assert set(media_sources) == set(sources), media_sources.keys()
    for episode, expected in sources.items():
        external = [s for s in media_sources[episode]['MediaStreams'] if s.get('IsExternal')]
        assert len(external) == len(expected), (episode, external)
        for stream in external:
            assert stream.get('Language') == 'rus' and os.path.samefile(stream['Path'], expected[stream['Type']]), stream
    print('PASS linked episode versions each expose their own tracks after one ordinary scan', flush=True)
    return {'versionCount': len(media_sources), 'scanRequestsAfterMerge': 1, 'pathsAndStreamsVerified': True}


try:
    info = start_server()
    server_abi = '.'.join((info['Version'].split('.') + ['0'])[:4])
    assert server_abi == '12.4.0.0', info
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
    catalog_proof = None
    previous_settings = None
    if old_version:
        assert_active(old_version)
        previous_settings = api('GET', plugin_config_route)
        previous_settings['EnabledLibraryIds'] = [library['ItemId']]
        previous_settings['Rules']['MinimumAgeSeconds'] = 17
        api('POST', plugin_config_route, previous_settings)

    if catalog_url:
        api('POST', '/Repositories', [{'Name': 'Sidecar Bridge test', 'Url': catalog_url, 'Enabled': True}])
        packages = api('GET', '/Packages')
        package = next(item for item in packages if item['guid'].replace('-', '') == guid.replace('-', ''))
        selected = package['versions'][0]
        assert package['name'] == 'Sidecar Bridge' and selected['version'] == expected_version, package
        assert selected['targetAbi'] == server_abi, selected
        if a.scheduled_update:
            run_task('PluginUpdates')
        else:
            query = urllib.parse.urlencode({'assemblyGuid': guid, 'repositoryUrl': catalog_url})
            api('POST', '/Packages/Installed/Sidecar%20Bridge?' + query)
        installed = data / 'plugins' / f'Sidecar Bridge_{expected_version}'
        installed_meta = json.loads((installed / 'meta.json').read_text(encoding='utf-8'))
        assert installed_meta['version'] == expected_version, installed_meta
        assert installed_meta['autoUpdate'] is expected_auto_update, installed_meta
        assert all((installed / name).is_file() for name in assemblies)
        assert api('GET', '/System/Info')['HasPendingRestart']
        api('POST', '/System/Shutdown')
        server.wait(timeout=30)
        assert start_server()['Version'] == info['Version']
        assert_active(expected_version)
        if previous_settings is not None:
            assert api('GET', plugin_config_route) == previous_settings, 'Plugin configuration changed during the upgrade'
        catalog_proof = {
            'url': catalog_url, 'version': expected_version, 'targetAbi': selected['targetAbi'],
            'sourceUrl': selected['sourceUrl'], 'checksum': selected['checksum'], 'restartedAndActive': True,
            'upgradedFrom': old_version, 'settingsPreserved': previous_settings is not None,
            'scheduledUpdate': a.scheduled_update,
        }
        print(f'PASS Jellyfin catalog installation and restart: {expected_version}', flush=True)
        if old_version:
            print(f'PASS upgrade from {old_version}: existing settings preserved', flush=True)

    config_body = api('GET', plugin_config_route)
    config_body['EnabledLibraryIds'] = [library['ItemId']]
    api('POST', plugin_config_route, config_body)

    def run_scan():
        # One request to the ordinary library scan; no per-item refresh calls.
        run_task('RefreshLibrary')
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
    proof={'jellyfin':info['Version'],'platform':os.name,'catalog':catalog_proof,'A':{'scanRequests':1,'streams':external}}
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
    proof['linkedEpisodes'] = check_linked_episodes(user, config_body)
    for path,digest in originals.items(): assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest
    (root/'proof.json').write_text(json.dumps(proof,ensure_ascii=False,indent=2),encoding='utf-8')
    print('PASS A: first scan registers hardlinked audio + subtitle in MediaStreams')
    print('PASS B: one ordinary scan registers new audio on unchanged video')
    print('PASS original file hashes and hardlink identities')
    print('PASS source replacement, idempotent registry, preview and anonymous access denial')
finally:
    if server is not None and server.poll() is None:
        server.terminate()
        try: server.wait(timeout=15)
        except subprocess.TimeoutExpired: server.kill(); server.wait()
    if catalog_server is not None:
        catalog_server.shutdown()
        catalog_server.server_close()
    log.close()
