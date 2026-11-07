# 教 AI 爬塔: explainer film source (Manim CE, 3Blue1Brown style)

About 5 minutes (298.3 s), 1920x1080 at 30 fps, Chinese narration (edge-tts zh-CN-YunxiNeural) with an
`.srt` sidecar and no burned-in captions. Scenes: title (1.5 s), s01, s02, s04, s05, s06, s08,
s11, end card (3 s). Every number comes from `script.json` (narration, on-screen text, steps,
data and source paths per scene). Audio, renders and the final MP4 are not committed.

## Toolchain (verified 2026-11-06 on macOS)
- Homebrew: `ffmpeg cairo pango pkg-config`
- `uv venv .venv && source .venv/bin/activate && uv pip install manim==0.22.1 edge-tts==7.3.1`
- CJK font: PingFang SC. `fc-list` does not show it, but Pango finds it. `style.py` picks it via
  `manimpango.list_fonts()` and falls back to Hiragino Sans GB, then Noto Sans CJK SC, then Heiti SC.
  Numbers are set in Menlo.

## Files
- `style.py`: palette (bg `#0f1419`, blue `#58C4DD`, yellow `#FFD35A`, green `#83C167`, red `#FC6255`,
  teal `#5CD0B3`, gray `#9AA5B1`), text helpers, `make_axes`, `title_card`, and `StyledScene`
  (`wait_until` / `beat` / `pad_to`, which keep animation beats on narration timestamps).
- `narrate.py <workflow-output.json>`: synthesises `audio/<id>.mp3` with sentence boundaries,
  writes `script.json` (per-scene audio length, beats anchored to sentences) and `out/narration.srt`.
- `scenes/group_A.py` (S01 S02 S04), `group_B.py` (S05 S06), `group_C.py` (S08 S11), `bookends.py`
  (Title, EndCard), `smoke.py` (toolchain smoke test). Each scene adds its own narration with
  `add_sound` at t=0 and lasts audio + 1.0 s.
- `assemble.sh`: normalises all parts (libx264 crf 20, yuv420p, 30 fps, AAC 160k 48 kHz) and
  concatenates them with the concat demuxer into `out/教AI爬塔.mp4`.

## Render recipe
```bash
source .venv/bin/activate
python narrate.py path/to/script_source.json          # audio/*.mp3, script.json, out/narration.srt
for c in "group_A S01 S02 S04" "group_B S05 S06" "group_C S08 S11"; do set -- $c
  manim -qh --fps 30 --media_dir tmp/m --disable_caching scenes/$1.py ${@:2}; done
# copy tmp/m/videos/<group>/1080p30/S0x.mp4 -> renders/s0x.mp4 (lower-case ids)
manim -qh --fps 30 --media_dir tmp/mBook --disable_caching scenes/bookends.py Title EndCard
./assemble.sh
```
Rendering is fast: about 0.3 to 0.45 s of wall time per second of video at 1080p30, and roughly
1.5 minutes for the whole film. `out/narration.srt` timings assume the 1.5 s lead-in and the
actual part durations. If you re-render any part, recompute the srt offsets from the
`tmp/norm` durations.

## Known caveats
- Illustrative values: the s02 and s04 terminal state and the gate rows (HP 54, intent attack 12),
  plus the s11 single-point scatter (synthetic AR(1) data, r = 0.4, titled 示意), are not
  recorded data. The real figures are ±0.03, r ≈ 0.40 and ±0.014.
- Omitted for time: the gray failed branches in s06 and the paired-difference histogram in s11.
- Small overlaps: in s06 the red 0.434 label touches the B line, and the +0.026/+0.011/+0.003
  bubbles crowd each other. In s11 the +0.003 label crosses the zero line.
