//! Rust-owned, immutable timeline track evaluator.
//! C# performs the original semantic timeline construction (including callback/no-op
//! filtering, sentinel resolution and object-specific behavior), then passes the
//! finished flat track arrays once. One FFI call evaluates *all* tracks per frame.
//! No C# / SDL object, string or GPU resource is held by Rust.
use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};

pub const TRACK_ABI: u32 = 0x0003_0000;
const MAX_TRACKS: usize = 100_000;
const MAX_SEGMENTS: usize = 4_000_000;
const EASE_COUNT: u32 = 31;

#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct TrackSpec {
    pub offset: u32,
    pub count: u32,
    pub initial: f64,
}

#[repr(C)]
#[derive(Clone, Copy, Debug)]
pub struct TrackSegment {
    pub time: f64,
    pub duration: f64,
    pub from: f64,
    pub to: f64,
    pub ease: u32,
    pub reserved: u32,
}

struct Runtime {
    tracks: Vec<TrackSpec>,
    segments: Vec<TrackSegment>,
}

#[inline]
fn bounce(mut x: f64) -> f64 {
    if x < 1.0 / 2.75 { 7.5625*x*x }
    else if x < 2.0 / 2.75 { x -= 1.5/2.75; 7.5625*x*x+0.75 }
    else if x < 2.5 / 2.75 { x -= 2.25/2.75; 7.5625*x*x+0.9375 }
    else { x -= 2.625/2.75; 7.5625*x*x+0.984375 }
}
#[inline]
fn in_out(x: f64, power: i32) -> f64 {
    if x < 0.5 { (2.0*x).powi(power)/2.0 }
    else { 1.0 - (2.0-2.0*x).powi(power)/2.0 }
}
/// Ids are shared with RustTimelineBridge.EaseNames in the exact order below.
fn ease(id: u32, t: f64) -> f64 {
    // Mirror Math.Clamp: NaN is not silently replaced with 0 or 1.
    let t = if t.is_nan() {t} else {t.max(0.0).min(1.0)};
    if t == 0.0 || t == 1.0 { return t; }
    const BACK: f64 = 1.70158;
    use std::f64::consts::PI;
    match id {
        0 => t,                                          // linear
        1 => 1.0-(t*PI/2.0).cos(),                     // inSine
        2 => (t*PI/2.0).sin(),                         // outSine
        3 => (1.0-(PI*t).cos())/2.0,                   // inOutSine
        4 => t*t,                                       // inQuad
        5 => 1.0-(1.0-t).powi(2),                     // outQuad
        6 => in_out(t,2),                              // inOutQuad
        7 => t*t*t,                                     // inCubic
        8 => 1.0-(1.0-t).powi(3),                     // outCubic
        9 => in_out(t,3),                              // inOutCubic
        10 => t.powi(4),                               // inQuart
        11 => 1.0-(1.0-t).powi(4),                    // outQuart
        12 => in_out(t,4),                             // inOutQuart
        13 => t.powi(5),                               // inQuint
        14 => 1.0-(1.0-t).powi(5),                    // outQuint
        15 => in_out(t,5),                             // inOutQuint
        16 => (2.0_f64).powf(10.0*(t-1.0)),            // inExpo
        17 => 1.0-(2.0_f64).powf(-10.0*t),            // outExpo
        18 => if t < 0.5 {(2.0_f64).powf(20.0*t-10.0)/2.0} else {(2.0-(2.0_f64).powf(-20.0*t+10.0))/2.0},
        19 => 1.0-(1.0-t*t).sqrt(),                    // inCirc
        20 => (1.0-(t-1.0).powi(2)).sqrt(),            // outCirc
        21 => if t < 0.5 {(1.0-(1.0-4.0*t*t).sqrt())/2.0}
              else {((1.0-(-2.0*t+2.0).powi(2)).sqrt()+1.0)/2.0},
        22 => (BACK+1.0)*t*t*t-BACK*t*t,               // inBack
        23 => 1.0+(BACK+1.0)*(t-1.0).powi(3)+BACK*(t-1.0).powi(2),
        24 => {let b=BACK*1.525;
               if t<0.5 {(2.0*t).powi(2)*((b+1.0)*2.0*t-b)/2.0}
               else {((2.0*t-2.0).powi(2)*((b+1.0)*(t*2.0-2.0)+b)+2.0)/2.0}},
        25 => -(2.0_f64).powf(10.0*t-10.0)*((t*10.0-10.75)*2.0*PI/3.0).sin(),
        26 => (2.0_f64).powf(-10.0*t)*((t*10.0-0.75)*2.0*PI/3.0).sin()+1.0,
        27 => if t<0.5 {-(2.0_f64).powf(20.0*t-10.0)*((20.0*t-11.125)*2.0*PI/4.5).sin()/2.0}
              else {(2.0_f64).powf(-20.0*t+10.0)*((20.0*t-11.125)*2.0*PI/4.5).sin()/2.0+1.0},
        28 => 1.0-bounce(1.0-t),                       // inBounce
        29 => bounce(t),                               // outBounce
        30 => if t<0.5 {(1.0-bounce(1.0-2.0*t))/2.0} else {(1.0+bounce(2.0*t-1.0))/2.0},
        _ => f64::NAN, // creation validates ID, so never evaluated
    }
}

fn eval_track(spec: &TrackSpec, segments: &[TrackSegment], t: f64) -> f64 {
    let a = spec.offset as usize;
    let end = a + spec.count as usize;
    let track = &segments[a..end];
    // Last time <= t; ties preserve original declaration order.
    // If t is NaN, C# binary search finds no matching event.
    let mut lo=0usize;
    let mut hi=track.len();
    while lo<hi {
        let mid = lo+(hi-lo)/2;
        if track[mid].time<=t {lo=mid+1;} else {hi=mid;}
    }
    if lo==0 {return spec.initial;}
    let s=&track[lo-1];
    if s.duration<=0.0 {s.to}
    else {s.from+(s.to-s.from)*ease(s.ease,(t-s.time)/s.duration)}
}

#[no_mangle]
pub extern "C" fn kg_core_tracks_abi_version() -> u32 {TRACK_ABI}

/// # Safety
/// `specs` and `segments` are readable, aligned arrays with the given counts;
/// `output` is a writable pointer. All inputs remain valid for the call.
#[no_mangle]
pub unsafe extern "C" fn kg_core_tracks_create(
    specs:*const TrackSpec, tracks:usize, segments:*const TrackSegment, count:usize,
    output:*mut *mut c_void,
)->i32 {
    if output.is_null() || (tracks>0 && specs.is_null()) || (count>0 && segments.is_null()) {return -1;}
    if tracks>MAX_TRACKS || count>MAX_SEGMENTS ||
        tracks>isize::MAX as usize/std::mem::size_of::<TrackSpec>() ||
        count>isize::MAX as usize/std::mem::size_of::<TrackSegment>() {return -2;}
    match catch_unwind(AssertUnwindSafe(|| {
        unsafe {*output=std::ptr::null_mut();}
        let desc = if tracks>0 {unsafe {std::slice::from_raw_parts(specs,tracks)}} else {&[]};
        let items = if count>0 {unsafe {std::slice::from_raw_parts(segments,count)}} else {&[]};
        for s in desc {
            let offset=s.offset as usize;
            let len=s.count as usize;
            if offset>count || len>count-offset {return -4;}
            let slice=&items[offset..offset+len];
            if slice.iter().any(|seg| seg.ease>=EASE_COUNT || seg.reserved!=0) {return -5;}
            if slice.windows(2).any(|pair| pair[0].time>pair[1].time) {return -6;}
        }
        let obj=Box::new(Runtime {tracks:desc.to_vec(),segments:items.to_vec()});
        unsafe {*output=Box::into_raw(obj).cast::<c_void>();}
        0
    })) {Ok(rc)=>rc,Err(_)=>-3}
}

/// # Safety
/// `handle` must come from kg_core_tracks_create, remain live for this call,
/// and not be concurrently freed. `output` must point to `count` writable f64s.
#[no_mangle]
pub unsafe extern "C" fn kg_core_tracks_eval(
    handle:*const c_void,t:f64, output:*mut f64,count:usize,
)->i32 {
    if handle.is_null() || (count>0 && output.is_null()) {return -1;}
    if count>MAX_TRACKS || count>isize::MAX as usize/8 {return -2;}
    match catch_unwind(AssertUnwindSafe(|| {
        let runtime=unsafe {&*(handle as *const Runtime)};
        if count!=runtime.tracks.len() {return -4;}
        if count==0 {return 0;}
        let out=unsafe {std::slice::from_raw_parts_mut(output,count)};
        for (item,spec) in out.iter_mut().zip(&runtime.tracks) {
            *item=eval_track(spec,&runtime.segments,t);
        }
        0
    })) {Ok(rc)=>rc,Err(_)=>-3}
}

/// # Safety
/// Must be a non-null live handle from kg_core_tracks_create, freed exactly once,
/// with no concurrent calls to kg_core_tracks_eval.
#[no_mangle]
pub unsafe extern "C" fn kg_core_tracks_free(handle:*mut c_void) {
    if !handle.is_null() {
        let _=catch_unwind(AssertUnwindSafe(|| unsafe {drop(Box::from_raw(handle as *mut Runtime))}));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn layout_and_easing_boundary() {
        assert_eq!(std::mem::size_of::<TrackSpec>(),16);
        assert_eq!(std::mem::size_of::<TrackSegment>(),40);
        assert_eq!(kg_core_tracks_abi_version(),0x0003_0000);
        for id in 0..EASE_COUNT {
            assert_eq!(ease(id,-1.0),0.0);
            assert_eq!(ease(id,2.0),1.0);
            assert!((ease(id,0.5)).is_finite());
            assert!(ease(id,f64::NAN).is_nan());
        }
    }
    #[test]
    fn track_ties_initial_reverse_seek_and_proxy_isolation() {
        let specs=[TrackSpec{offset:0,count:3,initial:7.0},TrackSpec{offset:3,count:1,initial:0.0},TrackSpec{offset:4,count:0,initial:9.0}];
        let s=|time,duration,from,to,ease| TrackSegment{time,duration,from,to,ease,reserved:0};
        let segments=[s(1.0,0.0,0.0,1.0,0),s(2.0,1.0,1.0,3.0,0),s(2.0,0.0,3.0,5.0,0),s(0.0,1.0,0.0,1.0,4)];
        let mut handle=std::ptr::null_mut();
        assert_eq!(unsafe{kg_core_tracks_create(specs.as_ptr(),3,segments.as_ptr(),4,&mut handle)},0);
        let mut out=[0.0;3];
        for (at,expected) in [(0.0,[7.0,0.0,9.0]),(2.0,[5.0,1.0,9.0]),(0.5,[7.0,0.25,9.0]),(1.0,[1.0,1.0,9.0]),(2.0,[5.0,1.0,9.0])] {
            assert_eq!(unsafe{kg_core_tracks_eval(handle,at,out.as_mut_ptr(),3)},0);
            assert_eq!(out,expected);
        }
        unsafe {kg_core_tracks_free(handle)};
    }
    #[test]
    fn invalid_layout_is_rejected() {
        let specs=[TrackSpec{offset:3,count:9,initial:0.0}];
        let mut handle=std::ptr::null_mut();
        assert_eq!(unsafe{kg_core_tracks_create(specs.as_ptr(),1,std::ptr::null(),0,&mut handle)},-4);
        assert!(handle.is_null());
    }
}
