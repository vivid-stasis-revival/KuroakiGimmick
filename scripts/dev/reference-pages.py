"""Build presentation blocks from the supplied documents. No runtime semantics are inferred."""
from __future__ import annotations
import re

def present(data, plain, cells, is_separator):
    sources={s['File']:s['Text'].splitlines() for s in data['Sources']}
    entries=[e for e in data['Entries'] if e['Kind'] not in ('guide','note','source')]
    row_ids={(e['Source'],e['StartLine']):e['Id'] for e in entries if e['Cells']}
    def text(s):
        # Only presentation wrappers, never grammar placeholders or value ranges.
        s=plain(s)
        s=re.sub(r'【([^】]+)】',r'\1',s)
        return s
    def block(kind, value='', **kw):
        return dict(Kind=kind,Text=value,**kw)
    # Navigation subheadings organize the supplied paragraphs; they add no parameter semantics.
    section_labels={
        'custom.section.1':{11:'配置文件',21:'可用配置',37:'配置示例'},
        'custom.section.2':{49:'字幕文件',59:'单文件字幕',68:'多文件字幕'},
        'custom.section.3':{90:'自定义背景',95:'plaudite背景',108:'控制参数'},
        'custom.section.4':{118:'VSP 文件',144:'图层声明',154:'图片声明',179:'控制参数'},
        'custom.section.10':{323:'addVelo',332:'addVeloTween'},
    }
    def parse(source,start,end,entry_id):
        lines=sources[source][start-1:end]; output=[]; i=0
        while i<len(lines):
            line=lines[i].strip()
            if title:=section_labels.get(entry_id,{}).get(start+i):
                output.append(block('heading',title))
            if not line or re.fullmatch(r'<br\s*/?>',line,re.I): i+=1; continue
            single=re.fullmatch(r'(`{3,})(.+?)\1',line)
            if single:
                output.append(block('code',single.group(2)));i+=1;continue
            fence=re.match(r'^(`{3,})([^`]*)$',line)
            if fence:
                language=fence.group(2).strip(); buf=[];i+=1
                while i<len(lines) and not re.fullmatch(r'`{3,}\s*',lines[i].strip()):buf.append(lines[i]);i+=1
                output.append(block('code','\n'.join(buf),Language=language));i+=1;continue
            if line.startswith('|') and i+1<len(lines) and is_separator(cells(lines[i+1])):
                header=[text(c) for c in cells(line)];rows=[];links=[];i+=2
                while i<len(lines) and lines[i].strip().startswith('|'):
                    rows.append([text(c).strip('"') for c in cells(lines[i])]);links.append(row_ids.get((source,start+i),''));i+=1
                output.append(block('table',Columns=header,Rows=rows,RowLinks=links));continue
            heading=re.match(r'^(#{1,6})\s+(.+)',line)
            if heading:
                output.append(block('heading',text(heading.group(2))));i+=1;continue
            inline_heading=re.match(r'^\*\*([^*]+)\*\*<br\s*/?>(.+)$',line,re.I)
            if inline_heading:
                output.append(block('heading',text(inline_heading.group(1))))
                output.append(block('paragraph',text(inline_heading.group(2))))
                i+=1;continue
            # Source uses bold stand-alone labels as subheadings in VSM enumerations.
            if re.fullmatch(r'\*\*[^*]+\*\*(?:<br\s*/?>)?',line,re.I):
                output.append(block('heading',text(line)));i+=1;continue
            kind='callout' if line.startswith('>') else 'list-item' if line.startswith('- ') else 'paragraph'
            value=text(re.sub(r'^(?:>|-)\s*','',line))
            if value:output.append(block(kind,value))
            i+=1
        # The page already has its own title. Do not repeat the leading Markdown heading.
        if output and output[0]['Kind']=='heading': output.pop(0)
        return output
    for e in entries:
        e['Name']=e['Name'].replace(' / 原文规则','')
        e['Category']=e['Category'].replace(' / 原文规则','')
        e['Notes']=[]
        e['Context']=[re.sub(r'^(?:本节原文注：|本组原文说明：|文档约定：|VSM 文档：)','',s).replace('原文说明：','') for s in e['Context']]
        if e['Kind']=='section':e['Summary']=''
        elif e['Summary'].startswith('原文'):e['Summary']=''
        if e['Kind'] in ('section','syntax'):
            e['Blocks']=parse(e['Source'],e['StartLine'],e['EndLine'],e['Id'])
        elif e['Kind']=='mpf':
            objects=[x for x in entries if x['Kind']=='object' and e['Name'] in plain(x['Cells'][2]).splitlines()]
            e['Body']=['可用 obj：'+ '、'.join(x['Name'] for x in objects)]
            e['Blocks']=[block('heading','可用 obj'),block('table',Columns=['obj','可用 mpf'],Rows=[[x['Name'],plain(x['Cells'][2])] for x in objects],RowLinks=[x['Id'] for x in objects])]
        else:
            blocks=[]
            for s in e['Body']:
                label,sep,value=s.partition('：')
                if sep:
                    blocks.append(block('heading',text(label)));blocks.append(block('paragraph',text(value)))
                elif s:blocks.append(block('paragraph',text(s)))
            if e['Context']:
                blocks.append(block('heading','适用条件'))
                blocks.extend(block('paragraph',text(s)) for s in e['Context'])
            e['Blocks']=blocks
        # Keep raw source excerpts solely for provenance/testing; never surface them as a page.
        e['Body']=[text(s) for s in e['Body']]
    data['Entries']=entries
    data['Revision']='editor-preview.9'
    data['Counts']={k:sum(e['Kind']==k for e in entries) for k in sorted({e['Kind'] for e in entries})}
    return data
