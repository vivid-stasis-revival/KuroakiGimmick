/* K/G-specific mono-nx interpreter entry point. SPDX-License-Identifier: MPL-2.0 */
#include "core.h"
#include <sys/stat.h>
#include <stdlib.h>
#include <malloc.h>
#include <unistd.h>
#include <sys/socket.h>
#include <arpa/inet.h>
#define SDL_MAIN_HANDLED
#include <SDL3/SDL.h>
#ifdef KG_SWITCH_AOT
#include "kg_aot_payload.h"
bool kg_aot_prepare(void);
static FILE *kg_boot_file;
static int kg_boot_socket = -1;
void kg_aot_stage(const char *stage)
{
    char line[768];
    int length = snprintf(line, sizeof(line), "[K/G early a7] %s\n", stage);
    if (length < 0 || (size_t)length >= sizeof(line)) return;
    if (kg_boot_file) { fwrite(line, 1, length, kg_boot_file); fflush(kg_boot_file); }
    if (kg_boot_socket >= 0) send(kg_boot_socket, line, length, MSG_DONTWAIT);
}
#endif

void kg_memory_log(const char *phase)
{
    struct mallinfo info = mallinfo();
    extern char *fake_heap_end;
    uintptr_t top = (uintptr_t)sbrk(0), end = (uintptr_t)fake_heap_end;
    /* Also called from native P/Invoke callbacks. GC counters take cooperative
     * locks and cannot be queried from an already GC-safe native region. */
    io_debugf("[K/G memory] %s: native_used=%u MiB pooled_free=%u MiB tail_free=%llu MiB",
        phase, (unsigned int)info.uordblks / 1048576, (unsigned int)info.fordblks / 1048576,
        (unsigned long long)(end > top ? end - top : 0) / 1048576);
}

static void kg_sdl_log(void *userdata, int category, SDL_LogPriority priority, const char *message)
{
    fprintf(stderr, "[SDL category=%d priority=%d] %s\n", category, (int)priority, message);
    fflush(stderr);
    if (priority >= SDL_LOG_PRIORITY_ERROR && category == SDL_LOG_CATEGORY_GPU) kg_memory_log("SDL GPU error");
}

int main(int argc, char **argv)
{
    mkdir("/switch/KuroakiGimmick", 0777);
    mkdir("/switch/KuroakiGimmick/UserData", 0777);
#ifdef KG_SWITCH_AOT
    kg_boot_file = fopen("/switch/KuroakiGimmick/UserData/aot-boot.log", "a");
    kg_aot_stage("main entered; CoreLib specific=65536 gsharedvt=8192");
    if (KG_SWITCH_AOT_NXLINK && __nxlink_host.s_addr != 0) {
        kg_aot_stage("initializing early network logging");
        socket_esnure_init_thread_safe();
        kg_boot_socket = nxlinkConnectToHost(false, false);
        io_set_nxlink_socket(kg_boot_socket);
        kg_aot_stage(kg_boot_socket >= 0 ? "early nxlink connected" : "early nxlink unavailable");
    }
    else kg_aot_stage("automatic nxlink logging disabled; SD diagnostics enabled");
#endif
    mkdir("/switch/KuroakiGimmick/Temp", 0777);
    setenv("TMPDIR", "/switch/KuroakiGimmick/Temp", 1);
#ifdef KG_SWITCH_AOT
    setenv("KG_SWITCH_ROOT", "/switch/KuroakiGimmick", 1);
    kg_aot_stage("preparing exact DLL metadata");
    if (!kg_aot_prepare()) { console_ensure_init(); fatal_error("Full AOT metadata preparation failed"); return 1; }
    kg_aot_stage("metadata ready; initializing ICU/configuration");
    if (!application_initialize(KG_AOT_CONFIG_PATH)) return 1;
    kg_aot_stage("configuration and ICU ready");
    if (kg_boot_file) { fclose(kg_boot_file); kg_boot_file = NULL; }
    kg_boot_socket = -1; /* Existing stdio tee now owns the socket. */
#else
    if (!application_initialize("/switch/KuroakiGimmick/mono.ini")) return 1;
#endif
#ifndef KG_SWITCH_AOT
    if (__nxlink_host.s_addr != 0) {
        socket_esnure_init_thread_safe();
        int log_socket = nxlinkConnectToHost(false, false);
        io_set_nxlink_socket(log_socket);
        io_debugf("K/G nxlink host=%s connected=%s", inet_ntoa(__nxlink_host), log_socket >= 0 ? "true" : "false");
    }
#endif
#ifdef KG_SWITCH_AOT
    io_debugf("K/G Switch r5 a7: allocator diagnostics; strict Mono Full AOT; CoreLib specific=65536 gsharedvt=8192; nxlinkLog=%d; metadata=%s", KG_SWITCH_AOT_NXLINK, KG_AOT_RUNTIME_ROOT);
#if defined(KG_SWITCH_TLS_FIX)
    io_debugf("K/G runtime: LLVM-PIC / libnx TLS cleanup fix r1");
    io_debugf("K/G UI r1: recoverable settings save; native-only memory counters; text diagnostics");
#endif
#else
    io_debugf("K/G Switch r4: audio backend pacing fixes and frame/audio diagnostics");
#endif
    kg_memory_log("before Mono");
    SDL_SetLogOutputFunction(kg_sdl_log, NULL);
    SDL_SetLogPriority(SDL_LOG_CATEGORY_GPU, SDL_LOG_PRIORITY_INFO);
    SDL_SetLogPriority(SDL_LOG_CATEGORY_AUDIO, SDL_LOG_PRIORITY_INFO);
    /* Text console owns the same default NWindow as SDL: release before graphics. */
    console_dispose();
#ifdef KG_SWITCH_AOT
#define KG_AOT_MODULE(symbol) do { extern void *symbol; mono_aot_register_module(symbol); } while (0)
#include "kg_aot_modules.h"
#undef KG_AOT_MODULE
    mono_jit_set_aot_mode(MONO_AOT_MODE_FULL);
#else
    mono_jit_set_aot_mode(MONO_AOT_MODE_INTERP_ONLY);
#endif
    application_configure_mono();
    MonoDomain *domain = mono_jit_init("kuroaki_switch");
    if (!domain) { fatal_error("Mono domain initialization failed"); return 1; }
    kg_memory_log("Mono initialized");
#ifdef KG_SWITCH_AOT
    const char *dll = KG_AOT_ASSEMBLY_PATH;
#else
    const char *dll = "/switch/KuroakiGimmick/KuroakiGimmick.dll";
#endif
    application_chdir_to_assembly(dll);
    MonoAssembly *assembly = mono_domain_assembly_open(domain, dll);
    if (!assembly) { fatal_error("KuroakiGimmick.dll could not be loaded"); return 1; }
#if defined(KG_SWITCH_COLOR_DIAGNOSTICS)
    extern void kg_check_managed_colors(MonoAssembly *assembly);
    kg_check_managed_colors(assembly);
#endif
#ifdef KG_SWITCH_AOT
    chdir("sdmc:/switch/KuroakiGimmick");
#endif
    char *managed_args[] = {(char *)dll};
    int result = mono_jit_exec(domain, assembly, 1, managed_args);
    mono_jit_cleanup(domain);
    application_terminate();
    return result;
}
