#!/usr/bin/env python3
"""Pixel regressions through the actual SDL/OpenGL renderer (Python 3 stdlib).

python3 scripts/dev/check-rendering.py bin/Release/net8.0/KuroakiGimmick.dll
Set --dotnet for a private runtime; offscreen SDL/Mesa can be selected via env.
Only synthetic charts are created, in a temporary directory. No song assets.
"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import struct
import zlib


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("assembly", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    assembly = args.assembly.resolve()
    checks = 0

    def check(ok, message):
        nonlocal checks
        if not ok:
            raise AssertionError(message)
        checks += 1
        print("PASS", message, flush=True)

    with tempfile.TemporaryDirectory(prefix="scarlet-render-") as folder:
        root = Path(folder)
        (root / "fixture.sgv.json").write_text(json.dumps({
            "Chart": "fixture.vsc", "Gimmick": "fixture.vsm", "Bpm": 120,
            "ScrollSpeed": 1, "PostProcessing": False, "GameUiEnabled": False,
        }))

        def render(notes="", mods=(), time=1, obj="obj_base_gimmick"):
            (root / "fixture.vsc").write_text(notes)
            (root / "fixture.vsm").write_text(f"!obj:{obj}\n0,0,linear,0,0,holdoverlayalpha,-1\n" + "\n".join(
                f"0,0,linear,{0 if name=='plaudite_pburst' else value},{value},{name},{proxy}"
                for name, value, proxy in mods))
            result = subprocess.run([
                args.dotnet, str(assembly), "--snapshot", str(root / "fixture.sgv.json"),
                "--scene", "--time", str(time), "--out", str(root / "frame.ppm"),
            ], capture_output=True, text=True)
            if result.returncode:
                raise RuntimeError(result.stdout + result.stderr)
            magic, size, maximum, pixels = (root / "frame.ppm").read_bytes().split(b"\n", 3)
            assert (magic, size, maximum, len(pixels)) == (b"P6", b"320 180", b"255", 320*180*3)
            return pixels

        def pixel(frame, x, y):
            index = (y * 320 + x) * 3
            return tuple(frame[index:index+3])

        def changed(a, b):
            return [(index // 3 % 320, index // 3 // 320)
                    for index in range(0, len(a), 3) if a[index:index+3] != b[index:index+3]]

        identity = (("pra", 1, 0),)
        full = render()
        half = render(mods=(("bgalph", .5, -1),))
        check(all(pixel(full, x, y) == (255, 255, 255)
                  for x in (114, 206) for y in range(165)), "both outer rails span the playfield y=0..164")
        check(all(max(abs(c - 128) for c in pixel(half, x, y)) <= 1
                  for x in (114, 206) for y in range(165)), "50% lane opacity is applied once throughout the playfield")
        check(all(pixel(full, x, y) == color for x, color in
                  ((137, (0, 59, 99)), (160, (69, 51, 124)), (183, (96, 0, 58)))
                  for y in (0, 9, 100, 158, 164)), "three lane separators span the playfield")
        check(not any(full[165*320*3:]), "lane leaves the song footer y=165..179 clear")
        check(full == render(mods=identity), "identity proxy equals the direct field")
        check(half == render(mods=identity+(("bgalph", .5, -1),)), "proxy preserves lane opacity")
        shifted=render(mods=identity+(("pry",-20,0),))
        check(shifted[165*320*3:]==full[165*320*3:],"proxy motion leaves the original bottom strip in place")
        hidden_ui=render(mods=identity+(("uialpha",0,-1),))
        check(not any(hidden_ui[165*320*3:]),"uialpha hides the bottom UI strip after proxy composition")
        hidden_base=render(mods=identity+(("hom",1,-1),))
        check(not any(hidden_base[165*320*3:]),"hom hides the untransformed bottom strip")
        gray = render(mods=(("holdoverlayalpha", 1, -1),))
        check(pixel(gray, 115, 145) == (39, 39, 39) and pixel(gray, 137, 145) == (64, 64, 64)
              and pixel(gray, 160, 145) == (86, 86, 86), "original gray judgment texture is drawn")
        check(pixel(gray, 115, 143) == pixel(full, 115, 143)
              and pixel(gray, 115, 144) == (255, 255, 255), "original judgment line is at y=144")

        expired = "\n".join(("1000,0,0", "1000,1,0", "500,2,1,1000", "1000,6,2", "1000,7,1", "1000,8,2"))
        # 判定特效接管了音符结束后的那几帧（指示框 .5 秒、钻尘最长 75/240 秒），所以比较点挪到全部特效过期之后；
        # 音符本身没有消失的回归依旧会在这一帧被抓到。
        check(render(expired, time=1.5) == full, "ended chip/hold/bumper/mine notes and their hit effects both expire")
        check(render(expired, time=1+1/60) != full, "the judged frame still draws the hit effect")
        check(render(expired, time=.95) != full, "notes remain visible before their end")
        active = "500,2,0,1500"
        hold = render(active)
        check(any(y < 147 for _, y in changed(hold, full)), "active hold body remains above the receptor")
        check(all(y < 144 for _, y in changed(hold, full)), "pressed hold has no start cap below the judgment line")

        # Open the proxy crop and move it upwards, exposing pixels below the
        # normal field. This catches leaks hidden by the usual y=165 composite.
        exposed = identity+(("prct", 0, 0), ("pry", -40, 0))
        for title, notes, extra in (
            ("rotated chip", "1200,0,0", (("yoffset", 400, -1), ("noterot", 90, -1))),
            ("hold body and caps", "1200,2,0,1700", (("yoffset", 600, -1),)),
        ):
            mods = exposed+extra
            base, frame = render(mods=mods), render(notes, mods=mods)
            delta = changed(base, frame)
            check(bool(delta) and all(y < 125 for _, y in delta),
                  title+" clips at the local bottom before proxy translation")

        # 拿同一像素的满不透明值当基准，而不是写死颜色：音符皮肤换成真实素材后
        # 这里不再是灰的，三通道各不相同，关键在于 alpha 只乘了一次。
        alpha = (("bgalph", 0, -1), ("notealp", .5, -1))
        opaque = pixel(render("1500,0,0", mods=(("bgalph", 0, -1),)), 126, 97)
        check(any(opaque), "the opacity probe actually lands on the note")
        fading = render("1500,0,0", mods=alpha)
        check(all(abs(c - round(o * .5)) <= 1 for c, o in zip(pixel(fading, 126, 97), opaque)),
              "50% note opacity is applied once")
        fading_proxy = render("1500,0,0", mods=alpha+(("pra", .5, 0),))
        check(all(abs(c - round(o * .25)) <= 1 for c, o in zip(pixel(fading_proxy, 126, 97), opaque)),
              "proxy fade multiplies note opacity once")
        check(render(active) == hold, "backward seek reproduces identical hold pixels")
        check(pixel(hold,126,143)!=(0,0,0) and pixel(hold,126,144)==pixel(full,126,144),
              "pressed hold body ends at y=144 without a normal start cap")
        # endypos 比 chip 多一个 +6，所以尾帽的首行正好落在普通 chip 首行下面六像素处。
        # 比的是两张图的同一行像素，换皮肤不会失效；y=99 仍是空的把偏移钉死为 6 而不是 5。
        cap = render("1500,0,0")
        check(pixel(hold,126,100)==pixel(cap,126,94) and pixel(hold,126,99)==pixel(full,126,99),
              "remaining hold end cap keeps source six-pixel offset")
        bare=(("bgalph",0,-1),)
        base=render(mods=bare)
        rotated=changed(base,render("1500,1,0",mods=bare+(("noterot",90,-1),)))
        check((min(x for x,y in rotated),max(x for x,y in rotated))==(135,141),
              "90-degree bumper uses original one-pixel x compensation")
        rotated=changed(base,render("1500,0,0",mods=bare+(("noterot",180,-1),)))
        check((min(y for x,y in rotated),max(y for x,y in rotated))==(94,100),
              "180-degree chip uses original one-pixel y compensation")


        # Spatial jacket multiplication: every nonblack particle pixel is checked
        # against the cover at that pixel, not a particle centre or birth colour.
        def png(path, colour):
            def chunk(kind, data):
                return struct.pack(">I", len(data))+kind+data+struct.pack(">I", zlib.crc32(kind+data))
            rows = b"".join(b"\0"+bytes(c for x in range(320) for c in colour(x,y)) for y in range(180))
            path.write_bytes(b"\x89PNG\r\n\x1a\n"+chunk(b"IHDR", struct.pack(">IIBBBBB",320,180,8,2,0,0,0))+
                             chunk(b"IDAT",zlib.compress(rows))+chunk(b"IEND",b""))

        project = {"Chart":"fixture.vsc", "Gimmick":"fixture.vsm", "Bpm":120,
                   "Notes":False, "PostProcessing":False,"GameUiEnabled":False}
        (root / "fixture.sgv.json").write_text(json.dumps(project))
        (root / "cgmk_config.json").write_text('{"JACKET_MANAGE_MODE":"custom"}')
        def particles(mods=(), time=1):
            # Keep the playhead range beyond the burst lifetime; snapshots clamp
            # to the session duration just like the interactive viewer.
            return render(notes="10000,0,0", mods=mods, time=time, obj="obj_custom_gimmick")
        white = particles()
        check(any(white) and all(r==g==b for r,g,b in zip(white[::3],white[1::3],white[2::3])),
              "no-cover particle baseline uses white original sprite pixels")
        colour = lambda x,y: (255 if x%32<16 else 32, 255 if y%32<16 else 32, 128)
        png(root / "jacket.png", colour)
        for time in (1,1.4):
            # Obtain the matching untinted positions independently at each time.
            (root / "jacket.png").rename(root / "cover-disabled.png")
            baseline=particles(time=time)
            (root / "cover-disabled.png").rename(root / "jacket.png")
            tinted=particles(time=time)
            check(all(abs(tinted[(y*320+x)*3+c]-round(baseline[(y*320+x)*3+c]*colour(x,y)[c]/255))<=1
                      for y in range(180) for x in range(320) for c in range(3)),
                  f"jacket multiplies every current screen pixel at {time}s")
        check(particles()==particles(time=1), "jacket pixels reproduce after backward seek")
        png(root / "jacket[2].png", lambda x,y:(0,0,255))
        selected=particles((("custom_jacket",1.1,-1),))
        check(selected[::3]==bytes(320*180) and selected[1::3]==bytes(320*180) and selected[2::3]==white[2::3],
              "fractional custom_jacket selects ceil index and applies that external cover")
        check(particles((("custom_jacket",-1,-1),))==selected, "negative jacket index selects last slot")
        check(particles((("custom_jacket",3,-1),))==particles(), "jacket selection wraps using source array length")
        burstmods=(("particle_alpha",0,-1),("pburstspeed",25,-1),("plaudite_pburst",25,-1))
        burst=particles(burstmods,time=.1)
        check(any(burst) and not any(particles((("particle_alpha",0,-1),),time=.1)),
              "source burst remains visible when ordinary particle_alpha is zero")
        check(particles(burstmods+(("particlexpower",999,-1),("particleypower",999,-1)),time=.1)==burst,
              "burst object does not inherit ordinary particle motion powers")
        check(not any(particles(burstmods,time=2.1)), "burst is destroyed by its original two-second fade or bounds")
        check(particles((("df_whitebg",1,-1),))==bytes([255])*(320*180*3), "df_whitebg draws white after jacket tint")
        project["Notes"]=True
        (root / "fixture.sgv.json").write_text(json.dumps(project))
        with_lane=particles()
        check(all(pixel(with_lane,x,y)==(255,255,255) for x in (114,206) for y in range(165)),
              "jacket tint does not recolour the later original lane sprites")

    print(f"{checks} GPU pixel checks passed.")


if __name__ == "__main__":
    main()
