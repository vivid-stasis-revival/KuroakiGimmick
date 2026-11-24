//! 按帧批量求值的只读轨道快照。
//!
//! C# 先处理回调、哨兵与对象规则，再传入轨道描述和连续片段。Rust 复制输入，在每帧按轨道顺序写出采样结果。

use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};

/// 轨道快照接口的 ABI 版本，独立于音符运动 ABI。
pub const TRACK_ABI: u32 = 0x0003_0000;
const MAX_TRACKS: usize = 100_000;
const MAX_SEGMENTS: usize = 4_000_000;
const EASE_COUNT: u32 = 31;

/// 一条轨道在共享片段数组中的范围与初值。
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct TrackSpec {
    /// 首个片段的元素索引。
    pub offset: u32,
    /// 该轨道连续片段的数量。
    pub count: u32,
    /// 首段生效之前，或轨道为空时返回的值。
    pub initial: f64,
}

/// 一个按绝对歌曲时刻定位的缓动片段。
#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct TrackSegment {
    /// 片段开始的歌曲秒数，同轨内按非降序排列。
    pub time: f64,
    /// 持续秒数，非正时直接使用终值。
    pub duration: f64,
    /// 缓动起值。
    pub from: f64,
    /// 缓动终值。
    pub to: f64,
    /// 与 C# EaseNames 共享的编号，范围为 0 至 30。
    pub ease: u32,
    /// 创建时要求为零的 ABI 保留位。
    pub reserved: u32,
}

struct Runtime {
    tracks: Vec<TrackSpec>,
    segments: Vec<TrackSegment>,
}

#[inline]
fn bounce(mut x: f64) -> f64 {
    if x < 1.0 / 2.75 {
        7.5625 * x * x
    } else if x < 2.0 / 2.75 {
        x -= 1.5 / 2.75;
        7.5625 * x * x + 0.75
    } else if x < 2.5 / 2.75 {
        x -= 2.25 / 2.75;
        7.5625 * x * x + 0.9375
    } else {
        x -= 2.625 / 2.75;
        7.5625 * x * x + 0.984375
    }
}
#[inline]
fn in_out(x: f64, power: i32) -> f64 {
    if x < 0.5 {
        (2.0 * x).powi(power) / 2.0
    } else {
        1.0 - (2.0 - 2.0 * x).powi(power) / 2.0
    }
}
/// 缓动编号与 RustTimelineBridge.EaseNames 的排列顺序一致。
fn ease(id: u32, t: f64) -> f64 {
    // COMPAT: 保留 NaN，并沿用原有下界运算对负零的处理。
    let t = t.clamp(0.0, 1.0);
    if t == 0.0 {
        return 0.0;
    }
    if t == 1.0 {
        return t;
    }
    const BACK: f64 = 1.70158;
    use std::f64::consts::PI;
    match id {
        0 => t,                                 // linear
        1 => 1.0 - (t * PI / 2.0).cos(),        // inSine
        2 => (t * PI / 2.0).sin(),              // outSine
        3 => (1.0 - (PI * t).cos()) / 2.0,      // inOutSine
        4 => t * t,                             // inQuad
        5 => 1.0 - (1.0 - t).powi(2),           // outQuad
        6 => in_out(t, 2),                      // inOutQuad
        7 => t * t * t,                         // inCubic
        8 => 1.0 - (1.0 - t).powi(3),           // outCubic
        9 => in_out(t, 3),                      // inOutCubic
        10 => t.powi(4),                        // inQuart
        11 => 1.0 - (1.0 - t).powi(4),          // outQuart
        12 => in_out(t, 4),                     // inOutQuart
        13 => t.powi(5),                        // inQuint
        14 => 1.0 - (1.0 - t).powi(5),          // outQuint
        15 => in_out(t, 5),                     // inOutQuint
        16 => (2.0_f64).powf(10.0 * (t - 1.0)), // inExpo
        17 => 1.0 - (2.0_f64).powf(-10.0 * t),  // outExpo
        18 => {
            if t < 0.5 {
                (2.0_f64).powf(20.0 * t - 10.0) / 2.0
            } else {
                (2.0 - (2.0_f64).powf(-20.0 * t + 10.0)) / 2.0
            }
        }
        19 => 1.0 - (1.0 - t * t).sqrt(),       // inCirc
        20 => (1.0 - (t - 1.0).powi(2)).sqrt(), // outCirc
        21 => {
            if t < 0.5 {
                (1.0 - (1.0 - 4.0 * t * t).sqrt()) / 2.0
            } else {
                ((1.0 - (-2.0 * t + 2.0).powi(2)).sqrt() + 1.0) / 2.0
            }
        }
        22 => (BACK + 1.0) * t * t * t - BACK * t * t, // inBack
        23 => 1.0 + (BACK + 1.0) * (t - 1.0).powi(3) + BACK * (t - 1.0).powi(2),
        24 => {
            let b = BACK * 1.525;
            if t < 0.5 {
                (2.0 * t).powi(2) * ((b + 1.0) * 2.0 * t - b) / 2.0
            } else {
                ((2.0 * t - 2.0).powi(2) * ((b + 1.0) * (t * 2.0 - 2.0) + b) + 2.0) / 2.0
            }
        }
        25 => -(2.0_f64).powf(10.0 * t - 10.0) * ((t * 10.0 - 10.75) * 2.0 * PI / 3.0).sin(),
        26 => (2.0_f64).powf(-10.0 * t) * ((t * 10.0 - 0.75) * 2.0 * PI / 3.0).sin() + 1.0,
        27 => {
            if t < 0.5 {
                -(2.0_f64).powf(20.0 * t - 10.0) * ((20.0 * t - 11.125) * 2.0 * PI / 4.5).sin()
                    / 2.0
            } else {
                (2.0_f64).powf(-20.0 * t + 10.0) * ((20.0 * t - 11.125) * 2.0 * PI / 4.5).sin()
                    / 2.0
                    + 1.0
            }
        }
        28 => 1.0 - bounce(1.0 - t), // inBounce
        29 => bounce(t),             // outBounce
        30 => {
            if t < 0.5 {
                (1.0 - bounce(1.0 - 2.0 * t)) / 2.0
            } else {
                (1.0 + bounce(2.0 * t - 1.0)) / 2.0
            }
        }
        _ => f64::NAN, // 创建快照时已校验编号
    }
}

fn eval_track(spec: &TrackSpec, segments: &[TrackSegment], t: f64) -> f64 {
    let a = spec.offset as usize;
    let end = a + spec.count as usize;
    let track = &segments[a..end];
    // 按起点取最后一个不晚于 t 的片段，同刻采用后声明项；t 为 NaN 时与 C# 一样找不到匹配项。
    let mut lo = 0usize;
    let mut hi = track.len();
    while lo < hi {
        let mid = lo + (hi - lo) / 2;
        if track[mid].time <= t {
            lo = mid + 1;
        } else {
            hi = mid;
        }
    }
    if lo == 0 {
        return spec.initial;
    }
    let s = &track[lo - 1];
    if s.duration <= 0.0 {
        s.to
    } else {
        s.from + (s.to - s.from) * ease(s.ease, (t - s.time) / s.duration)
    }
}

/// 查询轨道创建与求值接口的版本。
///
/// # Returns
///
/// 返回 [`TRACK_ABI`]。
#[no_mangle]
pub extern "C" fn kg_core_tracks_abi_version() -> u32 {
    TRACK_ABI
}

/// 校验轨道范围并复制片段，创建与输入数组独立的只读快照。
///
/// # Arguments
///
/// - `specs`：轨道描述数组。
/// - `tracks`：描述元素数，最多 100000。
/// - `segments`：所有轨道共用的连续片段数组。
/// - `count`：片段总数，最多 4000000。
/// - `output`：成功时接收不透明句柄。
///
/// # Returns
///
/// 0 表示成功；-1 表示必需指针为空，-2 表示长度超限，-3 表示捕获到 panic。
/// -4 表示轨道范围越界，-5 表示缓动编号或保留位无效，-6 表示片段时间逆序。
///
/// # Safety
///
/// 非空输入数组须对齐、已初始化且在调用期间保持可读，每段数组均在单个分配内。
/// output 须独占可写、正确对齐且与输入不重叠。仅在成功时接管句柄，最后交由 [`kg_core_tracks_free`] 释放一次。
#[no_mangle]
pub unsafe extern "C" fn kg_core_tracks_create(
    specs: *const TrackSpec,
    tracks: usize,
    segments: *const TrackSegment,
    count: usize,
    output: *mut *mut c_void,
) -> i32 {
    // 1. 在借用原始数组前检查指针、数量上限和地址空间。
    if output.is_null() || (tracks > 0 && specs.is_null()) || (count > 0 && segments.is_null()) {
        return -1;
    }
    if tracks > MAX_TRACKS
        || count > MAX_SEGMENTS
        || tracks > isize::MAX as usize / std::mem::size_of::<TrackSpec>()
        || count > isize::MAX as usize / std::mem::size_of::<TrackSegment>()
    {
        return -2;
    }
    catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: 已排除空地址，调用方为句柄提供了对齐且独占可写的槽。
        unsafe {
            *output = std::ptr::null_mut();
        }
        let desc = if tracks > 0 {
            // SAFETY: specs 只在 tracks 非零时借用，对齐、元素数及生存期由创建契约保证。
            unsafe { std::slice::from_raw_parts(specs, tracks) }
        } else {
            &[]
        };
        let items = if count > 0 {
            // SAFETY: 片段地址与非零数量已经检查，调用方保证对应区域在复制期间可读。
            unsafe { std::slice::from_raw_parts(segments, count) }
        } else {
            &[]
        };
        // 2. 验证各轨道的子区间、缓动编号及时间顺序。
        for s in desc {
            let offset = s.offset as usize;
            let len = s.count as usize;
            if offset > count || len > count - offset {
                return -4;
            }
            let slice = &items[offset..offset + len];
            if slice
                .iter()
                .any(|seg| seg.ease >= EASE_COUNT || seg.reserved != 0)
            {
                return -5;
            }
            if slice.windows(2).any(|pair| pair[0].time > pair[1].time) {
                return -6;
            }
        }
        // 3. 复制两张表后交付快照，调用方随即可以解除输入数组的固定。
        let obj = Box::new(Runtime {
            tracks: desc.to_vec(),
            segments: items.to_vec(),
        });
        // SAFETY: 结果槽仍有效，Box 的唯一所有权转移给句柄持有者。
        unsafe {
            *output = Box::into_raw(obj).cast::<c_void>();
        }
        0
    }))
    .unwrap_or(-3)
}

/// 在同一歌曲时刻采样句柄内的全部轨道。
///
/// # Arguments
///
/// - `handle`：创建成功且仍存活的轨道句柄。
/// - `t`：查询的歌曲秒数。
/// - `output`：接收每条轨道的 f64 值，排列与 specs 相同。
/// - `count`：输出元素数，必须等于创建时的轨道数。
///
/// # Returns
///
/// 成功返回 0；-1 表示缺少必需指针，-2 表示长度超限，-3 表示捕获到 panic，-4 表示轨道数不符。
///
/// # Safety
///
/// 句柄须来自 [`kg_core_tracks_create`] 且不能在采样期间释放；非空输出必须是连续、对齐、已初始化且独占可写的 f64 数组。
/// 多个线程同时采样时，各自的输出区不得重叠。
#[no_mangle]
pub unsafe extern "C" fn kg_core_tracks_eval(
    handle: *const c_void,
    t: f64,
    output: *mut f64,
    count: usize,
) -> i32 {
    if handle.is_null() || (count > 0 && output.is_null()) {
        return -1;
    }
    if count > MAX_TRACKS || count > isize::MAX as usize / 8 {
        return -2;
    }
    catch_unwind(AssertUnwindSafe(|| {
        // SAFETY: 有效句柄来自 Box::into_raw，采样只借用且契约禁止并发释放。
        let runtime = unsafe { &*(handle as *const Runtime) };
        if count != runtime.tracks.len() {
            return -4;
        }
        if count == 0 {
            return 0;
        }
        // SAFETY: 已确认轨道数一致且大于零，调用方提供独占的已初始化输出数组。
        let out = unsafe { std::slice::from_raw_parts_mut(output, count) };
        for (item, spec) in out.iter_mut().zip(&runtime.tracks) {
            *item = eval_track(spec, &runtime.segments, t);
        }
        0
    }))
    .unwrap_or(-3)
}

/// 销毁快照，归还创建时复制的轨道与片段数组。
///
/// # Arguments
///
/// - `handle`：创建入口返回的句柄；传入空指针直接返回。
///
/// # Safety
///
/// 非空句柄须来自 [`kg_core_tracks_create`] 并且尚未释放。调用前必须等待所有使用该句柄的采样结束。
#[no_mangle]
pub unsafe extern "C" fn kg_core_tracks_free(handle: *mut c_void) {
    if !handle.is_null() {
        // SAFETY: 此处取回创建端转出的唯一 Box；调用方保证没有剩余采样或第二次释放。
        let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
            drop(Box::from_raw(handle as *mut Runtime))
        }));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn layout_and_easing_boundary() {
        assert_eq!(std::mem::size_of::<TrackSpec>(), 16);
        assert_eq!(std::mem::size_of::<TrackSegment>(), 40);
        assert_eq!(kg_core_tracks_abi_version(), 0x0003_0000);
        for id in 0..EASE_COUNT {
            assert_eq!(ease(id, -1.0), 0.0);
            assert_eq!(ease(id, 2.0), 1.0);
            assert!((ease(id, 0.5)).is_finite());
            assert!(ease(id, f64::NAN).is_nan());
        }
    }
    #[test]
    fn track_ties_initial_reverse_seek_and_proxy_isolation() {
        let specs = [
            TrackSpec {
                offset: 0,
                count: 3,
                initial: 7.0,
            },
            TrackSpec {
                offset: 3,
                count: 1,
                initial: 0.0,
            },
            TrackSpec {
                offset: 4,
                count: 0,
                initial: 9.0,
            },
        ];
        let s = |time, duration, from, to, ease| TrackSegment {
            time,
            duration,
            from,
            to,
            ease,
            reserved: 0,
        };
        let segments = [
            s(1.0, 0.0, 0.0, 1.0, 0),
            s(2.0, 1.0, 1.0, 3.0, 0),
            s(2.0, 0.0, 3.0, 5.0, 0),
            s(0.0, 1.0, 0.0, 1.0, 4),
        ];
        let mut handle = std::ptr::null_mut();
        assert_eq!(
            // SAFETY: 两组数组的元素数与参数匹配，handle 是单独的可写输出槽。
            unsafe { kg_core_tracks_create(specs.as_ptr(), 3, segments.as_ptr(), 4, &mut handle) },
            0
        );
        let mut out = [0.0; 3];
        for (at, expected) in [
            (0.0, [7.0, 0.0, 9.0]),
            (2.0, [5.0, 1.0, 9.0]),
            (0.5, [7.0, 0.25, 9.0]),
            (1.0, [1.0, 1.0, 9.0]),
            (2.0, [5.0, 1.0, 9.0]),
        ] {
            assert_eq!(
                // SAFETY: 句柄来自成功创建；输出数组有三个元素，直到调用后才释放句柄。
                unsafe { kg_core_tracks_eval(handle, at, out.as_mut_ptr(), 3) },
                0
            );
            assert_eq!(out, expected);
        }
        // SAFETY: 最后一次采样已结束，测试只释放该句柄一次。
        unsafe { kg_core_tracks_free(handle) };
    }
    #[test]
    fn invalid_layout_is_rejected() {
        let specs = [TrackSpec {
            offset: 3,
            count: 9,
            initial: 0.0,
        }];
        let mut handle = std::ptr::null_mut();
        assert_eq!(
            // SAFETY: 空片段表的 count 为零；越界的描述值仅用于验证，不导致越界读取。
            unsafe { kg_core_tracks_create(specs.as_ptr(), 1, std::ptr::null(), 0, &mut handle) },
            -4
        );
        assert!(handle.is_null());
    }
}
