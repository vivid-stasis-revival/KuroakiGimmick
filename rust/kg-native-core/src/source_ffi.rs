//! Experimental Rust-owned parse/BPM layer for K/G RustBridge.
//! Memory transfer is a single bounded little-endian packet, freed by kg_core_free_packet.
//! This module is deliberately separate from the existing motion ABI (major 1).
use crate::chart::{self, Chart, ExtraValue};
use std::panic::{catch_unwind, AssertUnwindSafe};

const MAX_INPUT: usize = 128 * 1024 * 1024;
const MAX_VSM: usize = 64 * 1024 * 1024;
const MAX_PACKET: usize = 512 * 1024 * 1024;

fn u8_(buf: &mut Vec<u8>, value: u8) { buf.push(value); }
fn u32_(buf: &mut Vec<u8>, value: usize) -> Result<(), String> {
    let n = u32::try_from(value).map_err(|_| "packet count overflow".to_string())?;
    buf.extend_from_slice(&n.to_le_bytes()); Ok(())
}
fn i32_(buf: &mut Vec<u8>, value: i32) { buf.extend_from_slice(&value.to_le_bytes()); }
fn f64_(buf: &mut Vec<u8>, value: f64) { buf.extend_from_slice(&value.to_le_bytes()); }
fn text(buf: &mut Vec<u8>, s: &str) -> Result<(), String> {
    u32_(buf, s.len())?; buf.extend_from_slice(s.as_bytes()); Ok(())
}

fn encode_chart(c: &Chart, include_notes: bool) -> Result<Vec<u8>, String> {
    // Warnings are routed through the original C# parser to retain exact diagnostic semantics.
    if !c.warnings.is_empty() {return Err(format!("Rust parse requires legacy diagnostic path: {}", c.warnings[0]));}
    // No hard cap on note count. The ABI packet still has a memory budget;
    // reject impossible packets before allocating a huge Vec. A note's minimum
    // representation is 33 bytes (24 fixed + 4 extra count + 1 flag + 4 count).
    let min_note_bytes = c.notes.len().checked_mul(33)
        .ok_or("Rust parse note packet size overflow")?;
    if min_note_bytes > MAX_PACKET { return Err("Rust parse output exceeds 512 MiB".to_string()); }
    let reserve = c.notes.len().saturating_mul(48).saturating_add(4096).min(MAX_PACKET);
    let mut b = Vec::with_capacity(reserve);
    b.extend_from_slice(b"KGS2");
    u8_(&mut b, if c.csm_340 {1} else {0});
    u8_(&mut b, if c.proxies_declared {1} else {0});
    u8_(&mut b, c.proxies);
    text(&mut b, &c.object_name)?;
    u32_(&mut b, c.metadata_order.len())?;
    for key in &c.metadata_order {
        let value=c.metadata.get(key).ok_or("missing metadata entry")?;
        text(&mut b,key)?;text(&mut b,value)?;
    }
    u32_(&mut b, if include_notes {c.notes.len()} else {0})?;
    if include_notes {for n in &c.notes {
        f64_(&mut b, n.time);i32_(&mut b,n.kind as i32);i32_(&mut b,n.lane as i32);f64_(&mut b,n.end);
        u32_(&mut b,n.extra.len())?;
        for (key,val) in &n.extra {
            u8_(&mut b,*key);
            match val {
                ExtraValue::Number(number)=>{u8_(&mut b,0);f64_(&mut b,*number);}
                ExtraValue::TypedNumber(number,tag)=>{u8_(&mut b,*tag);f64_(&mut b,*number);}
                ExtraValue::Text(s)=>{u8_(&mut b,1);text(&mut b,s)?;}
                ExtraValue::Boolean(v)=>{u8_(&mut b,2);u8_(&mut b, if *v{1}else{0});}
            }
        }
        u8_(&mut b,if n.mod_extra_present{1}else{0});
        u32_(&mut b,n.mod_extra_order.len())?;
        for key in &n.mod_extra_order {
            let val=n.mod_extra.get(key).ok_or("missing modExtra entry")?;
            text(&mut b,key)?;
            u8_(&mut b, if val.is_some(){1}else{0});
            if let Some(value)=val {text(&mut b,value)?;}
        }
        if b.len() > MAX_PACKET { return Err("Rust parse output exceeds 512 MiB".to_string()); }
    }}
    u32_(&mut b,c.mods.len())?;
    for m in &c.mods {
        f64_(&mut b,m.beat);f64_(&mut b,m.duration);f64_(&mut b,m.from);f64_(&mut b,m.to);
        i32_(&mut b,m.proxy as i32);u32_(&mut b,m.order)?;u32_(&mut b,m.source_line)?;
        text(&mut b,&m.ease)?;text(&mut b,&m.name)?;
    }
    u32_(&mut b,c.per_frame.len())?;
    for f in &c.per_frame {
        f64_(&mut b,f.start);f64_(&mut b,f.end);text(&mut b,&f.function)?;
    }
    // KGS2: diagnostics follow all chart records. The source file path stays
    // on the managed side and is attached by C# when reconstructing Diagnostic.
    u32_(&mut b, c.diagnostics.len())?;
    for (line, message) in &c.diagnostics {
        u32_(&mut b, *line)?;
        text(&mut b, message)?;
    }
    if b.len()>MAX_PACKET {return Err("Rust parse output exceeds 512 MiB".to_string());}
    Ok(b)
}

// Outgoing packet ownership is transferred exactly once. Keep this pair together.
unsafe fn publish(bytes: Vec<u8>, output: *mut *mut u8, size: *mut usize) {
    let boxed = bytes.into_boxed_slice();
    *size = boxed.len();
    *output = Box::into_raw(boxed) as *mut u8;
}

unsafe fn run_packet<F>(data: *const u8, len: usize, output: *mut *mut u8, size: *mut usize,
                       limit: usize, job: F) -> i32
where F: FnOnce(&[u8])->Result<Vec<u8>,String> {
    if output.is_null() || size.is_null() {return -1;}
    *output=std::ptr::null_mut();*size=0;
    if len>limit || len>isize::MAX as usize {return -2;}
    if len>0 && data.is_null() {return -1;}
    let outcome=catch_unwind(AssertUnwindSafe(|| {
        let bytes=if len==0 {&[][..]} else {unsafe{std::slice::from_raw_parts(data,len)}};
        job(bytes)
    }));
    match outcome {
        Ok(Ok(bytes)) => {publish(bytes,output,size);0},
        Ok(Err(message)) => {publish(message.into_bytes(),output,size);-3},
        Err(_) => {publish(b"panic inside kg_native_core parsing".to_vec(),output,size);-4},
    }
}

/// Parse a UTF-8 VSC document. Do not pass a file path; the C# caller owns I/O.
/// On nonzero return, output may contain a UTF-8 error message and must still be freed.
#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsc(data:*const u8,len:usize,output:*mut *mut u8,size:*mut usize)->i32 {
    run_packet(data,len,output,size,MAX_INPUT,|bytes| {
        let s=std::str::from_utf8(bytes).map_err(|e|format!("Invalid VSC UTF-8: {e}"))?;
        encode_chart(&chart::parse_vsc(s,"native".to_string())?,true)
    })
}

#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsb(data:*const u8,len:usize,output:*mut *mut u8,size:*mut usize)->i32 {
    run_packet(data,len,output,size,MAX_INPUT,|bytes| {
        encode_chart(&chart::parse_vsb(bytes,"native".to_string())?,true)
    })
}

#[no_mangle]
pub unsafe extern "C" fn kg_core_parse_vsm(data:*const u8,len:usize,output:*mut *mut u8,size:*mut usize)->i32 {
    run_packet(data,len,output,size,MAX_VSM,|bytes| {
        let s=std::str::from_utf8(bytes).map_err(|e|format!("Invalid VSM UTF-8: {e}"))?;
        let mut c=Chart::default();
        chart::parse_vsm(s,&mut c)?;
        encode_chart(&c,false)
    })
}

#[repr(C)]
#[derive(Clone,Copy,Debug)]
pub struct BpmChange {pub time:f64,pub bpm:f64}

/// Build BPM segments in Rust; transfer once into the existing C# BpmMap object.
#[no_mangle]
pub unsafe extern "C" fn kg_core_build_bpm(changes:*const BpmChange,count:usize,base:f64,
  offset_ms:f64,fixed:f64,has_fixed:u32,output:*mut *mut u8,size:*mut usize)->i32 {
    if output.is_null() || size.is_null(){return -1;}
    *output=std::ptr::null_mut(); *size=0;
    if count>isize::MAX as usize/std::mem::size_of::<BpmChange>() ||
       (count>0 && changes.is_null()) {return -2;}
    let outcome=catch_unwind(AssertUnwindSafe(||->Result<Vec<u8>,String>{
        if !base.is_finite() || base<=0.0 || base>10000.0 ||
           !offset_ms.is_finite() || offset_ms.abs()>3_600_000.0 ||
           (has_fixed!=0 && (!fixed.is_finite() || fixed<=0.0 || fixed>10000.0)) {
            return Err("Invalid initial BPM/offset".into());
        }
        let changes=if count==0 {&[][..]}else {std::slice::from_raw_parts(changes,count)};
        // Caller supplies time-stably-sorted BPM notes, matching Chart.Notes.
        let mut segments=vec![(0.0_f64,0.0_f64,if has_fixed!=0{fixed}else{base})];
        if has_fixed==0 {for change in changes {
            if !change.bpm.is_finite() || change.bpm<=0.0 || change.bpm>10000.0 || !change.time.is_finite() {
                return Err("Invalid BPM change".into());
            }
            if change.time<0.0 {segments[0].2=change.bpm;continue;}
            let prev=segments[segments.len()-1];
            let beat=prev.1+(change.time-prev.0)*prev.2/60.0;
            if change.time==prev.0 {let last=segments.len()-1;segments[last]=(change.time,beat,change.bpm);}
            else {segments.push((change.time,beat,change.bpm));}
        }}
        let packet_bytes=segments.len().checked_mul(24).and_then(|n|n.checked_add(8))
            .filter(|n|*n<=MAX_PACKET).ok_or("Rust BPM output exceeds 512 MiB")?;
        let mut b=Vec::with_capacity(packet_bytes);
        b.extend_from_slice(b"KGB1");u32_(&mut b,segments.len())?;
        for (t,beat,bpm) in segments {f64_(&mut b,t);f64_(&mut b,beat);f64_(&mut b,bpm);}
        Ok(b)
    }));
    match outcome {
        Ok(Ok(b))=>{publish(b,output,size);0},
        Ok(Err(e))=>{publish(e.into_bytes(),output,size);-3},
        Err(_)=>{publish(b"Rust BPM panic".to_vec(),output,size);-4},
    }
}

#[no_mangle]
pub unsafe extern "C" fn kg_core_free_packet(ptr:*mut u8,len:usize){
    if !ptr.is_null() && len>0 && len<=MAX_PACKET {
        let slice=std::ptr::slice_from_raw_parts_mut(ptr,len);
        drop(Box::from_raw(slice));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn wire_roundtrip_shape() {
        let c=chart::parse_vsc("1000,0,1\n1200,2,2,1700\n1800,3,0,b:190\n", "test".into()).unwrap();
        let b=encode_chart(&c,true).unwrap();
        assert_eq!(&b[..4],b"KGS2");
        assert_eq!(c.notes.len(),3);
        assert!(c.notes[0].time<=c.notes[1].time);
    }
    #[test]
    fn vsc_ignored_timing_anchors_are_informational() {
        let input = "0,3,0,b:150|v:12|s:8\n1000,0,1\n2000,3,0,b:180|v:undefined|s:4\n";
        let c = chart::parse_vsc(input, "fixture".to_string()).unwrap();
        assert!(c.warnings.is_empty(), "valid v/s must not force fallback");
        assert_eq!(c.notes.len(), 3);
        assert_eq!(c.diagnostics, vec![
            (1, "VSC timing 'v' is not applied.".to_string()),
            (1, "VSC timing 's' is not applied.".to_string()),
            (3, "VSC timing 's' is not applied.".to_string()),
        ]);
        let packed = encode_chart(&c, true).expect("informational diagnostics can be packed");
        assert_eq!(&packed[..4], b"KGS2");
        assert!(packed.windows(b"VSC timing 's' is not applied.".len())
            .any(|w| w == b"VSC timing 's' is not applied."));
    }
    #[test]
    fn malformed_vsc_still_falls_back_to_csharp_diagnostics() {
        let c = chart::parse_vsc("0,3,0,b:150|v:5\nbad,row\n", "fixture".into()).unwrap();
        assert!(!c.warnings.is_empty());
        assert!(encode_chart(&c, true).is_err());
    }

    #[test]
    fn vsc_csm340_modextra_and_stable_order() {
        let c=chart::parse_vsc("1000,0,1,,flag:yes\n1000,1,0,,extra:undefined\n", "test".into()).unwrap();
        assert_eq!(c.notes.len(),2);
        assert_eq!(c.notes[0].kind,0);
        assert!(c.csm_340);
        assert_eq!(c.notes[0].mod_extra.get("flag"),Some(&Some("yes".to_string())));
        assert_eq!(c.notes[1].mod_extra.get("extra"),Some(&None));
    }
    #[test]
    fn vsm_ranges_and_metadata() {
        let mut c=Chart::default();
        chart::parse_vsm("!obj:obj_custom_gimmick\n!proxies:2\n0:2:1,1,linear,0,1,prx,-1\nmpf\n0,4,foo\n", &mut c).unwrap();
        assert_eq!(c.mods.len(),3);
        assert_eq!(c.mods[1].beat,1.0);
        assert_eq!(c.mods[0].source_line,3);
        assert_eq!(c.per_frame.len(),1);
        assert_eq!(c.proxies,2);
    }
    #[test]
    fn vsm_trailing_comments_preserve_values_and_pack_warning() {
        let mut c=Chart::default();
        chart::parse_vsm("!obj:obj_custom_gimmick // object\n!proxies:2 // count\n0,0,linear,0,1,prx,-1 // event\nmpf // marker\n0,4,foo // function\n", &mut c).unwrap();
        assert!(c.warnings.is_empty(), "valid inline comments must execute in Rust");
        assert_eq!(c.object_name,"obj_custom_gimmick");
        assert_eq!(c.proxies,2);
        assert_eq!(c.mods.len(),1);
        assert_eq!(c.mods[0].source_line,3);
        assert_eq!(c.per_frame[0].function,"foo");
        assert_eq!(c.diagnostics.len(),1);
        assert_eq!(c.diagnostics[0].0,1);
        assert!(encode_chart(&c,false).is_ok());
    }
    #[test]
    fn binary_vsb_fixture() {
        let bytes=include_bytes!("../tests/fixtures/demo.vsb");
        let c=chart::parse_vsb(bytes,"fixture".into()).unwrap();
        assert!(!c.notes.is_empty());
    }
    #[test]
    fn bpm_packet() {
        let c=[BpmChange{time:2.0,bpm:240.0},BpmChange{time:4.0,bpm:120.0}];
        let mut ptr=std::ptr::null_mut();let mut len=0;
        let rc=unsafe{kg_core_build_bpm(c.as_ptr(),2,120.0,0.0,0.0,0,&mut ptr,&mut len)};
        assert_eq!(rc,0);
        let s=unsafe{std::slice::from_raw_parts(ptr,len)};
        assert_eq!(&s[..4],b"KGB1");
        assert_eq!(u32::from_le_bytes(s[4..8].try_into().unwrap()),3);
        unsafe{kg_core_free_packet(ptr,len)};
    }
}
