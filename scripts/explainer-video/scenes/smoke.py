import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from manim import *  # noqa
from style import *  # noqa


class Smoke(StyledScene):
    def construct(self):
        # 0.0 - 2.5  title
        card = title_card("教 AI 爬塔", "静默猎手 · 进阶 10")
        self.play(Write(card.title), run_time=1.5, rate_func=smooth)
        self.play(GrowFromCenter(card.rule), FadeIn(card.subtitle, shift=UP * 0.2),
                  run_time=1.0, rate_func=smooth)
        self.wait_until(3.0)
        self.play(card.animate.scale(0.55).to_edge(UP, buff=0.4), run_time=1.0, rate_func=smooth)

        # 4.0 - 7.5  axes + curve drawn to scale
        g = make_axes(x_range=(0, 12000, 3000), y_range=(0, 0.5, 0.1),
                      x_length=8.5, y_length=4.2, x_label="训练更新步数", y_label="通关进度")
        g.next_to(card, DOWN, buff=0.5).shift(LEFT * 0.6)
        ax = g.axes
        self.play(Create(ax), FadeIn(VGroup(*g[1:])), run_time=1.2, rate_func=smooth)

        f = lambda u: 0.42 * (1 - np.exp(-u / 3200.0))
        curve = ax.plot(f, x_range=[0, 12000], color=BLUE, stroke_width=3)
        t = ValueTracker(0)
        dot = always_redraw(lambda: Dot(ax.c2p(t.get_value(), f(t.get_value())), radius=0.06, color=YELLOW))
        readout = always_redraw(lambda: VGroup(
            label_text("进度", size=24),
            number_text(f"{f(t.get_value()):.3f}", size=32, color=YELLOW),
        ).arrange(RIGHT, buff=0.2).to_corner(UR, buff=0.8).shift(DOWN * 1.4))
        self.add(dot, readout)
        self.play(Create(curve), t.animate.set_value(12000), run_time=3.5, rate_func=smooth)

        # 8.7 - 10.5  highlight + caption-free label
        ref = DashedLine(ax.c2p(0, 0.404), ax.c2p(12000, 0.404), color=GREEN, stroke_width=2)
        ref_lbl = label_text("最好成绩 0.404", size=22, color=GREEN).next_to(ref, UP, buff=0.1).align_to(ref, RIGHT)
        self.play(Create(ref), Write(ref_lbl), run_time=1.2, rate_func=smooth)
        self.wait_until(10.8)
        self.play(FadeOut(Group(*self.mobjects)), run_time=1.0, rate_func=smooth)
        self.pad_to(12.0)
