//! Isolated parser benchmark, compiled by native-vsm-repeat-bench.py.
//! Counting allocations adds overhead; results are not end-to-end app timings.
#![allow(dead_code)]
#[path = "chart.rs"]
mod chart;
#[path = "load_limits.rs"]
mod load_limits;

use std::alloc::{GlobalAlloc, Layout, System};
use std::sync::atomic::{AtomicUsize, Ordering::Relaxed};
use std::time::Instant;

struct CountingAllocator;
static LIVE: AtomicUsize = AtomicUsize::new(0);
static PEAK: AtomicUsize = AtomicUsize::new(0);
static CALLS: AtomicUsize = AtomicUsize::new(0);
static BYTES: AtomicUsize = AtomicUsize::new(0);

fn add_live(size: usize) {
    let live = LIVE.fetch_add(size, Relaxed) + size;
    PEAK.fetch_max(live, Relaxed);
}

unsafe impl GlobalAlloc for CountingAllocator {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        let ptr = System.alloc(layout);
        if !ptr.is_null() {
            add_live(layout.size());
            CALLS.fetch_add(1, Relaxed);
            BYTES.fetch_add(layout.size(), Relaxed);
        }
        ptr
    }

    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        LIVE.fetch_sub(layout.size(), Relaxed);
        System.dealloc(ptr, layout);
    }

    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        let new_ptr = System.realloc(ptr, layout, new_size);
        if !new_ptr.is_null() {
            if new_size >= layout.size() {
                add_live(new_size - layout.size());
            } else {
                LIVE.fetch_sub(layout.size() - new_size, Relaxed);
            }
            CALLS.fetch_add(1, Relaxed);
            BYTES.fetch_add(new_size, Relaxed);
        }
        new_ptr
    }
}

#[global_allocator]
static ALLOCATOR: CountingAllocator = CountingAllocator;

fn main() {
    let count: usize = std::env::args()
        .nth(1)
        .expect("event count is required")
        .parse()
        .expect("invalid event count");
    assert!(count > 0, "event count must be positive");
    let input = format!("0:{},1,easeOutSine,0,1,prx,0", count - 1);
    let initial_live = LIVE.load(Relaxed);
    PEAK.store(initial_live, Relaxed);
    CALLS.store(0, Relaxed);
    BYTES.store(0, Relaxed);

    let start = Instant::now();
    let mut chart = chart::Chart::default();
    load_limits::with_unlimited(|| chart::parse_vsm(&input, &mut chart)).unwrap();
    let elapsed_ms = start.elapsed().as_secs_f64() * 1000.0;
    let peak_live_heap_bytes = PEAK.load(Relaxed) - initial_live;
    let allocation_calls = CALLS.load(Relaxed);
    let allocated_bytes = BYTES.load(Relaxed);
    let retained_heap_bytes = LIVE.load(Relaxed) - initial_live;

    assert_eq!(chart.mods.len(), count);
    assert!(chart.warnings.is_empty());
    assert_eq!(chart.mods[count - 1].beat, (count - 1) as f64);
    assert_eq!(chart.mods[count - 1].order, count - 1);
    assert_eq!(chart.mods[count - 1].ease, "outSine");
    std::hint::black_box(&chart);
    println!(
        "{{\"events\":{count},\"event_size_bytes\":{},\"elapsed_ms\":{elapsed_ms:.3},\"peak_live_heap_bytes\":{peak_live_heap_bytes},\"retained_heap_bytes\":{retained_heap_bytes},\"allocation_calls\":{allocation_calls},\"allocated_bytes\":{allocated_bytes},\"final_capacity\":{}}}",
        std::mem::size_of::<chart::ModEvent>(),
        chart.mods.capacity(),
    );
}
