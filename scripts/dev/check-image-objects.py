#!/usr/bin/env python3
"""16.2 source contracts and independent numerical checks (NOT C# execution).
No compiler/GPU is invoked. Use scripts/verify.sh for production C# tests.
"""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
import math
import random
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
checks: list[str] = []

def require(ok: bool, label: str) -> None:
    if not ok:
        raise AssertionError(label)
    checks.append(label)
    print('PASS', label)

def source(name: str) -> str:
    return (ROOT / name).read_text(encoding='utf-8')

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path)
    args = parser.parse_args()
    model = source('src/Core/Editing/Images/ImageObjectModel.cs')
    document = source('src/Core/Editing/EditorDocument.ImageObjects.cs')
    canvas = source('src/UI/Editor/Viewer.Editor.ImageCanvas.cs')
    images = source('src/UI/Editor/Viewer.Editor.Images.cs')
    ui = source('src/UI/Editor/Viewer.Editor.ImageObjects.cs')
    timeline = source('src/UI/Editor/Viewer.Editor.Timeline.cs')
    panel = source('src/UI/Editor/Viewer.Editor.ImagePanel.cs')
    entry = source('src/App/Program.cs')
    require('target == id' in model and "name[(split + 1)..]" in source('src/Core/Assets/CustomImages.cs'), 'image lookup retains full underscored ID')
    require('rows.Select(c => c.Name).Distinct' in model and 'CompleteAxes(rows)' in model, 'duplicate writes and independently timed axes are not made into editable XY groups')
    require('c.RepeatEnd == null' in model and 'n != 573613' in model and 'value != "_"' in model, 'repeat and dynamic values remain raw')
    require(all(p in model for p in ['imgxtime', 'imgytime', 'imgscaleytime', 'imgskewx', 'imgskewy']), 'non-invertible layer/time dependencies have explicit transform guard')
    require(all(p in document for p in ['CheckImageSpan', 'Unparsed path event', 'Dynamic path values', 'Select its existing animation']), 'generated writes reject ambiguous overlaps and malformed/dynamic path data')
    require('Change("Set image pose:' in document and 'Change("Add image animation:' in document and 'Change("Offset image path:' in document, 'grouped operations use the existing undo transaction')
    require('if (neighbor.Length == 1)' in document and 'VsmDocument.Number(n.From) == VsmDocument.Number(before)' in document, 'joint linking requires a unique already-connected numeric neighbor')
    require('map.Time(end) - map.Time(begin)' in document and 'map.BpmAtBeat(begin)' in document, 'requested end time converted using production start-BPM duration convention')
    require('Current.Timeline.Bpm.Time(ImageEditBeat)' in ui and 'ImagePoseTarget.Path' in ui, 'pose mode has an explicit write target')
    require('LOCAL 320 x 180' in canvas and 'NO PROXY OR SCREEN FX' in canvas and 'DrawImageCanvas(previewImage, time)' in source('src/UI/Editor/Viewer.Editor.Drawing.cs'), 'image authoring canvas is explicitly separate from scene preview')
    require('imageCanvasMap.ToWorld' in canvas and 'ImageCanvasMap.Resize' in canvas and 'ImageCanvasMap.Hit' in canvas, 'drawing/picking/dragging use a common reversible coordinate map')
    require('editor.Revision == drag.Revision' in canvas and 'CancelImageGesture' in canvas and 'Sdl.SDL_CaptureMouse(false)' in canvas, 'gesture commit verifies owner/revision; cancellation releases capture')
    inp = source('src/UI/Viewer.Input.cs')
    require(inp.index('LogicalInput(e)') < inp.index('HandleActiveImageGesture(e)') < inp.index('HandleLayoutInput(e)'), 'DPI normalization then captured gestures before general layout input')
    require('handles.HasFlag(ImageChannels.Scale)' in canvas and 'handles.HasFlag(ImageChannels.Rotation)' in canvas, 'only writable animation channels expose transform handles')
    require('imageRotationAccumulated +=' in canvas, 'rotation unwrap preserves motion beyond half a turn')
    require('DrawImageGroupClips(track.ImageId!' in timeline and 'expandedImageTracks.Contains' in timeline, 'object tracks coexist with original parameter tracks')
    require('imageInsertPosition' in images and 'editor.ReplaceImageResource' in images and 'SelectImageObject(names[0])' in images, 'import, placement, resource replacement and whole-object selection are wired')
    require('fields[optional] = VsmDocument.N(item.Width)' in source('src/Core/Editing/VspDocument.cs'), 'resource replacement keeps original logical image dimensions')
    require('"+ ANIMATION"' in panel and '"CONTINUE"' in panel and '"START"' in panel and '"END"' in panel, 'animation creation and endpoint controls are present')
    require('if (args.Contains("--image-object-self-test")) return ImageObjectSelfTest.Run();' in entry and entry.index('ImageObjectSelfTest.Run()') < entry.index('new Host(cli)'), 'C# test entry precedes SDL/graphics initialization')
    tests = source('tests/SelfTests/ImageObjectSelfTest.cs')
    require('rebuilt.Timeline.Get' in tests and 'doc.SaveCopy' in tests and 'ChartExport.Write' in tests and 'doc.Undo()' in tests, 'unexecuted C# tests cover production evaluator, undo, save/reopen and actual export')
    require('public const string BuildNumber = "17.0"' in source('src/Core/Projects/Paths.cs') or '"17.0"' in source('src/Core/Projects/Paths.cs'), 'version build identifier is 17.0')
    require('<FileVersion>0.1.3.170</FileVersion>' in source('KuroakiGimmick.csproj'), 'assembly file version updated')
    # Independent double-precision model. This validates the proposed mathematics,
    # not that System.Numerics or UI handlers ran.
    rng = random.Random(162)
    def rot(p: tuple[float,float], a: float) -> tuple[float,float]:
        c,s = math.cos(a),math.sin(a)
        return (p[0]*c-p[1]*s, p[0]*s+p[1]*c)
    numeric_cases = 0
    for _ in range(800):
        angle=rng.uniform(-10,10);sx=rng.choice([-1,1])*rng.uniform(.1,5);sy=rng.choice([-1,1])*rng.uniform(.1,5)
        width,height=rng.uniform(8,1500),rng.uniform(8,1500)
        cx,cy=rng.uniform(-1000,1000),rng.uniform(-1000,1000)
        px,py=rng.uniform(-.49,.49),rng.uniform(-.49,.49)
        wx,wy=rot((px*width*sx,py*height*sy),angle);wx+=cx;wy+=cy
        dpi=rng.choice([1,1.25,1.5,2,3]);zoom=rng.uniform(.05,8);scale=rng.uniform(.5,6)*zoom
        ox,oy=rng.uniform(-500,500),rng.uniform(-500,500)
        device=((ox+wx*scale)*dpi,(oy+wy*scale)*dpi)
        world=((device[0]/dpi-ox)/scale,(device[1]/dpi-oy)/scale)
        ix,iy=rot((world[0]-cx,world[1]-cy),-angle)
        assert math.isclose(ix/(width*sx),px,abs_tol=1e-8) and math.isclose(iy/(height*sy),py,abs_tol=1e-8)
        factor=rng.uniform(-2,3);handle=(rng.choice([-.5,.5]),rng.choice([-.5,.5]))
        init=(handle[0]*width*sx,handle[1]*height*sy)
        target=rot((init[0]*factor,init[1]*factor),angle)
        local=rot(target,-angle);dot=(local[0]*init[0]+local[1]*init[1])/(init[0]**2+init[1]**2)
        assert math.isclose(dot,factor,abs_tol=1e-9)
        numeric_cases += 2
    require(numeric_cases==1600, 'independent projection/inverse-picking and signed uniform-resize mathematics: 1600 randomized cases')
    last=0.;total=0.
    for n in range(1,721):
        current=math.atan2(math.sin(math.radians(n)),math.cos(math.radians(n)))
        total+=math.atan2(math.sin(current-last),math.cos(current-last));last=current
    require(abs(math.degrees(total)-720)<1e-8,'independent continuous rotation unwrapping over 720 degrees')
    def time(b:float)->float: return b*.5 if b<4 else 2+(b-4)*.25
    def beat(t:float)->float: return t*2 if t<2 else 4+(t-2)*4
    for start,end in [(-2,1),(2,6),(4,10),(5,30)]:
        bpm=120 if start<4 else 240;duration=(time(end)-time(start))*bpm/60
        assert math.isclose(beat(time(start)+duration*60/bpm),end,abs_tol=1e-10)
    require(True,'independent animation end-time conversion across a BPM change')
    sample=ROOT/'Samples/ImageObjects162'
    project=json.loads((sample/'demo.sgv.json').read_text())
    require(all((sample/project[k]).is_file() for k in ['Chart','Gimmick','Images','Audio']),'demo project dependencies exist')
    rows=[next(csv.reader([l])) for l in (sample/'ENCORE.vsm').read_text().splitlines() if not l.startswith(('!','//')) and l.strip()]
    require(all(len(row)==7 for row in rows),'demo uses ordinary seven-field VSM only')
    require(sum(row[5].endswith('_card_front') and row[0]=='0' for row in rows)==6,'demo existing picture starts with one six-property pose')
    require(sum(row[5].startswith(('imgx_','imgy_')) and row[0] in ['4','8'] for row in rows)==4,'demo contains two independently addressable XY motion segments')
    require(any(row[3]=='_' for row in rows),'demo retains a dynamic legacy example')
    preservation={}
    if args.baseline:
        for prefix in ['Assets','Integrations','src/Core/Windows','src/Graphics']:
            old=args.baseline/prefix
            files=[p for p in old.rglob('*') if p.is_file()]
            require(all((ROOT/p.relative_to(args.baseline)).is_file() and p.read_bytes()==(ROOT/p.relative_to(args.baseline)).read_bytes() for p in files), 'unchanged existing '+prefix)
            preservation[prefix]=len(files)
    require(not any(p.suffix.lower() in ['.ttf','.ttc','.otf','.woff','.woff2'] for p in ROOT.rglob('*') if p.is_file()),'no system font binaries in source tree')
    report={'version':'v0.1.3 / 17.0','kind':'static contracts plus independent Python mathematics','csharpCompiled':False,'csharpTestsExecuted':False,'nativeUiExecuted':False,'checks':checks,'independentNumericCases':numeric_cases,'preservedBaseFiles':preservation}
    (ROOT/'docs/validation-image-objects-static.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    print(f'{len(checks)} static/mathematical checks passed; C# compilation and UI execution NOT tested.')

if __name__=='__main__': main()
