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


class BaselineError(RuntimeError):
    pass


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def load_manifest(baseline_dir):
    baseline_dir = Path(baseline_dir)
    manifest = json.loads((baseline_dir / 'manifest.json').read_text())
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


def restore(inst, baseline_dir):
    """Replace the instance save tree with the baseline; returns verification facts."""
    inst, baseline_dir = Path(inst).resolve(), Path(baseline_dir).resolve()
    manifest = load_manifest(baseline_dir)
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
    p.add_argument('--baseline', type=Path, default=Path('/workspace/private/bench-baseline'))
    a = p.parse_args()
    if a.command == 'verify':
        m = load_manifest(a.baseline)
        ok = sha256(a.baseline / m['tarball']) == m['sha256']
        print(json.dumps({'ok': ok, 'sha256': m['sha256']}))
        raise SystemExit(0 if ok else 1)
    if not a.inst:
        raise SystemExit('--inst required')
    print(json.dumps(restore(a.inst, a.baseline)))


if __name__ == '__main__':
    main()
