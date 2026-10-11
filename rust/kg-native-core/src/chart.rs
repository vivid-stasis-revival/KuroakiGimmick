//! Independent, std-only chart core. Units: seconds internally; VSC/VSB note timestamps: ms.
//! Reference: uploaded KuroakiGimmick Core/Parsing/VscReader.cs and VsbReader.cs.
use std::{collections::BTreeMap, fs, path::Path};

pub type Result<T> = std::result::Result<T, String>;
const MAX_CHART: u64 = 128 * 1024 * 1024;
const MAX_VSM: u64 = 64 * 1024 * 1024;
const MAX_MODS: usize = 1_000_000;

#[derive(Debug, Clone, PartialEq)]
pub struct Note {
    pub time: f64,
    pub kind: u8,
    pub lane: u8,
    pub end: f64,
    pub bpm: Option<f64>,
    pub extra: BTreeMap<u8, ExtraValue>,
    pub mod_extra: BTreeMap<String, Option<String>>,
    pub mod_extra_present: bool,
    pub mod_extra_order: Vec<String>,
}
#[derive(Debug, Clone, PartialEq)]
pub enum ExtraValue { Number(f64), TypedNumber(f64,u8), Text(String), Boolean(bool) }
#[derive(Debug, Clone, PartialEq)]
pub struct ModEvent {
    pub beat: f64, pub duration: f64, pub ease: String,
    pub from: f64, pub to: f64, pub name: String,
    pub proxy: i8, pub order: usize, pub source_line: usize,
}
#[derive(Debug, Clone, PartialEq)]
pub struct PerFrame { pub start: f64, pub end: f64, pub function: String }
#[derive(Debug, Clone)]
pub struct Chart {
    pub title: String,
    pub notes: Vec<Note>,
    pub mods: Vec<ModEvent>,
    pub per_frame: Vec<PerFrame>,
    pub object_name: String,
    pub proxies: u8,
    pub proxies_declared: bool,
    pub metadata: BTreeMap<String,String>,
    pub metadata_order: Vec<String>,
    pub csm_340: bool,
    pub warnings: Vec<String>,
    // Non-fatal, original-C#-compatible diagnostics sent across KGS2.
    pub diagnostics: Vec<(usize, String)>,
}
impl Default for Chart {
    fn default() -> Self {
        Self { title:String::new(), notes:Vec::new(), mods:Vec::new(),
            per_frame:Vec::new(), object_name:"obj_base_gimmick".to_string(),
            proxies:1, proxies_declared:false, metadata:BTreeMap::new(), metadata_order:Vec::new(),
            csm_340:false, warnings:Vec::new(), diagnostics:Vec::new() }
    }
}
impl Chart {
    pub fn stable_sort(&mut self) { self.notes.sort_by(|a,b| a.time.partial_cmp(&b.time).unwrap_or(std::cmp::Ordering::Equal)); }
    pub fn duration(&self) -> f64 { self.notes.iter().map(|n| n.end.max(n.time)).fold(0.0, f64::max) }
    pub fn playable_count(&self) -> usize { self.notes.iter().filter(|n| !matches!(n.kind, 3|4|5)).count() }
}
fn number(s: &str) -> Result<f64> {
    let n = s.trim().parse::<f64>().map_err(|_| format!("invalid number: '{s}'"))?;
    if !n.is_finite() { return Err(format!("non-finite number: '{s}'")); } Ok(n)
}
fn check_note(kind: u8, lane: u8, time: f64, end: f64) -> Result<()> {
    if kind > 8 { return Err(format!("invalid note type {kind}")); }
    if !time.is_finite() || !end.is_finite() {return Err("note time must be finite".into());}
    if ((kind == 0 || kind == 2 || kind == 6) && lane > 3)
        || ((kind == 1 || kind == 7 || kind == 8) && lane > 2) { return Err(format!("invalid lane {lane} for note type {kind}")); }
    if end < time { return Err("hold ends before start".into()); }
    Ok(())
}
fn read_limited(path: &Path, limit: u64) -> Result<Vec<u8>> {
    let size = fs::metadata(path).map_err(|e| format!("{}: {e}",path.display()))?.len();
    if size > limit { return Err(format!("file exceeds {} MiB limit",limit / (1024 * 1024))); }
    fs::read(path).map_err(|e| format!("{}: {e}",path.display()))
}
pub fn load(path: &Path) -> Result<Chart> {
    let bytes = read_limited(path, MAX_CHART)?;
    let title = path.file_stem().and_then(|s|s.to_str()).unwrap_or("chart").to_string();
    match path.extension().and_then(|s|s.to_str()).unwrap_or("").to_ascii_lowercase().as_str() {
        "vsc" => parse_vsc(std::str::from_utf8(&bytes).map_err(|e|format!("VSC is not UTF-8: {e}"))?, title),
        "vsb" => parse_vsb(&bytes, title),
        _ => Err("expected .vsc or .vsb file (use --vsm for separate gimmicks)".into()),
    }
}
pub fn parse_vsc(s: &str, title: String) -> Result<Chart> {
    let mut chart = Chart { title, ..Chart::default() };
    for (i, raw) in s.trim_start_matches('\u{feff}').lines().enumerate() {
        let value = raw.trim();
        if value.is_empty() || value.starts_with("//") || value.starts_with('#') { continue; }
        let parse = || -> Result<Note> {
            let p: Vec<&str> = value.splitn(5, ',').collect();
            if p.len() < 3 { return Err("expected time_ms,type,lane[,extra[,modExtra]]".into()); }
            let time = number(p[0])? / 1000.0;
            let kind = p[1].trim().parse::<u8>().map_err(|_|"bad note type")?;
            let lane = p[2].trim().parse::<u8>().map_err(|_|"bad lane")?;
            let mut end = time;
            let mut bpm = None;
            let mut extra = BTreeMap::new();
            if kind == 2 {
                if p.len() < 4 { return Err("hold requires absolute end milliseconds".into()); }
                end = number(p[3])? / 1000.0;
                extra.insert(1, ExtraValue::Number(end * 1000.0));
            } else if kind == 3 {
                if p.len() < 4 { return Err("timing row requires b:BPM".into()); }
                for field in p[3].split('|') {
                    if let Some((key,data)) = field.split_once(':') {
                        // Match VscReader: only literal empty/undefined are skipped.
                        if data.is_empty() || data == "undefined" { continue; }
                        let n = number(data)?;
                        if key == "b" {
                            if n <= 0.0 || n > 10000.0 { return Err("BPM out of range".into()); }
                            bpm = Some(n);
                            extra.insert(1, ExtraValue::Number(n));
                        }
                    }
                }
            }
            check_note(kind,lane,time,end)?;
            let mut mod_extra = BTreeMap::new();
            let mut mod_extra_order = Vec::new();
            if p.len() >= 5 && !p[4].is_empty() {
                for field in p[4].split('|') {
                    if let Some((key,data)) = field.split_once(':') {
                        let key = key.trim();
                        if !key.is_empty() {
                            let first = data.split(':').next().unwrap_or("");
                            let key=key.to_string();
                            if !mod_extra.contains_key(&key) {mod_extra_order.push(key.clone());}
                            mod_extra.insert(key, if first == "undefined" { None } else { Some(first.trim().to_string()) });
                        }
                    }
                }
            }
            Ok(Note {time,kind,lane,end,bpm,extra,mod_extra,mod_extra_present:p.len()>=5 && !p[4].is_empty(),mod_extra_order})
        };
        if value.splitn(5, ',').count() >= 5 { chart.csm_340 = true; }
        match parse() {
            Ok(n) => {
                // The original C# reader accepts v/s timing anchors while recording
                // one informational diagnostic per non-empty, defined anchor.
                // They must NOT disable the Rust parser or silently disappear.
                if n.kind == 3 {
                    if let Some(extra_text) = value.splitn(5, ',').nth(3) {
                        for field in extra_text.split('|') {
                            if let Some((key, data)) = field.split_once(':') {
                                if data.is_empty() || data == "undefined" { continue; }
                                if matches!(key, "v" | "s") {
                                    chart.diagnostics.push((i + 1,
                                        format!("VSC timing '{key}' is not applied.")));
                                }
                            }
                        }
                    }
                }
                chart.notes.push(n)
            },
            Err(e) => chart.warnings.push(format!("VSC line {}: {e}",i+1)),
        }
    }
    chart.stable_sort(); Ok(chart)
}

struct Reader<'a> { bytes:&'a [u8], pos:usize }
impl<'a> Reader<'a> {
    fn byte(&mut self)->Result<u8> { if self.pos>=self.bytes.len() {return Err(format!("truncated VSB at byte {}",self.pos));} let b=self.bytes[self.pos];self.pos+=1;Ok(b) }
    fn bytes<const N:usize>(&mut self)->Result<[u8;N]> { if self.pos+N>self.bytes.len(){return Err(format!("truncated VSB at byte {}",self.pos));} let mut a=[0;N];a.copy_from_slice(&self.bytes[self.pos..self.pos+N]);self.pos+=N;Ok(a) }
    fn f32(&mut self)->Result<f64>{let n=f32::from_le_bytes(self.bytes()? ) as f64;if !n.is_finite(){return Err("VSB non-finite f32".into());}Ok(n)}
    fn u32(&mut self)->Result<u32>{Ok(u32::from_le_bytes(self.bytes()?))}
    fn i32(&mut self)->Result<i32>{Ok(i32::from_le_bytes(self.bytes()?))}
    fn u16(&mut self)->Result<u16>{Ok(u16::from_le_bytes(self.bytes()?))}
    fn cstring(&mut self)->Result<String> {
        let start=self.pos;
        for _ in 0..65536 { if self.byte()?==0 {return String::from_utf8(self.bytes[start..self.pos-1].to_vec()).map_err(|e|format!("invalid VSB UTF-8: {e}"));} }
        Err("unterminated VSB string".into())
    }
    fn unexpected(&self, tag:u8)->String {format!("unexpected VSB tag 0x{tag:02x} at byte {}",self.pos-1)}
}
const GLOBAL_MODS:&str="unknown prx prxb prxc pry pryb pryc prsx pra przm przmb przx przy prrx prry prrz prrzb shxs shxp shxa shys shyp shya scrollspeed noterot velocity spinradius spiny spinx driven beat wave hom boost_distance boost_time yoffset notealp przmc prxd pryd prct prcb prcl prcr prvib shct shft shcb shfb shcl shfl shcr shfr scrollind0 scrollind1 scrollind2 scrollind3 scrollind4 scrollind5 scrollind6 drawdist pburstleft pburstright particlexpower particleypower uialpha fx_contrast fx_chroma_distort fx_film fx_glow fx_particleglow pburstspeed freeze drawuntil";
const EASES:&str="unknown linear outElastic inExpo outExpo inOutExpo inQuad outQuad inOutQuad inCubic outCubic inOutCubic outBack inSine outSine inOutSine outQuart inOutCirc inCirc outCirc";
fn half_to_f64(bits:u16)->f64 {
    let sign = if bits & 0x8000 != 0 { -1.0 } else {1.0};
    let exp = ((bits>>10)&0x1f) as i32;
    let mant = (bits&0x3ff) as f64;
    sign * if exp==0 { mant * 2f64.powi(-24) } else if exp==31 {if mant==0.0 {f64::INFINITY} else {f64::NAN}} else {(1024.0+mant)*2f64.powi(exp-25)}
}
pub fn parse_vsb(bytes:&[u8],title:String)->Result<Chart>{
    let mut r=Reader {bytes,pos:0};
    if bytes.len()<5 || &bytes[..5]!=b"VSC\x01\x00" {return Err("expected VSC version 1 header: 56 53 43 01 00".into());}
    r.pos=5;
    let mut c=Chart {title,..Chart::default()};
    let mut done=false;
    while !done {
        let tag=r.byte()?;
        match tag {
            255=>done=true,
            192=>loop {
                let t=r.byte()?;
                if t==193 {break;}
                if t!=160 {return Err(r.unexpected(t));}
                let (mut kind,mut lane,mut time)=(0u8,0u8,0.0);
                let mut extra=BTreeMap::new();
                loop {
                    match r.byte()? {
                        161=>break,
                        162=>kind=r.byte()?,
                        163=>lane=r.byte()?,
                        164=>time=r.f32()?/1000.0,
                        166=>loop {
                            let tag=r.byte()?;if tag==167 {break;}
                            let key=r.byte()?;
                            let v=match tag {
                                176=>ExtraValue::TypedNumber(r.byte()? as f64,3),
                                177=>ExtraValue::TypedNumber((r.byte()? as i8) as f64,4),
                                178=>ExtraValue::TypedNumber(r.u32()? as f64,5),
                                179=>ExtraValue::TypedNumber(r.i32()? as f64,6),
                                181=>ExtraValue::Number(half_to_f64(r.u16()?)),
                                182=>ExtraValue::Number(r.f32()?),
                                183=>ExtraValue::Boolean(r.byte()?!=0),
                                184=>ExtraValue::Text(r.cstring()?),
                                x=>return Err(r.unexpected(x)),
                            };
                            extra.insert(key,v);
                        },
                        x=>return Err(r.unexpected(x)),
                    }
                }
                let mut end=time;
                if kind==2 {
                    if let Some(value)=extra.get(&1){
                        if let ExtraValue::Number(n)|ExtraValue::TypedNumber(n,_) = value {end=*n/1000.0;}
                    }
                }
                check_note(kind,lane,time,end)?;
                let bpm = if kind==3 { match extra.get(&1) {
                    Some(ExtraValue::Number(x))|Some(ExtraValue::TypedNumber(x,_))=>Some(*x),_=>None} } else {None};
                c.notes.push(Note{time,kind,lane,end,bpm,extra,mod_extra:BTreeMap::new(),mod_extra_present:false,mod_extra_order:Vec::new()});
            },
            224=>loop {
                match r.byte()? {
                    225=>break,
                    228=>{c.proxies=r.byte()?;c.proxies_declared=true;if c.proxies>64{return Err("too many proxies".into());}},
                    229=>c.object_name=r.cstring()?,
                    226=>loop {
                        match r.byte()? {
                            227=>break,
                            233=>{
                                let beat=r.f32()?;let duration=r.f32()?;let ease=r.byte()?;
                                let from=r.f32()?;let to=r.f32()?;let id=r.byte()?;let proxy=r.byte()? as i8;
                                let name=if id<128 {GLOBAL_MODS.split_whitespace().nth(id as usize).map(str::to_owned).unwrap_or_else(||format!("global_{id}"))} else {format!("{}:mod_{id}",c.object_name)};
                                let ease=EASES.split_whitespace().nth(ease as usize).map(str::to_owned).unwrap_or_else(||format!("ease_{ease}"));
                                let order=c.mods.len();
                                c.mods.push(ModEvent{beat,duration,ease,from,to,name,proxy,order,source_line:0});
                            },
                            236=> {let start=r.f32()?;let end=r.f32()?;let function=r.cstring()?;c.per_frame.push(PerFrame{start,end,function});},
                            x=>return Err(r.unexpected(x)),
                        }
                        if c.mods.len()+c.per_frame.len()>MAX_MODS{return Err("too many mod events".into());}
                    },
                    x=>return Err(r.unexpected(x)),
                }
            },
            x=>return Err(r.unexpected(x)),
        }
    }
    c.stable_sort();Ok(c)
}

pub fn load_vsm(path: &Path, c:&mut Chart)->Result<()> {
    let bytes=read_limited(path,MAX_VSM)?;
    let text=std::str::from_utf8(&bytes).map_err(|e|format!("VSM UTF-8: {e}"))?;
    parse_vsm(text,c)
}
fn beat_range(s:&str)->Result<Vec<f64>>{
    let (a,b,step)=if let Some((a,tail))=s.split_once('('){
        let tail=tail.strip_suffix(')').ok_or("invalid parenthesized beat range")?;
        let p:Vec<&str>=tail.split(':').collect();
        if p.len()!=3 || !p[0].trim().is_empty(){return Err("expected start(:end:step)".into());}
        (number(a)?,number(p[1])?,number(p[2])?)
    }else{
        let p:Vec<&str>=s.split(':').collect();
        if p.len()>3 {return Err("invalid beat range".into());}
        (number(p[0])?,if p.len()>=2{number(p[1])?}else{number(p[0])?},if p.len()==3{number(p[2])?}else{1.0})
    };
    if step<=0.0 || b<a {return Err("invalid beat range direction/step".into());}
    let count=(((b-a)/step+1e-7).floor()+1.0) as usize;
    if count>MAX_MODS {return Err("beat range expands above one million events".into());}
    Ok((0..count).map(|i|a+(i as f64)*step).collect())
}
fn normalize_ease(s:&str)->String {
    let raw=s.trim();
    let stripped=if raw.get(..4).is_some_and(|prefix|prefix.eq_ignore_ascii_case("ease")) {&raw[4..]} else {raw};
    let known="linear inSine outSine inOutSine inQuad outQuad inOutQuad inCubic outCubic inOutCubic inQuart outQuart inOutQuart inQuint outQuint inOutQuint inExpo outExpo inOutExpo inCirc outCirc inOutCirc inBack outBack inOutBack inElastic outElastic inOutElastic inBounce outBounce inOutBounce";
    known.split_whitespace().find(|k|k.eq_ignore_ascii_case(stripped)).unwrap_or(stripped).to_string()
}

pub fn parse_vsm(text:&str,c:&mut Chart)->Result<()> {
    c.mods.clear();c.per_frame.clear();c.metadata.clear();c.metadata_order.clear();
    let mut per_frame=false;
    let mut trailing_comment_line=None;
    for (i,raw) in text.lines().enumerate(){
        let line=raw.trim();if line.is_empty(){continue;}
        if line.starts_with("//")||line.starts_with('#')||line.starts_with(';') {
            c.warnings.push(format!("VSM line {}: original game may reject comment rows",i+1));continue;
        }
        let line=if let Some((body,_))=line.split_once("//") {
            trailing_comment_line.get_or_insert(i+1);
            body.trim()
        }else{line};
        if line=="mpf"{per_frame=true;continue;}
        if line=="mods"{per_frame=false;c.warnings.push(format!("VSM line {}: original game may crash on bare 'mods' marker",i+1));continue;}
        if let Some(metadata)=line.strip_prefix('!') {
            if let Some((key,value))=metadata.split_once(':'){
                if key.trim().is_empty() {
                    c.warnings.push(format!("VSM line {}: empty metadata key",i+1));
                    continue;
                }
                let key=key.trim().to_string();
                if !c.metadata.contains_key(&key) {c.metadata_order.push(key.clone());}
                c.metadata.insert(key.clone(), value.trim().to_string());
                match key.trim(){"obj"=>c.object_name=value.trim().to_owned(),"proxies"=>{
                    let x=value.trim().parse::<u8>().map_err(|_|"bad proxy count")?;
                    if x>64{return Err("proxy count >64".into());}c.proxies=x;c.proxies_declared=true;},_=>{}}
            }else{c.warnings.push(format!("VSM line {}: missing metadata colon",i+1));}
            continue;
        }
        let p:Vec<&str>=line.split(',').map(str::trim).collect();
        let parse=||->Result<Vec<ModEvent>> {
            if per_frame{return Err("per-frame requires separate parser".into());}
            if p.len()!=7{return Err("expected beat,duration,ease,from,to,mod,proxy".into());}
            let range=beat_range(p[0])?;
            let duration=number(p[1])?;
            let from=if p[3]=="_" {573613.0}else{number(p[3])?};
            let to=if p[4]=="_" {573613.0}else{number(p[4])?};
            let proxy=p[6].parse::<i8>().map_err(|_|"invalid proxy")?;
            if !(-1..=63).contains(&proxy){return Err("proxy out of range".into());}
            Ok(range.into_iter().map(|beat|ModEvent{beat,duration,ease:normalize_ease(p[2]),from,to,name:p[5].to_owned(),proxy,order:0,source_line:i+1}).collect())
        };
        if per_frame {
            if p.len()==3 {if let (Ok(start),Ok(end))=(number(p[0]),number(p[1])) {
                if end>=start {c.per_frame.push(PerFrame{start,end,function:p[2].to_owned()});continue;}
            }}
            c.warnings.push(format!("VSM line {}: malformed mpf row",i+1));
        }else{match parse(){
            Ok(mut mods)=>{ if c.mods.len()+mods.len()>MAX_MODS{return Err("too many mods".into());}
                for mut event in mods.drain(..) {event.order=c.mods.len();c.mods.push(event);}
            },
            Err(e)=>c.warnings.push(format!("VSM line {}: {e}",i+1)),
        }}
    }
    if let Some(line)=trailing_comment_line {
        c.diagnostics.push((line, "K/G accepts trailing // comments, but the original read_mods_file does not strip them. Remove trailing comments before using this file in the game.".into()));
    }
    Ok(())
}

// Opt-in high-memory regression at the historical hard-coded boundary.
#[cfg(test)]
mod unbounded_note_tests {
    use super::*;

    #[test]
    #[ignore = "allocates more than two million notes; run explicitly for stress testing"]
    fn vsc_more_than_two_million_notes() {
        const COUNT: usize = 2_000_001;
        let input = "0,0,0\n".repeat(COUNT);
        let chart = parse_vsc(&input, "stress".to_string()).unwrap();
        assert_eq!(chart.notes.len(), COUNT);
    }
}
