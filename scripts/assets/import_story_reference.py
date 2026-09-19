"""Import read-only UTMT dialogue exports into private Assets (no game writes)."""
import json
import re
import shutil
import sys
from pathlib import Path

source, assets = map(Path, sys.argv[1:])
manifest = assets / "Gimmicks/obj_extendnova_gimmick/manifest.json"
data = json.loads(manifest.read_text())
cues, windows = [], []
for section in range(1, 9):
    code = (source / f"gml_GlobalScript_ss_en_story{section}.gml").read_text()
    trigger = f"setup_co_en_s{section}"
    window = {"trigger": trigger}
    speaker = ""
    blocks = re.split(r"return c_playm\((\d+), (\d+), (\d+)\);", code)
    for index in range(1, len(blocks), 4):
        minute, second, millis = map(int, blocks[index:index + 3])
        time = minute * 60 + second + millis / 1000
        body = blocks[index + 3]
        if "instance_create_depth" in body and "o_textbox" in body:
            window["start"] = time
        name = re.search(r'name_set\(("(?:[^"\\]|\\.)*")\)', body)
        if name:
            speaker = json.loads(name[1])
        text = re.search(r'\btext\(("(?:[^"\\]|\\.)*")\);', body)
        if text:
            cues.append(dict(time=time, trigger=trigger, speaker=speaker, text=json.loads(text[1])))
        if "text_clear();" in body:
            window["clear"] = time
        if "instance_destroy(o_textbox)" in body:
            window["destroy"] = time
    assert "start" in window
    windows.append(window)
assert len(cues) == 70 and len(windows) == 8
data["sequence"]["story"] = cues
data["sequence"]["storyWindows"] = windows
manifest.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
ui_path = assets / "GameUI/game-ui.gameui.json"
ui = json.loads(ui_path.read_text())
sprites = json.loads((source / "sprites.json").read_text())
for sprite in sprites.values():
    for frame in sprite["frames"]:
        shutil.copy2(source / frame, ui_path.parent / frame)
ui["sprites"].update(sprites)
ui_path.write_text(json.dumps(ui, ensure_ascii=False, separators=(",", ":")))
print(f"Imported {len(cues)} cues, {len(windows)} textbox lifetimes and original dialogue sprites.")
