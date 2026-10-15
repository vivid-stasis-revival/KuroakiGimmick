/* Asset-free SDL3/Horizon audio output probe. MIT. */
#define SDL_MAIN_HANDLED
#include <SDL3/SDL.h>
#include <SDL3/SDL_main.h>
#include <switch.h>
#include <stdio.h>
#include <math.h>
#include <sys/stat.h>
#include <arpa/inet.h>
#include <unistd.h>

static FILE *log_file;
static float tone[12000 * 2];
static int nx_socket = -1;

static void audio_log(void *userdata, int category, SDL_LogPriority priority, const char *message)
{
    fprintf(log_file, "[SDL category=%d priority=%d] %s\n", category, (int)priority, message);
    fflush(log_file);
    if (nx_socket >= 0) printf("[SDL audio] %s\n", message);
}

int main(int argc, char **argv)
{
    mkdir("sdmc:/switch/KuroakiGimmick", 0777);
    log_file = fopen("sdmc:/switch/KuroakiGimmick/audio-probe-r4.log", "w");
    if (!log_file) log_file = fopen("audio-probe-r4.log", "w");
    consoleInit(NULL);
    if (!log_file) { printf("Cannot open audio-probe-r4.log\n"); consoleUpdate(NULL); consoleExit(NULL); return 1; }
    fprintf(log_file, "K/G audio probe r4: entered main\n"); fflush(log_file);
    if (__nxlink_host.s_addr != 0 && R_SUCCEEDED(socketInitializeDefault())) {
        nx_socket = nxlinkStdio();
        printf("K/G audio nxlink host=%s connected=%s\n", inet_ntoa(__nxlink_host), nx_socket >= 0 ? "true" : "false");
        fflush(stdout);
    }
    SDL_SetLogOutputFunction(audio_log, NULL);
    SDL_SetLogPriority(SDL_LOG_CATEGORY_AUDIO, SDL_LOG_PRIORITY_INFO);
    SDL_SetMainReady();
    SDL_AudioStream *stream = NULL;
    bool ok = SDL_Init(SDL_INIT_AUDIO);
    if (!ok) goto end;
    SDL_AudioSpec spec = {SDL_AUDIO_F32, 2, 48000};
    stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, NULL, NULL);
    if (!stream) { ok = false; goto end; }
    for (int i = 0; i < 12000; ++i) {
        float sample = 0.12f * sinf((float)i * 6.28318530718f * 440.0f / 48000.0f);
        tone[i * 2] = tone[i * 2 + 1] = sample;
    }
    for (int i = 0; i < 4; ++i) {
        if (!SDL_PutAudioStreamData(stream, tone, sizeof(tone))) { ok = false; goto end; }
    }
    if (!SDL_ResumeAudioStreamDevice(stream)) { ok = false; goto end; }
    printf("K/G audio probe r4\n440 Hz tone for 10 seconds. Plus exits.\n");
    fprintf(log_file, "Audio resumed: input F32 stereo 48000 Hz; queued=%d bytes\n", SDL_GetAudioStreamQueued(stream));
    printf("Audio resumed: queued=%d bytes\n", SDL_GetAudioStreamQueued(stream)); fflush(stdout);
    fflush(log_file);
    PadState pad;
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    Uint64 start = SDL_GetTicks(), next_log = start;
    while (appletMainLoop() && SDL_GetTicks() - start < 10000) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
        int queued = SDL_GetAudioStreamQueued(stream);
        if (queued < 0) { ok = false; break; }
        if (queued < 48000 * 2 * 4 && !SDL_PutAudioStreamData(stream, tone, sizeof(tone))) { ok = false; break; }
        if (SDL_GetTicks() >= next_log) {
            fprintf(log_file, "t=%llu ms queued=%d bytes\n", (unsigned long long)(SDL_GetTicks() - start), queued);
            fflush(log_file);
            printf("audio t=%llu ms queued=%d bytes\n", (unsigned long long)(SDL_GetTicks() - start), queued); fflush(stdout);
            next_log = SDL_GetTicks() + 1000;
        }
        consoleUpdate(NULL);
        SDL_Delay(10);
    }
end:
    if (!ok) {
        fprintf(log_file, "FAIL: %s\n", SDL_GetError());
        printf("Audio probe failed: %s\n", SDL_GetError());
        fflush(stdout);
    } else {
        fprintf(log_file, "Probe ended. Queued-byte movement alone does not confirm audible output.\n");
        printf("Audio probe complete\n"); fflush(stdout);
    }
    fflush(log_file);
    if (stream) SDL_DestroyAudioStream(stream);
    SDL_Quit();
    consoleExit(NULL);
    fclose(log_file);
    if (nx_socket >= 0) { close(nx_socket); socketExit(); }
    return ok ? 0 : 1;
}
