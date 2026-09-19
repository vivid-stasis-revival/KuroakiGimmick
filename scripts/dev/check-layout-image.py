#!/usr/bin/env python3
"""Offline integration contracts for 16.2. This is NOT a C# compiler or UI test."""
from pathlib import Path
import json
import math
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
checks = []
def check(value, label):
    if not value:
        raise AssertionError(label)
    checks.append(label)
    print('PASS', label)
def text(name):
    return (ROOT / name).read_text(encoding='utf-8')

native = '\n'.join(p.read_text() for p in (ROOT/'src/Native').glob('*.cs'))
for function in ['SDL_GetWindowDisplayScale', 'SDL_GetWindowPixelDensity', 'SDL_CaptureMouse']:
    check(len(re.findall(r'extern\s+(?:float|bool)\s+'+function+r'\s*\(', native)) == 1,
          'single native declaration: '+function)
input_code = text('src/Native/Sdl.Input.cs')
for offset, field in [(20, 'DropX'), (24, 'DropY'), (40, 'DropData')]:
    check(bool(re.search(r'FieldOffset\('+str(offset)+r'\)\]\s*public\s+\w+\s+'+field, input_code)),
          '64-bit SDL drop offset: '+field)
check('e = LogicalInput(e);' in text('src/UI/Viewer.Input.cs') and
      'HandleImageDrop(e)' in text('src/UI/Viewer.Input.cs'), 'input projection and drop handler wired')
check('UpdateImageImport();' in text('src/UI/Viewer.Input.cs') and
      'ReleaseImageImports();' in text('src/UI/Viewer.cs'), 'image task completion and resource cleanup wired')
check('DrawLayout(w, h);' in text('src/UI/Viewer.Drawing.cs') and
      'DrawImageImport(w, h);' in text('src/UI/Viewer.Drawing.cs'), 'new overlays are actually drawn')
check('(!LayoutVisible || (layoutInput && layoutOpen))' in text('src/UI/Viewer.cs'),
      'closing layout overlay cannot activate controls')
check('Sdl.SDL_CaptureMouse(false)' in text('src/UI/Viewer.Layout.cs') and
      'resizeShownWidth + e.X - resizeStartX' in text('src/UI/Viewer.Layout.cs'),
      'splitter uses pointer delta and releases capture')
check('Workspace.Normalize()' in text('src/Core/Projects/ViewerSettings.cs') and
      'UiScale' not in text('src/Core/Models/ViewerProject.cs'), 'layout preferences remain device-only')
check('ImageText' in text('src/Core/Editing/EditorDocument.cs') and
      'Images.Restore(s.ImageText)' in text('src/Core/Editing/EditorDocument.cs'), 'VSP text participates in undo snapshots')
check('Images.PrepareSave(dir, stem, files)' in text('src/Core/Editing/EditorDocument.cs'), 'VSP and imported assets use save transaction')
check('body = input.ImageText;' in text('src/Core/Export/ChartExport.cs') and
      'VspDocument.Rewrite' in text('src/Core/Export/ChartExport.cs'), 'export consumes authored VSP and relocates references')
check('editedVsp, imageResourceRoot' in text('src/Core/Projects/Session.cs') and
      'imageText, imageRoot' in text('src/UI/Editor/Viewer.Editor.cs'), 'preview receives authored VSP and resource root')
check('ImageResult.FromStream' in text('src/Core/Editing/ImageImportBatch.cs') and
      'SHA256.HashData(stream)' in text('src/Core/Editing/ImageImportBatch.cs'), 'imports decode and hash private copies')
check('ImagePathsRelativeToVsp = false' in text('src/Core/Projects/Session.ProjectFiles.cs'),
      'attaching a raw VSP clears companion-relative mode')
check('--layout-image-self-test' in text('src/App/Program.cs'), 'C# regression suite reachable before GPU initialization')

# Independent mirror of projection and split geometry. Does not execute C#.
def viewport(w, h, display, density, user=1, follow=True):
    requested = user * (display / density if follow else 1)
    scale = max(.01, min(requested, w/1180, h/860))
    return max(1180, round(w/scale)), max(860, round(h/scale))
check(viewport(3840,2160,2,1)==(1920,1080), 'projection mirror: Windows 200 percent')
check(viewport(1920,1080,2,2)==(1920,1080), 'projection mirror: Retina is not double-scaled')
check(viewport(3840,2160,2,1,1.5,False)==(2560,1440), 'projection mirror: explicit manual scale')
check(viewport(1180,860,1,1,2.5)==(1180,860), 'projection mirror: zoom limit')
cases = 0
for w in (1180,1440,1920,2560,3840):
    for h in (860,1080,1440,2160):
        for editor in (False,True):
            for fraction in (.15,.52,.85):
                left,right=420.,480.
                excess=max(0,left+right-max(476,w-88-590))
                grow=left-194+right-282
                left-=excess*(left-194)/grow
                right-=excess*(right-282)/grow
                px,rx=44+left,w-24-right
                low=330 if editor else 355
                high=max(low+1,h-270)
                split=low+fraction*(high-low)
                assert rx-px-20 >= 589.99 and rx+right<=w-24+.01
                assert abs((split-low)/(high-low)-fraction)<1e-8
                assert split<h-200
                cases+=1
check(cases == 120, 'geometry mirror: 120 window/mode/split combinations')
for script in ['verify.sh','publish-common.sh','publish-mac.sh']:
    subprocess.run(['bash','-n',str(ROOT/'scripts'/script)],check=True)
check(True,'shell syntax for verify and publish scripts')
result={'kind':'offline source contracts and independent Python math mirror',
        'version':'v0.1.2 / 16.2','csharpCompiled':False,'csharpTestsExecuted':False,
        'nativeUiTested':False,'checks':checks,'geometryMirrorCases':cases}
(ROOT/'docs/validation-layout-image-static.json').write_text(json.dumps(result,indent=2)+'\n')
print(f'{len(checks)} offline contracts passed. C# execution is NOT covered.')
