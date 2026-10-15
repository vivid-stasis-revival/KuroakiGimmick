#include <stdint.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <pthread.h>
#include <stdatomic.h>
#include <assert.h>
typedef uint64_t u64;
typedef int32_t s32;
typedef int MonoNativeTlsKey;
typedef void *gpointer;
typedef int Mutex;
#define INVALID_HANDLE 0
#define MONO_TRACE_DIAGNOSTICS 0
#define g_assert(x) assert(x)
static void g_error(const char *format, ...) { va_list args; va_start(args,format); vfprintf(stderr,format,args); va_end(args); abort(); }
static void mono_trace_message(int category,const char *format,...) { (void)category; (void)format; }
static pthread_mutex_t lock=PTHREAD_MUTEX_INITIALIZER;
static void mutexLock(Mutex *m) { (void)m; pthread_mutex_lock(&lock); }
static void mutexUnlock(Mutex *m) { (void)m; pthread_mutex_unlock(&lock); }
static _Thread_local void *native_tls;
static void (*native_dtor)(void *);
static s32 threadTlsAlloc(void (*dtor)(void *)) { native_dtor=dtor; return 0; }
static void *threadTlsGet(s32 key) { assert(key==0); return native_tls; }
static void threadTlsSet(s32 key,void *value) { assert(key==0); native_tls=value; }
int mono_native_tls_set_value(MonoNativeTlsKey key,gpointer value);
void *mono_native_tls_get_value(MonoNativeTlsKey key);





/* MONO_TLS_SOURCE_INSERT */
static MonoNativeTlsKey info_key, companion_key, reset_key;
static _Atomic int calls, resets;
static bool always_reset;
static int value, companion;
static void info_dtor(void *arg) {
    assert(arg!=NULL); ++calls;
    mono_native_tls_set_value(info_key,arg);
    assert(mono_native_tls_get_value(companion_key)==&companion);
    mono_native_tls_set_value(info_key,NULL);
}
static void reset_dtor(void *arg) {
    assert(arg==&value);
    if (++resets<3 || always_reset) mono_native_tls_set_value(reset_key,arg);
}
static void simulate_exit(void) {
    void *saved=native_tls;
    native_tls=NULL; /* libnx threadExit clears the native slot before callback. */
    native_dtor(saved);
}
static void *worker(void *unused) {
    (void)unused;
    mono_native_tls_set_value(companion_key,&companion);
    mono_native_tls_set_value(info_key,&value);
    simulate_exit(); assert(native_tls==NULL);
    return NULL;
}
int main(int argc,char **argv) {
    assert(argc==2);
    mono_native_tls_alloc(&info_key,(void *)info_dtor);
    mono_native_tls_alloc(&companion_key,NULL);
    mono_native_tls_alloc(&reset_key,(void *)reset_dtor);
    mono_native_tls_set_value(companion_key,&companion);
    switch(argv[1][0]) {
    case '0':
        mono_native_tls_set_value(info_key,&value);
        mono_native_tls_set_value(info_key,NULL);
        simulate_exit(); assert(calls==0); break;
    case '1':
        mono_native_tls_set_value(info_key,&value);
        simulate_exit(); assert(calls==1); break;
    case '2':
        mono_native_tls_set_value(reset_key,&value);
        simulate_exit(); assert(resets==3); break;
    case '3':
        always_reset=true;
        mono_native_tls_set_value(reset_key,&value);
        simulate_exit(); assert(resets==4); break;
    case '4': {
        pthread_t threads[64];
        for(int i=0;i<64;i++) assert(pthread_create(&threads[i],NULL,worker,NULL)==0);
        for(int i=0;i<64;i++) assert(pthread_join(threads[i],NULL)==0);
        simulate_exit(); assert(calls==64); break;
    }
    default: return 2;
    }
    assert(native_tls==NULL);
    puts("TLS cleanup case passed");
    return 0;
}
