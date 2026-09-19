#!/usr/bin/env python3
"""Offline layout illustration from shipped PNG glyphs and generated document blocks.

This is a Pillow rendering, NOT a compiled SDL screenshot or an input test.
No system font binary is read or distributed. Pillow is optional for this script.
"""
from __future__ import annotations
import argparse
import json
import math
import re
from functools import lru_cache
from pathlib import Path
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[2]

class Face:
    def __init__(self,bold=False):
        name='editor-help-sans'+('-bold' if bold else '')
        self.metrics=json.loads((ROOT/'Assets/Fonts'/f'{name}.json').read_text())
        self.glyphs=self.metrics['Glyphs'];self.em=self.metrics['EmSize']
        self.image=Image.open(ROOT/'Assets/Fonts'/f'{name}.png').convert('RGBA')
    def measure(self,text,size):return sum(self.glyphs.get(c,self.glyphs['?'])['Advance']*size/self.em for c in text)
    @lru_cache(maxsize=4096)
    def glyph(self,c,size,color):
        g=self.glyphs.get(c,self.glyphs['?']); scale=size/self.em
        if not g['Width'] or not g['Height']:return None
        bounds=(g['X'],g['Y'],g['X']+g['Width'],g['Y']+g['Height'])
        mask=self.image.crop(bounds).getchannel('A').resize((max(1,round(g['Width']*scale)),max(1,round(g['Height']*scale))),Image.Resampling.BILINEAR)
        image=Image.new('RGBA',mask.size,color);image.putalpha(mask)
        return image,round(g['OffsetX']*scale),round(g['OffsetY']*scale)
    def draw(self,image,text,x,y,size,color):
        for c in text:
            g=self.glyphs.get(c,self.glyphs['?']);bitmap=self.glyph(c,size,color)
            if bitmap:
                bitmap,dx,dy=bitmap;image.alpha_composite(bitmap,(round(x+dx),round(y+dy)))
            x+=g['Advance']*size/self.em


def wrap(text,width,face,size):
    # Offline approximation of the native word/grapheme layout; bounds and complete text are checked below.
    output=[]
    for paragraph in text.split('\n'):
        line=''
        for token in re.findall(r'[A-Za-z0-9_./-]+|[^A-Za-z0-9_./-]',paragraph):
            if face.measure(line+token,size)<=width:line+=token;continue
            if line.strip():output.append(line.rstrip());line=''
            for char in token:
                if line and face.measure(line+char,size)>width:output.append(line.rstrip());line=''
                line+=char
        output.append(line.rstrip())
    return output

def article(entry,width,regular,bold):
    items=[];anchors=[];top=0
    for b in entry['Blocks']:
        kind=b['Kind'];value=b.get('Text','')
        if kind=='heading':
            if top:top+=18
            anchors.append((value,top));lines=wrap(value,width,bold,20);height=len(lines)*30+10
            items.append((kind,top,height,[lines],[width],''));top+=height
        elif kind=='code':
            lines=[]
            for line in value.split('\n'):
                pending=''
                for ch in line.replace('\t','    '):
                    if pending and regular.measure(pending+ch,16)>width-32:lines.append(pending);pending=''
                    pending+=ch
                lines.append(pending)
            height=40+max(1,len(lines))*26+14;items.append((kind,top,height,[lines],[width],value));top+=height+20
        elif kind=='table':
            n=len(b['Columns']);weights={1:[1],2:[.37,.63],3:[.29,.31,.4]}.get(n,[1/n]*n);widths=[width*w for w in weights]
            for i,row in enumerate([b['Columns']]+b['Rows']):
                head=i==0;face=bold if head else regular
                lines=[wrap(cell,max(1,widths[j]-24),face,15) for j,cell in enumerate(row)]
                height=max(1,max(map(len,lines)))*25+20
                items.append(('table-header' if head else 'table-row',top,height,lines,widths,''));top+=height
            top+=24
        elif value:
            note=kind=='callout';lst=kind=='list-item'
            lines=wrap(value,width-(28 if note else 20 if lst else 0),regular,17)
            height=len(lines)*28+(20 if note else 0);items.append((kind,top,height,[lines],[width],''));top+=height+(8 if lst else 16)
    return items,anchors or [(entry['Name'],0)],top

def render(output,width,height,entryid):
    data=json.loads((ROOT/'Assets/Documentation/vsm-reference.json').read_text());byid={e['Id']:e for e in data['Entries']};e=byid[entryid]
    regular,bold=Face(),Face(True)
    im=Image.new('RGBA',(width,height),'#090B10');draw=ImageDraw.Draw(im)
    def rect(x,y,w,h,c,border=None):draw.rectangle((round(x),round(y),round(x+w),round(y+h)),fill=c,outline=border)
    def text(t,x,y,size=17,c='#E6EAF1',heavy=False): (bold if heavy else regular).draw(im,t,x,y,size,c)
    def fit(t,w,size=15):
        while regular.measure(t,size)>w and len(t)>1:t=t[:-2]+'…'
        return t
    def button(t,x,y,w,h=34):
        rect(x,y,w,h,'#222632','#373F4F');text(t,x+(w-regular.measure(t,14))/2,y+4,14)
    rx=ry=16;rw=width-32;rh=height-32;nav=max(208,min(286,rw*.205));wide=rw>=1050;toc=194 if wide else 0
    cx=rx+nav+26;cw=max(120,rw-nav-toc-66);end=rx+rw-109
    rect(rx,ry,rw,rh,'#111319','#3A414F');rect(rx+1,ry+67,nav+8,rh-68,'#171A22')
    text('VSM 手册',rx+20,ry+17,23,heavy=True)
    searchw=max(80,end-cx-175);rect(cx,ry+15,searchw,36,'#202530','#3C4659');text('搜索文档…  /',cx+11,ry+20,15,'#A7B1C2')
    button('<',end-159,ry+16,32);button('>',end-121,ry+16,32);button('目录',end-82,ry+16,73);button('关闭',rx+rw-87,ry+17,67,32)
    rect(rx,ry+66,rw,1,'#303645');rect(rx+nav+9,ry+67,1,rh-68,'#303645')
    # Show actual navigation sections, scrolled to the selected document's neighborhood.
    navrows=[(s['Name'],s['Id']) for s in data['Entries'] if s['Kind']=='section'];at=next((i for i,(_,id) in enumerate(navrows) if id==e['Id']),0)
    first=max(0,at-8)
    y=ry+79
    for label,id in navrows[first:]:
        if y+34>ry+rh-56:break
        if id==e['Id']:rect(rx+12,y,nav-21,34,'#412736');rect(rx+12,y+4,2,26,'#FF8499')
        text('>',rx+21,y+5,14,'#A7B1C2');text(fit(label,nav-55),rx+43,y+5,15,'#E6EAF1' if id==e['Id'] else '#A7B1C2');y+=34
    text('Ctrl/Cmd+F 搜索 · F1 关闭',rx+20,ry+rh-35,12,'#A7B1C2')
    text(e['Category'],cx,ry+79,13,'#A7B1C2');text(e['Name'],cx,ry+109,25,heavy=True)
    items,anchors,total=article(e,cw-20,regular,bold);bodyh=max(1,rh-224);bodyy=ry+159
    body=Image.new('RGBA',(round(cw),round(bodyh)),(0,0,0,0));bd=ImageDraw.Draw(body)
    def brect(x,y,w,h,c):bd.rectangle((round(x),round(y),round(x+w),round(y+h)),fill=c)
    for kind,y,h,lines,widths,raw in items:
        if y>=bodyh:break
        if kind.startswith('table'):
            head=kind=='table-header';brect(0,y,cw-20,h,'#293140' if head else '#191E28');brect(0,y+h-1,cw-20,1,'#3A4252');x=0
            for j,col in enumerate(lines):
                for k,line in enumerate(col):(bold if head else regular).draw(body,line,x+12,y+8+k*25,15,'#FF8499' if j==0 and not head else '#E6EAF1')
                x+=widths[j]
        elif kind=='code':
            brect(0,y,cw-20,h,'#1C2230');regular.draw(body,'CODE',14,y+10,10,'#A7B1C2');regular.draw(body,'复制',cw-80,y+7,14,'#E6EAF1')
            for k,line in enumerate(lines[0]):regular.draw(body,line,14,y+40+k*26,16,'#ACD9E4')
        else:
            note=kind=='callout';lst=kind=='list-item';head=kind=='heading';x=14 if note else 20 if lst else 0
            if note:brect(0,y,cw-20,h,'#242937');brect(0,y,3,h,'#FF8499')
            if lst:brect(3,y+11,4,4,'#A7B1C2')
            for k,line in enumerate(lines[0]):(bold if head else regular).draw(body,line,x,y+(10 if note else 0)+k*(30 if head else 28),20 if head else 17,'#E6EAF1' if head else '#F0F2F7')
    im.alpha_composite(body,(round(cx),round(bodyy)))
    if total>bodyh:
        thumb=max(28,bodyh*bodyh/total);rect(cx+cw-6,bodyy,3,bodyh,'#262C38');rect(cx+cw-7,bodyy,4,thumb,'#7A879C')
    button('上一页',cx,ry+rh-51,88,31);button('下一页',cx+cw-88,ry+rh-51,88,31)
    if wide:
        tx=rx+rw-194;text('本页目录',tx+12,ry+84,14,heavy=True)
        for i,(label,_) in enumerate(anchors):
            if ry+124+i*37>ry+rh-35:break
            rect(tx+6,ry+119+i*37,2,35,'#FF8499' if i==0 else '#333D4C');text(fit(label,150,14),tx+17,ry+124+i*37,14,'#FF8499' if i==0 else '#A7B1C2')
    output.parent.mkdir(parents=True,exist_ok=True);im.convert('RGB').save(output)
    print(f'Offline illustration (not SDL): {output} {width}x{height}, article height {total:.0f}, {len(items)} blocks')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('output',type=Path);parser.add_argument('--width',type=int,default=1440);parser.add_argument('--height',type=int,default=960);parser.add_argument('--entry',default='custom.section.2');args=parser.parse_args()
    if args.width<800 or args.height<600:parser.error('Use at least 800x600 for this illustration.')
    render(args.output,args.width,args.height,args.entry)
