"""Scene builder C: s08 (gradient loudness) and s11 (measurement)."""
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from manim import *  # noqa
from style import *  # noqa
import numpy as np

AUDIO = str(pathlib.Path(__file__).resolve().parents[1] / "audio" / "{}.mp3")
S08_AUDIO = 60.12
S11_AUDIO = 43.848


def clear(scene, rt=0.5):
    mobs = [m for m in scene.mobjects]
    if mobs:
        scene.play(*[FadeOut(m) for m in mobs], run_time=rt, rate_func=smooth)


def header(s, size=34, color=WHITE):
    return chinese_text(s, size=size, color=color).to_edge(UP, buff=0.5)


def cross_mark(size=0.35, color=RED, sw=4):
    return VGroup(Line(UL * size, DR * size, color=color, stroke_width=sw),
                  Line(UR * size, DL * size, color=color, stroke_width=sw))


def check_mark(size=0.4, color=GREEN, sw=4):
    return VMobject(color=color, stroke_width=sw).set_points_as_corners(
        [LEFT * size + UP * 0.05, DOWN * size * 0.6 + LEFT * 0.1, RIGHT * size + UP * size * 0.9])


def bar(ax, x, y, width, color, y0=0.0, opacity=0.85):
    p0 = ax.c2p(x, y0)
    p1 = ax.c2p(x, y)
    h = max(abs(p1[1] - p0[1]), 0.001)
    r = Rectangle(width=width, height=h, stroke_width=0, fill_color=color, fill_opacity=opacity)
    r.move_to([p0[0], (p0[1] + p1[1]) / 2, 0])
    return r


def sup(e):
    """10^e with unicode superscripts (rendered with the CJK font)."""
    m = str.maketrans("-0123456789", "⁻⁰¹²³⁴⁵⁶⁷⁸⁹")
    return "10" + str(e).translate(m)


# =====================================================================================
class S08(StyledScene):
    def construct(self):
        self.add_sound(AUDIO.format("s08"), time_offset=0)

        # ---- act 1 (0.1-8.4): behaviour audit bars, to scale --------------------------
        h = header("行为审计（检查点 A，u5064）")
        g = make_axes(x_range=(0, 5, 1), y_range=(0, 500, 100), x_length=8.0, y_length=4.0,
                      y_label="次数", numbers=True, y_decimals=0)
        g.axes.x_numbers.set_opacity(0)
        g.move_to(DOWN * 0.3)
        ax = g.axes
        self.play(Write(h), Create(ax), FadeIn(VGroup(*g[2:]), g.axes.y_numbers), run_time=1.2, rate_func=smooth)
        specs = [(0.9, 453, BLUE, "休息"), (1.7, 0, YELLOW, "升级"),
                 (3.3, 203, GRAY, "可删牌"), (4.1, 0, RED, "删了")]
        bars, vals, names = VGroup(), VGroup(), VGroup()
        for x, v, c, n in specs:
            b = bar(ax, x, v, 0.6, c)
            bars.add(b)
            vals.add(number_text(v, size=26, color=c).next_to(b, UP, buff=0.12))
            names.add(chinese_text(n, size=26, color=c).next_to(ax.c2p(x, 0), DOWN, buff=0.2))
        grp1 = chinese_text("火堆", size=28, color=WHITE).next_to(names[0:2], DOWN, buff=0.25)
        grp2 = chinese_text("商店", size=28, color=WHITE).next_to(names[2:4], DOWN, buff=0.25)
        self.play(LaggedStart(*[GrowFromEdge(b, DOWN) for b in bars], lag_ratio=0.25),
                  FadeIn(names), FadeIn(grp1), FadeIn(grp2), run_time=2.0, rate_func=smooth)
        self.play(LaggedStart(*[Write(v) for v in vals], lag_ratio=0.2), run_time=1.2)
        note = label_text("塌缩在第 764 轮就已发生，早于宏动作", size=24).to_corner(UR, buff=0.6).shift(DOWN * 0.9)
        self.play(FadeIn(note, shift=UP * 0.1), run_time=0.8)

        # ---- act 2 (8.44-16.1): log ruler, P(smith) falls to 1e-38 --------------------
        self.wait_until(8.44)
        clear(self, 0.5)
        h2 = header("升级的概率（对数尺）")
        ruler = NumberLine(x_range=[-40, 0, 2], length=12.0, color=GRAY, stroke_width=2,
                           include_ticks=True, tick_size=0.06).move_to(DOWN * 0.2)
        tick_lbls = VGroup(*[chinese_text("1" if e == 0 else sup(e), size=24, color=GRAY)
                             .next_to(ruler.n2p(e), DOWN, buff=0.2) for e in (0, -10, -20, -30, -38)])
        self.play(Write(h2), Create(ruler), FadeIn(tick_lbls), run_time=1.2, rate_func=smooth)
        lp = ValueTracker(np.log10(0.5))
        dot = always_redraw(lambda: Dot(ruler.n2p(lp.get_value()), radius=0.09, color=YELLOW))
        tag = always_redraw(lambda: chinese_text("P(升级)", size=26, color=YELLOW)
                            .next_to(ruler.n2p(lp.get_value()), UP, buff=0.3))
        self.play(FadeIn(dot), FadeIn(tag), run_time=0.5)
        self.play(lp.animate.set_value(np.log10(1.4e-38)), run_time=3.2, rate_func=smooth)
        final = VGroup(chinese_text("P(升级) ≈ 1.4 × " + sup(-38), size=36, color=YELLOW),
                       label_text("（中位数）训练时永远抽不到", size=26)).arrange(DOWN, buff=0.25)
        final.next_to(ruler, DOWN, buff=1.0)
        self.play(Write(final), run_time=1.2)

        # ---- act 3 (16.2-20.4): four failed attempts -----------------------------------
        self.wait_until(16.2)
        clear(self, 0.4)
        h3 = header("强推探索：四次尝试")
        cards = VGroup()
        for name, why in [("E′", "KL 到 2.9×10¹²"), ("E2", "来回震荡"),
                          ("E3", "推力失控"), ("G", "抹掉已学会的升级")]:
            box = RoundedRectangle(width=2.9, height=2.0, corner_radius=0.12, stroke_color=GRAY, stroke_width=2)
            t = number_text(name, size=40, color=BLUE)
            w = chinese_text(why, size=24, color=GRAY)
            inner = VGroup(t, w).arrange(DOWN, buff=0.35).move_to(box)
            cards.add(VGroup(box, inner))
        cards.arrange(RIGHT, buff=0.35).move_to(DOWN * 0.2)
        self.play(Write(h3), LaggedStart(*[FadeIn(c, shift=UP * 0.2) for c in cards], lag_ratio=0.2),
                  run_time=1.6, rate_func=smooth)
        crosses = VGroup(*[cross_mark(0.3).next_to(c[0], UP, buff=-0.05).align_to(c[0], RIGHT).shift(LEFT * 0.15 + DOWN * 0.35)
                           for c in cards])
        self.play(LaggedStart(*[Create(x) for x in crosses], lag_ratio=0.25), run_time=1.2)

        # ---- act 4 (20.4-28.5): gradient magnitudes, log axis --------------------------
        self.wait_until(20.4)
        clear(self, 0.4)
        h4 = header("每一行对参数的梯度大小（中位数）")
        ax4 = Axes(x_range=[0, 4, 1], y_range=[-1, 3, 1], x_length=7.0, y_length=4.4, tips=False,
                   axis_config=dict(color=GRAY, stroke_width=2, include_ticks=True, tick_size=0.06))
        ax4.move_to(LEFT * 1.6 + DOWN * 0.4)
        ax4.x_axis.set_opacity(0)
        base4 = Line(ax4.c2p(0, -1), ax4.c2p(4, -1), color=GRAY, stroke_width=2)
        ylab = VGroup(*[chinese_text(sup(e) if e not in (0, 1) else ("1" if e == 0 else "10"), size=20, color=GRAY)
                        .next_to(ax4.c2p(0, e), LEFT, buff=0.15) for e in (-1, 0, 1, 2, 3)])
        ytitle = label_text("对数轴", size=22).next_to(ax4.y_axis, UP, buff=0.2).align_to(ax4.y_axis, LEFT)
        self.play(Write(h4), Create(ax4), Create(base4), FadeIn(ylab), FadeIn(ytitle), run_time=1.0)
        gb = VGroup(); gv = VGroup(); gn = VGroup()
        for x, v, c, n in [(0.8, 1.1, GRAY, "战斗动作"), (2.0, 238, YELLOW, "升级行"), (3.2, 754, RED, "删牌行")]:
            b = bar(ax4, x, np.log10(v), 0.7, c, y0=-1)
            gb.add(b)
            gv.add(number_text(v, size=26, color=c).next_to(b, UP, buff=0.12))
            gn.add(chinese_text(n, size=24, color=c).next_to(ax4.c2p(x, -1), DOWN, buff=0.2))
        self.play(GrowFromEdge(gb[0], DOWN), FadeIn(gn[0]), Write(gv[0]), run_time=1.0)
        self.play(GrowFromEdge(gb[1], DOWN), GrowFromEdge(gb[2], DOWN), FadeIn(gn[1:]), run_time=1.6, rate_func=smooth)
        self.play(Write(gv[1:]), run_time=0.8)
        side = VGroup(chinese_text("两三百倍", size=40, color=YELLOW),
                      chinese_text("而且在所有火堆决策上", size=26, color=WHITE),
                      chinese_text("方向近乎平行", size=26, color=WHITE)).arrange(DOWN, buff=0.25, aligned_edge=LEFT)
        side.next_to(ax4, RIGHT, buff=0.9).shift(UP * 0.5)
        self.wait_until(25.3)
        self.play(Write(side), run_time=1.5)

        # ---- act 5 (28.5-39.1): Adam knobs + nat ruler ---------------------------------
        self.wait_until(28.5)
        clear(self, 0.4)
        h5 = header("Adam 像自动音量：每个旋钮都拧差不多一格")
        knobs = VGroup()
        pointers = VGroup()
        for i in range(12):
            c = Circle(radius=0.32, color=PALE_BLUE, stroke_width=2.5)
            knobs.add(c)
        knobs.arrange(RIGHT, buff=0.32).move_to(UP * 1.4)
        for c in knobs:
            p = Line(c.get_center(), c.get_center() + UP * 0.28, color=YELLOW, stroke_width=3)
            pointers.add(p)
        sub = label_text("逐参数归一化 + 全局裁剪 0.5：不管梯度多大，步子都差不多", size=24).next_to(knobs, DOWN, buff=0.4)
        self.play(Write(h5), LaggedStart(*[Create(c) for c in knobs], lag_ratio=0.05),
                  FadeIn(pointers), run_time=1.4)
        self.play(*[Rotate(p, angle=-PI / 3, about_point=c.get_center()) for p, c in zip(pointers, knobs)],
                  FadeIn(sub), run_time=1.2, rate_func=smooth)
        # ruler in nats, to scale
        nl = NumberLine(x_range=[0, 13, 1], length=11.5, color=GRAY, stroke_width=2,
                        include_numbers=False, tick_size=0.06).move_to(DOWN * 1.3)
        nlab = VGroup(*[number_text(v, size=20, color=GRAY).next_to(nl.n2p(v), DOWN, buff=0.15) for v in range(0, 14, 2)])
        unit = label_text("一步后 log 概率的变化（nat）", size=22).next_to(nl, DOWN, buff=0.55).align_to(nl, RIGHT)
        self.wait_until(32.0)
        self.play(Create(nl), FadeIn(nlab), FadeIn(unit), run_time=0.8)
        step = 12.2 / 12
        arrows = VGroup(*[Arrow(nl.n2p(i * step) + UP * 0.45, nl.n2p((i + 1) * step) + UP * 0.45, buff=0,
                                stroke_width=4, color=YELLOW, max_tip_length_to_length_ratio=0.25,
                                tip_length=0.14) for i in range(12)])
        self.wait_until(33.6)
        self.play(LaggedStart(*[GrowArrow(a) for a in arrows], lag_ratio=0.6), run_time=2.6)
        big = chinese_text("+12.2 nat ≈ e¹² ≈ 16 万倍", size=30, color=YELLOW).next_to(arrows, UP, buff=0.25).align_to(arrows, RIGHT)
        self.play(Write(big), run_time=1.0)
        sgd = Arrow(nl.n2p(0) + DOWN * 1.3, nl.n2p(0.55) + DOWN * 1.3, buff=0, stroke_width=4, color=GREEN,
                    tip_length=0.12, max_tip_length_to_length_ratio=0.5)
        sgd_tick = DashedLine(nl.n2p(0), nl.n2p(0) + DOWN * 1.4, color=GRAY, stroke_width=1.5)
        sgd_l = chinese_text("对照：普通 SGD 小步（lr 1e-5）只有 +0.55 nat（同一把尺）", size=24, color=GREEN).next_to(sgd, RIGHT, buff=0.3)
        self.play(Create(sgd_tick), GrowArrow(sgd), FadeIn(sgd_l), run_time=1.0)

        # ---- act 6 (39.15-43.5): the rope circle ---------------------------------------
        self.wait_until(39.15)
        clear(self, 0.4)
        R = 1.3
        center = LEFT * 2.2 + DOWN * 0.3
        rope = Circle(radius=R, color=PALE_BLUE, stroke_width=2.5).move_to(center)
        rl = label_text("绳圈半径：±1 nat", size=26, color=PALE_BLUE).move_to(RIGHT * 3.6 + UP * 2.0)
        rng = np.random.default_rng(7)
        n = 30
        angles = rng.uniform(0, TAU, n)
        outside = np.array([True] * 27 + [False] * 3)
        rng.shuffle(outside)
        radii = np.where(outside, rng.uniform(1.25, 2.3, n), rng.uniform(0.3, 0.8, n)) * R
        dots = VGroup(*[Dot(center, radius=0.05, color=GREEN) for _ in range(n)])
        frac = ValueTracker(0)
        ctr = always_redraw(lambda: VGroup(label_text("被截断的样本", size=26, color=GRAY),
                                           number_text(f"{frac.get_value():.2f}", size=40, color=RED))
                            .arrange(DOWN, buff=0.2).move_to(RIGHT * 3.6 + UP * 0.6))
        self.play(Create(rope), FadeIn(rl), FadeIn(dots), FadeIn(ctr), run_time=0.6)
        anims = []
        for d, a, r, o in zip(dots, angles, radii, outside):
            target = center + r * np.array([np.cos(a), np.sin(a), 0])
            anims.append(d.animate.move_to(target).set_color(RED if o else GREEN))
        self.play(*anims, frac.animate.set_value(0.90), run_time=1.5, rate_func=smooth)
        meas = chinese_text("实测 push_clamped 0.89–0.93", size=24, color=WHITE).next_to(ctr, DOWN, buff=0.4)
        rest = chinese_text("剩下的乱走", size=24, color=GREEN).next_to(meas, DOWN, buff=0.25)
        inner = [d for d, o in zip(dots, outside) if not o]
        wander = [d.animate.shift(rng.uniform(-0.35, 0.35, 3) * np.array([1, 1, 0])) for d in inner]
        self.play(*wander, FadeIn(meas), FadeIn(rest), run_time=1.0)

        # ---- act 7 (43.6-50.4): line G erased the learned skill ------------------------
        self.wait_until(43.6)
        clear(self, 0.4)
        h7 = header("G 线：模型本来已经学会了有条件的升级")
        ax7 = NumberLine(x_range=[-30, 0, 5], length=4.6, color=GRAY, stroke_width=2, rotation=PI / 2,
                         include_ticks=True, tick_size=0.06).move_to(LEFT * 2.5 + DOWN * 0.5)
        l7 = VGroup(*[chinese_text("1" if e == 0 else sup(e), size=20, color=GRAY).next_to(ax7.n2p(e), LEFT, buff=0.2)
                      for e in (0, -10, -20, -30)])
        lt = label_text("升级概率（对数轴）", size=22).next_to(ax7, UP, buff=0.25)
        self.play(Write(h7), Create(ax7), FadeIn(l7), FadeIn(lt), run_time=1.0)
        lv = ValueTracker(np.log10(0.34))
        base = ax7.n2p(-30)

        def gbar():
            top = ax7.n2p(lv.get_value())
            hgt = max(top[1] - base[1], 0.001)
            return Rectangle(width=0.8, height=hgt, stroke_width=0, fill_color=YELLOW, fill_opacity=0.85
                             ).move_to([base[0] + 0.8, base[1] + hgt / 2, 0])
        gb7 = always_redraw(gbar)
        rd = always_redraw(lambda: VGroup(
            label_text(f"第 {int(round((lv.get_value() - np.log10(0.34)) / (np.log10(7e-28) - np.log10(0.34)) * 48))} 轮", size=26, color=GRAY),
            number_text(f"{10 ** lv.get_value():.1e}", size=36, color=YELLOW)).arrange(DOWN, buff=0.2, aligned_edge=LEFT)
            .move_to(RIGHT * 2.2 + UP * 0.6))
        self.play(FadeIn(gb7), FadeIn(rd), run_time=0.5)
        self.play(lv.animate.set_value(np.log10(7e-28)), run_time=3.0, rate_func=smooth)
        endl = VGroup(chinese_text("0.34 → 7×10⁻²⁸", size=30, color=YELLOW),
                      chinese_text("dev-128 读数掉到 0.398", size=26, color=RED)).arrange(DOWN, buff=0.25, aligned_edge=LEFT)
        endl.next_to(rd, DOWN, buff=0.6).align_to(rd, LEFT)
        self.play(FadeIn(endl, shift=UP * 0.1), run_time=0.8)

        # ---- act 8 (50.45-56.3): forced-choice negative bars ---------------------------
        self.wait_until(50.45)
        clear(self, 0.4)
        h8 = header("强制实验：无条件“总是做”，进度变化（A / C 检查点）", size=30)
        ax8 = Axes(x_range=[0, 4, 1], y_range=[-0.30, 0, 0.05], x_length=10.0, y_length=4.2, tips=False,
                   axis_config=dict(color=GRAY, stroke_width=2, include_ticks=True, tick_size=0.06))
        ax8.x_axis.set_opacity(0)
        ax8.move_to(DOWN * 0.5 + RIGHT * 0.3)
        zero = Line(ax8.c2p(0, 0), ax8.c2p(4, 0), color=WHITE, stroke_width=2)
        yl8 = VGroup(*[number_text(f"{v:+.2f}" if v else "0", size=18, color=GRAY).next_to(ax8.c2p(0, v), LEFT, buff=0.12)
                       for v in (0, -0.1, -0.2, -0.3)])
        self.play(Write(h8), Create(ax8.y_axis), Create(zero), FadeIn(yl8), run_time=0.9)
        data = [("总是升级", -0.101, -0.138), ("总是删牌", -0.053, -0.031),
                ("总是进精英", -0.093, -0.080), ("总是跳卡", -0.231, -0.250)]
        bars8, labs8, cats = VGroup(), VGroup(), VGroup()
        for i, (nm, a, c) in enumerate(data):
            xc = 0.5 + i
            for dx, v, col in ((-0.17, a, BLUE), (0.17, c, PALE_BLUE)):
                b = bar(ax8, xc + dx, v, 0.42, col)
                bars8.add(b)
                labs8.add(number_text(f"{v:.2f}".replace("-", "−"), size=18, color=col).next_to(b, DOWN, buff=0.1))
            cats.add(chinese_text(nm, size=24, color=WHITE).next_to(ax8.c2p(xc, 0), UP, buff=0.2))
        leg = VGroup(VGroup(Square(0.2, fill_color=BLUE, fill_opacity=0.85, stroke_width=0), label_text("A", size=22)).arrange(RIGHT, buff=0.12),
                     VGroup(Square(0.2, fill_color=PALE_BLUE, fill_opacity=0.85, stroke_width=0), label_text("C", size=22)).arrange(RIGHT, buff=0.12)
                     ).arrange(RIGHT, buff=0.4).to_corner(DR, buff=0.5)
        self.play(FadeIn(cats), LaggedStart(*[GrowFromEdge(b, UP) for b in bars8], lag_ratio=0.1),
                  FadeIn(leg), run_time=2.0, rate_func=smooth)
        allbelow = label_text("全部区间在 0 以下", size=24, color=RED).next_to(leg, LEFT, buff=0.6)
        self.play(FadeIn(labs8), FadeIn(allbelow), run_time=0.8)

        # ---- act 9 (56.35-59.6): conclusion card ---------------------------------------
        self.wait_until(56.35)
        clear(self, 0.4)
        left = VGroup(cross_mark(0.35), chinese_text("无条件强推", size=40, color=RED)).arrange(RIGHT, buff=0.35)
        right = VGroup(check_mark(0.4), chinese_text("学会什么时候做", size=40, color=GREEN)).arrange(RIGHT, buff=0.35)
        pair = VGroup(left, right).arrange(RIGHT, buff=1.6).move_to(UP * 0.4)
        foot = label_text("修法原型：推力从 Adam 拆出，按行梯度归一的小步（η = 0.5 nat），截断率降到 0",
                          size=22).to_edge(DOWN, buff=0.8)
        self.play(FadeIn(left, shift=RIGHT * 0.2), run_time=0.8)
        self.play(FadeIn(right, shift=LEFT * 0.2), FadeIn(foot), run_time=1.0)

        self.wait_until(S08_AUDIO + 0.1)
        clear(self, 0.7)
        self.pad_to(S08_AUDIO + 1.0)


# =====================================================================================
class S11(StyledScene):
    def construct(self):
        self.add_sound(AUDIO.format("s11"), time_offset=0)

        # ---- act 1 (0.1-7.6): jittering single readings, false new high ---------------
        h = header("dev-128 单点读数（示意）")
        g = make_axes(x_range=(0, 2000, 500), y_range=(0.34, 0.46, 0.02), x_length=10.0, y_length=4.4,
                      x_label="训练轮次", y_label="进度", y_decimals=2)
        g.move_to(DOWN * 0.4 + LEFT * 0.4)
        ax = g.axes
        self.play(Write(h), Create(ax), FadeIn(VGroup(*g[1:])), run_time=1.2, rate_func=smooth)
        true_y = 0.40
        band = Polygon(ax.c2p(0, true_y - 0.03), ax.c2p(2000, true_y - 0.03), ax.c2p(2000, true_y + 0.03),
                       ax.c2p(0, true_y + 0.03), stroke_width=0, fill_color=GRAY, fill_opacity=0.18)
        tline = DashedLine(ax.c2p(0, true_y), ax.c2p(2000, true_y), color=WHITE, stroke_width=2)
        tl = label_text("真实水平", size=22, color=WHITE).next_to(ax.c2p(2000, true_y), RIGHT, buff=0.15)
        bl = number_text("±0.03", size=24, color=GRAY).next_to(ax.c2p(2000, true_y + 0.03), RIGHT, buff=0.15)
        self.play(FadeIn(band), Create(tline), FadeIn(tl), FadeIn(bl), run_time=1.0)
        rng = np.random.default_rng(3)
        sig = 0.014
        e, ys = 0.0, []
        for i in range(20):
            e = 0.4 * e + np.sqrt(1 - 0.16) * sig * rng.standard_normal()
            ys.append(float(np.clip(e, -0.029, 0.029)))
        xs = [100 * (i + 1) for i in range(20)]
        ys = [true_y + v for v in ys]
        pts = VGroup(*[Dot(ax.c2p(x, y), radius=0.06, color=BLUE) for x, y in zip(xs, ys)])
        self.play(LaggedStart(*[FadeIn(p, scale=0.5) for p in pts], lag_ratio=0.15), run_time=2.5)
        imax = int(np.argmax(ys))
        bub = VGroup(chinese_text("新高！", size=28, color=YELLOW))
        bub.next_to(pts[imax], UP, buff=0.35)
        self.play(pts[imax].animate.set_color(YELLOW).scale(1.4), FadeIn(bub, shift=UP * 0.1), run_time=0.7)
        strike = Line(bub.get_left() + LEFT * 0.1, bub.get_right() + RIGHT * 0.1, color=RED, stroke_width=4)
        self.play(Create(strike), run_time=0.5)

        # ---- act 2 (7.66-16.6): adjacent correlation + needle ---------------------------
        self.wait_until(7.66)
        links = VGroup(*[Line(pts[i].get_center(), pts[i + 1].get_center(), color=BLUE, stroke_width=1.5, stroke_opacity=0.7)
                         for i in range(19)])
        corr = chinese_text("相邻检查点相关 r ≈ 0.40：抖动约六成来自检查点本身", size=26, color=PALE_BLUE)
        corr.next_to(h, DOWN, buff=0.25)
        self.play(FadeOut(bub), FadeOut(strike), pts[imax].animate.set_color(BLUE).scale(1 / 1.4),
                  Create(links), FadeIn(corr), run_time=1.5)
        self.wait_until(11.2)
        # a 0.01 effect, drawn to scale next to the band
        nx = ax.c2p(2000, 0)[0] + 1.1
        needle = Line([nx, ax.c2p(0, true_y)[1], 0], [nx, ax.c2p(0, true_y + 0.01)[1], 0], color=YELLOW, stroke_width=6)
        span = Line([nx - 0.3, ax.c2p(0, true_y - 0.03)[1], 0], [nx - 0.3, ax.c2p(0, true_y + 0.03)[1], 0], color=GRAY, stroke_width=3)
        nl = label_text("改动 ≈ 0.01", size=22, color=YELLOW).next_to(needle, DOWN, buff=0.15).shift(RIGHT * 0.05)
        self.play(FadeOut(tl), FadeOut(bl), run_time=0.3)
        self.play(Create(span), Create(needle), FadeIn(nl), run_time=1.0)
        wind = VGroup(chinese_text("像在风里", size=28, color=WHITE), chinese_text("听针掉", size=28, color=WHITE)
                      ).arrange(DOWN, buff=0.12).next_to(nl, DOWN, buff=0.5)
        self.play(FadeIn(wind), run_time=0.8)

        # ---- act 3 (16.65-25.6): 5-point window + pairing -------------------------------
        self.wait_until(16.65)
        self.play(FadeOut(wind), FadeOut(corr), FadeOut(links), FadeOut(needle), FadeOut(span), FadeOut(nl),
                  pts.animate.set_opacity(0.45), run_time=0.5)
        means = [float(np.mean(ys[i - 4:i + 1])) for i in range(4, 20)]
        mx = xs[4:]
        w = ValueTracker(4)

        def win():
            i = int(round(w.get_value()))
            x0, x1 = xs[i - 4] - 50, xs[i] + 50
            return Rectangle(width=ax.c2p(x1, 0)[0] - ax.c2p(x0, 0)[0], height=ax.c2p(0, 0.46)[1] - ax.c2p(0, 0.34)[1],
                             stroke_color=YELLOW, stroke_width=2, fill_color=YELLOW, fill_opacity=0.08
                             ).move_to([(ax.c2p(x0, 0)[0] + ax.c2p(x1, 0)[0]) / 2, ax.c2p(0, 0.40)[1], 0])

        def mdots():
            i = int(round(w.get_value()))
            return VGroup(*[Dot(ax.c2p(mx[k], means[k]), radius=0.09, color=YELLOW) for k in range(0, i - 3)])
        wr = always_redraw(win)
        md = always_redraw(mdots)
        wl = label_text("5 点窗口平均", size=24, color=YELLOW).next_to(h, DOWN, buff=0.25)
        self.add(md)
        self.play(FadeIn(wr), FadeIn(wl), run_time=0.4)
        self.play(w.animate.set_value(19), run_time=3.2, rate_func=linear)
        md.clear_updaters(); wr.clear_updaters()
        self.play(FadeOut(wr), run_time=0.3)
        band2 = Polygon(ax.c2p(0, true_y - 0.014), ax.c2p(2000, true_y - 0.014), ax.c2p(2000, true_y + 0.014),
                        ax.c2p(0, true_y + 0.014), stroke_width=0, fill_color=GREEN, fill_opacity=0.25)
        self.wait_until(21.0)
        pair_lbl = chinese_text("+ 两条线在同一批 128 个开局上逐局相减", size=26, color=WHITE).next_to(wl, RIGHT, buff=0.3)
        VGroup(wl, pair_lbl).arrange(RIGHT, buff=0.3).next_to(h, DOWN, buff=0.25)
        self.play(FadeIn(pair_lbl), wl.animate.move_to(VGroup(wl, pair_lbl).arrange(RIGHT, buff=0.3).next_to(h, DOWN, buff=0.25)[0].get_center()),
                  run_time=0.8)
        self.play(Transform(band, band2), run_time=1.5, rate_func=smooth)
        b2l = number_text("±0.03 → ±0.014", size=28, color=GREEN).next_to(ax.c2p(2000, true_y + 0.014), RIGHT, buff=0.1).shift(UP * 0.35 + LEFT * 1.2)
        self.play(Write(b2l), run_time=1.0)

        # ---- act 4 (25.6-37.4): macros-all reversal, forest plot to scale ---------------
        self.wait_until(25.6)
        clear(self, 0.5)
        h4 = header("反面教材：宏动作推广到所有界面（macros-all）", size=32)
        axf = NumberLine(x_range=[-0.04, 0.10, 0.02], length=7.0, color=GRAY, stroke_width=2,
                         include_ticks=True, tick_size=0.06).move_to(RIGHT * 2.5 + DOWN * 1.9)
        axl = VGroup(*[number_text(f"{v:+.2f}" if abs(v) > 1e-9 else "0", size=18, color=GRAY).next_to(axf.n2p(v), DOWN, buff=0.15)
                       for v in (-0.04, -0.02, 0, 0.02, 0.04, 0.06, 0.08, 0.10)])
        zl = DashedLine(axf.n2p(0) + UP * 4.0, axf.n2p(0), color=GRAY, stroke_width=1.5)
        self.play(Write(h4), Create(axf), FadeIn(axl), Create(zl), run_time=1.2)
        rows = [("① 单点 C − D", 0.049, (0.018, 0.080), "按规则采纳", GREEN),
                ("② 确认集 ab-512", -0.004, (-0.021, 0.013), "不显著", GRAY),
                ("③ 5 点窗口重读", 0.0195, None, "+0.0195", YELLOW),
                ("④ 换训练种子 C-rep − D", 0.003, (-0.027, 0.034), "不显著", GRAY)]
        ys_ = [1.6, 0.75, -0.1, -0.95]
        row_mobs = []
        for (lab, d, ci, verdict, col), yy in zip(rows, ys_):
            p = axf.n2p(d); y = yy
            dot = Dot([p[0], y, 0], radius=0.08, color=col if col != GRAY else PALE_BLUE)
            parts = [dot]
            if ci:
                l = Line([axf.n2p(ci[0])[0], y, 0], [axf.n2p(ci[1])[0], y, 0], color=PALE_BLUE, stroke_width=3)
                parts.insert(0, l)
            name = chinese_text(lab, size=26, color=WHITE).move_to([-5.0, y, 0], aligned_edge=LEFT)
            num = number_text(f"{d:+.3f}".replace("-", "−") if lab[0] != "③" else "+0.0195", size=22, color=col if col != GRAY else PALE_BLUE)
            num.next_to(dot, UP, buff=0.12)
            ver = chinese_text(verdict, size=22, color=col).next_to(name, DOWN, buff=0.1).align_to(name, LEFT) if lab[0] != "③" else VMobject()
            row_mobs.append(VGroup(*parts, name, num, ver))
        times = [26.8, 31.6, 31.9, 34.2]
        for rm, tt in zip(row_mobs, times):
            self.wait_until(tt)
            self.play(FadeIn(rm, shift=RIGHT * 0.15), run_time=0.8, rate_func=smooth)
        rej = chinese_text("合并 +0.003 → 判词 REJECT", size=28, color=RED).to_edge(DOWN, buff=0.5).to_edge(LEFT, buff=1.0)
        dflt = chinese_text("但它已经成了之后整条谱系的默认", size=24, color=RED)
        dflt.next_to(rej, UP, buff=0.2).align_to(rej, LEFT)
        rline = Line(dflt.get_left() + LEFT * 0.25 + DOWN * 0.15, dflt.get_left() + LEFT * 0.25 + UP * 0.6, color=RED, stroke_width=4)
        self.wait_until(35.6)
        self.play(Write(rej), FadeIn(dflt), Create(rline), run_time=1.2)

        # ---- act 5 (37.4-43.3): three rules -------------------------------------------
        self.wait_until(37.4)
        clear(self, 0.4)
        h5 = header("现在的规矩")
        rules = ["事先登记判据（预注册 JSON）", "两个训练种子同号才采纳", "确认集只在预注册点读一次"]
        stamps = VGroup()
        for i, r in enumerate(rules):
            t = chinese_text(r, size=34, color=WHITE)
            n = number_text(str(i + 1), size=30, color=YELLOW)
            row = VGroup(n, t).arrange(RIGHT, buff=0.4)
            box = SurroundingRectangle(row, color=TEAL, stroke_width=2, buff=0.22, corner_radius=0.08)
            stamps.add(VGroup(box, row))
        stamps.arrange(DOWN, buff=0.4, aligned_edge=LEFT).move_to(UP * 0.0)
        self.play(Write(h5), run_time=0.6)
        for s_, tt in zip(stamps, (38.0, 39.6, 41.0)):
            self.wait_until(tt)
            self.play(FadeIn(s_, scale=1.15), run_time=0.5, rate_func=smooth)
        foot = label_text("判词由 decide.py 算出：ADOPT / REJECT / CONTINUE / REPLICATE / KILL", size=22).to_edge(DOWN, buff=0.7)
        self.play(FadeIn(foot), run_time=0.6)

        self.wait_until(S11_AUDIO + 0.1)
        clear(self, 0.7)
        self.pad_to(S11_AUDIO + 1.0)
