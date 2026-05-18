#!/usr/bin/env python3
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "assets" / "demo" / "self-host-cycle.gif"
OUT.parent.mkdir(parents=True, exist_ok=True)

WIDTH = 1400
HEIGHT = 900
BG = "#0b1220"
PANEL = "#111827"
FG = "#d1fae5"
MUTED = "#93c5fd"
ACCENT = "#34d399"


def load_font(size: int):
    candidates = [
        "/usr/local/share/fonts/DejaVuSansMono.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/dejavu/DejaVuSansMono.ttf",
    ]
    for candidate in candidates:
        path = Path(candidate)
        if path.exists():
            return ImageFont.truetype(str(path), size=size)
    return ImageFont.load_default()


TITLE_FONT = load_font(28)
BODY_FONT = load_font(24)


def terminal_frame(title: str, body: str):
    image = Image.new("RGB", (WIDTH, HEIGHT), BG)
    draw = ImageDraw.Draw(image)

    pad = 40
    draw.rounded_rectangle((pad, pad, WIDTH - pad, HEIGHT - pad), radius=24, fill=PANEL)

    draw.text((pad + 24, pad + 18), title, font=TITLE_FONT, fill=MUTED)

    y = pad + 72
    line_height = 32
    for line in body.splitlines():
        color = FG
        if line.startswith("$ "):
            color = ACCENT
        draw.text((pad + 24, y), line, font=BODY_FONT, fill=color)
        y += line_height
    return image


frames = [
    (
        "ll-lang self-host cycle",
        """$ lllc --help
lllc build|check|run|self|mcp ...

$ cat > hello.lll
module Hello

add(a Int)(b Int) Int = a + b
greet(name Str) Str = strConcat "Hello, " name
factorial(n Int) Int =
  match n
    | 0 -> 1
    | _ -> n * factorial (n - 1)""",
        2200,
    ),
    (
        "compile the same source to TypeScript",
        """$ lllc build --target ts hello.lll
Built project 'hello' [typescript] -> hello.ts

$ cat hello.ts
export function add(a: number) {
  return (b: number) => a + b;
}

export function greet(name: string): string {
  return "Hello, " + name;
}""",
        2600,
    ),
    (
        "compile the same source to Python",
        """$ lllc build --target py hello.lll
Built project 'hello' [python] -> hello.py

# same ll-lang file, second target emitted successfully""",
        2000,
    ),
    (
        "self-host check",
        """$ lllc self check lllcself/src/Main.lll
OK 1 external fn
OK 2 external no args
OK 3 external platform
...
OK 22 project graph
OK 23 module path
OK 24 mixed precedence
Done
{"ok":true,"stage":"ok","primary_error":"","secondary_count":0}""",
        5600,
    ),
    (
        "fixpoint + multi-target compiler path",
        """compiler1 == compiler2
self-hosted compiler CLI: ~2600 lines
same source -> F# / TS / Python / Java / C#

$ ./tools/demo-self-host.sh
~24s total demo path""",
        2600,
    ),
]

images = [terminal_frame(title, body) for title, body, _ in frames]
durations = [duration for _, _, duration in frames]

images[0].save(
    OUT,
    save_all=True,
    append_images=images[1:],
    duration=durations,
    loop=0,
    disposal=2,
    optimize=False,
)

print(OUT)
