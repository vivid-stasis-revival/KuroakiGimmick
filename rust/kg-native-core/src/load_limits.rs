//! 当前线程的加载预算策略。
//!
//! 不限量入口临时放宽标有此策略的数量或内存预算；嵌套调用退出时恢复进入前的状态。

use std::cell::Cell;

thread_local! { static UNLIMITED: Cell<bool> = const { Cell::new(false) }; }
/// 读取当前线程的预算开关。
///
/// # Returns
///
/// 正在不限量作用域内时返回 true。
pub(crate) fn unlimited() -> bool {
    UNLIMITED.with(Cell::get)
}
/// 在当前线程临时放宽预算并执行闭包。
///
/// # Arguments
///
/// - `job`：在该作用域内同步执行的操作。
///
/// # Returns
///
/// 闭包返回的值。
pub(crate) fn with_unlimited<T>(job: impl FnOnce() -> T) -> T {
    struct Restore(bool);
    impl Drop for Restore {
        fn drop(&mut self) {
            UNLIMITED.with(|v| v.set(self.0));
        }
    }
    // WHY: 守卫持有进入前的值，正常返回与 panic 展开都会恢复外层设置。
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
