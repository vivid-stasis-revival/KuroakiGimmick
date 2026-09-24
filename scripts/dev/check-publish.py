#!/usr/bin/env python3
"""Shell packaging regression test with a fake dotnet publisher (no SDK/network).
Checks archive/layout and failure handling; does not claim native execution."""
import json,os,shutil,subprocess,tempfile,zipfile
from pathlib import Path
source=Path(__file__).resolve().parents[2];checks=0

def check(ok,why):
 global checks
 if not ok:raise AssertionError(why)
 checks+=1;print('PASS',why,flush=True)
with tempfile.TemporaryDirectory(prefix='kuroaki publish spaces ') as d:
 root=Path(d)/'project with spaces';root.mkdir();shutil.copytree(source/'scripts',root/'scripts')
 for n in ['KuroakiGimmick.csproj','README.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md']:shutil.copy2(source/n,root/n)
 for n in ['Assets','docs','ThirdParty','Samples','Integrations']:shutil.copytree(source/n,root/n)
 mock=Path(d)/'fake dotnet'
 mock.write_text('''#!/usr/bin/env python3
import json,os,shutil,sys
from pathlib import Path
a=sys.argv[1:];p=Path(a[1]).parent;out=Path(a[a.index('-o')+1]);rid=a[a.index('-r')+1]
print('MOCK publisher '+rid)
if os.getenv('FAIL_PUBLISH'):sys.exit(23)
assert a[:1]==['publish'] and a[a.index('-c')+1]=='Release'
assert a[a.index('--self-contained')+1]=='true'
assert '-p:PublishTrimmed=false' in a and '-p:UseAppHost=true' in a
single_file='true'
assert '-p:PublishSingleFile='+single_file in a
assert '-p:IncludeNativeLibrariesForSelfExtract='+single_file in a
out.mkdir(parents=True)
for n in ['Assets','Samples','Integrations']:shutil.copytree(p/n,out/n)
for f in ['KuroakiGimmick.exe'] if rid.startswith('win-') else ['KuroakiGimmick']:
 exe=out/f;exe.write_bytes(b'fixture')
 if not rid.startswith('win-'):exe.chmod(0o755)
''');mock.chmod(0o755)
 env=os.environ|{'KUROAKI_DOTNET':str(mock)}
 for script,rid in [('publish-mac.sh','osx-arm64'),('publish-mac.sh','osx-x64'),('publish-win.sh','win-x64'),('publish-win.sh','win-arm64')]:
  run=subprocess.run(['bash',str(root/'scripts'/script),rid],env=env,cwd='/',capture_output=True,text=True)
  check(run.returncode==0,'publish orchestration '+rid+' from unrelated cwd and paths with spaces')
  archive=next((root/'dist').glob('*'+rid+'*.zip'))
  with zipfile.ZipFile(archive) as z:
   names=z.namelist();folder=archive.stem+'/'
   check(all(folder+'Assets/'+n in names for n in ['App/Kuroaki.png','Fonts/sans.png','Shaders/proxy.frag','GameUI/game-ui.gameui.json','GimmickExtras/scene_gameplay.runtime.fx.json']),'all resource groups in sibling Assets in '+rid)
   check(not any('asset' in Path(n).parts for n in names),'legacy asset directory absent in '+rid)
   check(not any('MacOS/Assets/' in n for n in names),'no duplicate private Assets in '+rid)
   check(folder+'docs/EXPORT_AND_MARKERS_V0.1.2.md' in names and folder+'docs/DOCS_UI_PREVIEW9.md' in names and folder+'docs/LAYOUT_IMAGES_16_1.md' in names and folder+'docs/IMAGE_OBJECTS_16_2.md' in names,'current documentation included in '+rid)
   check('v0.1.3' in folder and '-17.0-' in folder,'requested release identifiers in '+rid)
   check(folder+'LICENSE' not in names and folder+'THIRD_PARTY_NOTICES.md' in names,'no project license; third-party notices retained in '+rid)
   check(folder+'CHANGELOG.md' in names,'changelog ships so the README footer link resolves in '+rid)
   check(folder+'docs/media/banner.svg' in names,'README banner accompanies '+rid)
   check(folder+'Samples/EditorDemo/demo.sgv.json' in names and folder+'Samples/ImageObjects162/demo.sgv.json' in names and any(n.startswith(folder+'Integrations/ExtCustomGimmick/') for n in names),'sample and original extension accompany '+rid)
   if rid.startswith('win-'):
    check(folder+'KuroakiGimmick.exe' in names and not any(n.startswith(folder) and '/' not in n[len(folder):] and n.endswith(('.dll','.deps.json','.runtimeconfig.json')) for n in names),'single-file Windows executable in '+rid)
   if rid.startswith('osx-'):
    import plistlib
    info=plistlib.loads(z.read(folder+'KuroakiGimmick.app/Contents/Info.plist'))
    check(info['CFBundleShortVersionString']=='0.1.3' and info['CFBundleVersion']=='17.0','macOS bundle release versions '+rid)
    check(info['CFBundleExecutable']=='KuroakiGimmick' and z.getinfo(folder+'KuroakiGimmick.app/Contents/MacOS/KuroakiGimmick').external_attr>>16&0o111,'valid plist and executable ZIP permissions '+rid)
    macos_entries=[n for n in names if n.startswith(folder+'KuroakiGimmick.app/Contents/MacOS/') and '/' not in n[len(folder+'KuroakiGimmick.app/Contents/MacOS/'):] and not n.endswith('/')]
    check(macos_entries==[folder+'KuroakiGimmick.app/Contents/MacOS/KuroakiGimmick'],'single-file macOS executable with no loose runtime files in '+rid)
  # Different RID, no successful pre-existing destination to hit the collision guard.
 previous={p.name:p.read_bytes() for p in (root/'dist').glob('*.zip')}
 run=subprocess.run(['bash',str(root/'scripts/publish-win.sh'),'bad-cpu'],env=env,capture_output=True)
 check(run.returncode==2,'invalid RID rejected before building')
 # Remove a completed x64 directory/archive in the fixture so timestamp collision
 # cannot mask the injected compiler failure; other successful packages stay intact.
 for f in list((root/'dist').glob('*win-x64*')):
  if f.is_dir():shutil.rmtree(f)
  else:f.unlink()
 run=subprocess.run(['bash',str(root/'scripts/publish-win.sh'),'x64'],env=env|{'FAIL_PUBLISH':'1'},capture_output=True,text=True)
 check(run.returncode!=0 and not list((root/'dist').glob('*win-x64*.zip')),'failed dotnet publish cannot produce a success archive')
 check(not list((root/'dist').glob('.publish-*')) and any('MOCK publisher' in p.read_text() for p in (root/'dist').glob('*win-x64*.log')),'failure removes private staging and retains the build log')
 check(all(p.read_bytes()==previous[p.name] for p in (root/'dist').glob('*.zip')),'other completed target packages survive a failed build')
print(f'{checks} publish layout/failure checks passed (mock publisher).')
