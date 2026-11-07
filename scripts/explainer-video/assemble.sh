#!/usr/bin/env bash
# Normalise every part (libx264 crf20 yuv420p 1920x1080 30fps, AAC 160k 48 kHz stereo) and concat.
# Run from this directory after the scene renders are in renders/<id>.mp4 and bookends are rendered.
set -euo pipefail
B=tmp/mBook/videos/bookends/1080p30; mkdir -p tmp/norm out
norm(){ # in out seconds [silent]
  local A="[0:a]aresample=48000,apad,atrim=duration=$3,asetpts=PTS-STARTPTS[a]" X=()
  if [ "${4:-}" = silent ]; then X=(-f lavfi -i anullsrc=r=48000:cl=stereo); A="[1:a]atrim=duration=$3,asetpts=PTS-STARTPTS[a]"; fi
  ffmpeg -y -v error -i "$1" "${X[@]}" -filter_complex \
    "[0:v]scale=1920:1080,fps=30,format=yuv420p,tpad=stop_mode=clone:stop_duration=1,trim=duration=$3,setpts=PTS-STARTPTS[v];$A" \
    -map "[v]" -map "[a]" -c:v libx264 -crf 20 -preset medium -pix_fmt yuv420p -r 30 \
    -c:a aac -b:a 160k -ar 48000 -ac 2 "$2"; }
norm $B/Title.mp4 tmp/norm/00_title.mp4 1.5 silent
norm $B/EndCard.mp4 tmp/norm/99_end.mp4 3.0 silent
i=1; for s in s01 s02 s04 s05 s06 s08 s11; do
  T=$(ffprobe -v error -show_entries format=duration -of csv=p=0 renders/$s.mp4)
  norm renders/$s.mp4 tmp/norm/0${i}_$s.mp4 "$T"; i=$((i+1)); done
for f in tmp/norm/*.mp4; do echo "file '$PWD/$f'"; done > tmp/concat.txt
ffmpeg -y -v error -f concat -safe 0 -i tmp/concat.txt -c copy -movflags +faststart "out/教AI爬塔.mp4"
ffprobe -v error -show_entries stream=codec_type,duration:format=duration,size -of compact "out/教AI爬塔.mp4"
