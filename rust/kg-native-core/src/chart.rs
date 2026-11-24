//! VSC、VSB 与 VSM 的谱面模型和解析器。
//!
//! 文本与二进制读取路径统一将音符时刻转换为秒，演出事件保留拍数。紧凑表示仍可按引用或重复组还原同刻事件的来源顺序。

use std::collections::{BTreeMap, HashMap};

/// 谱面解析结果，失败时以字符串携带格式或容量错误。
pub type Result<T> = std::result::Result<T, String>;
const MAX_MODS: usize = 1_000_000;

/// 谱面的音符或变速记录。
#[derive(Debug, Clone, PartialEq)]
pub struct Note {
    /// 起始歌曲时刻，单位为秒。
    pub time: f64,
    /// 源谱面类型编号，解析器接受 0 至 8。
    pub kind: u8,
    /// 源轨道编号，有效范围取决于 kind。
    pub lane: u8,
    /// 结束歌曲时刻，普通音符与起点相同。
    pub end: f64,
    /// 变速行中的 BPM；缺少数值时为 None。
    pub bpm: Option<f64>,
    /// 用数值键保存的扩展数据，保留二进制类型信息。
    pub extra: BTreeMap<u8, ExtraValue>,
    /// VSC 第五列的扩展键值，undefined 记为 None。
    pub mod_extra: BTreeMap<String, Option<String>>,
    /// 第五列存在且非空时为 true。
    pub mod_extra_present: bool,
    /// 扩展键首次出现的顺序，用于写出兼容数据包。
    pub mod_extra_order: Vec<String>,
}
/// 音符扩展字段的值及其传输表示。
#[derive(Debug, Clone, PartialEq)]
pub enum ExtraValue {
    /// 按浮点值传输的数值。
    Number(f64),
    /// 数值及 KGS2 整数类型标签，用于恢复托管端的原始数值类型。
    TypedNumber(f64, u8),
    /// 从以零结尾的 UTF-8 字段读取的文本。
    Text(String),
    /// 由二进制字段中的零或非零字节解析的布尔值。
    Boolean(bool),
}
/// 已展开的 VSM 或 VSB 演出事件。
#[derive(Debug, Clone, PartialEq)]
pub struct ModEvent {
    /// 事件的起始拍。
    pub beat: f64,
    /// 事件持续拍数。
    pub duration: f64,
    /// 缓动函数名。
    pub ease: String,
    /// 起始值，VSM 下划线对应哨兵 573613。
    pub from: f64,
    /// 结束值，VSM 下划线对应哨兵 573613。
    pub to: f64,
    /// 轨道或回调名称。
    pub name: String,
    /// 目标 proxy 编号，-1 表示全局。
    pub proxy: i8,
    /// 事件追加到谱面时的序号，从 0 开始。
    pub order: usize,
    /// VSM 的行号，从 1 开始；VSB 无文本行号时为 0。
    pub source_line: usize,
}
/// 谱面在指定拍区间内调用的逐帧函数。
#[derive(Debug, Clone, PartialEq)]
pub struct PerFrame {
    /// 起始拍。
    pub start: f64,
    /// 结束拍。
    pub end: f64,
    /// 源谱面中的函数名。
    pub function: String,
}
/// 音符、演出控制及来源诊断的解析结果。
#[derive(Debug, Clone)]
pub struct Chart {
    /// 音符与变速记录；紧凑解析时须结合引用表还原全部音符。
    pub notes: Vec<Note>,
    /// 按来源顺序追加的演出事件。
    pub mods: Vec<ModEvent>,
    /// 逐帧函数的触发范围。
    pub per_frame: Vec<PerFrame>,
    /// 源演出对象名，默认 obj_base_gimmick。
    pub object_name: String,
    /// proxy 数量，未声明时默认 1。
    pub proxies: u8,
    /// 来源显式声明 proxy 数量时为 true。
    pub proxies_declared: bool,
    /// 演出头字段的键值表。
    pub metadata: BTreeMap<String, String>,
    /// 头字段键首次出现的顺序。
    pub metadata_order: Vec<String>,
    /// 读取 VSC 时发现至少一条非注释数据行带第五列，即使该列为空。
    pub csm_340: bool,
    /// 按行恢复解析时记录的问题；编码桥接包时触发诊断回退。
    pub warnings: Vec<String>,
    /// 可通过 KGS2 传递的非致命诊断，包含行号和说明。
    pub diagnostics: Vec<(usize, String)>,
}
impl Default for Chart {
    fn default() -> Self {
        Self {
            notes: Vec::new(),
            mods: Vec::new(),
            per_frame: Vec::new(),
            object_name: "obj_base_gimmick".to_string(),
            proxies: 1,
            proxies_declared: false,
            metadata: BTreeMap::new(),
            metadata_order: Vec::new(),
            csm_340: false,
            warnings: Vec::new(),
            diagnostics: Vec::new(),
        }
    }
}
impl Chart {
    /// 按音符起始秒数稳定排序，同刻记录保留当前位置的相对顺序。
    pub fn stable_sort(&mut self) {
        self.notes.sort_by(|a, b| {
            a.time
                .partial_cmp(&b.time)
                .unwrap_or(std::cmp::Ordering::Equal)
        });
    }
}
fn number(s: &str) -> Result<f64> {
    let n = s
        .trim()
        .parse::<f64>()
        .map_err(|_| format!("invalid number: '{s}'"))?;
    if !n.is_finite() {
        return Err(format!("non-finite number: '{s}'"));
    }
    Ok(n)
}
fn check_note(kind: u8, lane: u8, time: f64, end: f64) -> Result<()> {
    if kind > 8 {
        return Err(format!("invalid note type {kind}"));
    }
    if !time.is_finite() || !end.is_finite() {
        return Err("note time must be finite".into());
    }
    if ((kind == 0 || kind == 2 || kind == 6) && lane > 3)
        || ((kind == 1 || kind == 7 || kind == 8) && lane > 2)
    {
        return Err(format!("invalid lane {lane} for note type {kind}"));
    }
    if end < time {
        return Err("hold ends before start".into());
    }
    Ok(())
}
/// 解析 VSC 文本并按起始时间稳定排列音符。
///
/// # Arguments
///
/// - `s`：完整谱面文本，可带 UTF-8 BOM。
///
/// # Returns
///
/// 返回包含有效音符、逐行警告和时序诊断的谱面。
///
/// # Errors
///
/// 逐行格式问题保存在 `Chart::warnings`；当前非紧凑路径不因单行无效而返回 Err。
pub fn parse_vsc(s: &str) -> Result<Chart> {
    let (mut chart, _) = parse_vsc_records(s, false)?;
    chart.stable_sort();
    Ok(chart)
}

/// 按源行解析 VSC，可为重复音符生成紧凑引用表。
///
/// # Arguments
///
/// - `s`：完整谱面文本。
/// - `compact`：是否复用短且不产生诊断的相同行。
///
/// # Returns
///
/// 返回记录表与出现位置的引用。普通模式的引用表为空，记录保持源顺序；紧凑模式对引用按时刻稳定排序。
///
/// # Errors
///
/// 紧凑模式中记录索引无法用 u32 表示时返回错误。行格式错误记入 warnings 后继续读取。
pub fn parse_vsc_records(s: &str, compact: bool) -> Result<(Chart, Vec<u32>)> {
    let mut chart = Chart::default();
    let mut references = Vec::new();
    // 1. 在单次读取中共享符合条件的源行，每次出现都保留对应引用。
    let mut cache: HashMap<&str, u32> = HashMap::new();
    for (i, raw) in s.trim_start_matches('\u{feff}').lines().enumerate() {
        let value = raw.trim();
        if value.is_empty() || value.starts_with("//") || value.starts_with('#') {
            continue;
        }
        if compact {
            if let Some(&index) = cache.get(value) {
                references.push(index);
                continue;
            }
        }
        // 2. 解析未命中的记录，只有不需逐行报告诊断的短行才进入缓存。
        let diagnostics_before = chart.diagnostics.len();
        let parse = || -> Result<Note> {
            let mut fields = [""; 5];
            let mut count = 0;
            for field in value.splitn(5, ',') {
                fields[count] = field;
                count += 1;
            }
            let p = &fields[..count];
            if p.len() < 3 {
                return Err("expected time_ms,type,lane[,extra[,modExtra]]".into());
            }
            let time = number(p[0])? / 1000.0;
            let kind = p[1].trim().parse::<u8>().map_err(|_| "bad note type")?;
            let lane = p[2].trim().parse::<u8>().map_err(|_| "bad lane")?;
            let mut end = time;
            let mut bpm = None;
            let mut extra = BTreeMap::new();
            if kind == 2 {
                if p.len() < 4 {
                    return Err("hold requires absolute end milliseconds".into());
                }
                end = number(p[3])? / 1000.0;
                extra.insert(1, ExtraValue::Number(end * 1000.0));
            } else if kind == 3 {
                if p.len() < 4 {
                    return Err("timing row requires b:BPM".into());
                }
                for field in p[3].split('|') {
                    if let Some((key, data)) = field.split_once(':') {
                        // COMPAT: 与 VscReader 一致，仅跳过字面空串与 undefined。
                        if data.is_empty() || data == "undefined" {
                            continue;
                        }
                        let n = number(data)?;
                        if key == "b" {
                            if n <= 0.0 || n > 10000.0 {
                                return Err("BPM out of range".into());
                            }
                            bpm = Some(n);
                            extra.insert(1, ExtraValue::Number(n));
                        }
                    }
                }
            }
            check_note(kind, lane, time, end)?;
            let mut mod_extra = BTreeMap::new();
            let mut mod_extra_order = Vec::new();
            if p.len() >= 5 && !p[4].is_empty() {
                for field in p[4].split('|') {
                    if let Some((key, data)) = field.split_once(':') {
                        let key = key.trim();
                        if !key.is_empty() {
                            let first = data.split(':').next().unwrap_or("");
                            let key = key.to_string();
                            if !mod_extra.contains_key(&key) {
                                mod_extra_order.push(key.clone());
                            }
                            mod_extra.insert(
                                key,
                                if first == "undefined" {
                                    None
                                } else {
                                    Some(first.trim().to_string())
                                },
                            );
                        }
                    }
                }
            }
            Ok(Note {
                time,
                kind,
                lane,
                end,
                bpm,
                extra,
                mod_extra,
                mod_extra_present: p.len() >= 5 && !p[4].is_empty(),
                mod_extra_order,
            })
        };
        if value.splitn(5, ',').count() >= 5 {
            chart.csm_340 = true;
        }
        match parse() {
            Ok(n) => {
                // COMPAT: v/s 锚点不参与求值；每个非空且已定义的锚点仍须产生与 C# 相同的源行诊断。
                if n.kind == 3 {
                    if let Some(extra_text) = value.split(',').nth(3) {
                        for field in extra_text.split('|') {
                            if let Some((key, data)) = field.split_once(':') {
                                if data.is_empty() || data == "undefined" {
                                    continue;
                                }
                                if matches!(key, "v" | "s") {
                                    chart.diagnostics.push((
                                        i + 1,
                                        format!("VSC timing '{key}' is not applied."),
                                    ));
                                }
                            }
                        }
                    }
                }
                if compact {
                    let index =
                        u32::try_from(chart.notes.len()).map_err(|_| "too many VSC records")?;
                    references.push(index);
                    if cache.len() < 4096
                        && value.len() <= 1024
                        && chart.diagnostics.len() == diagnostics_before
                    {
                        cache.insert(value, index);
                    }
                }
                chart.notes.push(n)
            }
            Err(e) => chart.warnings.push(format!("VSC line {}: {e}", i + 1)),
        }
    }
    if compact {
        // 3. 对出现序列稳定排序，记录表保持可索引。
        references.sort_by(|&a, &b| {
            chart.notes[a as usize]
                .time
                .partial_cmp(&chart.notes[b as usize].time)
                .unwrap()
        });
    }
    Ok((chart, references))
}

/// 同刻音符在记录表中的起始索引、连续记录数及该组的重复次数。
type RepeatGroup = (usize, usize, usize);

/// 识别整份源文本的重复周期，用等时记录组描述展开顺序。
///
/// # Arguments
///
/// - `s`：待探测的完整 VSC 文本。
///
/// # Returns
///
/// 成功时返回谱面记录、各组的（起始索引、组内元素数、重复次数）及展开后的总数。
/// 源文件较小、周期无法证实、含诊断或数量超限时返回 None，由常规解析器处理。
pub fn parse_periodic_vsc(s: &str) -> Option<(Chart, Vec<RepeatGroup>, usize)> {
    let s = s.trim_start_matches('\u{feff}');
    let bytes = s.as_bytes();
    if bytes.len() < 256 * 1024 {
        return None;
    }
    // 1. 用受限前缀找出候选周期，再校验换行边界及全文字节。
    let sample = &bytes[..bytes.len().min(64 * 1024)];
    let mut prefix = vec![0usize; sample.len()];
    for i in 1..sample.len() {
        let mut matched = prefix[i - 1];
        while matched > 0 && sample[i] != sample[matched] {
            matched = prefix[matched - 1];
        }
        if sample[i] == sample[matched] {
            matched += 1;
        }
        prefix[i] = matched;
    }
    let period = sample.len() - prefix[sample.len() - 1];
    if period * 2 > sample.len()
        || bytes[period - 1] != b'\n'
        || bytes[period..] != bytes[..bytes.len() - period]
    {
        return None;
    }
    let repeats = bytes.len() / period;
    let mut chart = parse_vsc(&s[..period]).ok()?;
    let tail = parse_vsc(&s[repeats * period..]).ok()?;
    // 2. 分别解析完整周期和尾块；有警告或诊断时回退，保留每次出现的源行号。
    if chart.notes.is_empty()
        || !chart.warnings.is_empty()
        || !chart.diagnostics.is_empty()
        || !tail.warnings.is_empty()
        || !tail.diagnostics.is_empty()
    {
        return None;
    }
    let base_count = chart.notes.len();
    let total = base_count
        .checked_mul(repeats)?
        .checked_add(tail.notes.len())?;
    if total > i32::MAX as usize {
        return None;
    }
    chart.csm_340 |= tail.csm_340;
    chart.notes.extend(tail.notes);
    let mut groups = Vec::new();
    for (begin, end, repeat) in [(0, base_count, repeats), (base_count, chart.notes.len(), 1)] {
        let mut start = begin;
        while start < end {
            let mut next = start + 1;
            while next < end && chart.notes[next].time == chart.notes[start].time {
                next += 1;
            }
            groups.push((start, next - start, repeat));
            start = next;
        }
    }
    // 3. 按时间稳定排列重复组，同刻的尾块记录仍在完整周期之后。
    groups.sort_by(|a, b| {
        chart.notes[a.0]
            .time
            .partial_cmp(&chart.notes[b.0].time)
            .unwrap()
    });
    Some((chart, groups, total))
}

struct Reader<'a> {
    bytes: &'a [u8],
    pos: usize,
}
impl<'a> Reader<'a> {
    fn byte(&mut self) -> Result<u8> {
        if self.pos >= self.bytes.len() {
            return Err(format!("truncated VSB at byte {}", self.pos));
        }
        let b = self.bytes[self.pos];
        self.pos += 1;
        Ok(b)
    }
    fn bytes<const N: usize>(&mut self) -> Result<[u8; N]> {
        if self.pos + N > self.bytes.len() {
            return Err(format!("truncated VSB at byte {}", self.pos));
        }
        let mut a = [0; N];
        a.copy_from_slice(&self.bytes[self.pos..self.pos + N]);
        self.pos += N;
        Ok(a)
    }
    fn f32(&mut self) -> Result<f64> {
        let n = f32::from_le_bytes(self.bytes()?) as f64;
        if !n.is_finite() {
            return Err("VSB non-finite f32".into());
        }
        Ok(n)
    }
    fn u32(&mut self) -> Result<u32> {
        Ok(u32::from_le_bytes(self.bytes()?))
    }
    fn i32(&mut self) -> Result<i32> {
        Ok(i32::from_le_bytes(self.bytes()?))
    }
    fn u16(&mut self) -> Result<u16> {
        Ok(u16::from_le_bytes(self.bytes()?))
    }
    fn cstring(&mut self) -> Result<String> {
        let start = self.pos;
        for _ in 0..65536 {
            if self.byte()? == 0 {
                return String::from_utf8(self.bytes[start..self.pos - 1].to_vec())
                    .map_err(|e| format!("invalid VSB UTF-8: {e}"));
            }
        }
        Err("unterminated VSB string".into())
    }
    fn unexpected(&self, tag: u8) -> String {
        format!("unexpected VSB tag 0x{tag:02x} at byte {}", self.pos - 1)
    }
}
const GLOBAL_MODS:&str="unknown prx prxb prxc pry pryb pryc prsx pra przm przmb przx przy prrx prry prrz prrzb shxs shxp shxa shys shyp shya scrollspeed noterot velocity spinradius spiny spinx driven beat wave hom boost_distance boost_time yoffset notealp przmc prxd pryd prct prcb prcl prcr prvib shct shft shcb shfb shcl shfl shcr shfr scrollind0 scrollind1 scrollind2 scrollind3 scrollind4 scrollind5 scrollind6 drawdist pburstleft pburstright particlexpower particleypower uialpha fx_contrast fx_chroma_distort fx_film fx_glow fx_particleglow pburstspeed freeze drawuntil";
const EASES:&str="unknown linear outElastic inExpo outExpo inOutExpo inQuad outQuad inOutQuad inCubic outCubic inOutCubic outBack inSine outSine inOutSine outQuart inOutCirc inCirc outCirc";
fn half_to_f64(bits: u16) -> f64 {
    let sign = if bits & 0x8000 != 0 { -1.0 } else { 1.0 };
    let exp = ((bits >> 10) & 0x1f) as i32;
    let mant = (bits & 0x3ff) as f64;
    sign * if exp == 0 {
        mant * 2f64.powi(-24)
    } else if exp == 31 {
        if mant == 0.0 {
            f64::INFINITY
        } else {
            f64::NAN
        }
    } else {
        (1024.0 + mant) * 2f64.powi(exp - 25)
    }
}
/// 按 VSC1 二进制标记读取音符、演出事件和逐帧函数。
///
/// # Arguments
///
/// - `bytes`：包含版本头与终止标记的 VSB 文件字节。
///
/// # Returns
///
/// 按音符时刻稳定排序的谱面。
///
/// # Errors
///
/// 头部、标记、字段编码、音符数据无效，输入被截断或演出数量超出当前预算时返回错误。
pub fn parse_vsb(bytes: &[u8]) -> Result<Chart> {
    // 1. 核对版本头后，按字节位置读取带标签的各段。
    let mut r = Reader { bytes, pos: 0 };
    if bytes.len() < 5 || &bytes[..5] != b"VSC\x01\x00" {
        return Err("expected VSC version 1 header: 56 53 43 01 00".into());
    }
    r.pos = 5;
    let mut c = Chart::default();
    let mut done = false;
    // 2. 分别解码音符段和演出段；未知标记保留偏移以便定位错误。
    while !done {
        let tag = r.byte()?;
        match tag {
            255 => done = true,
            192 => loop {
                let t = r.byte()?;
                if t == 193 {
                    break;
                }
                if t != 160 {
                    return Err(r.unexpected(t));
                }
                let (mut kind, mut lane, mut time) = (0u8, 0u8, 0.0);
                let mut extra = BTreeMap::new();
                loop {
                    match r.byte()? {
                        161 => break,
                        162 => kind = r.byte()?,
                        163 => lane = r.byte()?,
                        164 => time = r.f32()? / 1000.0,
                        166 => loop {
                            let tag = r.byte()?;
                            if tag == 167 {
                                break;
                            }
                            let key = r.byte()?;
                            let v = match tag {
                                176 => ExtraValue::TypedNumber(r.byte()? as f64, 3),
                                177 => ExtraValue::TypedNumber((r.byte()? as i8) as f64, 4),
                                178 => ExtraValue::TypedNumber(r.u32()? as f64, 5),
                                179 => ExtraValue::TypedNumber(r.i32()? as f64, 6),
                                181 => ExtraValue::Number(half_to_f64(r.u16()?)),
                                182 => ExtraValue::Number(r.f32()?),
                                183 => ExtraValue::Boolean(r.byte()? != 0),
                                184 => ExtraValue::Text(r.cstring()?),
                                x => return Err(r.unexpected(x)),
                            };
                            extra.insert(key, v);
                        },
                        x => return Err(r.unexpected(x)),
                    }
                }
                let mut end = time;
                if kind == 2 {
                    if let Some(ExtraValue::Number(n) | ExtraValue::TypedNumber(n, _)) =
                        extra.get(&1)
                    {
                        end = *n / 1000.0;
                    }
                }
                check_note(kind, lane, time, end)?;
                let bpm = if kind == 3 {
                    match extra.get(&1) {
                        Some(ExtraValue::Number(x)) | Some(ExtraValue::TypedNumber(x, _)) => {
                            Some(*x)
                        }
                        _ => None,
                    }
                } else {
                    None
                };
                c.notes.push(Note {
                    time,
                    kind,
                    lane,
                    end,
                    bpm,
                    extra,
                    mod_extra: BTreeMap::new(),
                    mod_extra_present: false,
                    mod_extra_order: Vec::new(),
                });
            },
            224 => loop {
                match r.byte()? {
                    225 => break,
                    228 => {
                        c.proxies = r.byte()?;
                        c.proxies_declared = true;
                        if c.proxies > 64 {
                            return Err("too many proxies".into());
                        }
                    }
                    229 => c.object_name = r.cstring()?,
                    226 => loop {
                        match r.byte()? {
                            227 => break,
                            233 => {
                                let beat = r.f32()?;
                                let duration = r.f32()?;
                                let ease = r.byte()?;
                                let from = r.f32()?;
                                let to = r.f32()?;
                                let id = r.byte()?;
                                let proxy = r.byte()? as i8;
                                let name = if id < 128 {
                                    GLOBAL_MODS
                                        .split_whitespace()
                                        .nth(id as usize)
                                        .map(str::to_owned)
                                        .unwrap_or_else(|| format!("global_{id}"))
                                } else {
                                    format!("{}:mod_{id}", c.object_name)
                                };
                                let ease = EASES
                                    .split_whitespace()
                                    .nth(ease as usize)
                                    .map(str::to_owned)
                                    .unwrap_or_else(|| format!("ease_{ease}"));
                                let order = c.mods.len();
                                c.mods.push(ModEvent {
                                    beat,
                                    duration,
                                    ease,
                                    from,
                                    to,
                                    name,
                                    proxy,
                                    order,
                                    source_line: 0,
                                });
                            }
                            236 => {
                                let start = r.f32()?;
                                let end = r.f32()?;
                                let function = r.cstring()?;
                                c.per_frame.push(PerFrame {
                                    start,
                                    end,
                                    function,
                                });
                            }
                            x => return Err(r.unexpected(x)),
                        }
                        if !crate::load_limits::unlimited()
                            && c.mods.len() + c.per_frame.len() > MAX_MODS
                        {
                            return Err("too many mod events".into());
                        }
                    },
                    x => return Err(r.unexpected(x)),
                }
            },
            x => return Err(r.unexpected(x)),
        }
    }
    // 3. 在完整记录间恢复时间顺序，同时保留同刻音符的先后。
    c.stable_sort();
    Ok(c)
}

#[derive(Debug, Clone, Copy)]
struct BeatRange {
    start: f64,
    step: f64,
    count: usize,
}
impl BeatRange {
    fn iter(self) -> impl ExactSizeIterator<Item = f64> {
        // COMPAT: 按索引乘步长计算拍数，避免逐次累加改变浮点舍入。
        (0..self.count).map(move |i| self.start + (i as f64) * self.step)
    }
}
fn beat_range(s: &str) -> Result<BeatRange> {
    let (a, b, step) = if let Some((a, tail)) = s.split_once('(') {
        let tail = tail
            .strip_suffix(')')
            .ok_or("invalid parenthesized beat range")?;
        let p: Vec<&str> = tail.split(':').collect();
        if p.len() != 3 || !p[0].trim().is_empty() {
            return Err("expected start(:end:step)".into());
        }
        (number(a)?, number(p[1])?, number(p[2])?)
    } else {
        let p: Vec<&str> = s.split(':').collect();
        if p.len() > 3 {
            return Err("invalid beat range".into());
        }
        (
            number(p[0])?,
            if p.len() >= 2 {
                number(p[1])?
            } else {
                number(p[0])?
            },
            if p.len() == 3 { number(p[2])? } else { 1.0 },
        )
    };
    if step <= 0.0 || b < a {
        return Err("invalid beat range direction/step".into());
    }
    let amount = ((b - a) / step + 1e-7).floor() + 1.0;
    if !amount.is_finite() || amount > (isize::MAX as usize / std::mem::size_of::<f64>()) as f64 {
        return Err("beat range cannot be represented in memory".into());
    }
    let count = amount as usize;
    if !crate::load_limits::unlimited() && count > MAX_MODS {
        return Err("beat range expands above one million events".into());
    }
    Ok(BeatRange {
        start: a,
        step,
        count,
    })
}
fn normalize_ease(s: &str) -> &str {
    let raw = s.trim();
    let stripped = if raw
        .get(..4)
        .is_some_and(|prefix| prefix.eq_ignore_ascii_case("ease"))
    {
        &raw[4..]
    } else {
        raw
    };
    let known="linear inSine outSine inOutSine inQuad outQuad inOutQuad inCubic outCubic inOutCubic inQuart outQuart inOutQuart inQuint outQuint inOutQuint inExpo outExpo inOutExpo inCirc outCirc inOutCirc inBack outBack inOutBack inElastic outElastic inOutElastic inBounce outBounce inOutBounce";
    known
        .split_whitespace()
        .find(|k| k.eq_ignore_ascii_case(stripped))
        .unwrap_or(stripped)
}

/// 替换谱面中的演出事件、逐帧区间与头字段。
///
/// # Arguments
///
/// - `text`：VSM 源文本。
/// - `c`：就地更新的谱面；音符保留，警告和诊断在原列表后追加。
///
/// # Returns
///
/// 处理完所有行后返回 Ok(())；能按行跳过的问题保存在谱面中。
///
/// # Errors
///
/// 声明的 proxy 数量无效或累计 mod 数量超过当前预算时返回错误，此前写入的行仍保留。
pub fn parse_vsm(text: &str, c: &mut Chart) -> Result<()> {
    // 1. 清空本次要重建的演出数据，保留音符及既有诊断。
    c.mods.clear();
    c.per_frame.clear();
    c.metadata.clear();
    c.metadata_order.clear();
    let mut per_frame = false;
    let mut trailing_comment_line = None;
    // 2. 识别头字段和分区，在相应模式下解析事件或逐帧区间。
    for (i, raw) in text.lines().enumerate() {
        let line = raw.trim();
        if line.is_empty() {
            continue;
        }
        if line.starts_with("//") || line.starts_with('#') || line.starts_with(';') {
            c.warnings.push(format!(
                "VSM line {}: original game may reject comment rows",
                i + 1
            ));
            continue;
        }
        let line = if let Some((body, _)) = line.split_once("//") {
            trailing_comment_line.get_or_insert(i + 1);
            body.trim()
        } else {
            line
        };
        if line == "mpf" {
            per_frame = true;
            continue;
        }
        if line == "mods" {
            per_frame = false;
            c.warnings.push(format!(
                "VSM line {}: original game may crash on bare 'mods' marker",
                i + 1
            ));
            continue;
        }
        if let Some(metadata) = line.strip_prefix('!') {
            if let Some((key, value)) = metadata.split_once(':') {
                if key.trim().is_empty() {
                    c.warnings
                        .push(format!("VSM line {}: empty metadata key", i + 1));
                    continue;
                }
                let key = key.trim().to_string();
                if !c.metadata.contains_key(&key) {
                    c.metadata_order.push(key.clone());
                }
                c.metadata.insert(key.clone(), value.trim().to_string());
                match key.trim() {
                    "obj" => c.object_name = value.trim().to_owned(),
                    "proxies" => {
                        let x = value.trim().parse::<u8>().map_err(|_| "bad proxy count")?;
                        if x > 64 {
                            return Err("proxy count >64".into());
                        }
                        c.proxies = x;
                        c.proxies_declared = true;
                    }
                    _ => {}
                }
            } else {
                c.warnings
                    .push(format!("VSM line {}: missing metadata colon", i + 1));
            }
            continue;
        }
        let p: Vec<&str> = line.split(',').map(str::trim).collect();
        let parse = || -> Result<(BeatRange, f64, f64, f64, i8)> {
            if per_frame {
                return Err("per-frame requires separate parser".into());
            }
            if p.len() != 7 {
                return Err("expected beat,duration,ease,from,to,mod,proxy".into());
            }
            let range = beat_range(p[0])?;
            let duration = number(p[1])?;
            let from = if p[3] == "_" { 573613.0 } else { number(p[3])? };
            let to = if p[4] == "_" { 573613.0 } else { number(p[4])? };
            let proxy = p[6].parse::<i8>().map_err(|_| "invalid proxy")?;
            if !(-1..=63).contains(&proxy) {
                return Err("proxy out of range".into());
            }
            Ok((range, duration, from, to, proxy))
        };
        if per_frame {
            if p.len() == 3 {
                if let (Ok(start), Ok(end)) = (number(p[0]), number(p[1])) {
                    if end >= start {
                        c.per_frame.push(PerFrame {
                            start,
                            end,
                            function: p[2].to_owned(),
                        });
                        continue;
                    }
                }
            }
            c.warnings
                .push(format!("VSM line {}: malformed mpf row", i + 1));
        } else {
            match parse() {
                Ok((range, duration, from, to, proxy)) => {
                    if !crate::load_limits::unlimited() && c.mods.len() + range.count > MAX_MODS {
                        return Err("too many mods".into());
                    }
                    // 先检查整行和累计预算，再直接追加展开事件，无需临时拍数数组。
                    let ease = normalize_ease(p[2]);
                    for beat in range.iter() {
                        let order = c.mods.len();
                        c.mods.push(ModEvent {
                            beat,
                            duration,
                            ease: ease.to_owned(),
                            from,
                            to,
                            name: p[5].to_owned(),
                            proxy,
                            order,
                            source_line: i + 1,
                        });
                    }
                }
                Err(e) => c.warnings.push(format!("VSM line {}: {e}", i + 1)),
            }
        }
    }
    // 3. 将首次出现的行尾注释位置写入兼容性诊断。
    if let Some(line) = trailing_comment_line {
        c.diagnostics.push((line, "K/G accepts trailing // comments, but the original read_mods_file does not strip them. Remove trailing comments before using this file in the game.".into()));
    }
    Ok(())
}

#[cfg(test)]
mod vsm_repeat_tests {
    use super::*;

    #[test]
    fn compact_ranges_preserve_bitwise_progression() {
        let cases = [
            ("-0", -0.0, 1.0, 1),
            ("3:5", 3.0, 1.0, 3),
            ("0.1:0.7:0.1", 0.1, 0.1, 7),
            ("-1(:1:0.3)", -1.0, 0.3, 7),
            // 覆盖略超出端点但仍在历史容差内的情况。
            ("0:0.99999995:1", 0.0, 1.0, 2),
            // 在此数值量级，相邻事件可能舍入到同一拍。
            ("10000000000000000:10000000000000006:1", 1e16, 1.0, 7),
        ];
        for (text, start, step, count) in cases {
            let range = beat_range(text).unwrap();
            assert_eq!(range.count, count, "{text}");
            assert_eq!(range.iter().len(), count, "{text}");
            for (i, beat) in range.iter().enumerate() {
                let expected: f64 = start + (i as f64) * step;
                assert_eq!(beat.to_bits(), expected.to_bits(), "{text}, event {i}");
            }
        }
    }

    #[test]
    fn compact_ranges_preserve_errors_and_limits() {
        for (text, error) in [
            ("1(:2:1", "invalid parenthesized beat range"),
            ("1(2:3:1)", "expected start(:end:step)"),
            ("1:2:3:4", "invalid beat range"),
            ("2:1", "invalid beat range direction/step"),
            ("1:2:0", "invalid beat range direction/step"),
            ("1:2:-1", "invalid beat range direction/step"),
            ("0:1e30:1", "beat range cannot be represented in memory"),
            (
                "0:1e308:1e-308",
                "beat range cannot be represented in memory",
            ),
            ("0:1000000", "beat range expands above one million events"),
        ] {
            assert_eq!(beat_range(text).unwrap_err(), error, "{text}");
        }
        assert_eq!(beat_range("0:999999").unwrap().count, MAX_MODS);
        crate::load_limits::with_unlimited(|| {
            assert_eq!(beat_range("0:1000000").unwrap().count, MAX_MODS + 1);
            assert_eq!(
                beat_range("0:1e30:1").unwrap_err(),
                "beat range cannot be represented in memory"
            );
        });
        assert!(!crate::load_limits::unlimited());
    }

    #[test]
    fn repeated_rows_preserve_fields_order_and_diagnostics() {
        let text = "!proxies:2\n\
            1:1.2:0.1,0.5,EASEOUTsine,_,2,prx,-1 // comment\n\
            -0,-1,easeCustom,3,_,pry,63\n\
            0:3,1,linear,0,1,prx,64\n\
            2(:2.5:0.25),2,LiNeAr,4,5,prz,0\n\
            // standalone\n\
            mpf\n\
            0,3,custom_function\n";
        let mut c = Chart::default();
        parse_vsm(text, &mut c).unwrap();
        assert_eq!(c.mods.len(), 7);
        for (order, event) in c.mods.iter().enumerate() {
            assert_eq!(event.order, order);
            assert_eq!(event.source_line, [2, 2, 2, 3, 5, 5, 5][order]);
        }
        for (i, event) in c.mods[..3].iter().enumerate() {
            assert_eq!(event.beat.to_bits(), (1.0 + (i as f64) * 0.1).to_bits());
            assert_eq!(event.duration, 0.5);
            assert_eq!(event.ease, "outSine");
            assert_eq!((event.from, event.to), (573613.0, 2.0));
            assert_eq!(event.name, "prx");
            assert_eq!(event.proxy, -1);
        }
        let event = &c.mods[3];
        assert_eq!(event.beat.to_bits(), 0.0_f64.to_bits());
        assert_eq!(event.duration, -1.0);
        assert_eq!(event.ease, "Custom");
        assert_eq!((event.from, event.to), (3.0, 573613.0));
        assert_eq!(event.proxy, 63);
        for (i, event) in c.mods[4..].iter().enumerate() {
            assert_eq!(event.beat, 2.0 + (i as f64) * 0.25);
            assert_eq!(event.ease, "linear");
        }
        assert_eq!(
            c.warnings,
            [
                "VSM line 4: proxy out of range",
                "VSM line 6: original game may reject comment rows",
            ]
        );
        assert_eq!(c.diagnostics, vec![(2, "K/G accepts trailing // comments, but the original read_mods_file does not strip them. Remove trailing comments before using this file in the game.".into())]);
        assert_eq!(
            c.per_frame,
            vec![PerFrame {
                start: 0.0,
                end: 3.0,
                function: "custom_function".into()
            }]
        );
        assert_eq!(c.proxies, 2);
    }

    #[test]
    fn malformed_repeat_rows_are_atomic_and_keep_error_precedence() {
        let mut c = Chart::default();
        parse_vsm(
            "0,1,linear,0,1,prx,0\n\
            0:999999,bad,linear,0,1,prx,0\n\
            0:999999,1,linear,bad,1,prx,0\n\
            0:999999,1,linear,0,bad,prx,0\n\
            0:999999,1,linear,0,1,prx,bad\n\
            0:999999,1,linear,0,1,prx,64\n\
            0:1000000,bad,linear,0,1,prx,0\n\
            2,1,linear,0,1,pry,0",
            &mut c,
        )
        .unwrap();
        assert_eq!(c.mods.len(), 2);
        assert_eq!((c.mods[1].order, c.mods[1].source_line), (1, 8));
        assert_eq!(
            c.warnings,
            [
                "VSM line 2: invalid number: 'bad'",
                "VSM line 3: invalid number: 'bad'",
                "VSM line 4: invalid number: 'bad'",
                "VSM line 5: invalid proxy",
                "VSM line 6: proxy out of range",
                "VSM line 7: beat range expands above one million events",
            ]
        );
    }

    #[test]
    fn unlimited_malformed_repeat_does_not_expand_before_validation() {
        let mut c = Chart::default();
        crate::load_limits::with_unlimited(|| {
            // 旧实现会在拒绝此行前分配 800 MB 的拍数数组；测试数量也能在 32 位平台表示。
            parse_vsm("0:99999999,1,linear,0,1,prx,64", &mut c).unwrap();
        });
        assert!(c.mods.is_empty());
        assert_eq!(c.mods.capacity(), 0);
        assert_eq!(c.warnings, ["VSM line 1: proxy out of range"]);
    }

    #[test]
    fn mixed_repeat_and_single_rows_keep_amortized_capacity() {
        let mut c = Chart::default();
        parse_vsm(
            "0:32,1,linear,0,1,prx,0\n\
            0,1,linear,0,1,pry,0",
            &mut c,
        )
        .unwrap();
        let mut original_growth = Vec::new();
        for event in &c.mods {
            original_growth.push(event.clone());
        }
        // 若初次精确预留 33 项，最后一行会扩到 66 项；常规摊销增长只需 64 项。
        assert_eq!(c.mods.capacity(), original_growth.capacity());
        assert_eq!(c.mods.len(), 34);
    }

    #[test]
    fn cumulative_limit_rejects_the_whole_row_after_validation() {
        let mut c = Chart::default();
        let error = parse_vsm(
            "0:999999,1,linear,0,1,prx,0\n\
            0:1,1,linear,0,1,pry,64\n\
            0:1,1,linear,0,1,pry,0",
            &mut c,
        )
        .unwrap_err();
        assert_eq!(error, "too many mods");
        assert_eq!(c.mods.len(), MAX_MODS);
        assert_eq!(c.mods.last().unwrap().order, MAX_MODS - 1);
        assert!(c
            .mods
            .iter()
            .all(|event| event.name == "prx" && event.source_line == 1));
        assert_eq!(c.warnings, ["VSM line 2: proxy out of range"]);
    }

    #[test]
    #[ignore = "allocates more than one million mod events; run explicitly for stress testing"]
    fn unlimited_repeats_expand_above_one_million() {
        let mut c = Chart::default();
        crate::load_limits::with_unlimited(|| {
            parse_vsm(
                "0:1000000,1,linear,0,1,prx,0\n\
                2:3,1,linear,0,1,pry,0",
                &mut c,
            )
            .unwrap();
        });
        assert_eq!(c.mods.len(), MAX_MODS + 3);
        assert_eq!(c.mods[MAX_MODS].beat, MAX_MODS as f64);
        assert_eq!(c.mods[MAX_MODS].order, MAX_MODS);
        assert_eq!(c.mods[MAX_MODS + 1].beat, 2.0);
        assert_eq!(c.mods[MAX_MODS + 1].source_line, 2);
        assert!(c.warnings.is_empty());
    }
}

// 显式运行的高内存回归，覆盖旧版固定音符上限。
#[cfg(test)]
mod unbounded_note_tests {
    use super::*;

    #[test]
    #[ignore = "allocates more than two million notes; run explicitly for stress testing"]
    fn vsc_more_than_two_million_notes() {
        const COUNT: usize = 2_000_001;
        let input = "0,0,0\n".repeat(COUNT);
        let chart = parse_vsc(&input).unwrap();
        assert_eq!(chart.notes.len(), COUNT);
    }
}

#[cfg(test)]
mod compact_vsc_tests {
    use super::*;
    #[test]
    fn compact_references_preserve_order_metadata_and_diagnostics() {
        let block = "200,0,2\n100,0,1\n100,0,3\n100,2,0,500,k:undefined|x:a:b\n100,3,0,b:180|v:1\n0,0,0,,\n";
        let text = block.repeat(100);
        let original = parse_vsc(&text).unwrap();
        let (compact, ids) = parse_vsc_records(&text, true).unwrap();
        let restored: Vec<_> = ids
            .iter()
            .map(|&id| compact.notes[id as usize].clone())
            .collect();
        assert_eq!(original.notes, restored);
        assert_eq!(original.diagnostics, compact.diagnostics);
        assert_eq!(original.csm_340, compact.csm_340);
        assert!(compact.notes.len() < original.notes.len());
    }

    #[test]
    fn periodic_source_preserves_ties_partial_tail_and_metadata() {
        let block = "200,0,2\n100,0,1\n100,2,0,500,k:undefined|x:雪:b\n100,3,0,b:180\n-10,0,0,,\n# comment\n";
        for tail in ["", "200,0,2\n100,0,1\n", "200,0,2"] {
            let text = block.repeat(5000) + tail;
            let original = parse_vsc(&text).unwrap();
            let (chart, groups, total) = parse_periodic_vsc(&text).expect("period not detected");
            let mut restored = Vec::new();
            for (start, count, repeats) in groups {
                for _ in 0..repeats {
                    restored.extend_from_slice(&chart.notes[start..start + count]);
                }
            }
            assert_eq!(original.notes.len(), total);
            assert_eq!(original.notes, restored);
            assert_eq!(original.csm_340, chart.csm_340);
        }
        // 仅前缀重复不能证明全文具有相同周期。
        let mut changed = block.repeat(5000).into_bytes();
        changed[block.len() * 4000] = b'9';
        assert!(parse_periodic_vsc(std::str::from_utf8(&changed).unwrap()).is_none());
        let diagnostic = "100,3,0,b:180|v:1\n".repeat(20000);
        assert!(parse_periodic_vsc(&diagnostic).is_none());
    }
}
