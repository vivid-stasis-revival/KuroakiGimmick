/* Read-only startup log tail, without Mono, SDL, Mesa or AOT. MIT. */
#include <switch.h>
#include <arpa/inet.h>
#include <stdio.h>
#include <errno.h>
#include <dirent.h>
#include <string.h>
#include <unistd.h>
#include <sys/stat.h>

static void tail(const char *path)
{
    FILE *file = fopen(path, "rb");
    if (!file) { printf("OPEN %s: errno=%d %s\n", path, errno, strerror(errno)); return; }
    if (fseek(file, 0, SEEK_END) != 0) { fclose(file); return; }
    long size = ftell(file);
    if (size < 0) { fclose(file); return; }
    long start = size > 65536 ? size - 65536 : 0;
    if (fseek(file, start, SEEK_SET) != 0) { fclose(file); return; }
    printf("\n--- TAIL %s bytes=%ld offset=%ld ---\n", path, size, start);
    char block[1025];
    size_t count;
    while ((count = fread(block, 1, 1024, file)) > 0) {
        block[count] = 0;
        fputs(block, stdout);
    }
    printf("\n--- END TAIL ---\n");
    fclose(file);
    fflush(stdout);
}

static void reports(const char *path)
{
    DIR *dir = opendir(path);
    if (!dir) { printf("DIR %s: errno=%d\n", path, errno); return; }
    printf("REPORT FILES %s\n", path);
    struct dirent *entry;
    unsigned int count = 0;
    char latest[768] = {0};
    time_t latest_time = 0;
    while ((entry = readdir(dir)) && count++ < 256) {
        printf("  %s\n", entry->d_name);
        size_t length = strlen(entry->d_name);
        if (length < 4 || strcmp(entry->d_name + length - 4, ".log") != 0) continue;
        char file[768];
        snprintf(file, sizeof(file), "%s/%s", path, entry->d_name);
        struct stat info;
        if (stat(file, &info) == 0 && S_ISREG(info.st_mode) &&
            (!latest[0] || info.st_mtime > latest_time ||
             (info.st_mtime == latest_time && strcmp(file, latest) > 0))) {
            snprintf(latest, sizeof(latest), "%s", file);
            latest_time = info.st_mtime;
        }
    }
    closedir(dir);
    if (latest[0]) tail(latest);
}

static void rename_probe(void)
{
    char source[256], target[256];
    unsigned long long id = (unsigned long long)armGetSystemTick();
    snprintf(source, sizeof(source), "sdmc:/switch/KuroakiGimmick/UserData/.kg-rename-%llx-src", id);
    snprintf(target, sizeof(target), "sdmc:/switch/KuroakiGimmick/UserData/.kg-rename-%llx-dst", id);
    FILE *a = fopen(source, "wx"), *b = fopen(target, "wx");
    if (!a || !b) {
        printf("RENAME PROBE create failed errno=%d\n", errno);
        if (a) { fclose(a); remove(source); }
        if (b) { fclose(b); remove(target); }
        return;
    }
    fputs("new", a); fputs("old", b);
    bool written = fclose(a) == 0;
    written = (fclose(b) == 0) && written;
    if (written) {
        errno = 0;
        int result = rename(source, target);
        int error = errno;
        printf("RENAME PROBE overwrite result=%d errno=%d %s\n", result, error, strerror(error));
    }
    remove(source); remove(target);
}

static u64 read64(const unsigned char *data, size_t offset)
{
    u64 value;
    memcpy(&value, data + offset, sizeof(value));
    return value;
}

static void fatal_summaries(void)
{
    const char *root = "sdmc:/atmosphere/fatal_errors";
    DIR *dir = opendir(root);
    if (!dir) return;
    struct dirent *entry;
    unsigned int count = 0;
    while ((entry = readdir(dir)) && count < 16) {
        size_t length = strlen(entry->d_name);
        if (length < 4 || strcmp(entry->d_name + length - 4, ".bin") != 0) continue;
        ++count;
        char path[768];
        snprintf(path, sizeof(path), "%s/%s", root, entry->d_name);
        FILE *file = fopen(path, "rb");
        if (!file) continue;
        unsigned char data[0x250];
        size_t size = fread(data, 1, sizeof(data), file);
        fclose(file);
        if (size != sizeof(data) || memcmp(data, "AFE2", 4) != 0) {
            printf("FATAL %s: unknown binary format bytes=%zu\n", path, size); continue;
        }
        u32 description; memcpy(&description, data + 4, sizeof(description));
        printf("FATAL %s: description=0x%x program=%016llx pc=%016llx base=%016llx lr=%016llx sp=%016llx\n",
            path, description, (unsigned long long)read64(data, 0x8),
            (unsigned long long)read64(data, 0x110), (unsigned long long)read64(data, 0x118),
            (unsigned long long)read64(data, 0x100), (unsigned long long)read64(data, 0x108));
        u64 depth = read64(data, 0x140);
        if (depth > 32) depth = 32;
        for (u64 i = 0; i < depth; ++i)
            printf("  FATAL RETURN %02llu %016llx\n", (unsigned long long)i,
                   (unsigned long long)read64(data, 0x150 + i * 8));
    }
    closedir(dir);
}

int main(int argc, char **argv)
{
    (void)argc; (void)argv;
    consoleInit(NULL);
    int socket_fd = -1;
    if (__nxlink_host.s_addr && R_SUCCEEDED(socketInitializeDefault())) socket_fd = nxlinkStdio();
    printf("K/G startup log probe: no Mono/SDL/AOT, nxlink=%s\n", socket_fd >= 0 ? "connected" : "offline");
    tail("sdmc:/switch/KuroakiGimmick/UserData/mono-aot.log");
    tail("sdmc:/switch/KuroakiGimmick/UserData/aot-boot.log");
    tail("sdmc:/switch/KuroakiGimmick/UserData/settings.json");
    tail("sdmc:/switch/KuroakiGimmick/UserData/settings.json.tmp");
    rename_probe();
    reports("sdmc:/atmosphere/crash_reports");
    reports("sdmc:/atmosphere/fatal_reports");
    reports("sdmc:/atmosphere/fatal_errors");
    fatal_summaries();
    printf("Log inspection complete. Press Plus to return to hbmenu.\n");
    fflush(stdout);
    PadState pad;
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&pad);
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
        consoleUpdate(NULL);
        svcSleepThread(16000000);
    }
    if (socket_fd >= 0) { fflush(stdout); fflush(stderr); close(socket_fd); socketExit(); }
    consoleExit(NULL);
    return 0;
}
