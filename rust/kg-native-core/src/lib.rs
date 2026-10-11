//! RustBridge Optimization Engine v0.3.0: parsing, audio, frame track evaluation and note motion.
//! ABI uses only C-compatible scalars, pointers and caller-owned arrays.
//! No SDL / GPU / UI state enters Rust. Units: ms for distances, px for positions.
use std::panic::{catch_unwind, AssertUnwindSafe};
mod chart;
mod audio;
mod source_ffi;
mod timeline_runtime;

/// ABI-major 1, minor 0. Increment major for any struct layout or function change.
const ABI_VERSION: u32 = 0x0001_0000;

#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct MotionParams {
    pub beat: f64,
    pub alignment: f64,
    pub driven: f64,
    pub beats_per_second: f64,
    pub wave: f64,
    pub beat_motion: f64,
    pub custom: u32,
    pub reserved: u32,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct MotionInput {
    pub distance: f64,
    pub end_distance: f64,
    pub scroll: f64,
    pub x_offset: f64,
    pub y_offset: f64,
    pub boost_time: f64,
    pub boost_distance: f64,
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default)]
pub struct MotionOutput {
    pub x_offset: f64,
    pub y: f64,
    pub end_y: f64,
}

#[no_mangle]
pub extern "C" fn kg_core_abi_version() -> u32 { ABI_VERSION }

#[inline]
fn x_from_values(beat_at_time: f64, distance: f64, x_offset: f64, beat_motion: f64) -> f64 {
    // Preserve C# % semantics including negative beat values.
    let beat = (beat_at_time + 0.3) % 2.0;
    let f = beat % 1.0;
    let mut amount = 0.0;
    if f < 0.3 {
        amount = (f / 0.3).powf(2.0);
    } else if f < 0.7 {
        amount = 1.0 - ((f - 0.3) / 0.4).powf(2.0);
    }
    x_offset + beat_motion / 100.0 * 11.0 * amount
        * (if beat < 1.0 { 1.0 } else { -1.0 })
        * (distance / 60.0 + std::f64::consts::PI / 2.0).sin()
}

#[inline]
fn y_from_values(p: &MotionParams, distance: f64, n: &MotionInput) -> f64 {
    let mut y = p.alignment + 144.0
        - (((distance - n.y_offset) * 0.1) + p.driven * p.beats_per_second * 22.0) * n.scroll
        - p.wave * 0.2 * (distance * 0.006578947368421052).sin();

    if p.custom != 0 {
        // Caller gates per-lane boost_distance when global boost_distance == 0.
        if n.boost_distance != 0.0 {
            y += -n.boost_distance
                + if distance < n.boost_time && n.boost_time > 0.0 {
                    n.boost_distance * ((n.boost_time - distance) / n.boost_time).powf(3.0)
                } else { 0.0 };
        }
    } else if n.boost_time == 0.0 {
        y -= n.boost_distance;
    } else {
        // Math.Clamp(value, 0, 1) keeps NaN as NaN, unlike f64::clamp.
        let ratio = (n.boost_time - distance) / n.boost_time;
        let clamped = if ratio.is_nan() { ratio } else { ratio.max(0.0).min(1.0) };
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

/// Write `count` outputs to caller-owned memory. Neither input nor output may overlap.
/// Return codes: 0 OK, -1 null pointer, -2 invalid buffer length, -3 internal panic.
///
/// # Safety
/// `params` must point to one aligned initialized `MotionParams`. For count>0,
/// `inputs` and `outputs` must point to `count` aligned, initialized/ writable
/// elements respectively; they must not overlap and must remain live during the call.
/// Inputs and outputs may be reused by the caller once this function returns.
#[no_mangle]
pub unsafe extern "C" fn kg_core_motion_batch(
    params: *const MotionParams,
    inputs: *const MotionInput,
    count: usize,
    outputs: *mut MotionOutput,
) -> i32 {
    if params.is_null() || (count > 0 && (inputs.is_null() || outputs.is_null())) {
        return -1;
    }
    if count > isize::MAX as usize / std::mem::size_of::<MotionInput>()
        || count > isize::MAX as usize / std::mem::size_of::<MotionOutput>() {
        return -2;
    }
    let operation = || {
        let p = unsafe { &*params };
        if count == 0 { return; }
        let in_slice = unsafe { std::slice::from_raw_parts(inputs, count) };
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
        let p = MotionParams { beat: 8.5, alignment: 3.0, driven: 0.0,
            beats_per_second: 2.0, wave: 0.0, beat_motion: 0.0, custom: 0, reserved: 0 };
        let a = [MotionInput {distance: 0.0, end_distance: 100.0, scroll: 1.0,
            x_offset: 13.0, y_offset: 0.0, boost_time: 0.0, boost_distance: 0.0},
            MotionInput {distance: -100.0, end_distance: 500.0, scroll: 0.0,
            x_offset: -5.0, y_offset: 0.0, boost_time: 20.0, boost_distance: 40.0}];
        let mut result = [MotionOutput::default(); 2];
        assert_eq!(unsafe {kg_core_motion_batch(&p, a.as_ptr(), a.len(), result.as_mut_ptr())},0);
        assert!((result[0].y - 147.0).abs() < 1e-10);
        assert!((result[0].end_y - 137.0).abs() < 1e-10);
        assert_eq!(result[0].x_offset, 13.0);
        assert!((result[1].y - 147.0).abs() < 1e-10);
        assert!((result[1].end_y - 107.0).abs() < 1e-10);
        assert_eq!(unsafe {kg_core_motion_batch(std::ptr::null(), a.as_ptr(), 2, result.as_mut_ptr())}, -1);
        assert_eq!(unsafe {kg_core_motion_batch(&p, std::ptr::null(), 0, std::ptr::null_mut())},0);
    }
    #[test]
    fn custom_boost_and_negative_beat() {
        let p=MotionParams{beat:-1.5,alignment:-4.0,driven:0.0,beats_per_second:3.0,
            wave:0.0,beat_motion:2.0,custom:1,reserved:0};
        let n=MotionInput{distance:-150.0,end_distance:100.0,scroll:0.0,x_offset:0.0,
            y_offset:0.0,boost_time:200.0,boost_distance:80.0};
        let r=evaluate(&p,&n);
        assert!(r.x_offset.is_finite());
        assert!((r.y-(-80.0+80.0*((200.0_f64+150.0)/200.0).powf(3.0)+140.0)).abs()<1e-9);
    }
}
