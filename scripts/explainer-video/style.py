"""Shared 3Blue1Brown-style toolkit for the film.

Usage in a scene file (scenes/*.py):
    import sys, pathlib; sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
    from style import *
    class S01(StyledScene):
        def construct(self):
            self.wait_until(0.0) ...
"""
from __future__ import annotations

from manim import (
    Axes, Scene, Text, VGroup, Line, Write, FadeIn, FadeOut, Create,
    config, smooth, DOWN, UP, LEFT, RIGHT, ORIGIN,
)

# ---------------------------------------------------------------- palette
BG = "#0f1419"          # dark blue-charcoal, never pure black
BLUE = "#58C4DD"
PALE_BLUE = "#9CDCEB"
YELLOW = "#FFD35A"
GREEN = "#83C167"
RED = "#FC6255"
TEAL = "#5CD0B3"
GRAY = "#9AA5B1"        # secondary text
WHITE = "#ECEFF1"       # primary text (soft white, not #fff)
GRID = "#2a3440"        # faint gridlines / guides

PALETTE = dict(BG=BG, BLUE=BLUE, PALE_BLUE=PALE_BLUE, YELLOW=YELLOW, GREEN=GREEN,
               RED=RED, TEAL=TEAL, GRAY=GRAY, WHITE=WHITE, GRID=GRID)

# ---------------------------------------------------------------- fonts
# Verified via manimpango.list_fonts() on this Mac (Pango 1.58.2, CoreText):
CJK_FONT = "PingFang SC"     # fallback order if absent: "Hiragino Sans GB", "Noto Sans CJK SC"
MONO_FONT = "Menlo"          # tabular digits for counters / tables

STROKE = 2.5                 # default line width (px at 1080p is ~ this * 1.0)

def _pick_font(preferred: str, fallbacks: tuple[str, ...]) -> str:
    try:
        import manimpango
        avail = set(manimpango.list_fonts())
        for f in (preferred, *fallbacks):
            if f in avail:
                return f
    except Exception:
        pass
    return preferred

CJK_FONT = _pick_font(CJK_FONT, ("Hiragino Sans GB", "Noto Sans CJK SC", "Heiti SC"))
MONO_FONT = _pick_font(MONO_FONT, ("DejaVu Sans Mono", "Noto Sans Mono"))

config.background_color = BG


# ---------------------------------------------------------------- text helpers
def chinese_text(s: str, size: float = 36, color: str = WHITE, weight: str = "NORMAL", **kw) -> Text:
    """Chinese/mixed text in the CJK font. size is font_size (manim units, 48 = default)."""
    return Text(s, font=CJK_FONT, font_size=size, color=color, weight=weight, **kw)


def number_text(s, size: float = 36, color: str = YELLOW, **kw) -> Text:
    """Numbers in a monospace face so digits do not jitter while counting."""
    return Text(str(s), font=MONO_FONT, font_size=size, color=color, **kw)


def label_text(s: str, size: float = 24, color: str = GRAY, **kw) -> Text:
    """Small secondary label (axis labels, sources)."""
    return chinese_text(s, size=size, color=color, **kw)


# ---------------------------------------------------------------- axes
def make_axes(x_range=(0, 10, 1), y_range=(0, 1, 0.2), x_length=9.0, y_length=4.5,
              x_label: str | None = None, y_label: str | None = None,
              x_decimals: int = 0, y_decimals: int = 1, numbers: bool = True,
              color: str = GRAY) -> VGroup:
    """Axes in house style, with tick numbers in MONO_FONT and CJK axis labels.

    Returns VGroup(axes, labels...) with .axes attribute for coords (axes.c2p / plot)."""
    ax = Axes(
        x_range=list(x_range), y_range=list(y_range),
        x_length=x_length, y_length=y_length,
        tips=False,
        axis_config=dict(color=color, stroke_width=2, include_ticks=True,
                         tick_size=0.06, include_numbers=False),
    )
    group = VGroup(ax)
    if numbers:
        def _fmt(v, d):
            return f"{v:.{d}f}"
        xs = _frange(*x_range)
        ys = _frange(*y_range)
        xn = VGroup(*[number_text(_fmt(v, x_decimals), size=18, color=GRAY)
                      .next_to(ax.c2p(v, y_range[0]), DOWN, buff=0.15) for v in xs])
        yn = VGroup(*[number_text(_fmt(v, y_decimals), size=18, color=GRAY)
                      .next_to(ax.c2p(x_range[0], v), LEFT, buff=0.15) for v in ys])
        group.add(xn, yn)
        ax.x_numbers, ax.y_numbers = xn, yn
    if x_label:
        xl = label_text(x_label, size=22).next_to(ax.x_axis.get_end(), DOWN, buff=0.45).align_to(ax.x_axis.get_end(), RIGHT)
        group.add(xl); ax.x_label_mob = xl
    if y_label:
        yl = label_text(y_label, size=22).next_to(ax.y_axis.get_end(), UP, buff=0.2).align_to(ax.y_axis, LEFT)
        group.add(yl); ax.y_label_mob = yl
    group.axes = ax
    return group


def _frange(a, b, step):
    out, v, n = [], a, 0
    while v <= b + 1e-9:
        out.append(round(v, 10)); n += 1; v = a + n * step
    return out


# ---------------------------------------------------------------- title card
def title_card(title: str, subtitle: str | None = None, title_size: float = 54,
               subtitle_size: float = 28) -> VGroup:
    """Centered title (+ optional gray subtitle) with a thin underline rule."""
    t = chinese_text(title, size=title_size, color=WHITE)
    parts = [t]
    rule = Line(LEFT, RIGHT, stroke_width=2, color=BLUE)
    rule.set_width(max(t.width * 0.6, 2.0))
    parts.append(rule)
    if subtitle:
        parts.append(chinese_text(subtitle, size=subtitle_size, color=GRAY))
    g = VGroup(*parts).arrange(DOWN, buff=0.35)
    g.title, g.rule = t, rule
    g.subtitle = parts[2] if subtitle else None
    return g


# ---------------------------------------------------------------- scene base
class StyledScene(Scene):
    """Scene with the house background and an audio-sync clock.

    self.t0      scene-local time origin (seconds of renderer time when construct began)
    self.now()   seconds elapsed since t0
    self.wait_until(sec)  wait until `sec` seconds into the scene (narration timestamp);
                          no-op (with a warning) if animations already ran past it.
    self.beat(sec, *anims, **kw)  wait_until(sec) then self.play(*anims, **kw)
    """

    def setup(self):
        super().setup()
        self.camera.background_color = BG
        self.t0 = self.renderer.time

    def now(self) -> float:
        return self.renderer.time - self.t0

    def wait_until(self, sec: float, min_wait: float = 0.0):
        dt = sec - self.now()
        if dt > 1e-3:
            self.wait(dt)
        else:
            if dt < -0.05:
                print(f"[wait_until] {type(self).__name__}: already at {self.now():.2f}s > {sec:.2f}s")
            if min_wait > 0:
                self.wait(min_wait)

    def beat(self, sec: float, *anims, **kw):
        self.wait_until(sec)
        if anims:
            kw.setdefault("rate_func", smooth)
            self.play(*anims, **kw)

    def pad_to(self, total: float):
        """Hold the last frame so the scene lasts exactly `total` seconds (match audio)."""
        self.wait_until(total)


__all__ = [n for n in dir() if not n.startswith("_")] + ["_pick_font"]
