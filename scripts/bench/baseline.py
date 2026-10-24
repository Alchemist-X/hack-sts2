#!/usr/bin/env python3
"""Freeze/restore the benchmark save baseline for one worker instance.

The baseline is a tarball of ``<inst>/home/.local/share/SlayTheSpire2`` (minus
logs/sentry/shader cache) plus a manifest with its sha256 and the sha256 of the
active profile's ``progress.save`` and of ``settings.save`` (MCP consent lives
there).  ``restore`` verifies all three, so every benchmark run starts from the
byte-identical snapshot.  Never point this at the human save directory.
"""
import argparse
import hashlib
import json
import os
import shutil
import tarfile
from pathlib import Path

SAVE_REL = Path('home/.local/share/SlayTheSpire2')
HUMAN_SAVE = Path.home() / '.local/share/SlayTheSpire2'
BASELINE_ROOT = Path(os.environ.get('STS2_BENCH_BASELINE_ROOT', '/workspace/private/bench-baseline'))
# Named baselines -> manifest file inside BASELINE_ROOT.  v1 = the original
# worker profile (IRONCLAD3 revealed); v2 = v1 fully unlocked by
# scripts/bench/make_unlocked_save.py (all epochs/characters/cards/relics/potions).
BASELINES = {'v1': 'manifest.json', 'v2': 'manifest-v2-unlocked.json'}
DEFAULT_BASELINE = 'v2'


class BaselineError(RuntimeError):
    pass


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def manifest_path(baseline=DEFAULT_BASELINE):
    """``v1``/``v2`` (see BASELINES), a baseline directory (its manifest.json) or a manifest file."""
    if str(baseline) in BASELINES:
        return BASELINE_ROOT / BASELINES[str(baseline)]
    path = Path(baseline)
    return path / 'manifest.json' if path.is_dir() else path


def load_manifest(baseline=DEFAULT_BASELINE):
    path = manifest_path(baseline)
    if not path.is_file():
        raise BaselineError(f'baseline manifest not found: {path}')
    manifest = json.loads(path.read_text())
    for key in ('tarball', 'sha256', 'active_profile', 'progress_save_sha256', 'settings_save_sha256'):
        if key not in manifest:
            raise BaselineError(f'baseline manifest missing {key}')
    return manifest


def _instance_running(inst):
    process = Path(inst) / 'process.json'
    if not process.exists():
        return False
    try:
        pid = int(json.loads(process.read_text()).get('pid'))
        os.kill(pid, 0)
    except (ValueError, TypeError, ProcessLookupError, json.JSONDecodeError):
        return False
    except PermissionError:
        return True
    return True


def _safe_members(tar, root):
    for member in tar.getmembers():
        name = Path(member.name)
        if name.is_absolute() or '..' in name.parts or name.parts[0] != root:
            raise BaselineError(f'unsafe path in baseline tarball: {member.name}')
        if member.issym() or member.islnk():
            raise BaselineError(f'links are not allowed in the baseline: {member.name}')
        yield member


def restore(inst, baseline=DEFAULT_BASELINE):
    """Replace the instance save tree with the baseline; returns verification facts."""
    inst = Path(inst).resolve()
    manifest = load_manifest(baseline)
    baseline_dir = manifest_path(baseline).resolve().parent
    target = inst / SAVE_REL
    if target.resolve() == HUMAN_SAVE.resolve() or 'worker-pool' not in str(inst):
        raise BaselineError(f'refusing to restore outside a worker pool: {target}')
    if _instance_running(inst):
        raise BaselineError(f'{inst.name} is running; stop it before restoring')
    tarball = baseline_dir / manifest['tarball']
    actual = sha256(tarball)
    if actual != manifest['sha256']:
        raise BaselineError(f'baseline tarball sha256 {actual} != manifest {manifest["sha256"]}')
    if target.exists():
        shutil.rmtree(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(tarball) as tar:
        extra = {'filter': 'data'} if hasattr(tarfile, 'data_filter') else {}
        tar.extractall(target.parent, members=list(_safe_members(tar, SAVE_REL.name)), **extra)
    active = target / manifest['active_profile']
    progress = sha256(active / 'saves/progress.save')
    settings_path = target / Path(manifest['active_profile']).parts[0] / Path(manifest['active_profile']).parts[1] / 'settings.save'
    settings = sha256(settings_path)
    if progress != manifest['progress_save_sha256'] or settings != manifest['settings_save_sha256']:
        raise BaselineError(f'restored save hash mismatch: progress={progress} settings={settings}')
    return {'instance': inst.name, 'baseline': manifest.get('name'), 'tarball_sha256': actual,
            'progress_save_sha256': progress, 'settings_save_sha256': settings}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['restore', 'verify'])
    p.add_argument('--inst', type=Path)
    p.add_argument('--baseline', default=DEFAULT_BASELINE,
                   help=f'{" | ".join(BASELINES)} (default {DEFAULT_BASELINE}), a baseline dir or a manifest file')
    a = p.parse_args()
    if a.command == 'verify':
        m = load_manifest(a.baseline)
        ok = sha256(manifest_path(a.baseline).parent / m['tarball']) == m['sha256']
        print(json.dumps({'ok': ok, 'name': m.get('name'), 'sha256': m['sha256']}))
        raise SystemExit(0 if ok else 1)
    if not a.inst:
        raise SystemExit('--inst required')
    print(json.dumps(restore(a.inst, a.baseline)))


if __name__ == '__main__':
    main()
