#!/usr/bin/env python3
"""Freeze/restore the benchmark save baseline for one worker instance.

The baseline is a tarball of the worker save tree (``<inst>/home/.local/share/
SlayTheSpire2`` on Linux, ``<inst>/home/Library/Application Support/SlayTheSpire2``
on macOS; minus logs/sentry/shader cache) plus a manifest with its sha256 and the
sha256 of the active profile's ``progress.save`` and of ``settings.save`` (MCP
consent lives there).  ``restore`` verifies all three, so every benchmark run
starts from the byte-identical snapshot.  ``freeze`` writes such a tarball and
manifest deterministically (same tree -> same sha256).  Never point this at the
human save directory.
"""
import argparse
import gzip
import hashlib
import io
import json
import os
import shutil
import tarfile
from datetime import datetime, timezone
from pathlib import Path

SAVE_REL = Path('home/.local/share/SlayTheSpire2')  # Linux workers (Godot user:// under XDG data)
MAC_SAVE_REL = Path('home/Library/Application Support/SlayTheSpire2')  # macOS workers
HUMAN_SAVE = Path.home() / '.local/share/SlayTheSpire2'
HUMAN_SAVES = (HUMAN_SAVE, Path.home() / 'Library/Application Support/SlayTheSpire2')
BASELINE_ROOT = Path(os.environ.get('STS2_BENCH_BASELINE_ROOT', '/workspace/private/bench-baseline'))
# Named baselines -> manifest file inside BASELINE_ROOT/<game version>/ (e.g. v0.111.0/),
# falling back to BASELINE_ROOT/ itself (the v0.107.1 layout).  v1 = the original
# worker profile (IRONCLAD3 revealed); v2 = fully unlocked by
# scripts/bench/make_unlocked_save.py (all epochs/characters/cards/relics/potions);
# v2-a10 = v2 with every character's preferred ascension 10 (the lobby starts at A10).
BASELINES = {'v1': 'manifest.json', 'v2': 'manifest-v2-unlocked.json', 'v2-a10': 'manifest-v2-a10.json'}
DEFAULT_BASELINE = 'v2'
ACTIVE_PROFILE = 'default/1/modded/profile1'
# Left out of a frozen baseline: logs, crash reporting and caches, never game state.
FREEZE_EXCLUDE_TOP = ('localization_override', 'logs', 'sentry.dat', 'shader_cache', 'vulkan')
FREEZE_MTIME = 1793000000  # fixed member mtime (with uid/gid 0, fixed modes, gzip mtime 0)


class BaselineError(RuntimeError):
    pass


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def save_rel(inst):
    """The instance's save tree (relative to the instance dir): the macOS layout when the
    worker has one (provisioning writes settings.save there; checked on the parent so an
    interrupted restore, which only removes SlayTheSpire2/, keeps the layout), else Linux."""
    return MAC_SAVE_REL if (Path(inst) / MAC_SAVE_REL).parent.is_dir() else SAVE_REL


def version_dir(game_version):
    """``0.111.0`` / ``v0.111.0`` -> ``v0.111.0`` (the per-version baseline directory name)."""
    version = str(game_version or '').strip()
    return version if not version or version.startswith('v') else f'v{version}'


def manifest_path(baseline=DEFAULT_BASELINE, game_version=None):
    """A label (``v1``/``v2``/``v2-a10``, see BASELINES) resolves under BASELINE_ROOT/<game_version>/
    when that manifest exists, else under BASELINE_ROOT/ (the flat v0.107.1 layout; a benchmark
    config's baseline pins then refuse a wrong game version's baseline by name and sha256).
    Otherwise a baseline directory (its manifest.json) or a manifest file."""
    if str(baseline) in BASELINES:
        name = BASELINES[str(baseline)]
        if game_version:
            versioned = BASELINE_ROOT / version_dir(game_version) / name
            if versioned.is_file():
                return versioned
        return BASELINE_ROOT / name
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
    process = Path(inst) / 'process.json'  # hack-sts2-cloud worker_runtime.py
    pid_file = Path(inst) / 'pid'  # scripts/headless_launch.sh (pid file only)
    if not process.exists() and not pid_file.exists():
        return False
    try:
        pid = (int(json.loads(process.read_text()).get('pid')) if process.exists()
               else int(pid_file.read_text().strip()))
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
    target = inst / save_rel(inst)
    if any(target.resolve() == h.resolve() for h in HUMAN_SAVES) or 'worker-pool' not in str(inst):
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
        tar.extractall(target.parent, members=list(_safe_members(tar, target.name)), **extra)
    active = target / manifest['active_profile']
    progress = sha256(active / 'saves/progress.save')
    settings_path = target / Path(manifest['active_profile']).parts[0] / Path(manifest['active_profile']).parts[1] / 'settings.save'
    settings = sha256(settings_path)
    if progress != manifest['progress_save_sha256'] or settings != manifest['settings_save_sha256']:
        raise BaselineError(f'restored save hash mismatch: progress={progress} settings={settings}')
    return {'instance': inst.name, 'baseline': manifest.get('name'), 'tarball_sha256': actual,
            'progress_save_sha256': progress, 'settings_save_sha256': settings}


def freeze(inst, out_dir, name, manifest_file, extra=None):
    """Freeze ``inst``'s (stopped) save tree into ``out_dir/<name>.tar.gz`` and ``out_dir/<manifest_file>``.

    Deterministic: members sorted, uid/gid 0, fixed modes and mtimes, no gzip timestamp, so the
    same tree always gives the same tarball sha256.  ``extra`` (e.g. game_version, game_commit,
    sts2_dll_sha256, profile_origin) is merged into the manifest.  Returns the manifest."""
    inst = Path(inst).resolve()
    rel = save_rel(inst)
    save_root = inst / rel
    if 'worker-pool' not in str(inst) or any(save_root.resolve() == h.resolve() for h in HUMAN_SAVES):
        raise BaselineError(f'refusing to freeze outside a worker pool: {save_root}')
    if _instance_running(inst):
        raise BaselineError(f'{inst.name} is running; stop it before freezing')
    active = save_root / ACTIVE_PROFILE
    settings_path = save_root / Path(ACTIVE_PROFILE).parts[0] / Path(ACTIVE_PROFILE).parts[1] / 'settings.save'
    if not (active / 'saves/progress.save').is_file() or not settings_path.is_file():
        raise BaselineError(f'no progress.save/settings.save under {save_root}; launch the worker once first')
    members = []
    for path in sorted(save_root.rglob('*')):
        relative = path.relative_to(save_root)
        if relative.parts[0] in FREEZE_EXCLUDE_TOP:
            continue
        if path.is_symlink():
            raise BaselineError(f'links are not allowed in the baseline: {path}')
        members.append((relative, path))
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.PAX_FORMAT) as tar:
        def add(arcname, path):
            info = tar.gettarinfo(str(path), arcname=arcname)
            info.uid = info.gid = 0
            info.uname = info.gname = ''
            info.mtime = FREEZE_MTIME
            info.mode = 0o755 if info.isdir() else 0o644
            info.pax_headers = {}
            if info.isfile():
                with open(path, 'rb') as handle:
                    tar.addfile(info, handle)
            else:
                tar.addfile(info)
        add(save_root.name, save_root)
        for relative, path in members:
            add(str(Path(save_root.name) / relative), path)
    out_dir = Path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    tarball = out_dir / f'{name}.tar.gz'
    for target in (tarball, out_dir / manifest_file):
        if target.exists():
            raise BaselineError(f'{target} exists; frozen baselines are never overwritten')
    with open(tarball, 'wb') as handle:
        with gzip.GzipFile(filename='', mode='wb', fileobj=handle, mtime=0, compresslevel=9) as gz:
            gz.write(raw.getvalue())
    manifest = {
        'name': name, 'tarball': tarball.name, 'sha256': sha256(tarball), 'tar_root': save_root.name,
        'active_profile': ACTIVE_PROFILE,
        'progress_save_sha256': sha256(active / 'saves/progress.save'),
        'settings_save_sha256': sha256(settings_path),
        'files': {str(relative): sha256(path) for relative, path in members if path.is_file()},
        'excluded_top_level': list(FREEZE_EXCLUDE_TOP), 'save_layout': str(rel),
        'created_at': datetime.now(timezone.utc).isoformat(), **(extra or {}),
    }
    (out_dir / manifest_file).write_text(json.dumps(manifest, indent=2) + '\n')
    return manifest


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('command', choices=['restore', 'verify', 'freeze'])
    p.add_argument('--inst', type=Path)
    p.add_argument('--baseline', default=DEFAULT_BASELINE,
                   help=f'{" | ".join(BASELINES)} (default {DEFAULT_BASELINE}), a baseline dir or a manifest file')
    p.add_argument('--game-version', help='resolve a baseline label under BASELINE_ROOT/<version>/ (e.g. v0.111.0)')
    p.add_argument('--out', type=Path, help='freeze: output dir, e.g. $STS2_BENCH_BASELINE_ROOT/v0.111.0')
    p.add_argument('--name', help='freeze: baseline name, e.g. sts2-bench-baseline-v2-unlocked-v0.111.0')
    p.add_argument('--manifest-file', help=f'freeze: manifest file name, e.g. {BASELINES["v2"]}')
    p.add_argument('--extra', type=json.loads, default=None, help='freeze: JSON object merged into the manifest')
    a = p.parse_args()
    if a.command == 'verify':
        path = manifest_path(a.baseline, a.game_version)
        m = load_manifest(path)
        ok = sha256(path.parent / m['tarball']) == m['sha256']
        print(json.dumps({'ok': ok, 'name': m.get('name'), 'sha256': m['sha256'], 'manifest': str(path)}))
        raise SystemExit(0 if ok else 1)
    if not a.inst:
        raise SystemExit('--inst required')
    if a.command == 'freeze':
        if not (a.out and a.name and a.manifest_file):
            raise SystemExit('freeze needs --out, --name and --manifest-file')
        m = freeze(a.inst, a.out, a.name, a.manifest_file, a.extra)
        print(json.dumps({k: m[k] for k in ('name', 'tarball', 'sha256', 'progress_save_sha256',
                                             'settings_save_sha256')}, indent=2))
        return
    print(json.dumps(restore(a.inst, manifest_path(a.baseline, a.game_version))))


if __name__ == '__main__':
    main()
