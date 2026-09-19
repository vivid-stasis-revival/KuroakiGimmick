#!/usr/bin/env python3
"""Independent offline source/index/atlas checks; NOT a C# compiler or SDL UI test."""
from __future__ import annotations
import hashlib
import json
import re
from pathlib import Path
from collections import Counter
from reference_sources import read_source
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[2]
DOC=ROOT/'Assets/Documentation'


def main() -> None:
    checks=[]
    def check(ok: bool, label: str) -> None:
        if not ok: raise AssertionError(label)
        checks.append(label)
        print('PASS',label)
    data=json.loads((DOC/'vsm-reference.json').read_text(encoding='utf-8'))
    entries=data['Entries']; by_id={e['Id']:e for e in entries}
    counts=dict(sorted(Counter(e['Kind'] for e in entries).items()))
    check(len(by_id)==len(entries),'unique reference entry IDs')
    check(counts==data['Counts'],'declared counts agree with independently counted entries')
    source_lines={}
    for s in data['Sources']:
        raw=read_source(DOC,s['File'],s['Sha256'])
        check(hashlib.sha256(raw).hexdigest()==s['Sha256'],'source SHA-256: '+s['File'])
        text=raw.decode('utf-8-sig'); lines=text.splitlines(); source_lines[s['File']]=lines
        check(text==s['Text'] and len(lines)==s['LineCount'],'complete embedded source: '+s['File'])
        # Independent table-row detection: the only non-data pipe rows are the
        # header immediately preceding an alignment row, and the alignment itself.
        table_lines=[]
        def separator(line: str) -> bool:
            return bool(line.strip().startswith('|')) and set(line.replace('|','').replace(':','').replace('-','').strip())==set()
        for i,line in enumerate(lines):
            if not line.lstrip().startswith('|') or separator(line): continue
            if i+1<len(lines) and separator(lines[i+1]): continue
            table_lines.append(i+1)
        indexed=[e['StartLine'] for e in entries if e['Source']==s['File'] and e['Cells']]
        check(sorted(table_lines)==sorted(indexed),'all source table rows indexed exactly once: '+s['File'])
        check(not any(e['Kind']=='source' for e in entries),'no full-source pages in navigation')
        chapter_lines=set()
        for e in entries:
            if e['Source']==s['File'] and e['Kind']=='section': chapter_lines.update(range(e['StartLine'],e['EndLine']+1))
        nonblank={i+1 for i,line in enumerate(lines) if line.strip()}
        check(nonblank<=chapter_lines,'every nonblank source line also belongs to a readable chapter: '+s['File'])

    for e in entries:
        if e['Source']:
            lines=source_lines[e['Source']]
            check(1<=e['StartLine']<=e['EndLine']<=len(lines),'valid source span: '+e['Id'])
            if e['Cells']:
                raw=[v.strip() for v in lines[e['StartLine']-1].strip().strip('|').split('|')]
                check(raw==e['Cells'],'source table cells preserved: '+e['Id'])
        if e['ParentId']: check(e['ParentId'] in by_id,'parent exists: '+e['Id'])

    def match(name: str) -> list[dict]:
        exact=[e for e in entries if e['Kind']=='mod' and not e['MatchPattern'] and e['Name']==name]
        if exact:return exact
        output=[]
        for e in entries:
            if e['Kind']!='mod' or not e['MatchPattern']:continue
            # This exercises generated regular-expression contracts, not .NET Regex execution.
            pattern=re.sub(r'\(\?<([A-Za-z]+)>',r'(?P<\1>',e['MatchPattern']).replace(r'\z',r'\Z')
            if re.fullmatch(pattern,name,re.I):output.append(e)
        return output
    for name in ['imgx_bg_front','imgytime_背景','textscale_intro_2','textX_任意字符串','notealpind6','twa4']:
        check(len(match(name))==1,'document template match: '+name)
    for name in ['imgx_','notealpind7','notealpind-1','twa0','twa5','distortedBG_alp','fx_colorise','other_unlisted_mod']:
        check(not match(name),'no fabricated document match: '+name)
    check(len(match('textX_nameb'))==2,'both candidates retained for trailing-b text ambiguity')
    check('行间距' in match('textsep_intro')[0]['Summary'],'textsep summary follows the supplied document')
    check('负数为顺时针' in ''.join(match('noterot')[0]['Body']),'source rotation sign retained')
    check(any('默认值为1' in s for s in match('scrollspeed')[0]['Body']),'source scrollspeed default is 1')
    check('图像初始宽度},{图像初始宽度}' in '\n'.join(source_lines['Custom Gimmick说明.md']),'static declaration source not corrected')
    check(all(name in {e['Name'] for e in entries if e['Kind']=='mpf'} for name in ['wigglr','filcker']),'mpf spellings retained')
    check(not any(e['Kind'] in {'guide','note','source'} or e['Notes'] for e in entries), 'automatic editor notes and raw-source pages removed')
    check(all(e['Blocks'] for e in entries), 'every remaining entry has formatted presentation blocks')
    for e in entries:
        for b in e['Blocks']:
            check(b['Kind'] in {'heading','paragraph','callout','table','code','list-item'}, 'known block kind: '+e['Id'])
            if b['Kind']=='table':
                check(all(len(row)==len(b['Columns']) for row in b['Rows']), 'table columns align: '+e['Id'])
                check(len(b['Rows'])==len(b['RowLinks']) and all(not link or link in by_id for link in b['RowLinks']), 'table row links resolve: '+e['Id'])
    visible=str([{k:e[k] for k in ('Name','Summary','Body','Context','Blocks')} for e in entries])
    check(not any(label in visible for label in ('【编辑器说明】','【原文】','【匹配提示】')), 'no injected editorial wrappers in visible entries')
    check('特殊参数自身的单位仍以该条目说明为准' not in visible and '时间单位均为节拍。特殊参数' not in visible, 'no repeated synthesized unit boilerplate in parameter pages')
    check('下述所有配置都会被认为是true' in '\n'.join(source_lines['Custom Gimmick说明.md']) and 'false' in str([e['Cells'] for e in entries if e['Kind']=='config']), 'source default ambiguity remains without editorial synthesis')
    check('Extra Gimmicks(NO COMMENTS).xlsx' in str([e['Blocks'] for e in entries if e['Kind']=='section']), 'missing attachment reference stays a source limitation, not invented parameters')
    # Check decoded values, including raw Markdown, rather than the JSON encoding itself.
    def strings(value):
        if isinstance(value,str):yield value
        elif isinstance(value,list):
            for x in value:yield from strings(x)
        elif isinstance(value,dict):
            for x in value.values():yield from strings(x)
    needed={c for s in strings(data) for c in s if not c.isspace()}
    for stem in ('editor-help-sans','editor-help-sans-bold'):
        metrics=json.loads((ROOT/'Assets/Fonts'/f'{stem}.json').read_text(encoding='utf-8'))
        check(needed<=set(metrics['Glyphs']),stem+': all reference characters covered')
        check(metrics['Family']=='Noto Sans CJK SC' and metrics['EmSize']==64,stem+': preview.7 typography retained')
    project=ET.parse(ROOT/'KuroakiGimmick.csproj').getroot()
    check(any(e.attrib.get('LogicalName')=='KuroakiGimmick.VsmReference.json' for e in project.iter('EmbeddedResource')),'reference index embedded in assembly')
    preview=(ROOT/'src/UI/Editor/EditorTrackHelp.cs').read_text()
    check(preview.index('EditorReferenceHelp.TryGet')<preview.index('var legacy = Legacy'),'document explanations take precedence over legacy help')
    model=(ROOT/'src/Core/Documentation/VsmReference.cs').read_text()
    check(not any(s in model for s in ('ModCatalog.Register','VsmDocument.Replace','Timeline.Get','File.WriteAllText','File.WriteAllBytes')),'reference model is read-only; no evaluator registration or document writing')
    inputs=(ROOT/'src/UI/Editor/Viewer.Editor.Input.cs').read_text()
    check(inputs.index('if (ReferenceVisible) return HandleReferenceInput(e);')<inputs.index('if (e.Type == 0x301)'),'reference input captured before editor shortcuts')
    view=(ROOT/'src/UI/Viewer.Drawing.cs').read_text()
    check('if (ReferenceVisible)' in view and 'DrawReference(w, h)' in view and 'Canvas.Unfaded()' in view,'docs drawn as presentation overlay on either workspace')
    reader=(ROOT/'src/UI/Editor/Viewer.Editor.Reference.Drawing.cs').read_text()
    check(all(name in reader for name in ('DrawReferenceNavigation','DrawReferenceArticle','DrawReferenceOutline','DrawReferencePageFooter')), 'section navigation, measured article, outline and page navigation are connected')
    check('SourceExcerpt(' not in reader and 'SourceExcerpt(' not in (ROOT/'src/UI/Editor/Viewer.Editor.Reference.cs').read_text(), 'F1 reader does not display or copy full raw source')
    check('e.Scan == 58' in inputs,'F1 reference shortcut wired')
    report=dict(scope='offline source/index/regex-contract/font checks; no C# or GPU execution',
        compiled=False,cpuSelfTestsExecuted=False,gpuExecuted=False,counts=counts,
        sourceHashes={s['File']:s['Sha256'] for s in data['Sources']},checkCount=len(checks),checks=checks)
    (ROOT/'docs/validation-vsm-reference.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print('PASS',len(checks),'offline checks.',counts)

if __name__=='__main__': main()
