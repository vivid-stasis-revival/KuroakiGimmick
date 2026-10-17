//! Rust-owned decode + bounded PCM cache. SDL and GUI objects never enter this module.
//! ABI returns a caller-owned boxed f32 slice; managed side MUST call kg_audio_free.
use std::collections::hash_map::DefaultHasher;
use std::fs::{self, File};
use std::hash::{Hash, Hasher};
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};
use std::sync::atomic::{AtomicU64,Ordering};
use std::time::UNIX_EPOCH;

use symphonia::core::audio::SampleBuffer;
use symphonia::core::codecs::{DecoderOptions, CODEC_TYPE_NULL};
use symphonia::core::errors::Error;
use symphonia::core::formats::FormatOptions;
use symphonia::core::io::MediaSourceStream;
use symphonia::core::meta::MetadataOptions;
use symphonia::core::probe::Hint;

const MAX_PCM_BYTES: usize = 1_024_000_000;
const MAX_CACHE_FILE_BYTES: u64 = MAX_PCM_BYTES as u64 + 64;
const MAGIC: &[u8; 8] = b"KGPCM002";

struct Audio { samples: Vec<f32>, rate: u32, channels: u32, cache_hit: u32 }
#[derive(Clone, Copy, PartialEq, Eq)]
struct Stamp { size: u64, mtime: u128 }
struct Hot { path: PathBuf, stamp: Stamp, rate: u32, channels: u32, data: Vec<f32> }
static HOT: OnceLock<Mutex<Option<Hot>>> = OnceLock::new();
static TEMP_ID: AtomicU64 = AtomicU64::new(0);
fn hot() -> &'static Mutex<Option<Hot>> { HOT.get_or_init(|| Mutex::new(None)) }

fn stamp(path: &Path) -> Result<Stamp,String> {
    let m = fs::metadata(path).map_err(|e| e.to_string())?;
    let time = m.modified().map_err(|e| e.to_string())?
        .duration_since(UNIX_EPOCH).map_err(|e| e.to_string())?.as_nanos();
    Ok(Stamp { size:m.len(), mtime:time })
}
fn cache_file(path: &Path, dir: &Path) -> PathBuf {
    let mut hasher = DefaultHasher::new();
    path.hash(&mut hasher);
    dir.join(format!("{:016x}.kgpcm", hasher.finish()))
}
fn cache_read(path: &Path, source_stamp:Stamp) -> Option<Audio> {
    let mut file = File::open(path).ok()?;
    let size = file.metadata().ok()?.len();
    if size < 48 || (!crate::load_limits::unlimited() && size > MAX_CACHE_FILE_BYTES) { return None; }
    let mut header = [0u8; 48];
    file.read_exact(&mut header).ok()?;
    if &header[..8] != MAGIC {return None;}
    let get_u64 = |range:std::ops::Range<usize>| u64::from_le_bytes(header[range].try_into().unwrap());
    let raw_len = get_u64(8..16);
    let mtime_lo = get_u64(16..24);
    let mtime_hi = get_u64(24..32);
    let rate = u32::from_le_bytes(header[32..36].try_into().unwrap());
    let channels = u32::from_le_bytes(header[36..40].try_into().unwrap());
    let count = get_u64(40..48);
    if raw_len != source_stamp.size || (u128::from(mtime_hi)<<64 | u128::from(mtime_lo)) != source_stamp.mtime
        || !(8000..=384000).contains(&rate) || !(1..=32).contains(&channels)
        || count == 0 || (!crate::load_limits::unlimited() && count > MAX_PCM_BYTES as u64 / 4) || count.checked_mul(4)?.checked_add(48)? != size
        || count % channels as u64 != 0 { return None; }
    let byte_len=usize::try_from(count.checked_mul(4)?).ok()?;
    if byte_len>isize::MAX as usize { return None; }
    let mut bytes = vec![0; byte_len];
    file.read_exact(&mut bytes).ok()?;
    let samples = bytes.chunks_exact(4).map(|c| f32::from_le_bytes(c.try_into().unwrap())).collect();
    Some(Audio { samples,rate,channels,cache_hit:2 })
}
fn cache_write(path: &Path, st: Stamp, audio: &Audio) -> std::io::Result<()> {
    let Some(dir) = path.parent() else {return Ok(());};
    fs::create_dir_all(dir)?;
    let temp=dir.join(format!(".{}.{}.{}.tmp", path.file_name().unwrap_or_default().to_string_lossy(),std::process::id(),
        TEMP_ID.fetch_add(1,Ordering::Relaxed)));
    let result = (|| {
        let mut f = File::create(&temp)?;
        f.write_all(MAGIC)?;
        f.write_all(&st.size.to_le_bytes())?;
        f.write_all(&(st.mtime as u64).to_le_bytes())?;
        f.write_all(&((st.mtime>>64) as u64).to_le_bytes())?;
        f.write_all(&audio.rate.to_le_bytes())?;
        f.write_all(&audio.channels.to_le_bytes())?;
        f.write_all(&(audio.samples.len() as u64).to_le_bytes())?;
        for block in audio.samples.chunks(8192) {
            let mut buf = Vec::with_capacity(block.len()*4);
            for x in block {buf.extend_from_slice(&x.to_le_bytes());}
            f.write_all(&buf)?;
        }
        f.flush()?;
        drop(f);
        // Cache is disposable; do not overwrite an existing entry on Windows without first removing it.
        #[cfg(target_os="windows")]
        { let _ = fs::remove_file(path); }
        fs::rename(&temp,path)
    })();
    if result.is_err() {let _=fs::remove_file(&temp);}
    result
}

fn decode(path: &Path) -> Result<Audio,String> {
    let file=File::open(path).map_err(|e|format!("Open audio: {e}"))?;
    let mss=MediaSourceStream::new(Box::new(file),Default::default());
    let mut hint=Hint::new();
    if let Some(ext)=path.extension().and_then(|x|x.to_str()) {hint.with_extension(ext);}
    let format=symphonia::default::get_probe()
        .format(&hint,mss,&FormatOptions {enable_gapless:true,..Default::default()},&MetadataOptions::default())
        .map_err(|e|format!("Probe audio: {e}"))?;
    let mut format=format.format;
    let track=format.tracks().iter().find(|t|t.codec_params.codec!=CODEC_TYPE_NULL)
        .ok_or("No supported audio track")?;
    let id=track.id;
    let mut rate=track.codec_params.sample_rate.unwrap_or(0);
    let mut channels=track.codec_params.channels.map(|x|x.count() as u32).unwrap_or(0);
    let mut decoder=symphonia::default::get_codecs().make(&track.codec_params,&DecoderOptions::default())
        .map_err(|e|format!("Create audio decoder: {e}"))?;
    let mut pcm=Vec::new();
    let mut sample_buffer: Option<SampleBuffer<f32>> = None;
    let mut sample_capacity = 0usize;
    loop {
        let packet=match format.next_packet() {
            Ok(p)=>p,
            Err(Error::IoError(e)) if e.kind()==std::io::ErrorKind::UnexpectedEof=>break,
            Err(Error::ResetRequired)=>return Err("Audio track reset requires fallback".into()),
            Err(e)=>return Err(format!("Read audio packet: {e}")),
        };
        if packet.track_id()!=id {continue;}
        let decoded=match decoder.decode(&packet) {
            Ok(buffer)=>buffer,
            Err(Error::DecodeError(_))=>return Err("Corrupted audio packet".into()),
            Err(Error::IoError(e)) if e.kind()==std::io::ErrorKind::UnexpectedEof=>break,
            Err(e)=>return Err(format!("Decode audio: {e}")),
        };
        let spec=*decoded.spec();
        if rate==0 {rate=spec.rate;}
        if channels==0 {channels=spec.channels.count() as u32;}
        if rate!=spec.rate || channels!=spec.channels.count() as u32 {return Err("Mid-stream audio format change".into());}
        let samples_count = (decoded.frames() as usize).checked_mul(channels as usize)
            .ok_or("Decoded audio sample count overflow")?;
        if samples_count.checked_mul(4).and_then(|n|n.checked_add(pcm.len()*4)).is_none_or(|n| !crate::load_limits::unlimited() && n>MAX_PCM_BYTES) {
            return Err("Decoded audio exceeds 1024 MB".into());
        }
        if decoded.capacity() > sample_capacity {
            sample_capacity = decoded.capacity();
            sample_buffer = Some(SampleBuffer::<f32>::new(sample_capacity as u64, spec));
        }
        let out = sample_buffer.as_mut().ok_or("Missing audio sample buffer")?;
        out.copy_interleaved_ref(decoded);
        pcm.extend_from_slice(out.samples());
    }
    if !(8000..=384000).contains(&rate) || !(1..=32).contains(&channels) || pcm.is_empty() || pcm.len()%channels as usize!=0 {
        return Err("Invalid/empty decoded audio".into());
    }
    Ok(Audio {samples:pcm,rate,channels,cache_hit:0})
}

fn decode_cached(path: &Path, cache_dir:Option<&Path>) -> Result<Audio,String> {
    let path=path.canonicalize().map_err(|e|e.to_string())?;
    let st=stamp(&path)?;
    if cache_dir.is_some() {
        if let Ok(lock)=hot().lock() {
            if let Some(hit)=lock.as_ref() {
                if hit.path==path && hit.stamp==st {
                    return Ok(Audio { samples:hit.data.clone(), rate:hit.rate,channels:hit.channels,cache_hit:1 });
                }
            }
        }
    }
    let cache_path=cache_dir.map(|d|cache_file(&path,d));
    let result = match cache_path.as_ref().and_then(|p|cache_read(p,st)) {
        Some(hit) => hit, None => decode(&path)?,
    };
    if result.cache_hit==0 {
        if let Some(p)=cache_path.as_ref() {let _=cache_write(p,st,&result);}
    }
    // Only one recently used decoded track; no permanent unbounded Rust allocations.
    if cache_dir.is_some() && result.samples.len()*4<=128*1024*1024 {
        if let Ok(mut lock)=hot().lock() {
            *lock=Some(Hot {path,stamp:st,rate:result.rate,channels:result.channels,data:result.samples.clone()});
        }
    }
    Ok(result)
}

#[repr(C)]
pub struct AudioResult {
    pub data: *mut f32,
    pub length: usize,
    pub sample_rate: u32,
    pub channels: u32,
    pub cache_hit: u32, // 0 decoded, 1 memory, 2 disk
}
// SAFETY: The caller supplies UTF-8 buffers alive for the duration of this call, an aligned
// writable `AudioResult`, and frees the returned data exactly once via kg_audio_free.
#[no_mangle]
pub unsafe extern "C" fn kg_audio_decode(path:*const u8,path_len:usize,cache:*const u8,cache_len:usize,out:*mut AudioResult)->i32 {
    if path.is_null() || out.is_null() || path_len==0 || path_len>32768 || cache_len>32768 || (cache_len>0 && cache.is_null()) {return -1;}
    let operation=||->Result<AudioResult,String>{
        let pb=std::str::from_utf8(unsafe{std::slice::from_raw_parts(path,path_len)}).map_err(|e|e.to_string())?;
        let cache_dir=if cache_len>0 {
            let p=std::str::from_utf8(unsafe{std::slice::from_raw_parts(cache,cache_len)}).map_err(|e|e.to_string())?;
            Some(Path::new(p))
        }else{None};
        let data=decode_cached(Path::new(pb),cache_dir)?;
        let len=data.samples.len();
        if !crate::load_limits::unlimited() && len > MAX_PCM_BYTES / 4 { return Err("Decoded audio exceeds 1024 MB".into()); }
        let mut boxed=data.samples.into_boxed_slice();
        let ptr=boxed.as_mut_ptr();std::mem::forget(boxed);
        Ok(AudioResult{data:ptr,length:len,sample_rate:data.rate,channels:data.channels,cache_hit:data.cache_hit})
    };
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(operation)) {
        Ok(Ok(value))=>{unsafe{*out=value};0},
        Ok(Err(e))=>{eprintln!("[Optimization Audio] {e}");-2},
        Err(_)=>-3,
    }
}
#[no_mangle]
pub unsafe extern "C" fn kg_audio_free(data:*mut f32,length:usize){
    if data.is_null() || length==0 || length>isize::MAX as usize/4 {return;}
    let _=std::panic::catch_unwind(||unsafe{
        drop(Box::from_raw(std::ptr::slice_from_raw_parts_mut(data,length)));
    });
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn cache_header_rejects_bad_stamp_and_truncation() {
        let dir=std::env::temp_dir().join(format!("kg-audio-test-{}",std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let path=dir.join("demo.kgpcm");
        let st=Stamp{size:123,mtime:456};
        let a=Audio{samples:vec![0.0,0.5,-0.25,1.0],rate:48000,channels:2,cache_hit:0};
        cache_write(&path,st,&a).unwrap();
        assert_eq!(cache_read(&path,st).unwrap().samples,a.samples);
        assert!(cache_read(&path,Stamp{size:124,mtime:456}).is_none());
        fs::write(&path,b"broken").unwrap();
        assert!(cache_read(&path,st).is_none());
        let _=fs::remove_dir_all(&dir);
    }
    #[test]
    fn rust_decodes_wav_and_hits_memory_cache() {
        // Stereo 16-bit PCM WAV: four interleaved samples, two frames.
        let dir=std::env::temp_dir().join(format!("kg-wav-test-{}",std::process::id()));
        fs::create_dir_all(&dir).unwrap();
        let wav=dir.join("test.wav");
        let mut b=Vec::new();
        b.extend_from_slice(b"RIFF");b.extend_from_slice(&44u32.to_le_bytes());
        b.extend_from_slice(b"WAVEfmt ");b.extend_from_slice(&16u32.to_le_bytes());
        b.extend_from_slice(&1u16.to_le_bytes());b.extend_from_slice(&2u16.to_le_bytes());
        b.extend_from_slice(&48000u32.to_le_bytes());b.extend_from_slice(&192000u32.to_le_bytes());
        b.extend_from_slice(&4u16.to_le_bytes());b.extend_from_slice(&16u16.to_le_bytes());
        b.extend_from_slice(b"data");b.extend_from_slice(&8u32.to_le_bytes());
        for n in [0i16,16384,-16384,32767] {b.extend_from_slice(&n.to_le_bytes());}
        fs::write(&wav,b).unwrap();
        let cache=dir.join("cache");
        let first=decode_cached(&wav,Some(&cache)).unwrap();
        assert_eq!(first.rate,48000);
        assert_eq!(first.channels,2);
        assert_eq!(first.samples.len(),4);
        assert_eq!(first.cache_hit,0);
        let second=decode_cached(&wav,Some(&cache)).unwrap();
        assert_eq!(second.cache_hit,1);
        assert_eq!(second.samples,first.samples);
        let _=fs::remove_dir_all(dir);
    }

}

#[no_mangle]
pub unsafe extern "C" fn kg_audio_decode_unlimited(path:*const u8,path_len:usize,cache:*const u8,cache_len:usize,out:*mut AudioResult)->i32 {
    crate::load_limits::with_unlimited(|| kg_audio_decode(path,path_len,cache,cache_len,out))
}
