/* Controller-to-SDL3 bridge and literal path input. MIT. */
#include <switch.h>
#include <SDL3/SDL.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

static PadState pad;
static bool initialized;
static float cursor_x = 640, cursor_y = 360;
static Uint64 last_tick;
static Uint64 last_memory_tick;
extern void kg_memory_log(const char *phase);

static void key(SDL_Window *window, SDL_Scancode scan, bool down)
{
    SDL_Event event = {0};
    event.type = down ? SDL_EVENT_KEY_DOWN : SDL_EVENT_KEY_UP;
    event.key.windowID = SDL_GetWindowID(window);
    event.key.scancode = scan;
    event.key.key = SDL_GetKeyFromScancode(scan, SDL_KMOD_NONE, false);
    event.key.down = down;
    SDL_PushEvent(&event);
}

void kg_pump_input(SDL_Window *window)
{
    if (!initialized) {
        padConfigureInput(1, HidNpadStyleSet_NpadStandard);
        padInitializeDefault(&pad);
        last_tick = SDL_GetTicksNS();
        initialized = true;
    }
    padUpdate(&pad);
    uint64_t down = padGetButtonsDown(&pad), up = padGetButtonsUp(&pad);
    if (down & HidNpadButton_Plus) {
        SDL_Event event = {0}; event.type = SDL_EVENT_QUIT; SDL_PushEvent(&event);
    }
    const struct { uint64_t button; SDL_Scancode scan; } keys[] = {
        {HidNpadButton_X, SDL_SCANCODE_SPACE}, {HidNpadButton_B, SDL_SCANCODE_ESCAPE},
        {HidNpadButton_Y, SDL_SCANCODE_H}, {HidNpadButton_Minus, SDL_SCANCODE_F1},
        {HidNpadButton_Left, SDL_SCANCODE_LEFT}, {HidNpadButton_Right, SDL_SCANCODE_RIGHT},
        {HidNpadButton_Up, SDL_SCANCODE_UP}, {HidNpadButton_Down, SDL_SCANCODE_DOWN}
    };
    for (size_t i = 0; i < sizeof(keys)/sizeof(keys[0]); i++) {
        if (down & keys[i].button) key(window, keys[i].scan, true);
        if (up & keys[i].button) key(window, keys[i].scan, false);
    }
    int w = 1280, h = 720;
    SDL_GetWindowSize(window, &w, &h);
    Uint64 tick = SDL_GetTicksNS();
    if (tick - last_memory_tick >= 5000000000ULL) {
        last_memory_tick = tick;
        kg_memory_log("frame");
    }
    float delta = fminf((tick - last_tick) / 1000000000.0f, 0.05f);
    last_tick = tick;
    HidAnalogStickState stick = padGetStickPos(&pad, 0);
    if (abs(stick.x) > 4000 || abs(stick.y) > 4000) {
        cursor_x = fminf(fmaxf(cursor_x + stick.x / 32768.0f * 900 * delta, 0), w - 1);
        cursor_y = fminf(fmaxf(cursor_y - stick.y / 32768.0f * 900 * delta, 0), h - 1);
        SDL_Event event = {0}; event.type = SDL_EVENT_MOUSE_MOTION;
        event.motion.windowID = SDL_GetWindowID(window);
        event.motion.x = cursor_x; event.motion.y = cursor_y;
        event.motion.state = (padGetButtons(&pad) & HidNpadButton_A) ? SDL_BUTTON_LMASK : 0;
        SDL_PushEvent(&event);
    }
    if ((down | up) & HidNpadButton_A) {
        SDL_Event event = {0};
        event.type = (down & HidNpadButton_A) ? SDL_EVENT_MOUSE_BUTTON_DOWN : SDL_EVENT_MOUSE_BUTTON_UP;
        event.button.windowID = SDL_GetWindowID(window);
        event.button.button = SDL_BUTTON_LEFT; event.button.down = !!(down & HidNpadButton_A);
        event.button.clicks = 1; event.button.x = cursor_x; event.button.y = cursor_y;
        SDL_PushEvent(&event);
    }
}

const char *kg_prompt_path(const char *initial, bool save)
{
    static char output[1024];
    SwkbdConfig config;
    if (R_FAILED(swkbdCreate(&config, 0))) return NULL;
    swkbdConfigMakePresetDefault(&config);
    swkbdConfigSetGuideText(&config, save ? "Save path on SD card" : "Chart, project, VSM or song folder path");
    swkbdConfigSetInitialText(&config, initial ? initial : "/switch/KuroakiGimmick/Songs");
    swkbdConfigSetStringLenMax(&config, sizeof(output) - 1);
    Result result = swkbdShow(&config, output, sizeof(output));
    swkbdClose(&config);
    return R_SUCCEEDED(result) && output[0] ? output : NULL;
}

void *getsym_KGSwitch(const char *name)
{
    if (!strcmp(name, "kg_pump_input")) return (void *)kg_pump_input;
    if (!strcmp(name, "kg_prompt_path")) return (void *)kg_prompt_path;
    return NULL;
}
