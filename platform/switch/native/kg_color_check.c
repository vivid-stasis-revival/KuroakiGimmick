/* Inspect managed RGBA values before GPU upload. SPDX-License-Identifier: MPL-2.0 */
#include <stdint.h>
#include <mono/metadata/assembly.h>
#include <mono/metadata/class.h>
#include <mono/metadata/object.h>
#include "io_util.h"

static void report_color(MonoClass *klass, const char *name, MonoObject *value)
{
    const char *fields[] = {"<R>k__BackingField", "<G>k__BackingField",
                            "<B>k__BackingField", "<A>k__BackingField"};
    float rgba[4] = {0};
    if (!value) { io_debugf("[K/G color] %s returned null", name); return; }
    for (int i = 0; i < 4; ++i) {
        MonoClassField *field = mono_class_get_field_from_name(klass, fields[i]);
        if (!field) { io_debugf("[K/G color] missing field %s", fields[i]); return; }
        mono_field_get_value(value, field, &rgba[i]);
    }
    io_debugf("[K/G color] %s RGBA=%.9g %.9g %.9g %.9g", name,
              rgba[0], rgba[1], rgba[2], rgba[3]);
}

void kg_check_managed_colors(MonoAssembly *assembly)
{
    MonoClass *klass = mono_class_from_name(mono_assembly_get_image(assembly),
                                          "KuroakiGimmick.Graphics", "Color");
    if (!klass) { io_debugf("[K/G color] class unavailable"); return; }
    uint32_t white = 0xffffff;
    float alpha = 1;
    double packed = 16777215;
    const char *names[] = {"get_White", "Hex", "GameMaker"};
    int counts[] = {0, 2, 1};
    void *hex_args[] = {&white, &alpha}, *packed_args[] = {&packed};
    void **args[] = {NULL, hex_args, packed_args};
    for (int i = 0; i < 3; ++i) {
        MonoMethod *method = mono_class_get_method_from_name(klass, names[i], counts[i]);
        if (!method) { io_debugf("[K/G color] %s unavailable", names[i]); continue; }
        MonoObject *exception = NULL;
        MonoObject *value = mono_runtime_invoke(method, NULL, args[i], &exception);
        if (exception) { io_debugf("[K/G color] %s raised an exception", names[i]); continue; }
        report_color(klass, names[i], value);
    }
}
