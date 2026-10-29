/* Native allocator diagnostics: preserve System.Native allocation semantics. SPDX-License-Identifier: WTFPL */
#include <stdint.h>
#include <stddef.h>
#include <errno.h>
#include "io_util.h"

extern void *__real_SystemNative_Malloc(uintptr_t bytes);
extern int32_t __real_SystemNative_Rename(const char *source, const char *target);
extern void kg_memory_log(const char *phase);

void *__wrap_SystemNative_Malloc(uintptr_t bytes)
{
    void *result = __real_SystemNative_Malloc(bytes);
    int saved_errno = errno;
    if (!result && bytes) {
        io_debugf("[K/G native alloc] FAILED bytes=%zu errno=%d", (size_t)bytes, saved_errno);
        kg_memory_log("NativeMemory allocation failure");
    } else if (bytes >= 16 * 1024 * 1024) {
        io_debugf("[K/G native alloc] allocated bytes=%zu", (size_t)bytes);
        kg_memory_log("large NativeMemory allocation");
    }
    errno = saved_errno;
    return result;
}

int32_t __wrap_SystemNative_Rename(const char *source, const char *target)
{
    int32_t result = __real_SystemNative_Rename(source, target);
    int saved_errno = errno;
    io_debugf("[K/G file] rename %s -> %s result=%d errno=%d", source, target,
              (int)result, result ? saved_errno : 0);
    errno = saved_errno;
    return result;
}
