/* Host fault injection for the actual SDL Switch playback callbacks. MIT. */
#include <assert.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
typedef uint8_t Uint8;
typedef uint32_t Result;
enum { AudioDriverWaveBufState_Free, AudioDriverWaveBufState_Queued, AudioDriverWaveBufState_Playing, AudioDriverWaveBufState_Done };
typedef struct { int state; } AudioDriverWaveBuf;
typedef struct { int dummy; } AudioDriver;
struct SDL_PrivateAudioData {
    AudioDriver driver;
    AudioDriverWaveBuf buffer[2];
    void *buffer_tmp, *pool;
    bool audr_device, audr_driver;
};
typedef struct {
    struct SDL_PrivateAudioData *hidden;
    int buffer_size, shutdown;
} SDL_AudioDevice;
static SDL_AudioDevice *active;
static int waits, updates, starts, queue_calls, errors, frees, stops, closes, exits;
static bool inside_play, fail_update, fail_queue;
#define R_FAILED(r) ((r) != 0)
#define R_SUCCEEDED(r) ((r) == 0)
#define SDL_assert assert
#define SDL_memcpy memcpy
static bool SDL_SetError(const char *format, ...) { errors++; return false; }
static int SDL_GetAtomicInt(int *value) { return *value; }
static void armDCacheFlush(void *ptr, int size) { assert(size == active->buffer_size); }
static Result audrvUpdate(AudioDriver *driver) { updates++; return fail_update ? 1 : 0; }
static bool audrvVoiceAddWaveBuf(AudioDriver *driver, int voice, AudioDriverWaveBuf *wave)
{
    queue_calls++;
    if (fail_queue) return false;
    wave->state = AudioDriverWaveBufState_Queued;
    return true;
}
static void audrvVoiceStart(AudioDriver *driver, int voice) { starts++; }
static void audrenWaitFrame(void)
{
    assert(!inside_play); // Never wait while SDL holds the device lock.
    waits++;
    active->hidden->buffer[0].state = AudioDriverWaveBufState_Done;
}
static void audrenStopAudioRenderer(void) { stops++; }
static void audrvClose(AudioDriver *driver) { assert(stops == 1); closes++; }
static void audrenExit(void) { assert(closes == 1); exits++; }
static void test_free(void *ptr) { assert(exits == 1); frees++; free(ptr); }
#define free test_free
#define SDL_free test_free

/* Actual callback bodies are inserted here by test_switch_audio_backend.py. */
/* KG_AUDIO_CALLBACKS */

#undef free
int main(void)
{
    SDL_AudioDevice device = {0};
    device.buffer_size = 4;
    device.hidden = calloc(1, sizeof(*device.hidden));
    device.hidden->pool = calloc(1, 8);
    device.hidden->buffer_tmp = malloc(4);
    memcpy(device.hidden->buffer_tmp, "tone", 4);
    device.hidden->audr_device = device.hidden->audr_driver = true;
    active = &device;
    inside_play = true;
    assert(SWITCHAUDIO_PlayDevice(&device, device.hidden->buffer_tmp, 4));
    inside_play = false;
    assert(waits == 0 && queue_calls == 1 && starts == 1 && updates == 1);
    assert(memcmp(device.hidden->pool, "tone", 4) == 0);
    device.hidden->buffer[0].state = AudioDriverWaveBufState_Playing;
    device.hidden->buffer[1].state = AudioDriverWaveBufState_Queued;
    assert(SWITCHAUDIO_WaitDevice(&device));
    assert(waits == 1 && device.hidden->buffer[0].state == AudioDriverWaveBufState_Done);
    int size = 0;
    assert(SWITCHAUDIO_GetDeviceBuf(&device, &size) == device.hidden->buffer_tmp && size == 4);
    device.hidden->buffer[0].state = AudioDriverWaveBufState_Playing;
    inside_play = true;
    assert(!SWITCHAUDIO_PlayDevice(&device, device.hidden->buffer_tmp, 4));
    inside_play = false;
    assert(waits == 1 && errors == 1); // Full queue must not index buffer[-1] or block.
    fail_update = true;
    assert(!SWITCHAUDIO_WaitDevice(&device) && errors == 2);
    fail_update = false;
    device.shutdown = 1;
    int prior_updates = updates;
    assert(SWITCHAUDIO_WaitDevice(&device) && updates == prior_updates);
    device.shutdown = 0;
    device.hidden->buffer[0].state = AudioDriverWaveBufState_Free;
    fail_queue = true;
    inside_play = true;
    assert(!SWITCHAUDIO_PlayDevice(&device, device.hidden->buffer_tmp, 4));
    inside_play = false;
    assert(errors == 3 && waits == 1);
    SWITCHAUDIO_CloseDevice(&device);
    assert(device.hidden == NULL && frees == 3 && stops == 1 && closes == 1 && exits == 1);
    SWITCHAUDIO_CloseDevice(&device);
    assert(frees == 3);
    puts("PASS actual Switch audio callbacks: nonblocking Play, paced Wait, full/error/shutdown paths, buffer size, complete/idempotent cleanup.");
}
