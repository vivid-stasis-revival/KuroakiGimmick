/* Asset-free SDL3 GPU + Mesa VI WSI probe. SPDX-License-Identifier: MPL-2.0 */
#define SDL_MAIN_HANDLED
#include <SDL3/SDL.h>
#include <SDL3/SDL_main.h>
#include <switch.h>
#include <stdio.h>
#include <stdarg.h>
#include <sys/stat.h>

static FILE *probe_file;
static void probe_log(const char *format, ...)
{
    va_list args;
    va_start(args, format);
    vfprintf(probe_file ? probe_file : stderr, format, args);
    va_end(args);
    fflush(probe_file ? probe_file : stderr);
}

static void probe_sdl_log(void *userdata, int category, SDL_LogPriority priority, const char *message)
{
    probe_log("[SDL category=%d priority=%d] %s\n", category, (int)priority, message);
}

static bool probe_buffers(SDL_GPUDevice *gpu)
{
    const Uint32 sizes[] = {4096, 1024 * 1024, 8 * 1024 * 1024};
    for (unsigned int i = 0; i < sizeof(sizes) / sizeof(sizes[0]); ++i) {
        SDL_GPUTransferBufferCreateInfo upload_info = {0};
        SDL_GPUBufferCreateInfo vertex_info = {0};
        upload_info.usage = SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;
        upload_info.size = sizes[i];
        vertex_info.usage = SDL_GPU_BUFFERUSAGE_VERTEX;
        vertex_info.size = sizes[i];
        probe_log("TEST upload buffer bytes=%u\n", sizes[i]);
        SDL_GPUTransferBuffer *upload = SDL_CreateGPUTransferBuffer(gpu, &upload_info);
        if (!upload) {
            probe_log("FAIL create upload buffer bytes=%u: %s\n", sizes[i], SDL_GetError());
            return false;
        }
        void *mapped = SDL_MapGPUTransferBuffer(gpu, upload, false);
        if (!mapped) {
            probe_log("FAIL map upload buffer: %s\n", SDL_GetError());
            SDL_ReleaseGPUTransferBuffer(gpu, upload);
            return false;
        }
        SDL_memset(mapped, 0, sizes[i]);
        SDL_UnmapGPUTransferBuffer(gpu, upload);
        SDL_GPUBuffer *vertex = SDL_CreateGPUBuffer(gpu, &vertex_info);
        if (!vertex) {
            probe_log("FAIL create vertex buffer: %s\n", SDL_GetError());
            SDL_ReleaseGPUTransferBuffer(gpu, upload);
            return false;
        }
        SDL_GPUCommandBuffer *command = SDL_AcquireGPUCommandBuffer(gpu);
        SDL_GPUCopyPass *copy = command ? SDL_BeginGPUCopyPass(command) : NULL;
        bool ok = copy != NULL;
        if (ok) {
            SDL_GPUTransferBufferLocation source = {upload, 0};
            SDL_GPUBufferRegion destination = {vertex, 0, sizes[i]};
            SDL_UploadToGPUBuffer(copy, &source, &destination, false);
            SDL_EndGPUCopyPass(copy);
            ok = SDL_SubmitGPUCommandBuffer(command);
            command = NULL;
        }
        if (command) SDL_CancelGPUCommandBuffer(command);
        if (ok) ok = SDL_WaitForGPUIdle(gpu);
        if (!ok) probe_log("FAIL upload/submit/wait: %s\n", SDL_GetError());
        SDL_ReleaseGPUBuffer(gpu, vertex);
        SDL_ReleaseGPUTransferBuffer(gpu, upload);
        if (!ok) return false;
        probe_log("PASS upload/map/copy/submit/wait bytes=%u\n", sizes[i]);
    }
    return true;
}

int main(int argc, char **argv)
{
    mkdir("sdmc:/switch/KuroakiGimmick", 0777);
    probe_file = fopen("sdmc:/switch/KuroakiGimmick/probe-r3.log", "w");
    if (!probe_file) probe_file = fopen("probe-r3.log", "w");
    if (!probe_file) {
        consoleInit(NULL);
        printf("Cannot create probe-r3.log on SD. Press Plus to exit.\n");
        PadState pad;
        padConfigureInput(1, HidNpadStyleSet_NpadStandard);
        padInitializeDefault(&pad);
        while (appletMainLoop()) {
            padUpdate(&pad);
            if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
            consoleUpdate(NULL);
        }
        consoleExit(NULL);
        return 1;
    }
    probe_log("K/G Vulkan/buffer probe r3: entered main; appletType=%u\n", (unsigned int)appletGetAppletType());
    SDL_SetLogOutputFunction(probe_sdl_log, NULL);
    SDL_SetLogPriority(SDL_LOG_CATEGORY_GPU, SDL_LOG_PRIORITY_INFO);
    if (appletGetAppletType() != AppletType_Application && appletGetAppletType() != AppletType_SystemApplication) {
        probe_log("Use title takeover, not album applet mode.\n"); return 1;
    }
    SDL_SetMainReady();
    SDL_Window *window = NULL;
    SDL_GPUDevice *gpu = NULL;
    bool claimed = false;
    probe_log("STEP SDL_Init\n");
    bool ok = SDL_Init(SDL_INIT_VIDEO);
    if (!ok) goto end;
    probe_log("STEP SDL_CreateWindow\n");
    window = SDL_CreateWindow("K/G Vulkan probe r3", 1280, 720, 0);
    if (!window) goto end;
    probe_log("STEP SDL_CreateGPUDevice/claim\n");
    gpu = SDL_CreateGPUDevice(SDL_GPU_SHADERFORMAT_SPIRV, false, "vulkan");
    if (!gpu || !SDL_ClaimWindowForGPUDevice(gpu, window)) goto end;
    claimed = true;
    if (!probe_buffers(gpu)) goto end;
    probe_log("SDL video=%s GPU=%s; cyan clear/present loop. Plus exits.\n", SDL_GetCurrentVideoDriver(), SDL_GetGPUDeviceDriver(gpu));
    PadState pad;
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    bool first_frame = true;
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
        SDL_Event event;
        while (SDL_PollEvent(&event)) { }
        if (first_frame) probe_log("STEP acquire command/swapchain\n");
        SDL_GPUCommandBuffer *command = SDL_AcquireGPUCommandBuffer(gpu);
        if (!command) goto end;
        SDL_GPUTexture *swapchain;
        if (!SDL_WaitAndAcquireGPUSwapchainTexture(command, window, &swapchain, NULL, NULL)) {
            SDL_CancelGPUCommandBuffer(command); goto end;
        }
        if (swapchain) {
            if (first_frame) probe_log("STEP begin clear pass\n");
            SDL_GPUColorTargetInfo target = {0};
            target.texture = swapchain;
            target.clear_color = (SDL_FColor){0.03f, 0.35f + (SDL_GetTicks() % 2000) / 4000.0f, 0.75f, 1.0f};
            target.load_op = SDL_GPU_LOADOP_CLEAR; target.store_op = SDL_GPU_STOREOP_STORE;
            SDL_GPURenderPass *pass = SDL_BeginGPURenderPass(command, &target, 1, NULL);
            if (!pass) { SDL_CancelGPUCommandBuffer(command); goto end; }
            SDL_EndGPURenderPass(pass);
        }
        if (!SDL_SubmitGPUCommandBuffer(command)) goto end;
        if (first_frame && swapchain) {
            probe_log("PASS first clear/present submitted\n");
            first_frame = false;
        }
    }
    probe_log("Probe completed. Buffers and WSI exercised; K/G shaders and Mono not tested.\n");
    if (gpu) { SDL_WaitForGPUIdle(gpu); SDL_ReleaseWindowFromGPUDevice(gpu, window); SDL_DestroyGPUDevice(gpu); }
    SDL_DestroyWindow(window); SDL_Quit();
    fclose(probe_file);
    return 0;
end:
    probe_log("Failed: %s\n", SDL_GetError());
    if (gpu) {
        if (claimed) SDL_ReleaseWindowFromGPUDevice(gpu, window);
        SDL_DestroyGPUDevice(gpu);
    }
    if (window) SDL_DestroyWindow(window);
    SDL_Quit();
    fclose(probe_file);
    return 1;
}
