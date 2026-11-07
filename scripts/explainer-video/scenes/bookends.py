"""Title lead-in (1.5 s) and end card (3.0 s), silent; audio added at normalise time."""
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from manim import *  # noqa
from style import *  # noqa


class Title(StyledScene):
    def construct(self):
        card = title_card("教 AI 爬塔", "静默猎手 · 进阶 10 · 训练手记", title_size=64)
        self.play(Write(card.title), run_time=0.8, rate_func=smooth)
        self.play(GrowFromCenter(card.rule), FadeIn(card.subtitle, shift=UP * 0.15),
                  run_time=0.4, rate_func=smooth)
        self.pad_to(1.5)


class EndCard(StyledScene):
    def construct(self):
        t = chinese_text("hack-sts2 · 2026-11", size=48, color=WHITE)
        rule = Line(LEFT, RIGHT, stroke_width=2, color=BLUE).set_width(4.0)
        sub = chinese_text("参考文献见附带 srt / 页面", size=28, color=GRAY)
        g = VGroup(t, rule, sub).arrange(DOWN, buff=0.35)
        self.play(FadeIn(t, shift=UP * 0.15), run_time=0.6, rate_func=smooth)
        self.play(GrowFromCenter(rule), FadeIn(sub), run_time=0.5, rate_func=smooth)
        self.wait_until(2.4)
        self.play(FadeOut(g), run_time=0.5, rate_func=smooth)
        self.pad_to(3.0)
