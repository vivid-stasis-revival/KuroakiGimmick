/* Copy the user's supplied missing music into the K/G song folder. SPDX-License-Identifier: WTFPL */
#include <switch.h>
#include <arpa/inet.h>
#include <stdio.h>
#include <stdarg.h>
#include <errno.h>
#include <string.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#include <zlib.h>
#include "nxlink-music-manifest.h"

static FILE *log_file;
static void report(const char *format, ...)
{
    char message[1024];
    va_list args;
    va_start(args, format); vsnprintf(message, sizeof(message), format, args); va_end(args);
    fputs(message, stdout); fflush(stdout);
    if (log_file) { fputs(message, log_file); fflush(log_file); }
}

static bool install_at(const char *target)
{
    char temporary[256] = {0};
    FILE *input = NULL, *output = NULL;
    bool success = false;
    report("Music destination=%s\n", target);
    FILE *existing = fopen(target, "rb");
    if (existing) { fclose(existing); report("Destination already exists; preserving it.\n"); return true; }
    if (errno != ENOENT) { report("Cannot inspect destination: errno=%d %s\n", errno, strerror(errno)); return false; }
    input = fopen("romfs:/music.ogg", "rb");
    if (!input) { report("Missing embedded music: errno=%d\n", errno); goto end; }
    snprintf(temporary, sizeof(temporary), "%s.nxlink-%llx.part", target, (unsigned long long)armGetSystemTick());
    int fd = open(temporary, O_WRONLY | O_CREAT | O_EXCL, 0666);
    if (fd < 0) { report("Create temporary file failed: errno=%d %s\n", errno, strerror(errno)); goto end; }
    output = fdopen(fd, "wb");
    if (!output) { close(fd); report("fdopen failed\n"); goto end; }
    unsigned char buffer[32768];
    size_t total = 0, bytes;
    uLong checksum = crc32(0, NULL, 0);
    while ((bytes = fread(buffer, 1, sizeof(buffer), input)) > 0) {
        if (fwrite(buffer, 1, bytes, output) != bytes) { report("Write failed: errno=%d %s\n", errno, strerror(errno)); goto end; }
        checksum = crc32(checksum, buffer, bytes); total += bytes;
    }
    if (ferror(input) || total != MUSIC_BYTES || checksum != MUSIC_CRC) {
        report("Payload verification failed: bytes=%zu crc32=%08lx\n", total, checksum); goto end;
    }
    if (fclose(output) != 0) { output = NULL; report("Close/flush failed\n"); goto end; }
    output = NULL;
    FILE *verify = fopen(temporary, "rb");
    if (!verify) { report("Cannot reopen temporary file\n"); goto end; }
    uLong actual = crc32(0, NULL, 0);
    size_t actual_size = 0;
    while ((bytes = fread(buffer, 1, sizeof(buffer), verify)) > 0) {
        actual = crc32(actual, buffer, bytes); actual_size += bytes;
    }
    bool valid = !ferror(verify) && actual_size == MUSIC_BYTES && actual == MUSIC_CRC;
    fclose(verify);
    if (!valid) { report("SD verification failed\n"); goto end; }
    existing = fopen(target, "rb");
    if (existing) { fclose(existing); report("Destination appeared during copy; preserving it.\n"); success = true; goto end; }
    if (rename(temporary, target) != 0) { report("Rename failed: errno=%d %s\n", errno, strerror(errno)); goto end; }
    temporary[0] = '\0';
    report("PASS music copied and verified: bytes=%zu crc32=%08lx\n", total, checksum);
    success = true;
end:
    if (input) fclose(input);
    if (output) fclose(output);
    if (temporary[0]) remove(temporary);
    return success;
}

static void performance(void)
{
    FILE *file = fopen("sdmc:/switch/KuroakiGimmick/UserData/mono-aot.log", "r");
    if (!file) return;
    char line[2048];
    while (fgets(line, sizeof(line), file))
        if (strstr(line, "K/G Switch r5") || strstr(line, "[K/G perf]") || strstr(line, "Ran out of trampolines")) report("%s", line);
    fclose(file);
}

int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    consoleInit(NULL);
    log_file = fopen("sdmc:/switch/KuroakiGimmick/install-music.log", "w");
    int socket_fd = -1;
    if (__nxlink_host.s_addr && R_SUCCEEDED(socketInitializeDefault())) socket_fd = nxlinkStdio();
    report("K/G music installer r5; collecting Full AOT performance\n");
    performance();
    bool success = false;
    Result result = romfsInit();
    if (R_FAILED(result)) { report("romfsInit failed: 0x%x\n", result); }
    else {
        mkdir("sdmc:/switch/KuroakiGimmick/Songs", 0777);
        mkdir("sdmc:/switch/KuroakiGimmick/Songs/nh", 0777);
        success = install_at("sdmc:/switch/KuroakiGimmick/Songs/nh/music.ogg");
        // The previous read-only probe found this second copy of the same chart.
        struct stat info;
        if (stat("sdmc:/nh/ENCORE.vsc", &info) == 0 && S_ISREG(info.st_mode)) {
            bool root_success = install_at("sdmc:/nh/music.ogg");
            success = root_success && success;
        }
        romfsExit();
    }
    report("%s. Press Plus to exit.\n", success ? "Installation complete" : "Installation failed");
    PadState pad; padConfigureInput(1, HidNpadStyleSet_NpadStandard); padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad); if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
        consoleUpdate(NULL); svcSleepThread(16000000);
    }
    if (log_file) { fclose(log_file); log_file = NULL; }
    if (socket_fd >= 0) { fflush(stdout); fflush(stderr); close(socket_fd); socketExit(); }
    consoleExit(NULL);
    return success ? 0 : 1;
}
