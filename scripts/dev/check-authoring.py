#!/usr/bin/env python3
"""Offline 16.2 integration and resource contracts; DOES NOT compile or execute C#.

Use --baseline with the last conversation source ZIP to verify byte preservation.
Use dotnet run -c Release -- --authoring-self-test for the real CPU tests.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path)
    args = parser.parse_args()
    checks = []
    def check(ok, label):
        if not ok:
            raise AssertionError(label)
        checks.append(label)
        print('PASS', label)
    def text(name): return (ROOT/name).read_text(encoding='utf-8')
    workflow = text('src/UI/Editor/Viewer.Editor.Workflow.cs')
    doc = text('src/Core/Editing/EditorDocument.cs')
    inputs = text('src/UI/Editor/Viewer.Editor.Input.cs')
    reference = text('src/UI/Editor/Viewer.Editor.Reference.Add.cs')
    export = text('src/Core/Export/ChartExport.cs')
    project = text('src/Core/Models/ViewerProject.cs')
    check(all(label in workflow for label in ('"VSM"', '"VSM + cgmk config"', '"Chart Folder"')), 'three export modes wired into workflow')
    check('ChartExportInput.Capture(editor, Current)' in workflow and 'Task.Run' in workflow, 'export uses an authored snapshot and worker task')
    check('Directory.Move(stage, plan.Destination)' in export and 'File.Move(Path.Combine(stage, item.Name), target, false)' in export, 'staged non-overwriting commit paths present')
    check('HashFile(target, cancellation) != item.Sha256' in export and 'Source changed while exporting' in export, 'resource integrity rechecked before commit')
    check('ECG_WINDOW_MOVEMENT_FILE' in text('src/Core/Export/ChartExportInput.cs'), 'inline window export removes obsolete FILE reference')
    check('Kuroaki.sgv.json' in export and 'project.Images = stem + ".vsp"' in export, 'folder writes relative project and selected VSP')
    check('public IReadOnlyList<TimelineMarker> Markers' in doc and 'TimelineMarker[] Markers' in doc, 'marker list participates in undo snapshots')
    check('EditorMarkers' in project and 'project.EditorMarkers = document.Markers.ToList()' in text('src/Core/Export/ChartExportInput.cs'), 'project persistence and export snapshot retain markers')
    check('e.Scan == 8 && editDrag == null' in inputs and 'e.Repeat != 0' in inputs and 'if (modalActive)' in inputs, 'E handling gated by edit context and repeat/text-input checks')
    check('MARK / E' in text('src/UI/Editor/Viewer.Editor.Timeline.cs') and 'CLEAR TARGET' in text('src/UI/Editor/Viewer.Editor.Timeline.cs'), 'visible marker and clear-anchor controls')
    check('CUSTOM NAME' in workflow and 'GimmickAuthoring.Create' in workflow, 'arbitrary-name addition uses shared validation')
    check('USE CUSTOM OBJ' in workflow and 'addSwitchObject' in workflow, 'custom object change requires explicit choice')
    check('e.Button == 3' in reference and 'referenceAddBeat = InsertionBeat' in reference, 'documentation right-click freezes insertion time')
    check('entry.MatchPattern.Length > 0' in reference and 'OpenAddGimmick(entry, referenceAddBeat)' in reference, 'templates enter the concrete-name form')
    check('source' not in reference.split('void AddReferenceEntry')[1].split('void DrawReferenceAddMenu')[0].lower(), 'documentation addition does not write source files directly')
    check('editor.Change("Add " + clip.Name' in workflow and 'FocusAddedTrack' in workflow, 'one add operation is undoable and reveals target track')
    check('Version = "0.1.3"' in text('src/Core/Projects/Paths.cs') and 'BuildNumber = "17.0"' in text('src/Core/Projects/Paths.cs'), 'runtime version is 0.1.3 / 17.0')
    check('--authoring-self-test' in text('src/App/Program.cs') and (ROOT/'tests/SelfTests/AuthoringSelfTest.cs').is_file(), 'real CPU tests included and routed before GPU initialization (not run here)')
    check('VSM_REFERENCE_PREVIEW8.md' not in text('scripts/publish-common.sh'), 'no stale mandatory preview8 packaging path')
    check('vsm格式说明.md' not in text('scripts/publish-common.sh') and 'Custom Gimmick说明.md' not in text('scripts/publish-windows.ps1'), 'publish does not depend on raw Chinese filenames')
    # This calculates selected declared color pairs, not whole-application accessibility.
    def lum(rgb):
        vals=[((c/255)/12.92 if c/255 <= .04045 else ((c/255+.055)/1.055)**2.4) for c in rgb]
        return sum(a*b for a,b in zip(vals, (.2126,.7152,.0722)))
    def color(n): return tuple(int(n[i:i+2],16) for i in (0,2,4))
    theme = text('src/UI/Viewer.cs') + text('src/UI/Viewer.Theme.cs')
    pairs={
        'body/panel': ('F4F7FC','171E2B'),
        'secondary/panel': ('B9C6D8','171E2B'),
        'button/primary': ('FFFFFF','B82046'),
        'trackLabel/alternate': ('F4F7FC','243044'),
    }
    ratios={}
    for name,(fg,bg) in pairs.items():
        check('0x'+fg in theme or fg=='FFFFFF', name+': foreground declared in shared UI source')
        a,b=sorted((lum(color(fg)),lum(color(bg))))
        ratios[name]=round((b+.05)/(a+.05),3)
        check(ratios[name]>=4.5, name+f': specified static contrast {ratios[name]}:1')
    check(not any(p.suffix.lower() in {'.ttf','.ttc','.otf','.otc','.woff','.woff2'} for p in ROOT.rglob('*') if p.is_file()), 'no font binaries in deliverable')
    preserved = {}
    if args.baseline:
        with zipfile.ZipFile(args.baseline) as z:
            project_names=[n for n in z.namelist() if n.endswith('KuroakiGimmick.csproj') and '__MACOSX' not in n]
            check(len(project_names)==1, 'baseline has a single project')
            prefix=project_names[0][:-len('KuroakiGimmick.csproj')]
            names=[n for n in z.namelist() if n.startswith(prefix) and not n.endswith('/')]
            groups=('Assets/', 'Integrations/', 'src/Graphics/', 'src/Core/Windows/')
            for group in groups:
                relevant=[n for n in names if n[len(prefix):].startswith(group)]
                check(all((ROOT/n[len(prefix):]).read_bytes()==z.read(n) for n in relevant), f'{group}: all {len(relevant)} original files byte-identical')
                preserved[group]=len(relevant)
    report={
        'version':'0.1.3','build':'17.0','scope':'offline source/payload/declared-color contracts only',
        'compiled':False,'csharpTestsExecuted':False,'nativeUiExecuted':False,
        'contrastPairs':ratios,'preservedFiles':preserved,'checks':checks,
    }
    (ROOT/'docs/validation-authoring-static.json').write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n', encoding='utf-8')
    print(f'{len(checks)} offline contracts passed. Not a compiler or UI execution test.')

if __name__=='__main__': main()
