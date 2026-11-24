//! 谱面解析和 BPM 分段的 C ABI。
//!
//! 将 chart 模块的结果编码为小端数据包。FFI 返回地址与字节数，托管端完成读取后调用 kg_core_free_packet，错误消息包遵循同一所有权约定。

use crate::chart::{self, Chart, ExtraValue};
use std::panic::{catch_unwind, AssertUnwindSafe};

const MAX_INPUT: usize = 128 * 1024 * 1024;
const MAX_VSM: usize = 64 * 1024 * 1024;
const MAX_PACKET: usize = 512 * 1024 * 1024;

fn u8_(buf: &mut Vec<u8>, value: u8) {
    buf.push(value);
}
fn u32_(buf: &mut Vec<u8>, value: usize) -> Result<(), String> {
    let n = u32::try_from(value).map_err(|_| "packet count overflow".to_string())?;
    buf.extend_from_slice(&n.to_le_bytes());
    Ok(())
}
fn i32_(buf: &mut Vec<u8>, value: i32) {
    buf.extend_from_slice(&value.to_le_bytes());
}
fn f64_(buf: &mut Vec<u8>, value: f64) {
    buf.extend_from_slice(&value.to_le_bytes());
}
fn text(buf: &mut Vec<u8>, s: &str) -> Result<(), String> {
    u32_(buf, s.len())?;
    buf.extend_from_slice(s.as_bytes());
    Ok(())
}

fn encode_chart(c: &Chart, include_notes: bool) -> Result<Vec<u8>, String> {
    // 1. 有逐行警告时交回 C# 诊断路径，保持原有错误呈现方式。
    if !c.warnings.is_empty() {
        return Err(format!(
            "Rust parse requires legacy diagnostic path: {}",
            c.warnings[0]
        ));
    }
    // 2. 每条音符的最小编码占 33 字节；在申请缓冲区前检查乘法溢出和当前包预算。
    let min_note_bytes = c
        .notes
        .len()
        .checked_mul(33)
        .ok_or("Rust parse note packet size overflow")?;
    if !crate::load_limits::unlimited() && min_note_bytes > MAX_PACKET {
        return Err("Rust parse output exceeds 512 MiB".to_string());
    }
    let reserve = c
        .notes
        .len()
        .saturating_mul(48)
        .saturating_add(4096)
        .min(MAX_PACKET);
    let mut b = Vec::with_capacity(reserve);
    b.extend_from_slice(b"KGS2");
    u8_(&mut b, if c.csm_340 { 1 } else { 0 });
    u8_(&mut b, if c.proxies_declared { 1 } else { 0 });
    u8_(&mut b, c.proxies);
    text(&mut b, &c.object_name)?;
    u32_(&mut b, c.metadata_order.len())?;
    for key in &c.metadata_order {
        let value = c.metadata.get(key).ok_or("missing metadata entry")?;
        text(&mut b, key)?;
        text(&mut b, value)?;
    }
    u32_(&mut b, if include_notes { c.notes.len() } else { 0 })?;
    if include_notes {
        for n in &c.notes {
            f64_(&mut b, n.time);
            i32_(&mut b, n.kind as i32);
            i32_(&mut b, n.lane as i32);
            f64_(&mut b, n.end);
            u32_(&mut b, n.extra.len())?;
            for (key, val) in &n.extra {
                u8_(&mut b, *key);
                match val {
                    ExtraValue::Number(number) => {
                        u8_(&mut b, 0);
                        f64_(&mut b, *number);
                    }
                    ExtraValue::TypedNumber(number, tag) => {
                        u8_(&mut b, *tag);
                        f64_(&mut b, *number);
                    }
                    ExtraValue::Text(s) => {
                        u8_(&mut b, 1);
                        text(&mut b, s)?;
                    }
                    ExtraValue::Boolean(v) => {
                        u8_(&mut b, 2);
                        u8_(&mut b, if *v { 1 } else { 0 });
                    }
                }
            }
            u8_(&mut b, if n.mod_extra_present { 1 } else { 0 });
            u32_(&mut b, n.mod_extra_order.len())?;
            for key in &n.mod_extra_order {
                let val = n.mod_extra.get(key).ok_or("missing modExtra entry")?;
                text(&mut b, key)?;
                u8_(&mut b, if val.is_some() { 1 } else { 0 });
                if let Some(value) = val {
                    text(&mut b, value)?;
                }
            }
            if !crate::load_limits::unlimited() && b.len() > MAX_PACKET {
                return Err("Rust parse output exceeds 512 MiB".to_string());
            }
        }
    }
    u32_(&mut b, c.mods.len())?;
    for m in &c.mods {
        f64_(&mut b, m.beat);
        f64_(&mut b, m.duration);
        f64_(&mut b, m.from);
        f64_(&mut b, m.to);
        i32_(&mut b, m.proxy as i32);
        u32_(&mut b, m.order)?;
        u32_(&mut b, m.source_line)?;
        text(&mut b, &m.ease)?;
        text(&mut b, &m.name)?;
    }
    u32_(&mut b, c.per_frame.len())?;
    for f in &c.per_frame {
        f64_(&mut b, f.start);
        f64_(&mut b, f.end);
        text(&mut b, &f.function)?;
    }
    // 3. KGS2 在全部记录之后附加行号与消息；文件路径由托管端重建诊断时补入。
    u32_(&mut b, c.diagnostics.len())?;
    for (line, message) in &c.diagnostics {
        u32_(&mut b, *line)?;
        text(&mut b, message)?;
    }
    if !crate::load_limits::unlimited() && b.len() > MAX_PACKET {
        return Err("Rust parse output exceeds 512 MiB".to_string());
    }
    Ok(b)
}

// SAFETY: 调用方保证 output 与 size 是独立的可写槽；转出 Box 后，只有接收方持有释放责任。
unsafe fn publish(bytes: Vec<u8>, output: *mut *mut u8, size: *mut usize) {
    let boxed = bytes.into_boxed_slice();
    *size = boxed.len();
    *output = Box::into_raw(boxed) as *mut u8;
}

unsafe fn run_packet<F>(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
    limit: usize,
    job: F,
) -> i32
where
    F: FnOnce(&[u8]) -> Result<Vec<u8>, String>,
{
    if output.is_null() || size.is_null() {
        return -1;
    }
    // 1. 先清空有效的输出槽，再检查输入长度及地址。
    *output = std::ptr::null_mut();
    *size = 0;
    if (!crate::load_limits::unlimited() && len > limit) || len > isize::MAX as usize {
        return -2;
    }
    if len > 0 && data.is_null() {
        return -1;
    }
    let outcome = catch_unwind(AssertUnwindSafe(|| {
        let bytes = if len == 0 {
            &[][..]
        } else {
            // SAFETY: 非零长度分支已检查长度和空指针，输入实际可读范围由各解析入口的调用约定保证。
            unsafe { std::slice::from_raw_parts(data, len) }
        };
        job(bytes)
    }));
    // 2. 正常结果和错误文本均使用同一分配方式交付；返回码标识包的内容。
    match outcome {
        Ok(Ok(bytes)) => {
            publish(bytes, output, size);
            0
        }
        Ok(Err(message)) => {
            publish(message.into_bytes(), output, size);
            -3
        }
        Err(_) => {
            publish(
                b"panic inside kg_native_core parsing".to_vec(),
                output,
                size,
            );
            -4
        }
    }
}

/// 解析 UTF-8 VSC 文本并向调用方交付谱面数据。
///
/// # Arguments
///
/// - `data`：UTF-8 VSC 文本的字节地址。
/// - `len`：输入字节数。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 成功返回 0 并交付 KGS2 包；-1 表示必需指针为空，-2 表示输入长度超限，
/// -3 表示解析或编码失败，-4 表示捕获到 panic。后两种情况返回 UTF-8 消息包。
///
/// # Safety
///
/// 非零长度的输入必须位于一个连续、可读且调用期间不被修改的分配内；长度为零时 data 可为空。
/// 两个输出槽必须对齐、独占可写，彼此及与输入区域不重叠。返回非空包时，
/// 调用方须在所有读取结束后以原地址和字节数调用 [`kg_core_free_packet`] 一次，包括错误消息包。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsc(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    run_packet(data, len, output, size, MAX_INPUT, |bytes| {
        let s = std::str::from_utf8(bytes).map_err(|e| format!("Invalid VSC UTF-8: {e}"))?;
        encode_chart(&chart::parse_vsc(s)?, true)
    })
}

/// 解析 VSC1 格式的 VSB 二进制谱面并向调用方交付谱面数据。
///
/// # Arguments
///
/// - `data`：VSC1 格式的 VSB 二进制谱面的字节地址。
/// - `len`：输入字节数。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 成功返回 0 并交付 KGS2 包；-1 表示必需指针为空，-2 表示输入长度超限，
/// -3 表示解析或编码失败，-4 表示捕获到 panic。后两种情况返回 UTF-8 消息包。
///
/// # Safety
///
/// 非零长度的输入必须位于一个连续、可读且调用期间不被修改的分配内；长度为零时 data 可为空。
/// 两个输出槽必须对齐、独占可写，彼此及与输入区域不重叠。返回非空包时，
/// 调用方须在所有读取结束后以原地址和字节数调用 [`kg_core_free_packet`] 一次，包括错误消息包。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsb(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    run_packet(data, len, output, size, MAX_INPUT, |bytes| {
        encode_chart(&chart::parse_vsb(bytes)?, true)
    })
}

/// 解析 UTF-8 VSM 演出文本并向调用方交付演出数据。
///
/// # Arguments
///
/// - `data`：UTF-8 VSM 演出文本的字节地址。
/// - `len`：输入字节数。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 成功返回 0 并交付 KGS2 包；-1 表示必需指针为空，-2 表示输入长度超限，
/// -3 表示解析或编码失败，-4 表示捕获到 panic。后两种情况返回 UTF-8 消息包。
///
/// # Safety
///
/// 非零长度的输入必须位于一个连续、可读且调用期间不被修改的分配内；长度为零时 data 可为空。
/// 两个输出槽必须对齐、独占可写，彼此及与输入区域不重叠。返回非空包时，
/// 调用方须在所有读取结束后以原地址和字节数调用 [`kg_core_free_packet`] 一次，包括错误消息包。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsm(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    run_packet(data, len, output, size, MAX_VSM, |bytes| {
        let s = std::str::from_utf8(bytes).map_err(|e| format!("Invalid VSM UTF-8: {e}"))?;
        let mut c = Chart::default();
        chart::parse_vsm(s, &mut c)?;
        encode_chart(&c, false)
    })
}

/// 托管端传入的一次 BPM 变更。
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct BpmChange {
    /// 变速时刻，单位为秒；负值用于更新初始段。
    pub time: f64,
    /// 从该点生效的每分钟拍数，须为有限正值且不超过 10000。
    pub bpm: f64,
}

/// 用上一段的速度累计拍数，将同刻变速合并为一个 KGB1 锚点。
///
/// # Arguments
///
/// - `changes`：按时刻稳定排序的 BPM 变化数组，空数组可传空指针。
/// - `count`：变化记录数。
/// - `base`：初始 BPM。
/// - `offset_ms`：工程偏移毫秒数，仅校验范围，包内分段不叠加该偏移。
/// - `fixed`：固定 BPM，has_fixed 非零时使用。
/// - `has_fixed`：非零时只生成固定速度的初始段。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 成功返回 0。输出槽为空返回 -1，变化数组的指针或长度不合法返回 -2；
/// 数值或包大小校验失败返回 -3，捕获到 panic 返回 -4。后两种情况交付 UTF-8 消息包。
///
/// # Safety
///
/// count 非零时，changes 必须指向对齐、已初始化且在调用期间可读的连续数组。
/// 两个输出槽必须对齐、独占可写，彼此及与输入区域不重叠。返回非空包时，
/// 调用方须在所有读取结束后以原地址和字节数调用 [`kg_core_free_packet`] 一次，包括错误消息包。
#[no_mangle]
pub unsafe extern "C" fn kg_core_build_bpm(
    changes: *const BpmChange,
    count: usize,
    base: f64,
    offset_ms: f64,
    fixed: f64,
    has_fixed: u32,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    if output.is_null() || size.is_null() {
        return -1;
    }
    *output = std::ptr::null_mut();
    *size = 0;
    if count > isize::MAX as usize / std::mem::size_of::<BpmChange>()
        || (count > 0 && changes.is_null())
    {
        return -2;
    }
    let outcome = catch_unwind(AssertUnwindSafe(|| -> Result<Vec<u8>, String> {
        // 1. 校验初始及固定速度、偏移范围，再按调用约定借用变速记录。
        if !base.is_finite()
            || base <= 0.0
            || base > 10000.0
            || !offset_ms.is_finite()
            || offset_ms.abs() > 3_600_000.0
            || (has_fixed != 0 && (!fixed.is_finite() || fixed <= 0.0 || fixed > 10000.0))
        {
            return Err("Invalid initial BPM/offset".into());
        }
        let changes = if count == 0 {
            &[][..]
        } else {
            // SAFETY: 非零分支已检查指针及地址范围，对齐和有效记录数由调用方保证。
            std::slice::from_raw_parts(changes, count)
        };
        // 2. 输入与 Chart.Notes 一样按时间稳定排序；同刻后写入的 BPM 覆盖同一个锚点。
        let mut segments = vec![(0.0_f64, 0.0_f64, if has_fixed != 0 { fixed } else { base })];
        if has_fixed == 0 {
            for change in changes {
                if !change.bpm.is_finite()
                    || change.bpm <= 0.0
                    || change.bpm > 10000.0
                    || !change.time.is_finite()
                {
                    return Err("Invalid BPM change".into());
                }
                if change.time < 0.0 {
                    segments[0].2 = change.bpm;
                    continue;
                }
                let prev = segments[segments.len() - 1];
                let beat = prev.1 + (change.time - prev.0) * prev.2 / 60.0;
                if change.time == prev.0 {
                    let last = segments.len() - 1;
                    segments[last] = (change.time, beat, change.bpm);
                } else {
                    segments.push((change.time, beat, change.bpm));
                }
            }
        }
        // 3. 校验分段表大小，将秒、拍及 BPM 以小端 f64 写入。
        let packet_bytes = segments
            .len()
            .checked_mul(24)
            .and_then(|n| n.checked_add(8))
            .filter(|n| crate::load_limits::unlimited() || *n <= MAX_PACKET)
            .ok_or("Rust BPM output exceeds 512 MiB")?;
        let mut b = Vec::with_capacity(packet_bytes);
        b.extend_from_slice(b"KGB1");
        u32_(&mut b, segments.len())?;
        for (t, beat, bpm) in segments {
            f64_(&mut b, t);
            f64_(&mut b, beat);
            f64_(&mut b, bpm);
        }
        Ok(b)
    }));
    match outcome {
        Ok(Ok(b)) => {
            publish(b, output, size);
            0
        }
        Ok(Err(e)) => {
            publish(e.into_bytes(), output, size);
            -3
        }
        Err(_) => {
            publish(b"Rust BPM panic".to_vec(), output, size);
            -4
        }
    }
}

/// 归还谱面或 BPM 入口交付的结果包或错误消息包。
///
/// # Arguments
///
/// - `ptr`：原始数据包首地址，空指针直接返回。
/// - `len`：对应调用写入 size 的字节数。
///
/// # Safety
///
/// 非空待释放数据必须来自本模块的包分配，长度必须与原始切片完全一致；不得重复释放，也不得在释放时继续读写。
#[no_mangle]
pub unsafe extern "C" fn kg_core_free_packet(ptr: *mut u8, len: usize) {
    if !ptr.is_null() && len > 0 && len <= isize::MAX as usize {
        let slice = std::ptr::slice_from_raw_parts_mut(ptr, len);
        // SAFETY: publish 通过 Box::into_raw 转移所有权，此处按原始长度重建同一切片。
        drop(Box::from_raw(slice));
    }
}

/// 在当前线程放宽加载预算后调用 [`kg_core_parse_vsc`]。
///
/// # Arguments
///
/// - `data`：UTF-8 VSC 文本的字节地址。
/// - `len`：输入字节数。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 状态码、结果格式与 [`kg_core_parse_vsc`] 相同，长度仍受地址空间限制。
///
/// # Safety
///
/// 输入、输出槽和返回包的释放要求与 [`kg_core_parse_vsc`] 相同。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsc_unlimited(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    crate::load_limits::with_unlimited(|| kg_core_parse_vsc(data, len, output, size))
}
/// 在当前线程放宽加载预算后调用 [`kg_core_parse_vsb`]。
///
/// # Arguments
///
/// - `data`：VSC1 格式的 VSB 二进制谱面的字节地址。
/// - `len`：输入字节数。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 状态码、结果格式与 [`kg_core_parse_vsb`] 相同，长度仍受地址空间限制。
///
/// # Safety
///
/// 输入、输出槽和返回包的释放要求与 [`kg_core_parse_vsb`] 相同。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsb_unlimited(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    crate::load_limits::with_unlimited(|| kg_core_parse_vsb(data, len, output, size))
}
/// 在当前线程放宽加载预算后调用 [`kg_core_parse_vsm`]。
///
/// # Arguments
///
/// - `data`：UTF-8 VSM 演出文本的字节地址。
/// - `len`：输入字节数。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 状态码、结果格式与 [`kg_core_parse_vsm`] 相同，长度仍受地址空间限制。
///
/// # Safety
///
/// 输入、输出槽和返回包的释放要求与 [`kg_core_parse_vsm`] 相同。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsm_unlimited(
    data: *const u8,
    len: usize,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    crate::load_limits::with_unlimited(|| kg_core_parse_vsm(data, len, output, size))
}
/// 放宽当前线程的数据包预算后构建 BPM 分段。
///
/// # Arguments
///
/// - `changes`：按时刻稳定排序的 BPM 变化数组，空数组可传空指针。
/// - `count`：变化记录数。
/// - `base`：初始 BPM。
/// - `offset_ms`：工程偏移毫秒数，仅校验范围，包内分段不叠加该偏移。
/// - `fixed`：固定 BPM，has_fixed 非零时使用。
/// - `has_fixed`：非零时只生成固定速度的初始段。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 返回码和 KGB1 格式与 [`kg_core_build_bpm`] 相同。
///
/// # Safety
///
/// 输入、输出槽及数据包所有权遵循 [`kg_core_build_bpm`] 的约定。
#[no_mangle]
pub unsafe extern "C" fn kg_core_build_bpm_unlimited(
    changes: *const BpmChange,
    count: usize,
    base: f64,
    offset_ms: f64,
    fixed: f64,
    has_fixed: u32,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    crate::load_limits::with_unlimited(|| {
        kg_core_build_bpm(
            changes, count, base, offset_ms, fixed, has_fixed, output, size,
        )
    })
}

/// 按实际重复情况为 VSC 选择周期包、引用表或普通包。
///
/// # Arguments
///
/// - `data`：UTF-8 VSC 文本的字节地址。
/// - `len`：输入字节数。
/// - `unlimited`：非零时在当前调用放宽加载预算。
/// - `output`：用于接收 Rust 分配的数据包首地址。
/// - `size`：用于接收数据包字节数，释放时须保留原值。
///
/// # Returns
///
/// 成功返回 0 并交付 KGR2 周期包、KGR1 引用包或 KGS2 普通包；-1 表示必需指针为空，-2 表示输入长度超限，
/// -3 表示解析或编码失败，-4 表示捕获到 panic。后两种情况返回 UTF-8 消息包。
///
/// # Safety
///
/// 非零长度的输入必须位于一个连续、可读且调用期间不被修改的分配内；长度为零时 data 可为空。
/// 两个输出槽必须对齐、独占可写，彼此及与输入区域不重叠。返回非空包时，
/// 调用方须在所有读取结束后以原地址和字节数调用 [`kg_core_free_packet`] 一次，包括错误消息包。
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsc_compact(
    data: *const u8,
    len: usize,
    unlimited: u8,
    output: *mut *mut u8,
    size: *mut usize,
) -> i32 {
    let work = || {
        run_packet(data, len, output, size, MAX_INPUT, |bytes| {
            let s = std::str::from_utf8(bytes).map_err(|e| format!("Invalid VSC UTF-8: {e}"))?;
            if let Some((chart, groups, total)) = chart::parse_periodic_vsc(s) {
                // 1. 首先尝试周期表示；常规模式仍按展开后的引用数检查预算。
                if !crate::load_limits::unlimited() && total.saturating_mul(4) > MAX_PACKET {
                    return Err("Rust parse output exceeds 512 MiB".into());
                }
                let mut packet = b"KGR2".to_vec();
                packet.extend_from_slice(&encode_chart(&chart, true)?);
                u32_(&mut packet, total)?;
                u32_(&mut packet, groups.len())?;
                for (start, count, repeat) in groups {
                    u32_(&mut packet, start)?;
                    u32_(&mut packet, count)?;
                    u32_(&mut packet, repeat)?;
                }
                return Ok(packet);
            }
            // 2. 采样前 4096 行估计重复程度，重复较少时直接编码为普通 KGS2。
            let mut seen = std::collections::HashSet::new();
            let mut sampled = 0usize;
            let mut repeats = 0usize;
            for raw in s.trim_start_matches('\u{feff}').lines().take(4096) {
                let row = raw.trim();
                if row.is_empty() || row.starts_with('#') || row.starts_with("//") {
                    continue;
                }
                sampled += 1;
                if row.len() <= 1024 && !seen.insert(row) {
                    repeats += 1;
                }
            }
            if repeats * 8 <= sampled {
                return encode_chart(&chart::parse_vsc(s)?, true);
            }
            // 3. 对重复较多的输入写入记录表，并追加每次出现的稳定索引。
            let (chart, references) = chart::parse_vsc_records(s, true)?;
            let records = encode_chart(&chart, true)?;
            let length = 8usize
                .checked_add(records.len())
                .and_then(|n| n.checked_add(references.len().checked_mul(4)?))
                .ok_or("compact packet overflow")?;
            if !crate::load_limits::unlimited() && length > MAX_PACKET {
                return Err("Rust parse output exceeds 512 MiB".into());
            }
            let mut packet = Vec::with_capacity(length);
            packet.extend_from_slice(b"KGR1");
            packet.extend_from_slice(&records);
            u32_(&mut packet, references.len())?;
            for index in references {
                packet.extend_from_slice(&index.to_le_bytes());
            }
            Ok(packet)
        })
    };
    if unlimited != 0 {
        crate::load_limits::with_unlimited(work)
    } else {
        work()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn wire_roundtrip_shape() {
        let c = chart::parse_vsc("1000,0,1\n1200,2,2,1700\n1800,3,0,b:190\n").unwrap();
        let b = encode_chart(&c, true).unwrap();
        assert_eq!(&b[..4], b"KGS2");
        assert_eq!(c.notes.len(), 3);
        assert!(c.notes[0].time <= c.notes[1].time);
    }
    #[test]
    fn vsc_ignored_timing_anchors_are_informational() {
        let input = "0,3,0,b:150|v:12|s:8\n1000,0,1\n2000,3,0,b:180|v:undefined|s:4\n";
        let c = chart::parse_vsc(input).unwrap();
        assert!(c.warnings.is_empty(), "valid v/s must not force fallback");
        assert_eq!(c.notes.len(), 3);
        assert_eq!(
            c.diagnostics,
            vec![
                (1, "VSC timing 'v' is not applied.".to_string()),
                (1, "VSC timing 's' is not applied.".to_string()),
                (3, "VSC timing 's' is not applied.".to_string()),
            ]
        );
        let packed = encode_chart(&c, true).expect("informational diagnostics can be packed");
        assert_eq!(&packed[..4], b"KGS2");
        assert!(packed
            .windows(b"VSC timing 's' is not applied.".len())
            .any(|w| w == b"VSC timing 's' is not applied."));
    }
    #[test]
    fn malformed_vsc_still_falls_back_to_csharp_diagnostics() {
        let c = chart::parse_vsc("0,3,0,b:150|v:5\nbad,row\n").unwrap();
        assert!(!c.warnings.is_empty());
        assert!(encode_chart(&c, true).is_err());
    }

    #[test]
    fn vsc_csm340_modextra_and_stable_order() {
        let c = chart::parse_vsc("1000,0,1,,flag:yes\n1000,1,0,,extra:undefined\n").unwrap();
        assert_eq!(c.notes.len(), 2);
        assert_eq!(c.notes[0].kind, 0);
        assert!(c.csm_340);
        assert_eq!(
            c.notes[0].mod_extra.get("flag"),
            Some(&Some("yes".to_string()))
        );
        assert_eq!(c.notes[1].mod_extra.get("extra"), Some(&None));
    }
    #[test]
    fn vsm_ranges_and_metadata() {
        let mut c = Chart::default();
        chart::parse_vsm(
            "!obj:obj_custom_gimmick\n!proxies:2\n0:2:1,1,linear,0,1,prx,-1\nmpf\n0,4,foo\n",
            &mut c,
        )
        .unwrap();
        assert_eq!(c.mods.len(), 3);
        assert_eq!(c.mods[1].beat, 1.0);
        assert_eq!(c.mods[0].source_line, 3);
        assert_eq!(c.per_frame.len(), 1);
        assert_eq!(c.proxies, 2);
    }
    #[test]
    fn vsm_trailing_comments_preserve_values_and_pack_warning() {
        let mut c = Chart::default();
        chart::parse_vsm("!obj:obj_custom_gimmick // object\n!proxies:2 // count\n0,0,linear,0,1,prx,-1 // event\nmpf // marker\n0,4,foo // function\n", &mut c).unwrap();
        assert!(
            c.warnings.is_empty(),
            "valid inline comments must execute in Rust"
        );
        assert_eq!(c.object_name, "obj_custom_gimmick");
        assert_eq!(c.proxies, 2);
        assert_eq!(c.mods.len(), 1);
        assert_eq!(c.mods[0].source_line, 3);
        assert_eq!(c.per_frame[0].function, "foo");
        assert_eq!(c.diagnostics.len(), 1);
        assert_eq!(c.diagnostics[0].0, 1);
        assert!(encode_chart(&c, false).is_ok());
    }
    #[test]
    fn binary_vsb_fixture() {
        let bytes = include_bytes!("../tests/fixtures/demo.vsb");
        let c = chart::parse_vsb(bytes).unwrap();
        assert!(!c.notes.is_empty());
    }
    #[test]
    fn bpm_packet() {
        let c = [
            BpmChange {
                time: 2.0,
                bpm: 240.0,
            },
            BpmChange {
                time: 4.0,
                bpm: 120.0,
            },
        ];
        let mut ptr = std::ptr::null_mut();
        let mut len = 0;
        // SAFETY: 本测试传入两个有效变速点以及独立的指针和长度输出槽。
        let rc =
            unsafe { kg_core_build_bpm(c.as_ptr(), 2, 120.0, 0.0, 0.0, 0, &mut ptr, &mut len) };
        assert_eq!(rc, 0);
        // SAFETY: 已断言调用成功，ptr 与 len 来自同一包，读取在释放之前完成。
        let s = unsafe { std::slice::from_raw_parts(ptr, len) };
        assert_eq!(&s[..4], b"KGB1");
        assert_eq!(u32::from_le_bytes(s[4..8].try_into().unwrap()), 3);
        // SAFETY: 此包尚未释放，传回的是创建入口给出的原始地址与长度。
        unsafe { kg_core_free_packet(ptr, len) };
    }
}
