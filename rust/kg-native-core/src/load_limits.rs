use std::cell::Cell;

thread_local! { static UNLIMITED: Cell<bool> = const { Cell::new(false) }; }
pub(crate) fn unlimited() -> bool { UNLIMITED.with(Cell::get) }
pub(crate) fn with_unlimited<T>(job: impl FnOnce() -> T) -> T {
    struct Restore(bool);
    impl Drop for Restore { fn drop(&mut self) { UNLIMITED.with(|v| v.set(self.0)); } }
    let _restore = Restore(UNLIMITED.with(|v| v.replace(true)));
    job()
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn policy_is_scoped_and_thread_local() {
        assert!(!unlimited());
        with_unlimited(|| {
            assert!(unlimited());
            std::thread::spawn(|| assert!(!unlimited())).join().unwrap();
        });
        assert!(!unlimited());
    }
}
