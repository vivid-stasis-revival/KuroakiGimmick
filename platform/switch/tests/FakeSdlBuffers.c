/* Fault injection for managed buffer growth, without a GPU. MIT. */
#include <stdint.h>
#include <stdbool.h>
typedef struct { uint32_t usage, size, props; } BufferInfo;
static int allow_transfer, vertex_allocations, released_old, released_new;
static int oom_mode, wait_calls, transfer_calls;
void kg_test_allow_transfer(void) { allow_transfer = 1; }
int kg_test_vertex_allocations(void) { return vertex_allocations; }
int kg_test_released_old(void) { return released_old; }
int kg_test_released_new(void) { return released_new; }
void kg_test_oom_mode(int mode) { oom_mode = mode; allow_transfer = 0; wait_calls = transfer_calls = 0; }
int kg_test_wait_calls(void) { return wait_calls; }
int kg_test_transfer_calls(void) { return transfer_calls; }
const char *SDL_GetError(void) { return oom_mode ? "vkAllocateMemory failed: VK_ERROR_OUT_OF_HOST_MEMORY" : "injected allocation failure"; }
bool SDL_WaitForGPUIdle(void *device)
{
    wait_calls++;
    if (oom_mode == 1) allow_transfer = 1;
    return true;
}
void *SDL_CreateGPUBuffer(void *device, const BufferInfo *info)
{
    return (void *)(uintptr_t)(100 + ++vertex_allocations);
}
void *SDL_CreateGPUTransferBuffer(void *device, const BufferInfo *info)
{
    transfer_calls++;
    return allow_transfer ? (void *)(uintptr_t)(200 + vertex_allocations) : 0;
}
void SDL_ReleaseGPUBuffer(void *device, void *buffer)
{
    if ((uintptr_t)buffer == 10) released_old |= 1;
    else released_new++;
}
void SDL_ReleaseGPUTransferBuffer(void *device, void *buffer)
{
    if ((uintptr_t)buffer == 11) released_old |= 2;
}
