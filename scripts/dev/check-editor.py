#!/usr/bin/env python3
"""Offline v0.1.1 editor contracts. NOT a C# compiler, SDL test or game parity proof.

Optional --baseline and --extension compare actual upload bytes. No fixture is rewritten.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]

def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()

def clean_name(name: str) -> bool:
    return not any(p in {'__MACOSX', '.DS_Store', '__pycache__'} or p.startswith('._') for p in Path(name).parts)

def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--baseline', type=Path)
    p.add_argument('--extension', type=Path)
    p.add_argument('--report', type=Path)
    a = p.parse_args()
    report = {'scope': 'offline source/resource contracts', 'compiled': False, 'cpuSelfTestsExecuted': False, 'gpuExecuted': False,
              'macOSExecuted': False, 'windowsExecuted': False, 'checks': []}
    def check(ok: bool, message: str) -> None:
        if not ok:
            raise AssertionError(message)
        report['checks'].append(message)
        print('PASS ' + message)
    check((ROOT/'src/Core/Editing/VsmDocument.cs').is_file() and (ROOT/'src/UI/Editor/Viewer.Editor.Timeline.cs').is_file(), 'editor source modules exist')
    timeline=(ROOT/'src/UI/Editor/Viewer.Editor.Timeline.cs').read_text()
    editor_src=(ROOT/'src/UI/Editor/Viewer.Editor.cs').read_text()
    input_src=(ROOT/'src/UI/Editor/Viewer.Editor.Input.cs').read_text()
    help_src=(ROOT/'src/UI/Editor/EditorTrackHelp.cs').read_text()
    help_view=(ROOT/'src/UI/Editor/Viewer.Editor.Help.cs').read_text()
    check('editTracks.Add(new(key, name, false, proxy, name))' in timeline, 'timeline labels preserve raw mod identifiers')
    check(all(name in editor_src for name in ('scrollspeed','velocity','noterot')), 'core note/SV tracks are exposed even before source clips exist')
    check('trackHelpWHeld' in input_src and 'e.Scan == 26' in input_src and 'Environment.TickCount64' in help_view, 'hover help supports long-hold W details')
    check('scrollspeed × velocity × scrollindN' in help_src and '普通 Note / Bumper' in help_src, 'track help documents SV and note rotation semantics')
    fonts_src=(ROOT/'src/Graphics/Text/Fonts.cs').read_text()
    check('cjk-editor.png' in fonts_src and 'cjk-editor.json' in fonts_src and 'TryCjk' in fonts_src,
          'UI text uses the dedicated bundled CJK bitmap atlas')
    cjk_table=json.loads((ROOT/'Assets/Fonts/cjk-editor.json').read_text())
    cjk_chars={ch for ch in help_src if ord(ch) > 127 and not ch.isspace()}
    check((ROOT/'Assets/Fonts/cjk-editor.png').is_file() and (ROOT/'Assets/Fonts/cjk-editor.png').stat().st_size > 10000,
          'dedicated CJK bitmap atlas is present')
    check(cjk_chars <= set(cjk_table), f'cjk-editor atlas covers all {len(cjk_chars)} non-ASCII characters used by editor track help')
    printable_ascii={chr(i) for i in range(32,127)}
    check(printable_ascii <= set(cjk_table), 'cjk-editor unified atlas covers printable ASCII for single-face help rendering')
    check('unified = false' in fonts_src and 'unified && TryCjk' in fonts_src,
          'Fonts exposes a single-face unified editor rendering path')
    check('HelpBodySize = 17' in help_view and 'HelpTitleSize = 22' in help_view,
          'help card declares readable body/title sizes')
    check('EditorHelpLayout.Wrap' in help_view and 'HelpBodyColor' in help_view,
          'help card wraps paragraphs with a dedicated high-contrast body style')
    help_fonts_src=(ROOT/'src/Graphics/Text/Fonts.Help.cs').read_text()
    check('atlas.Metrics.EmSize' in help_fonts_src and 'OffsetY' in help_fonts_src,
          'help rasterization uses its own em and baseline instead of legacy cell dimensions')
    for stem in ('editor-help-sans', 'editor-help-sans-bold'):
        m=json.loads((ROOT/f'Assets/Fonts/{stem}.json').read_text())
        check(m['Family']=='Noto Sans CJK SC' and m['EmSize']==64,
              stem+': proportional CJK family and explicit 64px em')
        check(cjk_chars | printable_ascii <= set(m['Glyphs']), stem+': current help text and printable ASCII covered')
    for filename in ('demo.sgv.json','proxy.sgv.json'):
        base = ROOT/'Samples/EditorDemo'
        project = json.loads((base/filename).read_text())
        for field in ('Chart','Gimmick','WindowMotion','Audio'):
            check((base/project[field]).is_file(), f'{filename}: referenced {field} exists')
        cfg = json.loads((base/project['WindowMotion']).read_text())
        required = {
            'NewWindowDance': {'op','t','w','overlap','preset','same','x','y','ux','uy','angle','uangle','ax','ay','uax','uay','speed','freq','period','subEase','easeType','reference','easeDur','ease'},
            'WindowResize': {'op','t','w','overlap','sx','sy','usx','usy','px','py','upx','upy','pivotMode','anchor','dur','ease'},
            'HideWindow': {'op','t','w','overlap','show'}, 'ReorderWindows': {'op','t','overlap','order'},
            'SetWindowContent': {'op','t','w','overlap','room'}}
        events = cfg.get('ECG_WINDOW_MOVEMENT_EVENTS', [])
        for index,e in enumerate(events):
            check(required[e['op']] <= set(e), f'{filename}: complete {e["op"]} schema at event {index}')
        if events:
            check(set(required)=={e['op'] for e in events}, 'demo exercises all five window event operations')
            check({'Move','Sway','Wrap','Ellipse','ShakePer'}=={e['preset'] for e in events if e['op']=='NewWindowDance'}, 'demo exercises all five window dance presets')
    for filename in ['scripts/run-editor.command','scripts/publish-common.sh']:
        run = subprocess.run(['bash','-n',str(ROOT/filename)], capture_output=True,text=True)
        check(run.returncode==0, 'bash syntax: '+filename)
    manifest=json.loads((ROOT/'Integrations/ExtCustomGimmick.source-sha256.json').read_text())
    check(all(sha((ROOT/'Integrations/ExtCustomGimmick'/name).read_bytes())==value for name,value in manifest.items()), 'bundled extension file hashes match recorded original inventory')
    check(not [p for p in ROOT.rglob('*') if not clean_name(str(p.relative_to(ROOT)))], 'no Finder metadata or Python cache in package')
    if a.baseline:
        with zipfile.ZipFile(a.baseline) as z:
            project_names=[n for n in z.namelist() if n.endswith('KuroakiGimmick.csproj') and clean_name(n)]
            check(len(project_names)==1,'baseline contains one project root')
            prefix=project_names[0][:-len('KuroakiGimmick.csproj')]
            source={n[len(prefix):]:z.read(n) for n in z.namelist() if n.startswith(prefix) and not n.endswith('/') and clean_name(n)}
            assets=[n for n in source if n.startswith('Assets/')]
            check(all((ROOT/n).is_file() and (ROOT/n).read_bytes()==source[n] for n in assets), f'all {len(assets)} imported assets remain byte-identical')
            check((ROOT/'packages.lock.json').read_bytes()==source['packages.lock.json'], 'dependency lock is unchanged')
            unchanged=[]
            protected=('src/Graphics/SdlGpu/Canvas','src/Graphics/SdlGpu/Shader','src/Graphics/SdlGpu/Texture','src/Graphics/Primitives/', 'src/Graphics/NativeGimmick/', 'src/Graphics/GameUi/')
            for name,data in source.items():
                if name.startswith(protected):
                    current=(ROOT/name).read_bytes()
                    if name in ('src/Graphics/SdlGpu/Canvas.cs','src/Graphics/SdlGpu/Canvas.Geometry.cs'):
                        # The two permitted Canvas changes add/reset presentation-only vertex alpha.
                        # Removing those additions must recover the exact original uploaded source.
                        restored=current.decode('utf-8').replace('        presentationOpacity = 1;\n','').replace('c.A * presentationOpacity','c.A')
                        check(restored.encode('utf-8')==data, 'Canvas unchanged apart from scoped UI opacity: '+name)
                    else:
                        check(current==data, 'protected renderer source unchanged: '+name);unchanged.append(name)
            check(all((ROOT/name).exists() for name in source), 'no non-Finder file from input package was removed')
            report['baselineSha256']=sha(a.baseline.read_bytes());report['unchangedAssets']=len(assets)
            report['changedOriginalFiles']=[name for name,data in source.items() if (ROOT/name).read_bytes()!=data]
    if a.extension:
        with zipfile.ZipFile(a.extension) as z:
            roots=[n for n in z.namelist() if n.endswith('codes/gml_GlobalScript_ExtCustomGimmick.gml') and clean_name(n)]
            check(len(roots)==1,'extension has one expected source root')
            prefix=roots[0][:-len('codes/gml_GlobalScript_ExtCustomGimmick.gml')]
            original={n[len(prefix):]:z.read(n) for n in z.namelist() if n.startswith(prefix) and not n.endswith('/') and clean_name(n)}
            check(set(original)==set(manifest),'extension inventory has all original non-Finder files')
            check(all(sha(data)==manifest[name] for name,data in original.items()),'bundled extension source/native binaries match actual uploaded ZIP bytes')
            report['extensionSha256']=sha(a.extension.read_bytes());report['extensionFiles']=len(original)
    report['status']='passed'
    if a.report:
        a.report.parent.mkdir(parents=True,exist_ok=True)
        a.report.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n')
    print(f'{len(report["checks"])} offline checks passed. Compilation and native execution NOT performed.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
