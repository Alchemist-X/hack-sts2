"""Scene builder B: s05 (PPO in three panels + model card), s06 (lineage timeline)."""
import sys, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))
from manim import *  # noqa
from style import *  # noqa

VDIR = pathlib.Path(__file__).resolve().parents[1]
AUD = VDIR / "audio"


def header(s, size=34, color=WHITE):
    return chinese_text(s, size=size, color=color).to_edge(UP, buff=0.55).to_edge(LEFT, buff=0.8)


# =====================================================================  S05
class S05(StyledScene):
    AUDIO = 38.544

    def construct(self):
        self.add_sound(str(AUD / "s05.mp3"), time_offset=0)
        END = self.AUDIO + 1.0

        # 0.1 title
        title = chinese_text("PPO：三步学习", size=44)
        sub = chinese_text("收集 → 打分 → 更新", size=28, color=GRAY)
        tg = VGroup(title, sub).arrange(DOWN, buff=0.35)
        self.beat(0.1, Write(title), run_time=1.2)
        self.beat(1.6, FadeIn(sub, shift=UP * 0.2), run_time=0.8)

        # ---------------- panel 1: collect (4.6 - 12.25)
        self.beat(4.4, FadeOut(tg), run_time=0.4)
        h1 = header("① 收集：2048 局 × 64 步")
        self.beat(4.8, Write(h1), run_time=0.8)
        ROWS, COLS = 16, 64
        cw, ch = 0.15, 0.24
        cells = VGroup(*[
            Square(side_length=1).stretch_to_fit_width(cw * 0.78).stretch_to_fit_height(ch * 0.78)
            .set_stroke(GRID, 1).set_fill(GRID, 0.35)
            .move_to(np.array([(c - (COLS - 1) / 2) * cw, ((ROWS - 1) / 2 - r) * ch, 0]))
            for r in range(ROWS) for c in range(COLS)
        ])
        cells.move_to(DOWN * 0.35)
        dots_lbl = label_text("⋮  共 2048 行（这里画 16 行）", size=22).next_to(cells, DOWN, buff=0.25)
        row_lbl = label_text("每一行 = 一局", size=22, color=PALE_BLUE).next_to(cells, LEFT, buff=0.25).shift(UP * 1.2)
        row_lbl.rotate(0)
        col_lbl = label_text("每一格 = 一个决定（64 列 = 64 步）", size=22, color=PALE_BLUE).next_to(cells, UP, buff=0.25).align_to(cells, LEFT)
        if row_lbl.get_left()[0] < -config.frame_width / 2 + 0.3:
            row_lbl.next_to(cells, DOWN, buff=0.25).align_to(cells, LEFT)
            dots_lbl.next_to(cells, DOWN, buff=0.25).align_to(cells, RIGHT)
        self.play(FadeIn(cells, lag_ratio=0.0), FadeIn(col_lbl), FadeIn(row_lbl), FadeIn(dots_lbl), run_time=0.8)
        k = ValueTracker(0)
        counter = always_redraw(lambda: number_text(f"{int(round(k.get_value() * 2048)):,}", size=40, color=YELLOW)
                                .to_edge(UP, buff=0.55).to_edge(RIGHT, buff=0.8))
        cnt_lbl = label_text("步 / 轮", size=24).next_to(counter, DOWN, buff=0.15).align_to(counter, RIGHT)
        cnt_lbl.to_edge(RIGHT, buff=0.8).shift(DOWN * 0.0)
        self.add(counter)
        self.play(FadeIn(cnt_lbl), run_time=0.3)
        # light columns left to right while the counter climbs to 64 * 2048
        def lit(mob, alpha):
            ncol = alpha * COLS
            for i, sq in enumerate(mob):
                c = i % COLS
                if c < ncol:
                    sq.set_fill(BLUE, 0.85).set_stroke(BLUE, 1)
            return mob
        t_start = self.now()
        run = 11.6 - t_start
        self.play(UpdateFromAlphaFunc(cells, lit), k.animate.set_value(64), run_time=run, rate_func=linear)
        eq = number_text("2048 × 64 = 131,072", size=30, color=YELLOW).next_to(cnt_lbl, DOWN, buff=0.15).align_to(cnt_lbl, RIGHT)
        self.play(FadeIn(eq), run_time=0.4)
        p1 = VGroup(h1, cells, dots_lbl, row_lbl, col_lbl, cnt_lbl, eq)

        # ---------------- panel 2: advantage (12.25 - 18.86)
        counter.clear_updaters()
        self.beat(12.15, FadeOut(p1), FadeOut(counter), run_time=0.5)
        h2 = header("② 打分：优势 = 实际回报 − 预期")
        self.play(Write(h2), run_time=0.8)
        baseline_y = -0.4
        vals = [0.9, -0.6, 1.4, 0.3, -1.1, 0.7, -0.3, 1.1, -0.8, 0.5, -1.4, 0.2]
        base = DashedLine(LEFT * 3.4, RIGHT * 6.0, dash_length=0.12, color=GRAY, stroke_width=2).shift(UP * baseline_y)
        base_lbl = VGroup(label_text("预期", size=26, color=WHITE), label_text("价值头的估计", size=20)).arrange(DOWN, buff=0.1)
        base_lbl.next_to(base, LEFT, buff=0.25)
        bw = 0.5
        bars = VGroup()
        for i, v in enumerate(vals):
            x = (i - (len(vals) - 1) / 2) * 0.78 + 1.3
            r = Rectangle(width=bw, height=abs(v) * 1.5, stroke_width=2, stroke_color=PALE_BLUE, fill_color=PALE_BLUE, fill_opacity=0.25)
            if v > 0:
                r.move_to(np.array([x, baseline_y, 0]), aligned_edge=DOWN)
            else:
                r.move_to(np.array([x, baseline_y, 0]), aligned_edge=UP)
            r.v = v
            bars.add(r)
        self.play(Create(base), FadeIn(base_lbl), run_time=0.8)
        self.play(LaggedStart(*[GrowFromEdge(b, DOWN if b.v > 0 else UP) for b in bars], lag_ratio=0.12), run_time=1.6)
        self.play(*[b.animate.set_style(stroke_color=GREEN if b.v > 0 else RED,
                                        fill_color=GREEN if b.v > 0 else RED, fill_opacity=0.55) for b in bars], run_time=1.0)
        pos_l = label_text("比预期好：优势 > 0", size=24, color=GREEN).to_edge(DOWN, buff=0.7).shift(LEFT * 2.0)
        neg_l = label_text("比预期差：优势 < 0", size=24, color=RED).to_edge(DOWN, buff=0.7).shift(RIGHT * 3.6)
        self.play(FadeIn(pos_l), FadeIn(neg_l), run_time=0.6)
        p2 = VGroup(h2, base, base_lbl, bars, pos_l, neg_l)

        # ---------------- panel 3: update + rope (18.86 - 31.16)
        self.beat(18.8, FadeOut(p2), run_time=0.5)
        h3 = header("③ 更新：多做好的，少做差的")
        self.play(Write(h3), run_time=0.8)
        # vertical ratio axis 0.6 .. 1.4
        nl = NumberLine(x_range=[0.6, 1.4, 0.1], length=4.6, rotation=PI / 2, color=GRAY,
                        stroke_width=2, include_ticks=True, tick_size=0.06).shift(DOWN * 0.15 + LEFT * 1.5)
        ticks = VGroup(*[number_text(f"{v:.1f}", size=20, color=GRAY).next_to(nl.n2p(v), LEFT, buff=0.18)
                         for v in (0.6, 0.8, 1.0, 1.2, 1.4)])
        ax_lbl = label_text("新 / 旧 概率比", size=22).next_to(nl, UP, buff=0.2)
        lo = DashedLine(nl.n2p(0.8) + LEFT * 0.3, nl.n2p(0.8) + RIGHT * 2.6, color=GRAY, stroke_width=1.5, dash_length=0.08)
        hi = DashedLine(nl.n2p(1.2) + LEFT * 0.3, nl.n2p(1.2) + RIGHT * 2.6, color=GRAY, stroke_width=1.5, dash_length=0.08)
        anchor_pt = nl.n2p(1.0) + RIGHT * 1.2
        anchor = Dot(anchor_pt, radius=0.05, color=GRAY)
        bt = ValueTracker(1.0)
        ball_pos = lambda: nl.n2p(bt.get_value()) + RIGHT * 1.2 + RIGHT * 0.0
        ball = always_redraw(lambda: Dot(ball_pos(), radius=0.14, color=YELLOW))
        taut = ValueTracker(0)
        def rope_fn():
            a, b = anchor_pt, ball_pos()
            d = b - a
            L = np.linalg.norm(d)
            if L < 1e-3:
                return VMobject()
            slack = 0.35 * (1 - taut.get_value()) * min(1, L / 0.4 + 0.3)
            n = np.array([-d[1], d[0], 0]) / L
            pts = [a + d * s + n * slack * np.sin(PI * s) * np.sin(3 * PI * s) for s in np.linspace(0, 1, 40)]
            col = interpolate_color(ManimColor(PALE_BLUE), ManimColor(YELLOW), taut.get_value())
            return VMobject(stroke_color=col, stroke_width=2.5).set_points_smoothly(pts)
        rope = always_redraw(rope_fn)
        self.play(Create(nl), FadeIn(ticks), FadeIn(ax_lbl), run_time=0.7)
        self.add(rope, anchor, ball)
        self.play(FadeIn(anchor), run_time=0.3)
        up_txt = label_text("优势 > 0：提高概率", size=24, color=GREEN).next_to(nl.n2p(1.2) + RIGHT * 3.6, RIGHT, buff=0)
        dn_txt = label_text("优势 < 0：降低概率", size=24, color=RED).next_to(nl.n2p(0.8) + RIGHT * 3.6, RIGHT, buff=0)
        self.play(FadeIn(up_txt), FadeIn(dn_txt), run_time=0.6)
        # 23.86 rope sentence
        self.beat(23.6, Create(lo), Create(hi), run_time=0.6)
        hi_l = number_text("1.2", size=22, color=YELLOW).next_to(hi, RIGHT, buff=0.1)
        lo_l = number_text("0.8", size=22, color=YELLOW).next_to(lo, RIGHT, buff=0.1)
        self.play(FadeIn(hi_l), FadeIn(lo_l), run_time=0.3)
        arrow_up = Arrow(ball_pos() + UP * 0.2, ball_pos() + UP * 1.0, buff=0, color=GREEN, stroke_width=4,
                         max_tip_length_to_length_ratio=0.25)
        arrow_up.add_updater(lambda m: m.put_start_and_end_on(ball_pos() + UP * 0.2, ball_pos() + UP * 1.0))
        self.play(GrowArrow(arrow_up), run_time=0.5)
        self.play(bt.animate.set_value(1.2), taut.animate.set_value(1), run_time=1.6, rate_func=smooth)
        clip_l = label_text("绳子绷直：被裁剪，不再贡献梯度", size=22, color=GRAY).next_to(up_txt, DOWN, buff=0.2).align_to(up_txt, LEFT)
        self.play(arrow_up.animate.set_color(GRAY), FadeIn(clip_l), run_time=0.6)
        arrow_up.clear_updaters()
        self.play(FadeOut(arrow_up), bt.animate.set_value(1.0), taut.animate.set_value(0), run_time=0.6)
        arrow_dn = Arrow(ball_pos() + DOWN * 0.2, ball_pos() + DOWN * 0.8, buff=0, color=RED, stroke_width=4,
                         max_tip_length_to_length_ratio=0.25)
        arrow_dn.add_updater(lambda m: m.put_start_and_end_on(ball_pos() + DOWN * 0.2, ball_pos() + DOWN * 0.8))
        self.play(GrowArrow(arrow_dn), run_time=0.4)
        self.play(bt.animate.set_value(0.8), taut.animate.set_value(1), run_time=1.2)
        self.play(arrow_dn.animate.set_color(GRAY), run_time=0.4)
        arrow_dn.clear_updaters()
        cap = chinese_text("每次只挪一小步：绳子 = 裁剪（clip 0.2）", size=30, color=YELLOW).to_edge(DOWN, buff=0.45)
        self.play(FadeIn(cap, shift=UP * 0.15), run_time=0.6)
        ball.clear_updaters(); rope.clear_updaters()
        p3 = VGroup(h3, nl, ticks, ax_lbl, lo, hi, hi_l, lo_l, anchor, ball, rope, up_txt, dn_txt, clip_l, arrow_dn, cap)

        # ---------------- panel 4: model (31.16 - 38.0)
        self.beat(31.0, FadeOut(p3), run_time=0.5)
        h4 = header("模型：290 万参数，实体编码器")
        names = ["手牌", "牌组", "敌人", "遗物", "药水"]
        counts = [5, 8, 3, 4, 3]
        rows = VGroup()
        for nm, c in zip(names, counts):
            lab = label_text(nm, size=22, color=WHITE)
            sq = VGroup(*[Square(0.22, stroke_color=BLUE, stroke_width=2) for _ in range(c)]).arrange(RIGHT, buff=0.06)
            row = VGroup(lab, sq).arrange(RIGHT, buff=0.2)
            rows.add(row)
        rows.arrange(DOWN, buff=0.28, aligned_edge=LEFT).to_edge(LEFT, buff=0.8).shift(DOWN * 0.2)
        ent_l = label_text("实体（每个一格）", size=20).next_to(rows, UP, buff=0.2).align_to(rows, LEFT)
        tf = RoundedRectangle(width=3.0, height=2.6, corner_radius=0.12, stroke_color=PALE_BLUE, stroke_width=2.5)
        tf.move_to(RIGHT * 0.2 + DOWN * 0.2)
        tf_t = VGroup(chinese_text("Transformer", size=28, color=PALE_BLUE),
                      label_text("3 层 · 6 头 · 宽 192", size=22, color=WHITE)).arrange(DOWN, buff=0.2).move_to(tf)
        a1 = Arrow(rows.get_right() + RIGHT * 0.1, tf.get_left(), buff=0.1, color=GRAY, stroke_width=3)
        heads = VGroup(
            label_text("策略：候选动作表逐行打分", size=22, color=YELLOW),
            label_text("价值头", size=22, color=WHITE),
            label_text("胜率头", size=22, color=WHITE),
            label_text("战斗结束 HP 头", size=22, color=WHITE),
        ).arrange(DOWN, buff=0.35, aligned_edge=LEFT).next_to(tf, RIGHT, buff=1.0)
        harrows = VGroup(*[Arrow(tf.get_right(), h.get_left(), buff=0.12, color=GRAY, stroke_width=2.5,
                                 max_tip_length_to_length_ratio=0.12) for h in heads])
        card = label_text("2,904,208 参数 · lr 3e-4 · clip 0.2 · γ 1.0 · λ 0.99 · 每轮 1 epoch · minibatch 4096",
                          size=22, color=GRAY).to_edge(DOWN, buff=0.5)
        self.play(Write(h4), FadeIn(rows, lag_ratio=0.15), FadeIn(ent_l), run_time=1.2)
        self.play(GrowArrow(a1), Create(tf), FadeIn(tf_t), run_time=1.2)
        self.play(LaggedStart(*[GrowArrow(a) for a in harrows], lag_ratio=0.15),
                  LaggedStart(*[FadeIn(h) for h in heads], lag_ratio=0.15), run_time=1.4)
        self.play(FadeIn(card, shift=UP * 0.15), run_time=0.8)

        # end
        self.wait_until(END - 0.7)
        self.play(*[FadeOut(m) for m in self.mobjects], run_time=0.6)
        self.wait_until(END)


# =====================================================================  S06
class S06(StyledScene):
    AUDIO = 52.776

    def construct(self):
        self.add_sound(str(AUD / "s06.mp3"), time_offset=0)
        END = self.AUDIO + 1.0

        g = make_axes(x_range=(0, 13000, 2000), y_range=(0.2, 0.65, 0.1), x_length=11.6, y_length=5.6,
                      x_label="训练轮数（update）", y_label="进度", y_decimals=1)
        g.move_to(DOWN * 0.15 + RIGHT * 0.25)
        ax = g.axes
        goal = DashedLine(ax.c2p(0, 0.60), ax.c2p(13000, 0.60), color=YELLOW, stroke_width=2, dash_length=0.12)
        goal_l = label_text("目标 0.60", size=22, color=YELLOW).next_to(ax.c2p(13000, 0.60), UP, buff=0.1).shift(LEFT * 0.6)
        legend = VGroup(
            VGroup(Dot(radius=0.06, color=WHITE), label_text("dev-128", size=20)).arrange(RIGHT, buff=0.12),
            VGroup(Square(0.12, color=WHITE, fill_opacity=1, stroke_width=0), label_text("ab-512", size=20)).arrange(RIGHT, buff=0.12),
        ).arrange(RIGHT, buff=0.4).next_to(ax.c2p(0, 0.65), RIGHT, buff=0.6).shift(UP * 0.05)
        self.beat(0.1, Create(ax), FadeIn(VGroup(*g[1:])), run_time=1.2)
        self.play(Create(goal), FadeIn(goal_l), FadeIn(legend), run_time=0.7)

        def dot(u, v, col):
            return Dot(ax.c2p(u, v), radius=0.065, color=col)

        def sq(u, v, col):
            return Square(0.12, color=col, fill_color=col, fill_opacity=1, stroke_width=0).move_to(ax.c2p(u, v))

        def val(s, mob, direction, col, size=18, buff=0.1):
            return number_text(s, size=size, color=col).next_to(mob, direction, buff=buff)

        # W0
        w0 = dot(382, 0.251, BLUE)
        w0v = val("0.251", w0, RIGHT, BLUE)
        w0l = label_text("W0 随机初始化，5,007 万步", size=22, color=WHITE).next_to(w0, UP, buff=0.35).align_to(w0, LEFT)
        self.beat(2.0, GrowFromCenter(w0), run_time=0.5)
        self.play(FadeIn(w0v), FadeIn(w0l), run_time=0.6)
        # C1
        self.wait_until(6.36)
        c1 = dot(764, 0.310, BLUE)
        seg = Line(w0.get_center(), c1.get_center(), color=BLUE, stroke_width=2.5)
        c1v = val("0.310", c1, RIGHT, BLUE)
        c1l = label_text("C1 循环成本 0.01：火堆 / 商店菜单绕一圈扣 0.01", size=22, color=WHITE)
        c1l.next_to(c1, UP, buff=0.35).align_to(w0, LEFT)
        self.play(FadeOut(w0l), Create(seg), run_time=0.6)
        self.play(GrowFromCenter(c1), FadeIn(c1v), FadeIn(c1l), run_time=0.6)
        # M
        mpts = [(1264, 0.3475), (1764, 0.3674), (2264, 0.3885), (2764, 0.3903), (3064, 0.4042)]
        mdots = VGroup(*[dot(u, v, BLUE) for u, v in mpts])
        mline = VMobject(color=BLUE, stroke_width=2.5).set_points_as_corners([c1.get_center()] + [d.get_center() for d in mdots])
        mv = val("0.404", mdots[-1], UP, BLUE, size=20)
        ml = label_text("M  4 亿步", size=22, color=WHITE).next_to(ax.c2p(800, 0.435), RIGHT, buff=0)
        self.beat(10.6, FadeOut(c1l), FadeOut(c1v), Create(mline), LaggedStart(*[GrowFromCenter(d) for d in mdots], lag_ratio=0.3), run_time=1.8)
        self.play(FadeIn(mv), FadeIn(ml), run_time=0.5)
        # A branch
        pM = mdots[-1].get_center()
        a_dot = dot(7664, 0.444, GREEN); a_sq = sq(7664, 0.464, GREEN)
        a_line = Line(pM, a_dot.get_center(), color=GREEN, stroke_width=2.5)
        a_l = label_text("A  宏动作 rest,shop + 熵退火 0.02→0.005", size=22, color=GREEN)
        a_l.next_to(ax.c2p(3300, 0.545), RIGHT, buff=0)
        a_dv = val("0.444", a_dot, LEFT, GREEN, buff=0.15).shift(UP * 0.13)
        a_sv = val("0.464", a_sq, UP, GREEN)
        self.beat(13.4, Create(a_line), FadeIn(a_l), run_time=1.6)
        self.play(GrowFromCenter(a_dot), FadeIn(a_dv), run_time=0.5)
        self.beat(21.5, GrowFromCenter(a_sq), FadeIn(a_sv), run_time=0.6)
        a_note = label_text("也多训了 4,600 轮：两者功劳没有分开", size=20, color=GRAY).next_to(a_l, DOWN, buff=0.15).align_to(a_l, LEFT)
        self.beat(24.0, FadeIn(a_note), run_time=0.6)
        # B branch
        b_sq = sq(7664, 0.434, RED)
        b_line = DashedLine(pM, b_sq.get_center(), color=RED, stroke_width=2.5, dash_length=0.1)
        b_v = val("0.434", b_sq, LEFT, RED, buff=0.15).shift(DOWN * 0.13)
        b_l = label_text("B  + harvest 难局重练：无提升", size=22, color=RED).next_to(ax.c2p(4200, 0.36), RIGHT, buff=0)
        b_d = label_text("B − A = −0.030（ab-512 配对）", size=20, color=GRAY).next_to(b_l, DOWN, buff=0.15).align_to(b_l, LEFT)
        self.beat(29.65, FadeOut(a_note), Create(b_line), FadeIn(b_l), run_time=1.2)
        self.play(GrowFromCenter(b_sq), FadeIn(b_v), FadeIn(b_d), run_time=0.6)
        # C lineage
        cpts = [(9164, 0.4485), (10664, 0.4753), (11164, 0.4863), (11664, 0.4886), (12164, 0.485)]
        csq = VGroup(*[sq(u, v, YELLOW) for u, v in cpts])
        cline = VMobject(color=YELLOW, stroke_width=2.5).set_points_as_corners([b_sq.get_center()] + [s.get_center() for s in csq])
        c_l = label_text("C  宏动作推广到所有选牌界面，之后纯续训", size=22, color=YELLOW)
        c_l.next_to(ax.c2p(2500, 0.555), RIGHT, buff=0)
        cv0 = val("0.449", csq[0], DOWN, YELLOW)
        cv3 = val("0.489", csq[3], DOWN, YELLOW).shift(DOWN * 0.05)
        self.beat(33.29, FadeOut(a_l), FadeOut(b_l), FadeOut(b_d), FadeOut(ml), FadeIn(c_l), run_time=0.6)
        self.play(Create(cline), LaggedStart(*[GrowFromCenter(s) for s in csq], lag_ratio=0.25), run_time=2.0)
        self.play(FadeIn(cv0), run_time=0.3)
        self.beat(38.4, FadeIn(cv3), run_time=0.4)
        # increments bubbles above C nodes
        incs = [("+0.026", 0, 1, 0.53), ("+0.011", 1, 2, 0.555), ("+0.003", 2, 3, 0.53)]
        bubbles = VGroup()
        for s, i, j, yl in incs:
            mid_u = (cpts[i][0] + cpts[j][0]) / 2
            t = number_text(s, size=20, color=WHITE)
            t.move_to(ax.c2p(mid_u, yl))
            c = SurroundingRectangle(t, buff=0.08, corner_radius=0.1, color=GRAY, stroke_width=1.5)
            bubbles.add(VGroup(c, t))
        # shrink sizes to emphasise diminishing increments
        bubbles[1].scale(0.85); bubbles[2].scale(0.72)
        self.beat(40.3, LaggedStart(*[FadeIn(b, shift=DOWN * 0.1) for b in bubbles], lag_ratio=0.5), run_time=2.4)
        stamp = VGroup(label_text("平台：5 点窗口斜率 ≈ 0", size=22, color=GRAY))
        stamp.add(SurroundingRectangle(stamp[0], buff=0.12, color=GRAY, stroke_width=2))
        stamp.rotate(-6 * DEGREES).move_to(ax.c2p(11000, 0.37))
        self.beat(44.6, FadeIn(stamp, scale=1.3), run_time=0.5)

        # S branch + inset (45.84 -)
        s_pts = [(11164, 0.4923), (11664, 0.4999)]
        s_sq = VGroup(*[sq(u, v, TEAL) for u, v in s_pts])
        s_line = VMobject(color=TEAL, stroke_width=2.5).set_points_as_corners([csq[1].get_center()] + [s.get_center() for s in s_sq])
        s_v = val("0.500", s_sq[1], UP, TEAL, buff=0.32)
        s_l = label_text("S  boss 学校：0.500，所有线最高", size=22, color=TEAL)
        s_l.next_to(ax.c2p(6400, 0.577), RIGHT, buff=0)
        self.beat(45.84, FadeOut(c_l), FadeOut(bubbles), Create(s_line), FadeIn(s_l), run_time=1.0)
        self.play(LaggedStart(*[GrowFromCenter(s) for s in s_sq], lag_ratio=0.4), FadeIn(s_v), run_time=0.8)
        # paired comparison card (to scale on a small delta axis) in the empty upper-left
        dax = NumberLine(x_range=[-0.02, 0.04, 0.01], length=4.4, color=GRAY, stroke_width=2,
                         include_ticks=True, tick_size=0.05)
        dax.move_to(ax.c2p(3000, 0.48))
        zero = DashedLine(dax.n2p(0) + UP * 0.95, dax.n2p(0) + DOWN * 0.15, color=WHITE, stroke_width=1.5, dash_length=0.06)
        dnums = VGroup(*[number_text(f"{v:+.2f}" if v else "0", size=16, color=GRAY).next_to(dax.n2p(v), DOWN, buff=0.12)
                         for v in (-0.02, 0.0, 0.02, 0.04)])
        rows = [(0.006, -0.010, 0.022, "u11164  +0.006 [−0.010, +0.022]"),
                (0.0113, -0.005, 0.028, "u11664  +0.011 [−0.005, +0.028]")]
        ivs = VGroup()
        for k, (d, lo_, hi_, txt) in enumerate(rows):
            y = 0.35 + 0.42 * k
            ln = Line(dax.n2p(lo_) + UP * y, dax.n2p(hi_) + UP * y, color=TEAL, stroke_width=3)
            pt = Dot(dax.n2p(d) + UP * y, radius=0.06, color=TEAL)
            tl = number_text(txt, size=16, color=TEAL).next_to(ln, RIGHT, buff=0.2)
            tl.set_x(dax.n2p(0.04)[0] + 0.25 + tl.width / 2)
            ivs.add(VGroup(ln, pt, tl))
        ttl = label_text("S − C3（配对，95% 区间）", size=20, color=WHITE).next_to(dax, UP, buff=1.05).align_to(dax, LEFT)
        lab_rows = VGroup()
        flag = label_text("两个区间都跨过 0：噪声内", size=22, color=YELLOW).next_to(dnums, DOWN, buff=0.15).align_to(dax, LEFT)
        card = VGroup(dax, zero, dnums, ivs, ttl, lab_rows, flag)
        self.beat(49.2, FadeOut(mv), FadeOut(a_dv), FadeOut(a_sv), FadeOut(b_v), FadeIn(ttl), Create(dax), FadeIn(dnums), Create(zero), run_time=0.8)
        self.play(LaggedStart(*[Create(v) for v in ivs], lag_ratio=0.4), FadeIn(lab_rows), run_time=1.0)
        self.play(FadeIn(flag, shift=LEFT * 0.1), run_time=0.5)

        self.wait_until(END - 0.8)
        self.play(*[FadeOut(m) for m in self.mobjects], run_time=0.7)
        self.wait_until(END)
