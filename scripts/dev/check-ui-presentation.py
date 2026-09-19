#!/usr/bin/env python3
"""Offline preview.9 presentation contracts. Does not compile or execute C#/SDL.

Requires Pillow for the independent glyph/layout mirror. Native C# layout and
motion execution is covered by the included (not locally executed) CPU tests.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import sys
import zipfile
from pathlib import Path
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--baseline',type=Path);args=parser.parse_args()
    checks=[]
    def check(ok,label):
        if not ok:raise AssertionError(label)
        checks.append(label);print('PASS',label)
    data=json.loads((ROOT/'Assets/Documentation/vsm-reference.json').read_text())
    check(len(data['Entries'])==301 and sum(e['Kind']=='mod' for e in data['Entries'])==205,'301 browsable entries retain 205 parameter rows')
    check(not any(e['Kind'] in {'guide','note','source'} or e['Notes'] for e in data['Entries']),'no injected note pages or full-source navigation entries')
    reference='\n'.join(p.read_text() for p in (ROOT/'src/UI/Editor').glob('*Reference*.cs'))
    check('SourceExcerpt(' not in reference and 'referenceRaw' not in reference,'native docs does not render raw source or expose full-source copy')
    inputs=(ROOT/'src/UI/Editor/Viewer.Editor.Input.cs').read_text()
    check('if (ReferenceVisible) return HandleReferenceInput(e);' in inputs and 'if (UiClosingOverlay)' in inputs,'open and closing overlays gate editor input')
    drawing=(ROOT/'src/UI/Editor/Viewer.Editor.Reference.Drawing.cs').read_text()
    check('referenceOutlineCoversArticle' in drawing and 'articleInput' in drawing,'compact outline masks clicks on underlying article cells and code buttons')
    check('referenceBodyShown' in drawing and 'referenceListShown' in drawing,'navigation and article hit testing use displayed scroll positions')
    timeline=(ROOT/'src/UI/Editor/Viewer.Editor.Timeline.cs').read_text()
    check('shownTrackScroll' in timeline and 'float playhead = BeatX(Current.Timeline.Bpm.Beat(time))' in timeline,'vertical list scroll is animated; playhead still comes from exact song time')
    help_src=(ROOT/'src/UI/Editor/Viewer.Editor.Help.cs').read_text()
    animations=(ROOT/'src/UI/Viewer.Animations.cs').read_text()
    final_drawing = (ROOT/'src/UI/Viewer.Drawing.cs').read_text()
    check('!UiBlockingOverlayVisible && !ImageGestureActive ? hoveredEditTrack' in final_drawing and 'UiOverlayVisible ? hoveredEditTrack' not in timeline,
          'track help is composited after grips and its visibility is not fed back into its hover source')
    check('trackHelpHitRect.Contains(mouseX, mouseY)' in help_src and 'TrackHelpLeaveGraceMs' in help_src and
          'UiBlockingOverlayVisible' in animations,
          'track help latches the source across the popup and short pointer gap')
    motion=(ROOT/'src/UI/Animation/UiMotion.cs').read_text()
    check(not any(x in motion for x in ('transport','VsmDocument','Renderer','songTime')),'motion engine has no playback/evaluation dependencies')
    check('tween.From = tween.At(now)' in motion and 'Duration <= 0 ? 1' in motion and 'values.Remove(key)' in motion,'motion implementation contains interruptible finite tweens and stale-key eviction')
    settings=(ROOT/'src/Core/Projects/ViewerSettings.cs').read_text()
    check('public bool UiAnimations { get; set; } = true;' in settings and 'preferences.UiAnimations = !preferences.UiAnimations' in (ROOT/'src/UI/Viewer.Settings.cs').read_text(),'animation preference is enabled by default and exposed in Settings')
    check('SetUiAnimationsForTest(false)' in (ROOT/'src/App/Program.cs').read_text(),'single-frame snapshots capture settled layouts')
    check((ROOT/'tests/SelfTests/UiPresentationSelfTest.cs').exists(),'actual C# motion and layout CPU tests are included (not executed by this script)')
    spec=importlib.util.spec_from_file_location('preview_layout',ROOT/'scripts/dev/render-reference-preview.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    regular,bold=module.Face(),module.Face(True)
    cases=0
    for entry in data['Entries']:
        for width in (330,600,900):
            items,anchors,total=module.article(entry,width,regular,bold)
            assert items,entry['Id']
            assert all(a[1]+a[2]<=b[1]+.01 for a,b in zip(items,items[1:])),entry['Id']
            assert items[-1][1]+items[-1][2]<=total+.01,entry['Id']
            assert all(0<=position<=total for _,position in anchors),entry['Id']
            for kind,top,height,lines,widths,text in items:
                if kind.startswith('table'):
                    assert abs(sum(widths)-width)<.01,(entry['Id'],width)
                    face=bold if kind=='table-header' else regular
                    for col,rows in enumerate(lines):
                        assert all(face.measure(row,15)<=widths[col]-24+.1 for row in rows),(entry['Id'],rows)
                elif kind=='code':
                    assert all(regular.measure(line,16)<=width-32+.1 for line in lines[0]),entry['Id']
            cases+=1
    check(cases==903,'independent Python layout mirror: all 301 pages at three widths are non-overlapping and table/code lines fit')
    if args.baseline:
        with zipfile.ZipFile(args.baseline) as z:
            prefix=next(n[:-len('KuroakiGimmick.csproj')] for n in z.namelist() if n.endswith('KuroakiGimmick.csproj') and '__MACOSX' not in n)
            old={n[len(prefix):]:z.read(n) for n in z.namelist() if n.startswith(prefix) and not n.endswith('/')}
        fonts=[n for n in old if n.startswith('Assets/Fonts/')]
        check(all((ROOT/n).read_bytes()==old[n] for n in fonts),f'all {len(fonts)} prior font assets are byte-identical')
        protected=[n for n in old if n.startswith(('src/Core/','src/Graphics/Scene/','src/Graphics/NativeGimmick/','src/Graphics/GameUi/')) and n not in {'src/Core/Projects/Paths.cs','src/Core/Projects/ViewerSettings.cs'} and not n.startswith('src/Core/Documentation/')]
        check(all((ROOT/n).read_bytes()==old[n] for n in protected),f'all {len(protected)} prior game/evaluation/edit/save/scene modules are byte-identical')
        assets=[n for n in old if n.startswith(('Assets/','Integrations/')) and n!='Assets/Documentation/vsm-reference.json']
        check(all((ROOT/n).read_bytes()==old[n] for n in assets),f'all {len(assets)} prior asset/extension files outside the generated docs index are byte-identical')
        check((ROOT/'packages.lock.json').read_bytes()==old['packages.lock.json'],'NuGet dependency lock unchanged')
    report={'revision':'editor-preview.9','scope':'offline source/data/asset/layout-mirror checks','compiled':False,'csharpTestsExecuted':False,'nativeExecuted':False,'pythonLayoutCases':cases,'checks':checks}
    (ROOT/'docs/validation-ui-presentation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    print('PASS',len(checks),'offline presentation checks; C#/SDL execution NOT performed.')

if __name__=='__main__':main()
