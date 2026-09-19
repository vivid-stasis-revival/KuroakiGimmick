#!/usr/bin/env python3
"""Build the read-only reference index from the two supplied Markdown documents.

This is an optional maintainer tool. Normal builds embed the generated JSON and do
not need Python. Original files are never rewritten. Source spelling, table cells,
ordering and contradictions are retained; editorial notes are separate fields.
"""
from __future__ import annotations
import argparse
import hashlib
import html
import json
import re
from pathlib import Path
from reference_sources import read_source

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'Assets/Documentation/vsm-reference.json'
INPUTS = ('vsm格式说明.md', 'Custom Gimmick说明.md')


def plain(text: str) -> str:
    text = re.sub(r'<br\s*/?>', '\n', text, flags=re.I)
    text = re.sub(r'<a\s+href="([^"]+)"[^>]*>(.*?)</a>', r'\2 (\1)', text, flags=re.S | re.I)
    text = re.sub(r'</?(?:u|b|i|strong|em)>', '', text, flags=re.I)
    text = text.replace('\\_', '_').replace('\\|', '|')
    text = re.sub(r'`+', '', text)
    text = text.replace('**', '').replace('~~', '')
    # Only strip paired emphasis; identifiers containing underscores are untouched.
    text = re.sub(r'(?<!\*)\*([^*\n]+)\*(?!\*)', r'\1', text)
    return html.unescape(text).strip()


def cells(line: str) -> list[str]:
    return [s.strip() for s in re.split(r'(?<!\\)\|', line.strip().strip('|'))]


def is_separator(row: list[str]) -> bool:
    return all(re.fullmatch(r':?-{2,}:?', s.replace(' ', '')) for s in row)


def paragraphs(lines: list[str]) -> list[str]:
    """Readable layout, including each non-empty table cell, without inventing values."""
    output: list[str] = []
    headers: list[str] | None = None
    fenced = False
    for i, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith('```'):
            # The supplied subtitle sample uses six backticks; preserve its inner line too.
            fenced = not fenced
            continue
        if stripped.startswith('|') and not fenced:
            row = cells(stripped)
            if is_separator(row):
                continue
            if i+1 < len(lines) and lines[i+1].strip().startswith('|') and is_separator(cells(lines[i+1])):
                headers = [plain(c) for c in row]
                continue
            if headers:
                label = plain(row[0]).strip('"')
                output.append(label)
                for head, value in zip(headers[1:], row[1:]):
                    if plain(value):
                        output.append(f'{head}：{plain(value)}')
                continue
        else:
            headers = None
        if not stripped or re.fullmatch(r'<br\s*/?>', stripped, re.I):
            continue
        if not fenced:
            stripped = re.sub(r'^#{1,6}\s+', '', stripped)
            stripped = re.sub(r'^>\s?', '', stripped)
        value = plain(stripped)
        if value:
            output.append(value)
    return output


CONFIG_NOTE = ('原文存在默认值差异：段落说“没有配置文件”及“不填”默认为 true，'
               '但表格将部分开关列为 false，并将 JACKET_MANAGE_MODE 列为 plaudite。'
               '两处均保留；本帮助不替原文确定优先解释，也不修改运行时配置逻辑。')
SCOPE_CUSTOM = '文档约定：未标注全局 gimmick 时，需要 !obj:obj_custom_gimmick。'
EXTRA_NOTE = ('原文引用 Extra Gimmicks(NO COMMENTS).xlsx 列出各 obj 独有 gimmick，'
              '但此次只提供了这两份 Markdown。未提供的表格内容没有补写。')


def generate_source_index() -> dict:
    sources, entries = [], []
    documents: dict[str, list[str]] = {}
    texts: dict[str, str] = {}
    for name in INPUTS:
        raw = read_source(ROOT / 'Assets/Documentation', name)
        text = raw.decode('utf-8-sig')
        lines = text.splitlines()
        documents[name] = lines
        texts[name] = text
        sources.append(dict(File=name, Sha256=hashlib.sha256(raw).hexdigest(),
                            LineCount=len(lines), Text=text))

    def add(id: str, kind: str, name: str, category: str, source: str = '', start: int = 0,
            end: int = 0, body: list[str] | None = None, summary: str = '',
            notes: list[str] | None = None, context: list[str] | None = None,
            parent: str = '', pattern: str = '', scope: str = '',
            raw_cells: list[str] | None = None) -> dict:
        e = dict(Id=id, Kind=kind, Name=name, Category=category, Source=source,
                 StartLine=start, EndLine=end, Summary=summary, Body=body or [],
                 Context=context or [], Notes=notes or [], ParentId=parent,
                 MatchPattern=pattern, Scope=scope, Cells=raw_cells or [])
        entries.append(e)
        return e

    add('reference.overview', 'guide', '手册首页 / 范围与使用', '使用说明', body=[
        '这里收录用户提供的《vsm格式说明.md》和《Custom Gimmick说明.md》。不是在线百科，也不是所有游戏版本的能力承诺。',
        '原始轨道名保持不变。悬停显示条目简述，按住 W 显示详细说明；F1 可以定位到手册中的对应条目。',
        '手册支持按原始名称、中文说明和多关键词搜索。各章节、表格条目及两份原始 Markdown 都可以离线查看。',
        '“说明”与“原文”切换只影响查看方式；“复制原文”复制选中条目对应的源行，不会复制编辑器补充说明。',
        '文档内容与编辑器提示分开显示。文档未填写的单元格不自动补默认值，不自动登记未验证的别名。',
        '收录说明不等于已经实现预览或写回。本次不修改事件解析、渲染、保存和窗口运动逻辑。',
        EXTRA_NOTE,
    ], summary='两份用户文档的离线索引；可搜索、查来源和复制原文。')

    # The entire source is also available, so metadata, malformed Markdown, examples,
    # unusual names and even blank table cells are never lost to the readable view.
    for name in INPUTS:
        add('source.'+name, 'source', name, '原始文档', name, 1, len(documents[name]),
            paragraphs(documents[name]), '完整原文；可切换源 Markdown 或复制全文。')

    vsm = INPUTS[0]
    specs = [
        ('vsm.prepare','准备 / 文件位置',1,4),
        ('vsm.format','文件格式 / 七字段与头部',5,27),
        ('vsm.mpf','mpf / 时间区间与函数',28,34),
        ('vsm.metadata','! 元信息',35,36),
        ('vsm.examples','语句示例',37,46),
        ('vsm.easing','可用缓动 / 原文规则',47,50),
        ('vsm.base','基础 gimmick',51,90),
        ('vsm.proxy','轨道控制 gimmick',91,137),
        ('vsm.objects','可用 obj 与 mpf',138,len(documents[vsm])),
    ]
    sections: dict[str, list[dict]] = {vsm: []}
    for id, name, start, end in specs:
        notes = [EXTRA_NOTE] if id == 'vsm.objects' else []
        if id == 'vsm.easing':
            notes = ['原文说明 easings.net 上的缓动均支持，但只明确举出 linear、inSine、outSine、inOutSine 的写法。本手册不据此凭空补一张外部缓动枚举表。']
        if id == 'vsm.base':
            notes = ['本节总注记说 proxy 必须为 -1，但 hom 行另写“proxies不能为-1”；两处均按原文保留。']
        e = add(id,'section',name,'VSM / '+name,vsm,start,end,
                paragraphs(documents[vsm][start-1:end]), '查看这一节的完整说明与条目。', notes)
        sections[vsm].append(e)

    cg = INPUTS[1]
    starts = [(i, s[3:].strip()) for i,s in enumerate(documents[cg],1) if s.startswith('## ')]
    sections[cg] = []
    for n,(start,name) in enumerate(starts):
        end = starts[n+1][0]-1 if n+1<len(starts) else len(documents[cg])
        if n == 0: start = 1
        notes = []
        if name == 'cgmk配置文件': notes.append(CONFIG_NOTE)
        if name == '图片插入':
            notes.append('静态图片声明的原文把两个可选尺寸都写成“图像初始宽度”，本手册保留原式；没有擅自把第二个名称改成高度。')
        if name == '背景':
            notes.append('原文写“范围0~11，从0到10分别对应”，表中再说明11是默认背景；命名 jacket[1.png 按原文保留，没有补右方括号。')
        id='custom.section.'+str(n)
        sections[cg].append(add(id,'section',name,'Custom / '+name,cg,start,end,
                                   paragraphs(documents[cg][start-1:end]), '查看这一节的完整说明与条目。', notes))

    def section_at(source: str, line: int) -> dict:
        return next(s for s in sections[source] if s['StartLine'] <= line <= s['EndLine'])

    tables = []
    for source in INPUTS:
        lines = documents[source]
        i = 0
        while i < len(lines)-1:
            if not (lines[i].strip().startswith('|') and lines[i+1].strip().startswith('|') and is_separator(cells(lines[i+1]))):
                i += 1
                continue
            header = [plain(c) for c in cells(lines[i])]
            j = i+2
            while j<len(lines) and lines[j].strip().startswith('|'):
                row = cells(lines[j]); line = j+1
                sec = section_at(source,line)
                raw_name=plain(row[0]).strip('"')
                kind='config' if header[0]=='配置名' else 'object' if header[0]=='可用obj' else 'mod'
                body=[f'{head}：{plain(value)}' for head,value in zip(header[1:],row[1:]) if plain(value)]
                blank=[h for h,v in zip(header[1:],row[1:]) if not plain(v)]
                notes = ([f'原文的“{h}”单元格未填写；未补写。' for h in blank])
                context=[]; scope=''
                if source==vsm and kind=='mod':
                    if sec['Id']=='vsm.base':
                        context.append('本节原文注：ssf 指 quaver 里 scroll speed factor 的效果；使用这些 gimmick 时 proxy 必须为 -1。')
                        scope='global'
                        if raw_name=='hom':
                            notes.append('本条另写“使用时proxies不能为-1”，与本节总注记不同。这里保留两处文字，不替它们选择解释。')
                            scope='conflict'
                    else:
                        context.append('本节原文注：横轴为 x 轴，纵轴为 y 轴，竖轴为 z 轴；轨道默认在（0，0）；proxy 不可为 -1。')
                        scope='proxy'
                elif kind=='mod':
                    context.append(SCOPE_CUSTOM)
                    scope='global' if '全局gimmick' in ''.join(row).replace(' ','') else 'custom'
                    if 61<=line<=85:
                        context += paragraphs(lines[48:55])
                        if line>=74:
                            context += paragraphs(lines[67:70])+paragraphs(lines[86:87])
                    if 182<=line<=194:
                        context += paragraphs(lines[116:118])+paragraphs(lines[172:178])+paragraphs(lines[194:197])
                    if 110<=line<=115: context += paragraphs(lines[89:93])
                    if 275<=line<=281: context += paragraphs(lines[271:272])
                    if 212<=line<=220: context += ['本组原文说明：a 指振幅，o 指周期的倒数，p 指相位；sino 行写“默认为0（0代表禁用），下同”，sinp 行写“p默认为1，下同”。']
                    if 223<=line<=226: context += ['本组原文说明：x、y 指扭曲中心位置，a 指扭曲程度，r 指扭曲半径；[id] 范围1~4。']
                    if raw_name in ('textcolhex','set_slash_col'):
                        context += paragraphs(lines[288:289])
                    if raw_name in ('fx_red','recolor','fx_red_intensity'):
                        notes += ['fx_colorise 在原文中用作效果比较名，没有独立指令定义；没有为它新增别名或执行支持。']
                elif kind=='config':
                    context += paragraphs(lines[14:17])+paragraphs(lines[36:37])
                    notes.append(CONFIG_NOTE)
                elif kind=='object':
                    context += paragraphs(documents[vsm][19:20])
                    notes.append('这里只收录原文列出的 obj / mpf 关系，未据名称推断函数行为或独有效果。')
                name=raw_name
                regex=''
                if '[lane]' in name: regex=r'\A'+re.escape(name).replace(re.escape('[lane]'),r'(?<lane>[0-6])')+r'\z'
                elif '[id]' in name: regex=r'\A'+re.escape(name).replace(re.escape('[id]'),r'(?<id>[1-4])')+r'\z'
                elif '[tid]' in name: regex=r'\A'+re.escape(name).replace(re.escape('[tid]'),r'(?<tid>.+)')+r'\z'
                elif '[图像名]' in name: regex=r'\A'+re.escape(name).replace(re.escape('[图像名]'),r'(?<image>.+)')+r'\z'
                summary=plain(row[1]) if len(row)>1 and plain(row[1]) else (plain(row[2]) if len(row)>2 and plain(row[2]) else '原文列出名称，但没有填写作用说明。')
                entry=add(f'{"vsm" if source==vsm else "custom"}.row.{line}',kind,name,sec['Category'],source,line,line,
                          body,summary,notes,context,sec['Id'],regex,scope,row)
                tables.append(entry)
                j+=1
            i=j

    # Syntax index points at actual source paragraphs, not synthesized sample statements.
    syntax=[
        ('!proxies',vsm,19,19,'轨道数量'),('!obj',vsm,20,20,'特效库 / 一个谱面只能使用一个 obj'),
        ('beat:endbeat:step',vsm,21,21,'重复时间范围'),('duration',vsm,22,22,'持续时间'),
        ('easing',vsm,23,23,'缓动字段'),('value1 / value2 / _ / 573613',vsm,24,24,'开始值、结束值与不使用值'),
        ('modname',vsm,25,25,'gimmick 名'),('proxy',vsm,26,26,'轨道编号'),
        ('mpf',vsm,28,33,'函数启用区间'),('!{name}:{content}',vsm,35,35,'其它感叹号元信息'),
        ('linear / inSine / outSine / inOutSine',vsm,49,49,'缓动拼写示例'),
        ('[arg] / {,{step}}',cg,5,8,'必填与可选约定'),
        ('[难度]_cgmk_config.json / cgmk_config.json',cg,10,45,'配置文件与优先级'),
        ('[难度]_text.txt',cg,49,55,'单文件字幕'),
        ('[难度]_text_[编号].txt / tid',cg,68,87,'多文件字幕与任意字符串 tid'),
        ('jacket[id / custom_jacket',cg,89,115,'背景文件和切换模式'),
        ('[难度名].vsp / GLOBAL.vsp',cg,117,118,'VSP 文件优先级'),
        ('#Layer / #Image',cg,120,154,'图层与图片声明'),
        ('static',cg,155,164,'静态图片声明'),('animated',cg,165,177,'动态图声明与资源规则'),
        ('msx',cg,195,197,'流速距离单位和图片中心'),
        ('[lane]',cg,271,272,'4K 与 Bumper 的 lane 编号'),
        ('[难度].vsv',cg,315,318,'传统不等距 SV 文件'),
        ('addVelo(time,velo)',cg,322,328,'指定毫秒时刻添加速度'),
        ('addVeloTween(time,timeEnd,stVelo,edVelo{,easing,step})',cg,331,341,'速度补间 / 默认 linear 与 step=32'),
    ]
    for n,(name,source,start,end,summary) in enumerate(syntax):
        sec=section_at(source,start)
        notes=[]
        if name=='static': notes=['原文的两个可选尺寸都写成“图像初始宽度”；照录，未自行更正。']
        if name.startswith('[难度]_cgmk'): notes=[CONFIG_NOTE]
        add('syntax.'+str(n),'syntax',name,'语法 / '+sec['Name'],source,start,end,
            paragraphs(documents[source][start-1:end]),summary,notes,parent=sec['Id'])

    functions: dict[str, list[dict]] = {}
    for e in tables:
        if e['Kind']=='object':
            for func in plain(e['Cells'][2]).splitlines():
                func=func.strip()
                if func and func!='无': functions.setdefault(func,[]).append(e)
    for n,(func,objects) in enumerate(functions.items()):
        first=objects[0]
        body=['原文列出可用 mpf 的 obj：'] + [f'{o["Name"]}（{o["Source"]}:{o["StartLine"]}）' for o in objects]
        body += paragraphs(documents[vsm][27:33])
        add('mpf.'+str(n),'mpf',func,'VSM / mpf 函数',vsm,min(o['StartLine'] for o in objects),max(o['EndLine'] for o in objects),body,
            '原文列出的函数名及可用 obj。', ['原文没有独立解释本函数的完整算法；保留原拼写，不自动更正为相似函数名。'], parent='vsm.objects')

    for id,title,source,start,end,body in [
        ('notes.scope','收录范围 / 未提供的独有效果表',vsm,173,173,[EXTRA_NOTE]),
        ('notes.config','配置默认值 / 原文差异',cg,15,37,[CONFIG_NOTE]),
        ('notes.hom','hom 的 proxy 限制 / 原文差异',vsm,51,86,['总注记要求 proxy 为 -1，hom 行却要求 proxies 不能为 -1。原文两处都展示，不借说明导入修改执行逻辑。']),
        ('notes.static','static 尺寸名称 / 原文重复',cg,159,168,['静态声明连续两次写“图像初始宽度”；动态声明写宽度、高度。原样保留，未替用户推断哪个名称应当改动。']),
        ('notes.spelling','原始名称 / 不添加猜测别名',vsm,162,162,['obj_ram_gimmick 的 mpf 原文为 quake、wigglr、filcker。',
         'Custom 文档中的 ditortedBG_alp、ditortedBG_col_rgb、BG_ditortScale、BG_ditortAmount、col_convertion 等名称也按原文保留。',
         '这些是来源中的拼写，不代表本次为它们新增或修改了运行时支持。']),
    ]:
        add(id,'note',title,'文档差异与范围',source,start,end,body,'编者注：原文未统一或未提供的部分。')

    ids=[e['Id'] for e in entries]
    assert len(ids)==len(set(ids))
    for e in entries:
        if e['Source']:
            assert 1<=e['StartLine']<=e['EndLine']<=len(documents[e['Source']]),e
    return dict(SchemaVersion=1, Revision='editor-preview.8', Sources=sources, Entries=entries,
                Counts={k:sum(e['Kind']==k for e in entries) for k in sorted({e['Kind'] for e in entries})})


def generate() -> dict:
    import runpy
    presentation = runpy.run_path(str(ROOT / 'scripts/dev/reference-pages.py'))
    return presentation['present'](generate_source_index(), plain, cells, is_separator)


def main() -> None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check',action='store_true',help='Fail if the committed index differs from its sources.')
    args=parser.parse_args()
    data=generate()
    text=json.dumps(data,ensure_ascii=False,indent=2)+'\n'
    if args.check:
        if not DEST.is_file() or DEST.read_text(encoding='utf-8')!=text:
            raise SystemExit('Reference index is stale. Run scripts/dev/build-vsm-reference.py.')
    else: DEST.write_text(text,encoding='utf-8')
    print(json.dumps(data['Counts'],ensure_ascii=False))
    print('Source documents retained byte-for-byte; runtime semantics were not generated.')

if __name__=='__main__': main()
