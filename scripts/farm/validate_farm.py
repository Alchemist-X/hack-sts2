#!/usr/bin/env python3
"""Run ``sts2rec validate`` over every farm session; write {session: result} JSON.

usage: validate_farm.py <farm-root> <out.json>
"""
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SUMMARY = re.compile(r'^(OK|FAIL\w*): .*\((\d+) errors?, (\d+) warnings?')


def main():
    farm, out = Path(sys.argv[1]), Path(sys.argv[2])
    results = {}
    for sdir in sorted(farm.glob('worker-*/sessions/*')):
        if not (sdir / 'manifest.json').exists():
            continue
        proc = subprocess.run(['uv', 'run', '--quiet', 'sts2rec', 'validate', str(sdir)], capture_output=True,
                              text=True, cwd=ROOT / 'sts2rec')
        text = (proc.stdout + proc.stderr).strip().splitlines()
        match = next((SUMMARY.match(line) for line in reversed(text) if SUMMARY.match(line)), None)
        results[f'{sdir.parents[1].name}/{sdir.name}'] = {
            'ok': proc.returncode == 0 and bool(match) and match.group(1) == 'OK',
            'rc': proc.returncode,
            'errors': int(match.group(2)) if match else None,
            'warnings': int(match.group(3)) if match else None,
            'error_lines': [line[:300] for line in text if line.startswith('ERROR')][:10],
        }
        print(sdir.name, results[f'{sdir.parents[1].name}/{sdir.name}']['ok'], flush=True)
    out.write_text(json.dumps(results, indent=2))
    bad = [k for k, v in results.items() if not v['ok']]
    print(json.dumps({'sessions': len(results), 'ok': len(results) - len(bad), 'failed': bad}))


if __name__ == '__main__':
    main()
