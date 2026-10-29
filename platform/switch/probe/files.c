/* Read-only SD path probe, without Mono/SDL. SPDX-License-Identifier: WTFPL */
#include <switch.h>
#include <arpa/inet.h>
#include <stdio.h>
#include <stdarg.h>
#include <errno.h>
#include <dirent.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

static FILE *log_file;
static void report(const char *format, ...)
{
    char text[1024];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof(text), format, args);
    va_end(args);
    fputs(text, stdout); fflush(stdout);
    if (log_file) { fputs(text, log_file); fflush(log_file); }
}

static void directory(const char *path)
{
    errno = 0;
    DIR *dir = opendir(path);
    if (!dir) { report("DIR %s failed errno=%d %s\n", path, errno, strerror(errno)); return; }
    report("DIR %s\n", path);
    struct dirent *entry;
    int count = 0;
    while ((entry = readdir(dir)) != NULL && count++ < 64)
        report("  name=[%s]\n", entry->d_name);
    closedir(dir);
}

static void file(const char *path)
{
    errno = 0;
    FILE *input = fopen(path, "rb");
    if (!input) { report("OPEN %s failed errno=%d %s\n", path, errno, strerror(errno)); return; }
    unsigned char head[16] = {0};
    size_t bytes = fread(head, 1, sizeof(head), input);
    fseek(input, 0, SEEK_END);
    long length = ftell(input);
    report("OPEN %s OK size=%ld header=%02x %02x %02x %02x read=%zu\n",
           path, length, head[0], head[1], head[2], head[3], bytes);
    fclose(input);
}

int main(int argc, char **argv)
{
    consoleInit(NULL);
    log_file = fopen("sdmc:/switch/KuroakiGimmick/file-probe.log", "w");
    int socket_fd = -1;
    if (__nxlink_host.s_addr != 0 && R_SUCCEEDED(socketInitializeDefault())) socket_fd = nxlinkStdio();
    report("K/G read-only file probe, nxlink=%s\n", socket_fd >= 0 ? "connected" : "not connected");
    directory("sdmc:/switch/KuroakiGimmick/Songs");
    directory("sdmc:/switch/KuroakiGimmick/Songs/nh");
    file("/switch/KuroakiGimmick/Songs/nh/music.ogg");
    file("sdmc:/switch/KuroakiGimmick/Songs/nh/music.ogg");
    directory("sdmc:/nh");
    file("sdmc:/nh/music.ogg");
    report("Read-only inspection complete. Press Plus to exit.\n");
    PadState pad;
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
        consoleUpdate(NULL);
        svcSleepThread(16000000);
    }
    report("File probe cleanup\n");
    if (log_file) { fclose(log_file); log_file = NULL; }
    if (socket_fd >= 0) { fflush(stdout); fflush(stderr); close(socket_fd); socketExit(); }
    consoleExit(NULL);
    return 0;
}
