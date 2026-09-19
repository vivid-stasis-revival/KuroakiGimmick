#!/usr/bin/env python3
"""Build the offline editor manual ONLY from the two supplied Markdown documents.

No network, language-model summaries, runtime-default inference or spelling corrections.
Table fields retain empty cells and source line references. Original files stay byte-exact.
Run this before build-editor-help-atlas.py when maintaining the documentation.
"""
from __future__ import annotations
import hashlib
import html
import json
import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOC_DIR = ROOT / 'Assets/Documentation'
SOURCES = ['vsm格式说明.md', 'Custom Gimmick说明.md']


def plain(text: str) -> str:
    """Only remove Markdown presentation, never rewrite identifiers or descriptions."""
    text = re.sub(r'<br\s*/?>', '\n', text, flags=re.I)
    text = re.sub(r'<a\s+href="([^"]+)"[^>]*>(.*?)</a>', r'\2（\1）', text)
    text = re.sub(r'</?(?:u|em|strong)>', '', text)
    text = text.replace('\\_', '_').replace('**', '').replace('~~', '')
    text = re.sub(r'`+', '', text)
    # Italic stars are markup in the supplied text. Underscores are VSM tokens.
    text = text.replace('*', '')
    return html.unescape(text).strip()


def paragraphs(lines: list[str]) -> list[str]:
    out: list[str] = []
    for raw in lines:
        if raw.strip().startswith('|'):  # table cells have their own indexed entries
            continue
        if re.fullmatch(r'\s*`{3,}\w*\s*', raw):
            continue
        value = plain(re.sub(r'^#{1,6}\s+', '', raw))
        if value:
            out.extend(s.strip() for s in value.split('\n') if s.strip())
    return out


def build() -> dict:
    entries: list[dict] = []
    source_meta = []
    table_rows = []
    source_lines: dict[str, list[str]] = {}
    category_order = ['使用说明', 'VSM / 文件语法', 'VSM / 基础 gimmick', 'VSM / 轨道控制',
                      'VSM / obj 与 mpf']

    def add(key, category, kind, short, detail, source='', start=0, end=0, scope='', pattern=''):
        item = dict(Id=f'{source}:{start}:{kind}:{key}', Key=key, Category=category, Kind=kind,
                    Short=short or '文档该栏未提供作用说明。', Detail=detail, Source=source,
                    StartLine=start, EndLine=end or start, Scope=scope, Pattern=pattern)
        if any(e['Id'] == item['Id'] for e in entries):
            raise ValueError('Duplicate entry: ' + item['Id'])
        entries.append(item)
        if category not in category_order:
            category_order.append(category)
        return item

    # These are editorial provenance/limitations, explicitly not additional game semantics.
    add('手册范围与使用', '使用说明', 'guide', '两份附件的完整离线索引；收录不等于运行时支持。', [
        '【编辑器说明】轨道名称保持源文件原样。悬停显示原文作用，按住 W 阅读说明与来源；F1 或 DOCS 打开本手册。',
        '【编辑器说明】可搜索原始名字、中文作用、配置名、obj、mpf 和章节；滚轮分别滚动目录与正文。',
        '【来源范围】本手册仅依据 vsm格式说明.md 与 Custom Gimmick说明.md。原文文件逐字节保留在 Assets/Documentation/Sources。',
        '【支持边界】收录一个名称不代表预览器已实现它。此更新不修改解析器、运行时默认值、绘制规则或游戏导出能力，实际限制请查看兼容报告。',
        '【缺失附件】VSM 文档末尾引用 Extra Gimmicks(NO COMMENTS).xlsx，本次未提供该文件；各 obj 的独有 gimmick 明细未补编。',
        '【原文空白】作用、默认值、单位、函数细节未写出的地方保留为空白说明，不用旧版推测或运行时默认值替代。',
        '【原文拼写】ditortedBG、BG_ditort、wigglr、filcker 等保持原文；不自动纠正，也不写回别名。',
        '【时间提示】VSM 事件字段使用节拍；VSV 语句 time/timeEnd 使用 ms。具体 gimmick 参数如 ms、msx 等按各条目原文说明阅读。'
    ])
    add('编辑器快捷键', '使用说明', 'guide', '沿用编辑器操作说明，不属于 VSM 语法。', [
        '【编辑器操作】点击 note 定位其准确时间；左侧 + GIMMICK CLIP 或窗口按钮添加事件。',
        '片段主体拖动时间，右侧手柄拖动持续长度；双击空轨新增片段。',
        '滚轮滚动轨道；Shift + 滚轮平移；Ctrl/Cmd + 滚轮缩放；Alt 临时关闭吸附。',
        '悬停原始轨道名显示简述；按住 W 展开说明；W + 滚轮翻阅；F1 打开当前名称的手册。',
        'Ctrl/Cmd + Z 撤销；Ctrl/Cmd + Shift + Z 重做；Delete 删除；D 复制。',
        'Ctrl/Cmd + S 保存副本；Ctrl/Cmd + Shift + S 另存为；Tab 切换 Viewer/Editor。',
        'Space 播放；A/B 设置循环边界；L 切换循环；Esc 退出实时窗口预览。',
        '数值框支持 1/3 与 128+1/4；窗口事件配置的秒单位与 VSM 节拍字段不同。',
        '手册打开时暂停播放，快捷键与滚轮不会穿透到下面的时间轴；Esc 关闭搜索框或手册。',
        '选用名字只更新左侧待添加名称；模板需要填写具体名字，不自动创建事件，不更改 !obj。'
    ])
    issues = [
        '【原文差异：配置默认值】Custom 文档 L17 与 L37 写无配置/未填写时为 true；L24 的 JACKET_MANAGE_MODE 为 plaudite，L31～33 的三个开关表格为 false。两种说法同时保留，未擅自选择一套运行规则。',
        '【原文差异：hom】VSM L52 要求基础 gimmick 的 proxy 为 -1；L86 的 hom 单独写“使用时proxies不能为-1”。保留该例外及原字段拼写，文档未进一步解释。',
        '【原文格式：静态图片】Custom L162 的两个可选尺寸都写“图像初始宽度”；L168 的动态图写宽度/高度。本手册不把静态声明的第二个宽度偷偷改为高度。',
        '【文档范围】基础缓动仅给出 linear 与 Sine 命名示例，并引用 easings.net；没有在附件外抓取补充列表。',
        '【缺失附件】Extra Gimmicks(NO COMMENTS).xlsx 未随本次附件提供，不包含其未知内容。'
    ]
    add('原文差异与未提供内容', '使用说明', 'guide', '默认值冲突、hom 例外、重复尺寸字段及缺失表格。', issues)

    object_records = []
    for source_index, name in enumerate(SOURCES):
        data = (DOC_DIR/'Sources'/name).read_bytes()
        lines = data.decode('utf-8-sig').splitlines()
        source_lines[name] = lines
        source_meta.append(dict(Name=name, Path='Sources/'+name, Sha256=hashlib.sha256(data).hexdigest(), Lines=len(lines)))
        # Full original, including all tables, code, punctuation and otherwise unclassified prose.
        add(name+' / 完整原文', '原文全文', 'source', '保留原始行号的完整 Markdown；索引不替代原文。',
            [f'{i+1:03d}  {s}' for i, s in enumerate(lines)], name, 1, len(lines))
        category = 'VSM / 文件语法' if source_index == 0 else 'Custom / 文档约定'
        header: list[str] = []
        for i, raw in enumerate(lines, 1):
            stripped=raw.strip()
            if source_index == 0:
                if '以下是基础gimmick' in stripped: category = 'VSM / 基础 gimmick'
                elif '这些是轨道控制gimmick' in stripped: category = 'VSM / 轨道控制'
                elif '以下是可用obj' in stripped: category = 'VSM / obj 与 mpf'
            elif stripped.startswith('## '):
                category = 'Custom / '+plain(stripped[3:])
                if category not in category_order: category_order.append(category)
            if not stripped.startswith('|'):
                header=[]
                continue
            cells=[plain(c) for c in stripped.strip('|').split('|')]
            if all(re.fullmatch(r'[:\-\s]+', c or '-') for c in cells): continue
            if cells[0] in {'可用mod','可用obj','配置名','gmk名','gimmick名'}:
                header=cells
                continue
            if not header: raise ValueError(f'{name}:{i}: table without header')
            if len(cells)!=len(header): raise ValueError(f'{name}:{i}: inconsistent table width')
            key=cells[0].strip('"')
            kind='obj' if header[0]=='可用obj' else 'config' if header[0]=='配置名' else 'mod'
            detail=[]
            for label, value in zip(header[1:], cells[1:]):
                detail.append(f'{label}（原文）：{value}' if value else f'{label}：原文此栏为空，未提供说明。')
            scope=''
            if source_index==0 and kind=='mod':
                scope='global' if category=='VSM / 基础 gimmick' else 'proxy'
                detail.append('适用范围（原文）：'+ ('使用这些gimmick时proxy必须为-1。' if scope=='global' else
                            '横轴为x轴，纵轴为y轴，竖轴为z轴；轨道默认在（0，0）；proxy不可为-1。'))
                if key=='hom':
                    detail.append(issues[1]); scope='document-conflict'
            elif source_index==1 and kind=='mod':
                scope='global' if '全局gimmick' in cells[-1] else 'custom'
                detail.append('适用范围（原文）：'+ ('是全局gimmick。' if scope=='global' else
                    '在没标注全局gimmick的情况下，本文档提到的gimmick得在obj为obj_custom_gimmick时才能使用。'))
                if '[tid]' in key:
                    detail.extend(['文件规则（原文）：[难度]_text_[编号].txt，内部格式同单文件字幕。',
                                   '版本（原文）：多文件字幕从v1.5及以后可用；从1.10.0开始，tid可以是任意字符串，之前只能是0~49的数字。'])
                    if key.startswith(('textX_', 'textY_')):
                        detail.append('【原文边界】tid 可含任意字符串，同时存在 [tid]b 形式；仅凭名字末尾 b 无法完全消除对象编号歧义。此帮助按更具体的带 b 模板展示，不改变目标绑定。')
                if '[lane]' in key:
                    detail.append('lane（原文）：0~3为4k的1~4轨，456为左中右bumper。')
                if key.startswith('tw') and '[id]' in key:
                    detail.append('同表组说明（原文，twx[id] 行）：x，y指扭曲中心位置，a指扭曲程度，r指扭曲半径；[id]范围1~4，下同。')
                if '[图像名]' in key:
                    detail.extend(['图片声明：参见“图片插入”章节的 VSP 格式。',
                                   '单位与中心（原文）：1 msx指图片所属lane的流速下1ms移动的距离；变换时参考的中心为图片的中心点。'])
                if key in {'sina','sino','sinp','cosa','cosp','coso','tana','tano','tanp'}:
                    detail.append('同表原文 L212～214：这类gmk的a指振幅，o指周期的倒数，p指相位，下同；默认为0（0代表禁用），下同；p默认为1，下同。')
            elif kind=='config':
                detail.extend(['文件优先级（原文）：[难度]_cgmk_config.json 优先于 cgmk_config.json。', issues[0]])
            if kind=='obj':
                object_records.append((key, cells[2], i))
                detail.extend(['!obj（原文）：一个谱面只能使用一个obj；未填默认为obj_base_gimmick。',
                    '【缺失附件】此 obj 的独有 gimmick 原文指向 Extra Gimmicks(NO COMMENTS).xlsx；该表未提供。'])
            pattern=''
            if kind=='mod' and re.search(r'\[(?:tid|lane|id|图像名)\]', key):
                pattern=re.escape(key)
                for placeholder, matcher in {'tid':r'[^\r\n,]+','图像名':r'[^\r\n,]+','lane':'[0-6]','id':'[1-4]'}.items():
                    pattern=pattern.replace(re.escape('['+placeholder+']'),f'({matcher})')
                pattern='^'+pattern+'$'
            add(key, category, kind, cells[1], detail, name, i, i, scope, pattern)
            table_rows.append(dict(Source=name, Line=i, Key=key, Kind=kind))
        # Non-table section articles preserve all prose and syntax, with pointers to table entries.
        starts=[i for i,s in enumerate(lines) if s.startswith('## ')]
        for ix, start in enumerate(starts):
            end=starts[ix+1] if ix+1<len(starts) else len(lines)
            title=plain(lines[start][3:])
            section=lines[start+1:end]
            details=paragraphs(section)
            tables_here=[r for r in table_rows if r['Source']==name and start+1<=r['Line']<=end]
            if tables_here:
                details.append('【目录索引】本节表格已逐行收录：'+'、'.join(r['Key'] for r in tables_here))
            if source_index==1 and title=='cgmk配置文件': details.append(issues[0])
            if source_index==1 and title=='图片插入': details.append(issues[2])
            add(title+' / 章节', 'VSM / 文件语法' if source_index==0 else 'Custom / '+title,
                'article', '附件章节：'+title, details, name, start+1, end)

    vsm=SOURCES[0]; custom=SOURCES[1]
    def excerpt(key, category, kind, source, start, end, short):
        return add(key, category, kind, short, paragraphs(source_lines[source][start-1:end]), source, start, end)
    grammar=[('!proxies',19,19,'指定谱面使用的轨道数量。'),('!obj',20,20,'指定特效库；一个谱面只能使用一个 obj。'),
             ('beat:endbeat:step',21,21,'重复起始拍、最后一次重复拍和间隔。'),('duration',22,22,'gimmick 持续时间；原字段说明拼作 duraion。'),
             ('easing',49,49,'缓动名称格式与文档提供的示例。'),('value1 / value2 / _ / 573613',24,24,'开始值、结束值与“不使用其中一个值”的标记。'),
             ('modname',25,25,'gimmick 名。'),('proxy',26,26,'控制的轨道编号从 0 开始；取值限制见具体条目。'),
             ('mpf',28,33,'函数启用区间及函数名。'),('!{name}:{content}',35,35,'非 obj/proxies 的感叹号元数据。')]
    for key,start,end,short in grammar:
        excerpt(key,'VSM / 文件语法','syntax',vsm,start,end,short)
    excerpt('VSM 语句模板', 'VSM / 文件语法','syntax',vsm,7,16,'所有事件时间字段以节拍为单位；括号表示可选部分。')
    for key,start,end,short in [('addVelo',322,328,'指定位置添加速度；time 使用 ms。'),
                              ('addVeloTween',331,341,'速度补间；time/timeEnd 使用 ms，默认 easing=linear、step=32。')]:
        excerpt(key,'Custom / 变速(SV)','syntax',custom,start,end,short)
    for key,start,end,short in [('字幕语句',49,55,'[拍数],[字幕的内容]'),
                             ('static 图片声明',159,162,'静态图片声明；两个尺寸字段保留原文重复的“宽度”。'),
                             ('animated 图片声明',165,174,'动画图片条带与帧数。')]:
        e=excerpt(key,'Custom / '+('字幕效果' if key=='字幕语句' else '图片插入'),'syntax',custom,start,end,short)
        if key=='static 图片声明': e['Detail'].append(issues[2])
    callbacks: dict[str,list[tuple[str,int]]] = {}
    for obj, names, line in object_records:
        for function in names.splitlines():
            if function and function!='无': callbacks.setdefault(function,[]).append((obj,line))
    for function, bindings in callbacks.items():
        add(function,'VSM / obj 与 mpf','mpf','文档列出的 mpf 函数；查看可用 obj。',[
            '函数名（原文）：'+function, '关联 obj（原表）：'+'、'.join(obj for obj,_ in bindings),
            'mpf（原文）：用于指定在beat处启用一个函数，一般这个函数与一个gimmick关联，在该gimmick填入的参数会送到该函数进行计算。',
            '启用语句（原文）：{beat},{e},{func}；beat 为启用时间，e 为结束启用时间，func 为函数名。',
            '【文档未提供】函数的算法与完整参数细节没有在此表展开，不据名称推测。',
            '来源行：'+ '、'.join(str(line) for _,line in bindings)
        ],vsm,min(line for _,line in bindings),max(line for _,line in bindings))
    # Put guide and grammar first, retaining source order within every category.
    if '原文全文' in category_order:
        category_order.remove('原文全文'); category_order.append('原文全文')
    entries.sort(key=lambda e:category_order.index(e['Category']))
    return dict(Schema=1, Revision='editor-preview.8', Sources=source_meta, Categories=category_order,
                Entries=entries, TableRows=table_rows,
                Counts=dict(Counter(e['Kind'] for e in entries)))


def main():
    data=build()
    DOC_DIR.mkdir(parents=True,exist_ok=True)
    (DOC_DIR/'vsm-manual.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(entries=len(data['Entries']),tableRows=len(data['TableRows']),categories=len(data['Categories']),counts=data['Counts']),ensure_ascii=False))

if __name__=='__main__': main()
