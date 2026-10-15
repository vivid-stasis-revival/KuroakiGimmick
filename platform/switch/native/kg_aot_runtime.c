/* Exact Full AOT metadata cache; user data and interpreter runtime are preserved. MIT. */
#ifdef KG_SWITCH_AOT
#include <switch.h>
#include <stdio.h>
#include <sys/stat.h>
#include <string.h>
#include <zlib.h>
#include "kg_aot_payload.h"
void kg_aot_stage(const char *stage);

static bool matches(const char *path, const struct kg_aot_file *expected)
{
    FILE *file = fopen(path, "rb");
    if (!file) return false;
    unsigned char block[32768];
    size_t total = 0, bytes;
    uLong crc = crc32(0, NULL, 0);
    while ((bytes = fread(block, 1, sizeof(block), file)) > 0) {
        total += bytes; crc = crc32(crc, block, bytes);
    }
    bool good = !ferror(file) && total == expected->size && crc == expected->crc;
    fclose(file);
    return good;
}

bool kg_aot_prepare(void)
{
    kg_aot_stage("mounting embedded ROMFS");
    Result result = romfsInit();
    if (R_FAILED(result)) {
        char error[96]; snprintf(error, sizeof(error), "ROMFS mount failed: 0x%08x", result);
        kg_aot_stage(error); return false;
    }
    mkdir(KG_AOT_RUNTIME_ROOT, 0777);
    for (unsigned int i = 0; i < sizeof(kg_aot_files) / sizeof(kg_aot_files[0]); ++i) {
        char target[512], source[512], temporary[520];
        kg_aot_stage(kg_aot_files[i].name);
        snprintf(target, sizeof(target), "%s/%s", KG_AOT_RUNTIME_ROOT, kg_aot_files[i].name);
        if (matches(target, &kg_aot_files[i])) continue;
        snprintf(source, sizeof(source), "romfs:/metadata/%s", kg_aot_files[i].name);
        snprintf(temporary, sizeof(temporary), "%s.nxpart", target);
        FILE *input = fopen(source, "rb");
        FILE *output = input ? fopen(temporary, "wb") : NULL;
        if (!input || !output) { if (input) fclose(input); return false; }
        unsigned char block[32768];
        size_t bytes;
        bool copied = true;
        while ((bytes = fread(block, 1, sizeof(block), input)) > 0) {
            if (fwrite(block, 1, bytes, output) != bytes) { copied = false; break; }
        }
        copied = copied && !ferror(input);
        fclose(input);
        if (fclose(output) != 0) copied = false;
        if (!copied || !matches(temporary, &kg_aot_files[i])) { remove(temporary); return false; }
        // This is an isolated, content-addressed cache belonging to this AOT build.
        remove(target);
        if (rename(temporary, target) != 0) return false;
    }
    FILE *config = fopen(KG_AOT_CONFIG_PATH, "wb");
    if (!config) return false;
    bool written = fwrite(kg_aot_config, 1, sizeof(kg_aot_config) - 1, config) == sizeof(kg_aot_config) - 1;
    if (fclose(config) != 0) written = false;
    romfsExit();
    return written;
}
#endif
