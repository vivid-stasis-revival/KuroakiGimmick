//! RustBridge 原生计算入口与音符运动 ABI。
//!
//! 托管端提供当前帧的轨道采样和候选音符，Rust 计算头尾位置；谱面解析、PCM 解码和轨道快照分别由子模块实现。

use std::panic::{catch_unwind, AssertUnwindSafe};
mod audio;
mod chart;
mod load_limits;
mod source_ffi;
mod timeline_runtime;

/// 运动 ABI 的主版本占高 16 位，次版本占低 16 位；改变现有结构布局或入口签名时须升级主版本。
const ABI_VERSION: u32 = 0x0001_0000;

/// 一个批次共用的歌曲时刻与运动控制量。
#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct MotionParams {
    /// 当前歌曲拍数，可为负。
    pub beat: f64,
    /// 叠加到判定线纵坐标的逻辑像素偏移。
    pub alignment: f64,
    /// 纵向运动的 driven 轨道值。
    pub driven: f64,
    /// 当前每秒拍数。
    pub beats_per_second: f64,
    /// 纵向波动的 wave 轨道值。
    pub wave: f64,
    /// 按拍驱动横移的 beat 轨道值。
    pub beat_motion: f64,
    /// 非零时使用 Custom 的 boost 公式。
    pub custom: u32,
    /// ABI 保留位，托管端填 0。
    pub reserved: u32,
}

/// 单个音符的时间差及所属轨道的运动参数。
#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct MotionInput {
    /// 音符起点减去滚动时刻的毫秒数。
    pub distance: f64,
    /// 音符终点减去滚动时刻的毫秒数。
    pub end_distance: f64,
    /// 已合并全局与逐轨控制的滚动倍率。
    pub scroll: f64,
    /// 基础横向偏移，单位为逻辑像素。
    pub x_offset: f64,
    /// 纵向公式从时间差中扣除的偏移，单位为毫秒。
    pub y_offset: f64,
    /// boost 过渡的时间阈值，单位为毫秒。
    pub boost_time: f64,
    /// boost 作用于纵坐标的位移量。
    pub boost_distance: f64,
}

/// 在逻辑画布上求得的音符横移及头尾位置。
#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct MotionOutput {
    /// 相对于轨道中心的横向偏移。
    pub x_offset: f64,
    /// 音符起点的纵坐标。
    pub y: f64,
    /// 音符终点的纵坐标。
    pub end_y: f64,
}

/// 查询音符运动接口的 ABI 版本。
///
/// # Returns
///
/// 当前为 `0x0001_0000`（主版本 1，次版本 0）。
#[no_mangle]
pub extern "C" fn kg_core_abi_version() -> u32 {
    ABI_VERSION
}

#[inline]
fn x_from_values(beat_at_time: f64, distance: f64, x_offset: f64, beat_motion: f64) -> f64 {
    if beat_motion == 0.0 && beat_at_time.is_finite() && distance.is_finite() {
        return x_offset;
    }
    // COMPAT: 使用与 C# 相同的带符号余数，以保留负拍数的相位。
    let beat = (beat_at_time + 0.3) % 2.0;
    let f = beat % 1.0;
    let mut amount = 0.0;
    if f < 0.3 {
        amount = (f / 0.3).powf(2.0);
    } else if f < 0.7 {
        amount = 1.0 - ((f - 0.3) / 0.4).powf(2.0);
    }
    x_offset
        + beat_motion / 100.0
            * 11.0
            * amount
            * (if beat < 1.0 { 1.0 } else { -1.0 })
            * (distance / 60.0 + std::f64::consts::PI / 2.0).sin()
}

#[inline]
fn y_from_values(p: &MotionParams, distance: f64, n: &MotionInput) -> f64 {
    let mut y = p.alignment + 144.0
        - (((distance - n.y_offset) * 0.1) + p.driven * p.beats_per_second * 22.0) * n.scroll
        - if p.wave == 0.0 && distance.is_finite() {
            0.0
        } else {
            p.wave * 0.2 * (distance * 0.006578947368421052).sin()
        };

    if p.custom != 0 {
        // COMPAT: 全局 boost_distance 为零时，由调用方屏蔽逐轨的 boost_distance。
        if n.boost_distance != 0.0 {
            y += -n.boost_distance
                + if distance < n.boost_time && n.boost_time > 0.0 {
                    n.boost_distance * ((n.boost_time - distance) / n.boost_time).powf(3.0)
                } else {
                    0.0
                };
        }
    } else if n.boost_time == 0.0 {
        y -= n.boost_distance;
    } else {
        // COMPAT: clamp 保留 NaN；零值沿用原有下界运算，规范为正零。
        let ratio = (n.boost_time - distance) / n.boost_time;
        let clamped = if ratio == 0.0 {
            0.0
        } else {
            ratio.clamp(0.0, 1.0)
        };
        y += -n.boost_distance + n.boost_distance * clamped.powf(3.0);
    }
    y
}

#[inline]
fn evaluate(p: &MotionParams, input: &MotionInput) -> MotionOutput {
    MotionOutput {
        x_offset: x_from_values(p.beat, input.distance, input.x_offset, p.beat_motion),
        y: y_from_values(p, input.distance, input),
        end_y: y_from_values(p, input.end_distance, input),
    }
}

/// 按输入顺序计算一批音符的运动结果。
///
/// # Arguments
///
/// - `params`：共用的帧参数，即使空批次也需要有效引用。
/// - `inputs`：已求出时间差的音符数组。
/// - `count`：输入和输出的元素数。
/// - `outputs`：接收对应位置的结果数组。
///
/// # Returns
///
/// 成功返回 0；必需指针为空返回 -1，长度不可表示返回 -2，捕获到 panic 返回 -3。
///
/// # Safety
///
/// 调用期间，所访问的每段数组均须位于同一有效分配内，正确对齐，按声明长度初始化。
/// 输入须保持可读且不被修改；输出须独占可写，不能与输入或参数结构重叠。
/// `count == 0` 时两个数组可以为空。
#[no_mangle]
pub unsafe extern "C" fn kg_core_motion_batch(
    params: *const MotionParams,
    inputs: *const MotionInput,
    count: usize,
    outputs: *mut MotionOutput,
) -> i32 {
    // 1. 检查必需指针及长度是否能表示为 Rust 切片。
    if params.is_null() || (count > 0 && (inputs.is_null() || outputs.is_null())) {
        return -1;
    }
    if count > isize::MAX as usize / std::mem::size_of::<MotionInput>()
        || count > isize::MAX as usize / std::mem::size_of::<MotionOutput>()
    {
        return -2;
    }
    // 2. 以调用方借出的帧参数和缓冲区逐项计算，结果写回相同下标。
    let operation = || {
        // SAFETY: 调用约定保证 params 对齐且在本次计算期间指向已初始化的帧参数。
        let p = unsafe { &*params };
        if count == 0 {
            return;
        }
        // SAFETY: 调用方保证 inputs 覆盖 count 个已初始化元素，计算期间保持只读。
        let in_slice = unsafe { std::slice::from_raw_parts(inputs, count) };
        // SAFETY: outputs 指向相同数量的已初始化结果槽，调用期间独占且与输入分离。
        let out_slice = unsafe { std::slice::from_raw_parts_mut(outputs, count) };
        for (i, o) in in_slice.iter().zip(out_slice.iter_mut()) {
            *o = evaluate(p, i);
        }
    };
    match catch_unwind(AssertUnwindSafe(operation)) {
        Ok(()) => 0,
        Err(_) => -3,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn abi_layout() {
        assert_eq!(std::mem::size_of::<MotionParams>(), 56);
        assert_eq!(std::mem::size_of::<MotionInput>(), 56);
        assert_eq!(std::mem::size_of::<MotionOutput>(), 24);
        assert_eq!(kg_core_abi_version(), 0x0001_0000);
    }
    #[test]
    fn batch_and_edge_cases() {
        let p = MotionParams {
            beat: 8.5,
            alignment: 3.0,
            driven: 0.0,
            beats_per_second: 2.0,
            wave: 0.0,
            beat_motion: 0.0,
            custom: 0,
            reserved: 0,
        };
        let a = [
            MotionInput {
                distance: 0.0,
                end_distance: 100.0,
                scroll: 1.0,
                x_offset: 13.0,
                y_offset: 0.0,
                boost_time: 0.0,
                boost_distance: 0.0,
            },
            MotionInput {
                distance: -100.0,
                end_distance: 500.0,
                scroll: 0.0,
                x_offset: -5.0,
                y_offset: 0.0,
                boost_time: 20.0,
                boost_distance: 40.0,
            },
        ];
        let mut result = [MotionOutput::default(); 2];
        assert_eq!(
            // SAFETY: 测试输入与默认初始化的输出数组长度一致且独立存放。
            unsafe { kg_core_motion_batch(&p, a.as_ptr(), a.len(), result.as_mut_ptr()) },
            0
        );
        assert!((result[0].y - 147.0).abs() < 1e-10);
        assert!((result[0].end_y - 137.0).abs() < 1e-10);
        assert_eq!(result[0].x_offset, 13.0);
        assert!((result[1].y - 147.0).abs() < 1e-10);
        assert!((result[1].end_y - 107.0).abs() < 1e-10);
        assert_eq!(
            // SAFETY: 空参数触发 -1 分支，入口在解引用前返回。
            unsafe { kg_core_motion_batch(std::ptr::null(), a.as_ptr(), 2, result.as_mut_ptr()) },
            -1
        );
        assert_eq!(
            // SAFETY: 帧参数有效，零长度分支不会借用空数组。
            unsafe { kg_core_motion_batch(&p, std::ptr::null(), 0, std::ptr::null_mut()) },
            0
        );
    }
    #[test]
    fn custom_boost_and_negative_beat() {
        let p = MotionParams {
            beat: -1.5,
            alignment: -4.0,
            driven: 0.0,
            beats_per_second: 3.0,
            wave: 0.0,
            beat_motion: 2.0,
            custom: 1,
            reserved: 0,
        };
        let n = MotionInput {
            distance: -150.0,
            end_distance: 100.0,
            scroll: 0.0,
            x_offset: 0.0,
            y_offset: 0.0,
            boost_time: 200.0,
            boost_distance: 80.0,
        };
        let r = evaluate(&p, &n);
        assert!(r.x_offset.is_finite());
        assert!(
            (r.y - (-80.0 + 80.0 * ((200.0_f64 + 150.0) / 200.0).powf(3.0) + 140.0)).abs() < 1e-9
        );
    }
}

/// 会话内稳定的音符记录，供逐帧候选索引引用。
#[repr(C)]
#[derive(Clone, Copy)]
pub struct NoteRuntimeData {
    /// 音符起始时刻，单位为秒。
    pub time: f64,
    /// 音符结束时刻，单位为秒。
    pub end: f64,
    /// 选择 lanes 参数数组的零基索引。
    pub lane: u32,
    /// 与 C# 顺序布局对应的保留位。
    pub reserved: u32,
}

/// 通过会话音符表和逐轨参数，只计算索引列出的候选项。
///
/// # Arguments
///
/// - `params`：当前帧参数。
/// - `notes`：稳定的会话音符表。
/// - `note_count`：音符表元素数。
/// - `indices`：待计算的音符索引，允许重复。
/// - `count`：候选数，也是输出元素数。
/// - `lanes`：按轨道索引的运动参数，时间差由音符时刻重新计算。
/// - `lane_count`：轨道参数数目。
/// - `scroll_time`：当前滚动时刻，单位为秒。
/// - `outputs`：依候选顺序写入的结果。
///
/// # Returns
///
/// 成功返回 0；必需指针为空返回 -1，长度或索引越界返回 -2，捕获到 panic 返回 -3。
///
/// # Safety
///
/// 调用期间，所访问的每段数组均须位于同一有效分配内，正确对齐，按声明长度初始化。
/// 输入须保持可读且不被修改；输出须独占可写，不能与输入或参数结构重叠。
/// 空批次允许数组指针为空；非空批次中即使音符表或轨道表长度为零，其指针仍须非空且对齐。
#[no_mangle]
pub unsafe extern "C" fn kg_core_motion_indexed(
    params: *const MotionParams,
    notes: *const NoteRuntimeData,
    note_count: usize,
    indices: *const u32,
    count: usize,
    lanes: *const MotionInput,
    lane_count: usize,
    scroll_time: f64,
    outputs: *mut MotionOutput,
) -> i32 {
    if params.is_null()
        || (count > 0
            && (notes.is_null() || indices.is_null() || lanes.is_null() || outputs.is_null()))
    {
        return -1;
    }
    if note_count > isize::MAX as usize / std::mem::size_of::<NoteRuntimeData>()
        || count > isize::MAX as usize / std::mem::size_of::<MotionOutput>()
        || count > isize::MAX as usize / std::mem::size_of::<u32>()
        || lane_count > isize::MAX as usize / std::mem::size_of::<MotionInput>()
    {
        return -2;
    }
    if count == 0 {
        return 0;
    }
    catch_unwind(AssertUnwindSafe(|| {
        // 1. 按已检查的长度借用数据。调用方保证输入对齐、已初始化且输出区独占。
        let p = &*params;
        let notes = std::slice::from_raw_parts(notes, note_count);
        let indices = std::slice::from_raw_parts(indices, count);
        let lanes = std::slice::from_raw_parts(lanes, lane_count);
        let output = std::slice::from_raw_parts_mut(outputs, count);
        // 2. 写入前校验全部音符与轨道索引，越界时托管端可对整批执行回退计算。
        for &i in indices {
            if i as usize >= notes.len() || notes[i as usize].lane as usize >= lanes.len() {
                return -2;
            }
        }
        // 3. 从歌曲秒数换算头尾时间差，沿用非索引入口的运动公式。
        for (&i, o) in indices.iter().zip(output.iter_mut()) {
            let n = notes[i as usize];
            let l = &lanes[n.lane as usize];
            let input = MotionInput {
                distance: (n.time - scroll_time) * 1000.0,
                end_distance: (n.end - scroll_time) * 1000.0,
                scroll: l.scroll,
                x_offset: l.x_offset,
                y_offset: l.y_offset,
                boost_time: l.boost_time,
                boost_distance: l.boost_distance,
            };
            *o = evaluate(p, &input);
        }
        0
    }))
    .unwrap_or(-3)
}

#[cfg(test)]
mod indexed_tests {
    use super::*;
    #[test]
    fn indexed_matches_legacy_and_rejects_bad_indices() {
        assert_eq!(std::mem::size_of::<NoteRuntimeData>(), 24);
        let p = MotionParams {
            beat: 8.5,
            alignment: 3.0,
            driven: 2.0,
            beats_per_second: 2.0,
            wave: 23.0,
            beat_motion: 12.0,
            custom: 1,
            reserved: 0,
        };
        let lanes = [MotionInput {
            distance: 0.0,
            end_distance: 0.0,
            scroll: -1.2,
            x_offset: 7.0,
            y_offset: 12.0,
            boost_time: 500.0,
            boost_distance: 50.0,
        }];
        let notes = [
            NoteRuntimeData {
                time: -0.2,
                end: 4.0,
                lane: 0,
                reserved: 0,
            },
            NoteRuntimeData {
                time: 2.0,
                end: 2.0,
                lane: 0,
                reserved: 0,
            },
        ];
        let ids = [1u32, 0u32, 1u32];
        let mut out = [MotionOutput::default(); 3];
        assert_eq!(
            // SAFETY: 各指针来自本测试持有的独立数组，输出有三个已初始化槽位。
            unsafe {
                kg_core_motion_indexed(
                    &p,
                    notes.as_ptr(),
                    notes.len(),
                    ids.as_ptr(),
                    ids.len(),
                    lanes.as_ptr(),
                    1,
                    1.25,
                    out.as_mut_ptr(),
                )
            },
            0
        );
        for (id, actual) in ids.iter().zip(out) {
            let n = notes[*id as usize];
            let l = &lanes[0];
            let expected = evaluate(
                &p,
                &MotionInput {
                    distance: (n.time - 1.25) * 1000.0,
                    end_distance: (n.end - 1.25) * 1000.0,
                    ..*l
                },
            );
            assert_eq!(actual.x_offset, expected.x_offset);
            assert_eq!(actual.y, expected.y);
            assert_eq!(actual.end_y, expected.end_y);
        }
        let bad = [2u32];
        assert_eq!(
            // SAFETY: 索引值故意越界，但所有数组指针和长度均有效；内核先验证索引再写入。
            unsafe {
                kg_core_motion_indexed(
                    &p,
                    notes.as_ptr(),
                    notes.len(),
                    bad.as_ptr(),
                    1,
                    lanes.as_ptr(),
                    1,
                    0.0,
                    out.as_mut_ptr(),
                )
            },
            -2
        );
    }
}
