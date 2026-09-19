#!/usr/bin/env python3
"""Independent source-to-index and help asset validation. Does not compile/execute C#.

Every original table row is reconciled by source file + line, not just by counting
entries. The external Extra Gimmicks workbook is explicitly outside this coverage.
"""
from __future__ import annotations
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
CHECKS: list[str] = []


def check(condition: bool, label: str) -> None:
    if not condition: raise AssertionError(label)
    CHECKS.append(label)


def normalize(s: str) -> str:
    return re.sub(r'`+|\*+|~~|</?u>', '', re.sub(r'<br\s*/?>', '\n', s, flags=re.I)).replace('\\_', '_').strip()


def main() -> None:
    path = ROOT/'Assets/Documentation/vsm-manual.json'
    data = json.loads(path.read_text(encoding='utf-8'))
    entries = data['Entries']
    check(len(entries) == len({e['Id'] for e in entries}) == 292, '292 distinct indexed entries')
    check(data['Counts'] == dict(guide=3,article=15,syntax=16,mod=205,obj=29,mpf=12,config=10,source=2), 'entry types match the supplied corpus')
    indexed = {(e['Source'],e['StartLine']): e for e in entries if e['Kind'] in {'mod','obj','config'}}
    raw_count = 0
    for source in data['Sources']:
        raw = (ROOT/'Assets/Documentation'/source['Path']).read_bytes()
        lines = raw.decode('utf-8-sig').splitlines()
        check(hashlib.sha256(raw).hexdigest() == source['Sha256'], source['Name']+': source bytes match recorded hash')
        check(len(lines) == source['Lines'], source['Name']+': line count matches')
        original = next(e for e in entries if e['Kind']=='source' and e['Source']==source['Name'])
        check(original['Detail'] == [f'{i+1:03d}  {line}' for i,line in enumerate(lines)], source['Name']+': every original line preserved in full-text entry')
        header = None
        for number, line in enumerate(lines,1):
            if not line.strip().startswith('|'):
                header = None
                continue
            cells = [normalize(s) for s in line.strip().strip('|').split('|')]
            if cells[0] in {'可用mod','可用obj','配置名','gmk名','gimmick名'}:
                header = cells
                continue
            if all(re.fullmatch(r'[-:\s]+',s or '-') for s in cells): continue
            assert header is not None, f'unclassified table header {source["Name"]}:{number}'
            entry = indexed[(source['Name'],number)]
            assert entry['Key'] == cells[0].strip('"'), f'changed identifier {number}'
            for field, content in zip(header[1:],cells[1:]):
                expected = f'{field}（原文）：{content}' if content else f'{field}：原文此栏为空，未提供说明。'
                assert expected in entry['Detail'], f'lost table cell {source["Name"]}:{number}:{field}'
            raw_count += 1
        check(all(1 <= e['StartLine'] <= e['EndLine'] <= len(lines) for e in entries if e['Source']==source['Name']), source['Name']+': every reference range is valid')
    check(raw_count == len(indexed) == len(data['TableRows']) == 244, '244/244 table rows preserve every name and cell at the correct original line')
    by_key = {e['Key']:e for e in entries}
    check('负数为顺时针旋转' in '\n'.join(by_key['noterot']['Detail']), 'noterot direction is sourced, not replaced with old runtime wording')
    check(by_key['textsep_[tid]']['Short'] == '调整字幕[tid]的行间距', 'textsep remains line spacing as supplied')
    check('单位msx' in '\n'.join(by_key['imgytime_[图像名]']['Detail']), 'msx unit preserved')
    for name in ('ditortedBG_alp','BG_ditortScale','wigglr','filcker','col_convertion'):
        check(name in by_key, 'original spelling preserved: '+name)
    check(all(by_key[n]['Pattern'] for n in ('imgx_[图像名]','textX_[tid]b','xoffsetind[lane]','twx[id]')), 'dynamic parameter families have anchored patterns')
    families = sorted([e for e in entries if e['Pattern']], key=lambda e:-len(re.sub(r'\[[^\]]+\]','',e['Key'])))
    exact = {e['Key']:e for e in entries if e['Kind']=='mod' and not e['Pattern']}
    def lookup(name):
        return exact.get(name) or next((e for e in families if re.fullmatch(e['Pattern'],name)),None)
    for raw, expected in {'imgx_封面_01':'imgx_[图像名]','textX_foo_barb':'textX_[tid]b','textX_foo_bar':'textX_[tid]',
                          'boost_timeind6':'boost_timeind[lane]','twx4':'twx[id]'}.items():
        check(lookup(raw)['Key']==expected, 'family lookup/specificity: '+raw)
    for raw in ('imgx_','twx0','twx5','notealpind7','scrollind7'):
        check(lookup(raw) is None, 'out-of-range/unresolved family rejected: '+raw)
    check('缺失附件' in '\n'.join(by_key['原文差异与未提供内容']['Detail']), 'missing workbook is disclosed, not filled in')
    check('document-conflict' == by_key['hom']['Scope'], 'ambiguous hom target is not auto-selected')
    check(all('原文差异：配置默认值' in '\n'.join(e['Detail']) for e in entries if e['Kind']=='config'), 'all configuration entries display the source default conflict')
    check('图像初始宽度},{图像初始宽度}' in '\n'.join(by_key['static 图片声明']['Detail']), 'static width/width text preserved with note')
    check('单位ms' in '\n'.join(by_key['addVelo']['Detail']) and '默认为32' in '\n'.join(by_key['addVeloTween']['Detail']), 'VSV millisecond fields and optional step retained')
    required={chr(n) for n in range(32,127)}
    for source in data['Sources']:
        required.update(c for c in (ROOT/'Assets/Documentation'/source['Path']).read_text(encoding='utf-8-sig') if not c.isspace())
    required.update(c for c in json.dumps(data,ensure_ascii=False) if not c.isspace())
    for source_file in (ROOT/'src/UI/Editor').glob('*.cs'):
        required.update(c for c in source_file.read_text(encoding='utf-8') if not c.isspace())
    for stem in ('editor-help-sans','editor-help-sans-bold'):
        table=json.loads((ROOT/'Assets/Fonts'/f'{stem}.json').read_text(encoding='utf-8'))
        check(required <= table['Glyphs'].keys(), stem+': every source/documentation/UI character covered')
    project=ET.parse(ROOT/'KuroakiGimmick.csproj').getroot()
    check(any(e.attrib.get('LogicalName')=='KuroakiGimmick.EditorManual.json' for e in project.iter('EmbeddedResource')), 'manual embedded in executable; no stale external-data override')
    # Determinism check is in addition to, not a replacement for, independent row reconciliation.
    spec=importlib.util.spec_from_file_location('manual_builder',ROOT/'scripts/dev/build-vsm-manual.py')
    module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    check(module.build()==data, 'generator reproduces committed index without network or runtime inference')
    report=dict(scope='offline source/table/regex/glyph reconciliation; not C# compilation',compiled=False,
                nativeUIExecuted=False,macOSExecuted=False,windowsExecuted=False,
                entries=len(entries),sourceTableRows=raw_count,missingWorkbook='Extra Gimmicks(NO COMMENTS).xlsx',checks=CHECKS)
    (ROOT/'docs/validation-vsm-manual.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for label in CHECKS: print('PASS '+label)
    print(f'{len(CHECKS)} offline manual checks passed. C# and native UI were NOT executed.')


if __name__=='__main__': main()
