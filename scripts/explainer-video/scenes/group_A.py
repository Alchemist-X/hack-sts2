"""Scene group A: s01 (opening tower), s02 (hard problem + parity shield), s04 (simulator)."""
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from manim import *  # noqa
from style import *  # noqa

AUDIO = str(pathlib.Path(__file__).resolve().parents[1] / "audio")
DUR = {"s01": 16.752, "s02": 35.424, "s04": 39.408}


def capsule(s, color=BLUE, size=28):
    t = chinese_text(s, size=size, color=color)
    r = RoundedRectangle(width=t.width + 0.5, height=t.height + 0.32, corner_radius=0.22,
                         stroke_color=color, stroke_width=STROKE)
    return VGroup(r, t.move_to(r))


def card_shape(w=0.55, h=0.8, color=GRAY, fill=None, fill_opacity=0.0):
    r = RoundedRectangle(width=w, height=h, corner_radius=0.06, stroke_color=color, stroke_width=STROKE)
    if fill:
        r.set_fill(fill, fill_opacity)
    return r


def ending(scene, sid):
    d = DUR[sid]
    scene.wait_until(d)
    mobs = [m for m in scene.mobjects]
    if mobs:
        scene.play(*[FadeOut(m) for m in mobs], run_time=0.8, rate_func=smooth)
    scene.wait_until(d + 1.0)


class LogGauge(VGroup):
    """Dial on a log10 scale from 10^0 to 10^6; needle driven by a ValueTracker (log10 value)."""
    A0, SWEEP = 210 * DEGREES, 240 * DEGREES

    def __init__(self, radius=1.6, color=BLUE, **kw):
        super().__init__(**kw)
        self.radius, self.color_ = radius, color
        self.tracker = ValueTracker(0.0)
        arc = Arc(radius=radius, start_angle=self.A0, angle=-self.SWEEP, color=GRAY, stroke_width=STROKE)
        self.add(arc)
        labels = ["1", "10", "100", "1k", "10k", "100k", "1M"]
        for i, lab in enumerate(labels):
            a = self.ang(i)
            d = np.array([np.cos(a), np.sin(a), 0])
            self.add(Line(d * radius * 0.9, d * radius, color=GRAY, stroke_width=2))
            self.add(number_text(lab, size=18, color=GRAY).move_to(d * radius * 0.72))
        self.center_dot = Dot(ORIGIN, radius=0.06, color=color)
        self.add(self.center_dot)

    def ang(self, v):
        return self.A0 - (v / 6.0) * self.SWEEP

    def needle(self):
        def mk():
            a = self.ang(self.tracker.get_value())
            c = self.center_dot.get_center()
            d = np.array([np.cos(a), np.sin(a), 0])
            return Line(c, c + d * self.radius * 0.82, color=self.color_, stroke_width=3)
        return always_redraw(mk)


# =====================================================================  S01
class S01(StyledScene):
    def construct(self):
        self.add_sound(f"{AUDIO}/s01.mp3")
        # tower: 3 acts, heights proportional to rooms (17, 16, 16 of 49 floors)
        H, base_y, x0, W = 5.0, -3.1, -4.3, 2.2
        rooms = [17, 16, 16]
        names = ["第一幕", "第二幕", "第三幕"]
        acts, labels, y = VGroup(), VGroup(), base_y
        for n, nm in zip(rooms, names):
            h = H * n / 49
            r = Rectangle(width=W, height=h, stroke_color=PALE_BLUE, stroke_width=STROKE)
            r.move_to([x0, y + h / 2, 0])
            acts.add(r)
            labels.add(label_text(nm, size=24, color=GRAY).move_to(r))
            y += h
        top = y
        # boss silhouette: horned head shape above the tower
        boss = VGroup(
            Circle(radius=0.32, color=RED, stroke_width=STROKE),
            Polygon([-0.28, 0.18, 0], [-0.45, 0.55, 0], [-0.1, 0.3, 0], color=RED, stroke_width=STROKE),
            Polygon([0.28, 0.18, 0], [0.45, 0.55, 0], [0.1, 0.3, 0], color=RED, stroke_width=STROKE),
        ).move_to([x0, top + 0.5, 0])
        self.wait_until(0.1)
        for r, l in zip(acts, labels):
            self.play(Create(r), FadeIn(l), run_time=0.8, rate_func=smooth)
        self.play(Create(boss), run_time=0.6, rate_func=smooth)

        # title
        card = title_card("教 AI 爬塔", "静默猎手 · 进阶 10", title_size=56, subtitle_size=32)
        card.move_to([2.6, 1.9, 0])
        self.wait_until(3.04)
        self.play(Write(card.title), run_time=1.4, rate_func=smooth)
        self.play(GrowFromCenter(card.rule), FadeIn(card.subtitle, shift=UP * 0.3), run_time=1.0, rate_func=smooth)

        # climber to 49 % of tower height
        p = ValueTracker(0.0)
        dot = always_redraw(lambda: Dot([x0 + W / 2 + 0.3, base_y + H * p.get_value(), 0], radius=0.11, color=YELLOW))
        readout = always_redraw(lambda: VGroup(
            label_text("现在约", size=24, color=GRAY),
            number_text(f"{p.get_value():.2f}", size=28, color=YELLOW)).arrange(RIGHT, buff=0.12)
            .next_to([x0 + W / 2 + 0.45, base_y + H * p.get_value(), 0], RIGHT, buff=0.12))
        guide = DashedLine([x0 + W / 2 + 0.3, base_y, 0], [x0 + W / 2 + 0.3, top, 0], color=GRID, stroke_width=2)
        self.wait_until(8.24)
        self.play(Create(guide), FadeIn(dot), FadeIn(readout), run_time=0.5)
        self.play(p.animate.set_value(0.49), run_time=2.4, rate_func=smooth)

        # keyword capsules + project tag
        caps = VGroup(capsule("神经网络策略", BLUE), capsule("信息对等", TEAL), capsule("人类基准", YELLOW))
        caps.arrange(RIGHT, buff=0.3).move_to([2.6, -1.9, 0])
        tag = label_text("hack-sts2 · 2026 年 11 月", size=22).to_corner(DR, buff=0.5)
        self.wait_until(11.4)
        for i, c in enumerate(caps):
            self.play(GrowFromCenter(c), run_time=0.6, rate_func=smooth)
            self.wait(0.3)
        self.play(FadeIn(tag), run_time=0.6)
        ending(self, "s01")


# =====================================================================  S02
class S02(StyledScene):
    def construct(self):
        self.add_sound(f"{AUDIO}/s02.mp3")
        header = chinese_text("3 幕 · 49 层 · 约 900 个决定", size=36).to_edge(UP, buff=0.5)

        # ---- the run loop
        def node(s, sub=None, color=BLUE):
            t = chinese_text(s, size=32, color=WHITE)
            g = VGroup(t)
            if sub:
                g.add(label_text(sub, size=20))
                g.arrange(DOWN, buff=0.15)
            r = RoundedRectangle(width=max(g.width + 0.5, 2.4), height=g.height + 0.45, corner_radius=0.2,
                                 stroke_color=color, stroke_width=STROKE)
            return VGroup(r, g.move_to(r))
        c = np.array([-2.0, -0.6, 0])
        n_map = node("地图选点").move_to(c + [0, 1.75, 0])
        n_room = node("进房间", "普通战 / 精英 / 火堆 / 商店 / 事件 / Boss", TEAL).move_to(c + [2.6, -1.2, 0])
        n_rew = node("领奖励", color=YELLOW).move_to(c + [-2.6, -1.2, 0])
        arrows = VGroup(
            CurvedArrow(n_map.get_right() + [0.1, 0, 0], n_room.get_top() + [0, 0.1, 0], angle=-PI / 3, color=GRAY, stroke_width=STROKE, tip_length=0.18),
            CurvedArrow(n_room.get_bottom() + [-0.6, -0.1, 0], n_rew.get_bottom() + [0.6, -0.1, 0], angle=-PI / 4, color=GRAY, stroke_width=STROKE, tip_length=0.18),
            CurvedArrow(n_rew.get_top() + [0, 0.1, 0], n_map.get_left() + [-0.1, 0, 0], angle=-PI / 3, color=GRAY, stroke_width=STROKE, tip_length=0.18),
        )
        # counter + 49-floor progress bar (to scale, act boundaries at 17 and 33)
        k = ValueTracker(0)
        bar_x0, bar_w, bar_y = 2.4, 3.6, 1.2
        counter = always_redraw(lambda: VGroup(
            label_text("决定数", size=24),
            number_text(f"{int(k.get_value()):4d}", size=40, color=YELLOW)).arrange(RIGHT, buff=0.2)
            .move_to([bar_x0 + bar_w / 2, bar_y + 0.9, 0]))
        bar_bg = Line([bar_x0, bar_y, 0], [bar_x0 + bar_w, bar_y, 0], color=GRID, stroke_width=8)
        ticks = VGroup()
        for f in (0, 17, 33, 49):
            x = bar_x0 + bar_w * f / 49
            ticks.add(Line([x, bar_y - 0.12, 0], [x, bar_y + 0.12, 0], color=GRAY, stroke_width=2))
            ticks.add(number_text(str(f), size=18, color=GRAY).move_to([x, bar_y - 0.35, 0]))
        bar_lab = label_text("层", size=20).move_to([bar_x0 + bar_w + 0.55, bar_y - 0.35, 0])
        bar_fill = always_redraw(lambda: Line([bar_x0, bar_y, 0],
                                              [bar_x0 + bar_w * max(k.get_value(), 1e-3) / 900, bar_y, 0],
                                              color=BLUE, stroke_width=8))
        self.wait_until(0.1)
        self.play(Write(header), run_time=1.0)
        self.play(*[FadeIn(n, scale=0.9) for n in (n_map, n_room, n_rew)], run_time=0.9)
        self.play(LaggedStart(*[Create(a) for a in arrows], lag_ratio=0.5), run_time=1.8)
        self.play(Create(bar_bg), FadeIn(ticks), FadeIn(bar_lab), FadeIn(counter), FadeIn(bar_fill), run_time=0.6)
        self.play(k.animate.set_value(900), run_time=4.5, rate_func=smooth)

        loop = VGroup(n_map, n_room, n_rew, arrows, bar_bg, ticks, bar_lab)
        self.wait_until(10.36)
        self.play(FadeOut(loop), FadeOut(counter), FadeOut(bar_fill), FadeOut(header), run_time=0.8)
        self.remove(counter, bar_fill)

        # ---- difficulty 1: hidden draw pile, visible intent
        t1 = chinese_text("难点 1：信息不完整", size=40, color=WHITE).to_edge(UP, buff=0.6)
        pile = VGroup(*[card_shape(1.3, 1.85, PALE_BLUE, BG, 1).shift([0.09 * i, 0.09 * i, 0]) for i in range(5)])
        q = chinese_text("?", size=60, color=YELLOW).move_to(pile[-1])
        pile_lab = label_text("抽牌堆：顺序看不见", size=26).next_to(pile, DOWN, buff=0.4)
        pile_g = VGroup(pile, q, pile_lab).move_to([-2.8, -0.5, 0])
        enemy = Circle(radius=0.75, color=RED, stroke_width=STROKE)
        intent = VGroup(Line([-0.25, -0.25, 0], [0.25, 0.25, 0], color=RED, stroke_width=4),
                        Polygon([0.25, 0.25, 0], [0.05, 0.22, 0], [0.22, 0.05, 0], color=RED, fill_opacity=1, stroke_width=0))
        intent_g = VGroup(intent, number_text("12", size=28, color=RED)).arrange(RIGHT, buff=0.12).next_to(enemy, UP, buff=0.25)
        en_lab = label_text("敌人意图：看得见", size=26).next_to(enemy, DOWN, buff=0.4)
        en_g = VGroup(enemy, intent_g, en_lab).move_to([2.8, -0.5, 0])
        self.wait_until(12.05)
        self.play(Write(t1), run_time=0.8)
        self.play(LaggedStart(*[FadeIn(c, shift=UP * 0.2) for c in pile], lag_ratio=0.2), run_time=1.0)
        self.play(Write(q), FadeIn(pile_lab), run_time=0.6)
        self.play(Create(enemy), FadeIn(intent_g), FadeIn(en_lab), run_time=0.9)

        # ---- difficulty 2: delayed reward on a 49-floor line, to scale
        t2 = chinese_text("难点 2：回报很远", size=40, color=WHITE).to_edge(UP, buff=0.6)
        nl = NumberLine(x_range=[1, 49, 8], length=11, color=GRAY, stroke_width=STROKE, include_numbers=False)
        nl.move_to([0, -1.4, 0])
        nums = VGroup(*[number_text(str(v), size=18, color=GRAY).next_to(nl.n2p(v), DOWN, buff=0.18) for v in (1, 9, 17, 25, 33, 41, 49)])
        nl_lab = label_text("层", size=20).next_to(nl, RIGHT, buff=0.25)
        p3, p33 = nl.n2p(3), nl.n2p(33)
        take = VGroup(card_shape(0.5, 0.72, YELLOW), label_text("第 3 层：拿卡", size=24, color=YELLOW))
        take[1].next_to(take[0], DOWN, buff=0.12)
        take.next_to(p3, UP, buff=0.15)
        skull = VGroup(Circle(radius=0.28, color=RED, stroke_width=STROKE),
                       Dot([-0.1, 0.03, 0], radius=0.06, color=RED), Dot([0.1, 0.03, 0], radius=0.06, color=RED),
                       Line([-0.12, -0.38, 0], [0.12, -0.38, 0], color=RED, stroke_width=STROKE),
                       Line([-0.12, -0.38, 0], [-0.12, -0.25, 0], color=RED, stroke_width=STROKE),
                       Line([0.12, -0.38, 0], [0.12, -0.25, 0], color=RED, stroke_width=STROKE))
        skull_lab = label_text("第 33 层：决定生死", size=24, color=RED)
        sk = VGroup(skull, skull_lab).arrange(DOWN, buff=0.12).next_to(p33, UP, buff=0.15)
        arc = CurvedArrow(take.get_top() + [0, 0.1, 0], sk.get_top() + [0, 0.1, 0], angle=-PI / 2.6,
                          color=PALE_BLUE, stroke_width=STROKE, tip_length=0.2)
        arc_lab = chinese_text("回报很远", size=32, color=PALE_BLUE).next_to(arc, UP, buff=0.1)
        self.wait_until(16.425)
        self.play(FadeOut(VGroup(pile_g, en_g)), ReplacementTransform(t1, t2), run_time=0.8)
        self.play(Create(nl), FadeIn(nums), FadeIn(nl_lab), run_time=0.9)
        self.play(FadeIn(take, shift=DOWN * 0.2), run_time=0.6)
        self.play(Create(arc), run_time=1.6)
        self.play(FadeIn(sk), Write(arc_lab), run_time=0.8)

        # ---- difficulty 3: two different decks
        t3 = chinese_text("难点 3：自己搭牌组", size=40, color=WHITE).to_edge(UP, buff=0.6)
        def deck(colors):
            cards = VGroup(*[card_shape(0.42, 0.62, col) for col in colors])
            cards.arrange_in_grid(cols=6, buff=0.12)
            return cards
        dA = deck([RED] * 5 + [BLUE] * 4 + [GREEN] * 3)
        dB = deck([RED] * 3 + [BLUE] * 6 + [GREEN] * 4 + [YELLOW] * 3 + [TEAL] * 2)
        lA = label_text("这一局", size=26).next_to(dA, DOWN, buff=0.35)
        lB = label_text("下一局", size=26).next_to(dB, DOWN, buff=0.35)
        gA, gB = VGroup(dA, lA), VGroup(dB, lB)
        VGroup(gA, gB).arrange(RIGHT, buff=1.8, aligned_edge=DOWN).move_to([0, -0.3, 0])
        diff = chinese_text("每局都不一样", size=34, color=YELLOW).next_to(VGroup(gA, gB), DOWN, buff=0.6)
        self.wait_until(22.625)
        self.play(FadeOut(VGroup(nl, nums, nl_lab, take, sk, arc, arc_lab)), ReplacementTransform(t2, t3), run_time=0.8)
        self.play(LaggedStart(*[FadeIn(c, shift=UP * 0.15) for c in dA], lag_ratio=0.08),
                  LaggedStart(*[FadeIn(c, shift=UP * 0.15) for c in dB], lag_ratio=0.06), FadeIn(lA), FadeIn(lB), run_time=1.6)
        self.play(Write(diff), run_time=0.8)

        # ---- information parity shield
        t4 = chinese_text("信息对等：不偷看、不读档、一遍定输赢", size=36, color=WHITE).to_edge(UP, buff=0.6)
        shield = Polygon([-1.3, 1.5, 0], [1.3, 1.5, 0], [1.3, 0.2, 0], [0, -1.7, 0], [-1.3, 0.2, 0],
                         color=TEAL, stroke_width=3).round_corners(0.25)
        sh_txt = chinese_text("信息对等", size=36, color=TEAL).move_to(shield.get_center() + [0, 0.3, 0])
        sh = VGroup(shield, sh_txt).move_to([-3.2, -0.2, 0])
        items = VGroup()
        for s in ("抽牌堆顺序", "未来随机数", "读档重来"):
            t = chinese_text(s, size=30, color=GRAY)
            box = RoundedRectangle(width=3.2, height=0.8, corner_radius=0.15, stroke_color=GRAY, stroke_width=2)
            items.add(VGroup(box, t.move_to(box)))
        items.arrange(DOWN, buff=0.35).move_to([2.6, 0.0, 0])
        strikes = VGroup(*[Line(it.get_left() + [0.15, 0, 0], it.get_right() - [0.15, 0, 0], color=RED, stroke_width=3) for it in items])
        note = label_text("决策时若做搜索，只在“确定化克隆”上做（看不见的部分重新洗过）", size=24).to_edge(DOWN, buff=0.6)
        self.wait_until(26.587)
        self.play(FadeOut(VGroup(gA, gB, diff)), ReplacementTransform(t3, t4), run_time=0.8)
        self.play(LaggedStart(*[FadeIn(it) for it in items], lag_ratio=0.3), run_time=1.2)
        self.play(FadeIn(sh, shift=DOWN * 1.2), run_time=1.0, rate_func=smooth)
        self.play(LaggedStart(*[Create(s) for s in strikes], lag_ratio=0.4), run_time=1.5)
        self.wait_until(31.5)
        self.play(FadeIn(note), run_time=0.8)
        ending(self, "s02")


# =====================================================================  S04
class S04(StyledScene):
    def construct(self):
        self.add_sound(f"{AUDIO}/s04.mp3")
        # ---- terminal: LLM reads text state, outputs an action
        term = RoundedRectangle(width=6.6, height=3.0, corner_radius=0.15, stroke_color=GRAY, stroke_width=STROKE)
        bar = Line(term.get_corner(UL) + [0, -0.45, 0], term.get_corner(UR) + [0, -0.45, 0], color=GRAY, stroke_width=2)
        ttl = label_text("真游戏 · 无界面 · 文本局面", size=20).move_to(term.get_top() + [0, -0.23, 0])
        lines = VGroup(
            chinese_text("楼层 12   HP 54/70   能量 3", size=24, color=PALE_BLUE),
            chinese_text("手牌：打击  防御  中和  生存者", size=24, color=PALE_BLUE),
            chinese_text("敌人意图：攻击 12", size=24, color=PALE_BLUE),
            chinese_text("LLM  →  打出 中和", size=26, color=YELLOW),
        ).arrange(DOWN, aligned_edge=LEFT, buff=0.2)
        lines.next_to(bar, DOWN, buff=0.3).align_to(term, LEFT).shift(RIGHT * 0.35)
        tg = VGroup(term, bar, ttl, lines).move_to([0, 1.75, 0])
        self.wait_until(0.1)
        self.play(Create(term), Create(bar), FadeIn(ttl), run_time=1.0)
        for ln in lines[:3]:
            self.play(Write(ln), run_time=0.8)
        self.play(FadeIn(lines[3], shift=RIGHT * 0.2), run_time=0.6)

        # score card + floor bar to scale (0..48, mean 29.7 -> 61.9 %)
        sc_title = chinese_text("LLM 基线：Sonnet 5.5 · A0（最低难度）", size=28)
        sc_win = VGroup(chinese_text("通关", size=28, color=GRAY), number_text("0/10", size=32, color=RED)).arrange(RIGHT, buff=0.2)
        sc_fl = VGroup(chinese_text("平均层数", size=28, color=GRAY), number_text("29.7", size=32, color=YELLOW),
                       chinese_text("（约 61.9%）", size=26, color=GRAY)).arrange(RIGHT, buff=0.2)
        sc = VGroup(sc_title, VGroup(sc_win, sc_fl).arrange(RIGHT, buff=0.8)).arrange(DOWN, buff=0.3, aligned_edge=LEFT)
        sc.move_to([0, -0.95, 0])
        fb = NumberLine(x_range=[0, 48, 12], length=5.6, color=GRAY, stroke_width=2, include_numbers=False)
        fb.next_to(sc, DOWN, buff=0.8).align_to(sc, LEFT)
        fb_nums = VGroup(*[number_text(str(v), size=18, color=GRAY).next_to(fb.n2p(v), DOWN, buff=0.15) for v in (0, 12, 24, 36, 48)])
        fb_lab = label_text("层", size=20).next_to(fb, RIGHT, buff=0.2)
        fl = ValueTracker(0.01)
        fb_fill = always_redraw(lambda: Line(fb.n2p(0), fb.n2p(fl.get_value()), color=YELLOW, stroke_width=8))
        self.wait_until(6.8)
        self.play(FadeIn(sc, shift=UP * 0.2), run_time=0.9)
        self.play(Create(fb), FadeIn(fb_nums), FadeIn(fb_lab), run_time=0.6)
        self.add(fb_fill)
        self.play(fl.animate.set_value(29.7), run_time=1.6)

        # ---- slow real game: log gauge
        self.wait_until(11.938)
        self.play(FadeOut(VGroup(tg, sc, fb, fb_nums, fb_lab)), FadeOut(fb_fill), run_time=0.7)
        self.remove(fb_fill)
        gL = LogGauge(radius=1.6, color=RED).move_to([-4.3, 0.6, 0])
        nL = gL.needle()
        gL_t = chinese_text("真游戏", size=32).next_to(gL, UP, buff=0.3)
        gL_axis = label_text("决定/秒（对数刻度）", size=20).next_to(gL, DOWN, buff=0.05)
        gL_v = VGroup(number_text("1.7", size=30, color=RED), chinese_text("个决定/秒/进程", size=26, color=GRAY)).arrange(RIGHT, buff=0.15)
        gL_v.next_to(gL_axis, DOWN, buff=0.3)
        gL_m = label_text("一局约 9 分钟", size=24).next_to(gL_v, DOWN, buff=0.2)
        self.play(Create(gL), FadeIn(gL_t), FadeIn(gL_axis), run_time=1.0)
        self.add(nL)
        self.play(gL.tracker.animate.set_value(np.log10(1.7)), run_time=2.0, rate_func=smooth)
        self.play(FadeIn(gL_v), FadeIn(gL_m), run_time=0.6)
        need = Rectangle(width=0.7, height=0.1, stroke_color=YELLOW, stroke_width=STROKE, fill_color=YELLOW, fill_opacity=0.25)
        need.move_to([5.6, -3.4, 0], aligned_edge=DOWN)
        need_lab = chinese_text("训练需要\n数亿个决定", size=26, color=YELLOW, line_spacing=0.9).move_to([3.7, -2.0, 0])
        self.play(need.animate.stretch_to_fit_height(8.0, about_edge=DOWN), FadeIn(need_lab), run_time=1.5)

        # ---- port the rules from decompiled source, file by file
        self.wait_until(19.075)
        self.play(FadeOut(need), FadeOut(need_lab), run_time=0.6)
        book = VGroup(
            Polygon([-0.9, 0.6, 0], [0, 0.45, 0], [0, -0.75, 0], [-0.9, -0.6, 0], color=PALE_BLUE, stroke_width=STROKE),
            Polygon([0.9, 0.6, 0], [0, 0.45, 0], [0, -0.75, 0], [0.9, -0.6, 0], color=PALE_BLUE, stroke_width=STROKE),
            *[Line([-0.75, 0.35 - 0.2 * i, 0], [-0.15, 0.25 - 0.2 * i, 0], color=GRID, stroke_width=2) for i in range(5)],
            *[Line([0.15, 0.25 - 0.2 * i, 0], [0.75, 0.35 - 0.2 * i, 0], color=GRID, stroke_width=2) for i in range(5)],
        ).move_to([0, 1.5, 0])
        book_lab = label_text("反编译源码", size=22).next_to(book, DOWN, buff=0.2)
        box = RoundedRectangle(width=4.2, height=1.5, corner_radius=0.18, stroke_color=BLUE, stroke_width=STROKE)
        box_t = VGroup(chinese_text("Sts2Sim", size=32, color=BLUE),
                       chinese_text("C# 规则内核，逐文件移植", size=22, color=WHITE)).arrange(DOWN, buff=0.15)
        bx = VGroup(box, box_t.move_to(box)).move_to([4.5, 1.5, 0])
        perm = label_text("作者书面许可 · 学术研究", size=20).next_to(bx, DOWN, buff=0.15).align_to(bx, RIGHT)
        self.play(Create(book), FadeIn(book_lab), Create(box), FadeIn(box_t), run_time=1.0)
        pages = VGroup(*[Rectangle(width=0.32, height=0.42, stroke_color=PALE_BLUE, stroke_width=2).move_to(book) for _ in range(7)])
        self.play(LaggedStart(*[p.animate.move_to(bx).scale(0.4).set_opacity(0) for p in pages], lag_ratio=0.18),
                  FadeIn(perm), run_time=2.6)
        self.remove(pages)

        # ---- fast simulator gauge + clone fan-out
        self.wait_until(26.628)
        gR = LogGauge(radius=1.6, color=GREEN).move_to([4.3, -1.4, 0]).scale(0.85)
        gR.radius *= 0.85
        nR = gR.needle()
        gR_v = VGroup(chinese_text("约", size=24, color=GRAY), number_text("140,000", size=26, color=GREEN),
                      chinese_text("个决定/秒/核", size=22, color=GRAY)).arrange(RIGHT, buff=0.12)
        gR_v.next_to(gR, DOWN, buff=0.1)
        self.play(FadeOut(VGroup(book, book_lab)), FadeIn(gR), run_time=0.6)
        self.add(nR)
        self.play(gR.tracker.animate.set_value(np.log10(140000)), FadeIn(gR_v), run_time=1.2, rate_func=smooth)
        st = VGroup(card_shape(0.8, 1.1, YELLOW, BG, 1), chinese_text("局面", size=22, color=YELLOW))
        st[1].move_to(st[0])
        pivot = np.array([0, -2.6, 0])
        st.move_to(pivot + [0, 1.45, 0])
        clone_lab = VGroup(chinese_text("克隆一个局面", size=26, color=WHITE), number_text("2.5 µs", size=28, color=YELLOW)).arrange(RIGHT, buff=0.2)
        clone_lab.move_to([0, 2.6, 0])
        self.play(FadeIn(st, scale=0.8), FadeIn(clone_lab), run_time=0.5)
        copies = VGroup(*[st.copy().set_opacity(0.55) for _ in range(30)])
        angs = np.linspace(-55, 55, 30) * DEGREES
        self.add(copies, st)
        self.play(LaggedStart(*[c.animate.rotate(-a, about_point=pivot) for c, a in zip(copies, angs)], lag_ratio=0.04),
                  run_time=1.8, rate_func=smooth)

        # ---- differential gate: human replay vs simulator, step by step
        self.wait_until(32.438)
        self.play(*[FadeOut(m) for m in list(self.mobjects)], run_time=0.5)
        self.clear()
        gt = chinese_text("差分闸：同一串动作，逐个决定对拍", size=36).to_edge(UP, buff=0.5)
        hL = chinese_text("人类录像", size=30, color=PALE_BLUE).move_to([-3.4, 2.2, 0])
        hR = chinese_text("模拟器", size=30, color=BLUE).move_to([3.4, 2.2, 0])
        rows = [("HP 54  格挡 0   能量 3", "攻击 12"), ("HP 54  格挡 5   能量 2", "攻击 12"),
                ("HP 51  格挡 0   能量 1", "攻击 12"), ("HP 51  格挡 0   能量 3", "格挡")]
        rowg = VGroup()
        for i, (s, it) in enumerate(rows):
            y = 1.35 - 0.72 * i
            a = chinese_text(f"{s}   意图 {it}", size=22, color=GRAY).move_to([-3.4, y, 0])
            b = chinese_text(f"{s}   意图 {it}", size=22, color=GRAY).move_to([3.4, y, 0])
            ck = VGroup(Line([-0.18, 0.0, 0], [-0.05, -0.14, 0]), Line([-0.05, -0.14, 0], [0.2, 0.16, 0]))
            ck.set_stroke(GREEN, 4).move_to([0, y, 0])
            rowg.add(VGroup(a, b, ck))
        divider = DashedLine([0, 2.5, 0], [0, -1.5, 0], color=GRID, stroke_width=2)
        k1, k2 = ValueTracker(0), ValueTracker(0)
        c1 = always_redraw(lambda: VGroup(chinese_text("战斗闸", size=28, color=WHITE),
                                          number_text(f"{int(round(k1.get_value())):3d}/131", size=30, color=GREEN)).arrange(RIGHT, buff=0.2).move_to([-3.2, -2.6, 0]))
        c2 = always_redraw(lambda: VGroup(chinese_text("整局从种子重放", size=28, color=WHITE),
                                          number_text(f"{int(round(k2.get_value())):3d}/753", size=30, color=GREEN)).arrange(RIGHT, buff=0.2).move_to([3.0, -2.6, 0]))
        self.play(Write(gt), FadeIn(hL), FadeIn(hR), Create(divider), run_time=0.7)
        for r in rowg[:2]:
            self.play(FadeIn(r[0]), FadeIn(r[1]), run_time=0.3)
            self.play(Create(r[2]), run_time=0.25)
        self.wait_until(34.9)
        for r in rowg[2:]:
            self.play(FadeIn(r[0]), FadeIn(r[1]), run_time=0.3)
            self.play(Create(r[2]), run_time=0.25)
        self.add(c1, c2)
        self.play(k1.animate.set_value(131), k2.animate.set_value(748), run_time=1.8, rate_func=smooth)
        ending(self, "s04")
