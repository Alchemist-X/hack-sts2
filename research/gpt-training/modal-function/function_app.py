"""Task103 isolated deployed Function. No deployment or paid invocation on import.

Deploy this file only from a frozen release containing the immutable original
image/Volume/bundle and separately reviewed transport overlay hashes.
"""
from pathlib import Path
import hashlib
import importlib.util
import json
import os
import re
import signal
import sys
import time
from uuid import uuid4

SOURCE = Path('/sts2-study')
OUTPUT = Path('/sts2-function-output')
ACCEPTED = Path('/sts2-function-accepted')
OVERLAY = Path('/sts2-function-code/modal_research')
CORE_COUNT = 32
MEMORY_MIB = 65536


def write_json(path, body):
    """Each handle has a unique attempt path; no overwrite is a lock."""
    data = json.dumps(body, sort_keys=True, separators=(',', ':'), allow_nan=False)
    with path.open('x') as handle:
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())


def check_bundle(root, expected):
    path = root / 'bundle.json'
    if path.is_symlink() or not 0 < path.stat().st_size <= 256000:
        raise ValueError('invalid immutable source bundle')
    if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        raise ValueError('source bundle hash differs')
    files = json.loads(path.read_bytes())['files']
    actual = {}
    for item in root.rglob('*'):
        if item.is_symlink() or (not item.is_file() and not item.is_dir()):
            raise ValueError('unsafe bundle member')
        if item.is_file() and item != path:
            with item.open('rb') as handle:
                actual[item.relative_to(root).as_posix()] = hashlib.file_digest(handle, 'sha256').hexdigest()
    if actual != files:
        raise ValueError('immutable source bundle members differ')


def import_overlay(name, expected, directory=OVERLAY):
    path = directory / (name + '.py')
    if path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        raise ValueError('transport overlay hash differs')
    fullname = 'modal_research.' + name
    spec = importlib.util.spec_from_file_location(fullname, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[fullname] = module
    spec.loader.exec_module(module)
    return module


def resume_root(location, token):
    if location == {'source': 'original'}:
        return SOURCE / 'outputs'
    if (type(location) is dict and set(location) == {'source', 'token', 'attempt'}
            and location['source'] in {'accepted', 'function'}
            and isinstance(location['token'], str) and re.fullmatch(r'[A-Za-z0-9_.:-]{1,192}', location['token'])
            and isinstance(location['attempt'], str) and re.fullmatch(r'[a-f0-9]{32}', location['attempt'])):
        # 'accepted' is a separate immutable prior Function Volume mounted RO.
        # 'function' is an earlier generation in this output Volume: the code
        # only reads it and copies into this invocation's private directory.
        base = ACCEPTED if location['source'] == 'accepted' else OUTPUT
        return base / 'attempts' / location['token'] / location['attempt'] / 'outputs'
    raise ValueError('invalid frozen resume location')


def execute(modal, *, packet, packet_hash, bundle_hash, cutoff, resume_location,
            output_volume, claims, release, clock=time.time, attempt=None):
    if type(cutoff) is not int or cutoff > release['absolute_deadline'] or clock() >= cutoff:
        raise ValueError('Function absolute execution deadline expired')
    if (not isinstance(packet, str) or not 0 < len(packet.encode()) <= 32000
            or hashlib.sha256(packet.encode()).hexdigest() != packet_hash):
        raise ValueError('invalid one-shot grant')
    raw = json.loads(packet)
    token = raw['launch_token']
    if not isinstance(token, str) or not re.fullmatch(r'[A-Za-z0-9_.:-]{1,192}', token):
        raise ValueError('invalid launch token')
    config_hash = raw['job']['payload']['config_hash']
    if release['bundle_hashes'].get(config_hash) != bundle_hash:
        raise ValueError('Function grant is outside frozen bundle allowlist')
    if cutoff != int(raw['job']['deadline']):
        raise ValueError('Function cutoff differs from broker grant')
    attempt = uuid4().hex if attempt is None else attempt
    if not isinstance(attempt,str) or not re.fullmatch(r"[a-f0-9]{32}",attempt):
        raise ValueError("invalid invocation attempt UUID")
    call_id = modal.current_function_call_id()
    input_id = modal.current_input_id()
    if not isinstance(call_id, str) or not re.fullmatch(r'fc-[A-Za-z0-9]+', call_id):
        raise ValueError('Function requires a durable call ID')
    # New single-use container, no files have been opened/written on the Volume.
    output_volume.reload()
    attempt_root = OUTPUT / 'attempts' / token / attempt
    attempt_root.mkdir(parents=True, exist_ok=False)
    handle = dict(call_id=call_id, input_id=input_id, launch_token=token,
                  packet_hash=packet_hash, attempt=attempt, cutoff=cutoff)
    write_json(attempt_root / 'handle.json', handle)
    output_volume.commit()
    report = dict(packet_hash=packet_hash, job_key=raw['job']['key'], launch_token=token,
                  fence=raw['fence'], status='failed', partial=None, result=None, candidate=None)
    # Server-side atomic if_not_exists is the admission fence. Neither a Volume
    # marker nor open(x) is used for distributed exclusion. Unknown put -> no train.
    acquired = claims.put(token, handle, skip_if_exists=True)
    if acquired is not True:
        existing = claims.get(token)
        if (existing['packet_hash'], existing['call_id']) != (packet_hash, call_id):
            raise ValueError('launch token reused by another call or grant')
        write_json(attempt_root / 'receipt.json', report)
        output_volume.commit()
        # The transport has reconciled the same already-claimed input. The
        # collector still requires an actual successful original worker receipt
        # and fully verified ready; this does not assert training succeeded.
        return {'exit_code': 0, 'attempt': attempt}
    signal.setitimer(signal.ITIMER_REAL, max(0.001, cutoff - clock()))
    try:
        bundle = SOURCE / 'bundles' / config_hash
        check_bundle(bundle, bundle_hash)
        # These are engineering thread caps, set before loading .NET or torch.
        os.environ.update(OMP_NUM_THREADS='1', MKL_NUM_THREADS='1', OPENBLAS_NUM_THREADS='1',
                          DOTNET_PROCESSOR_COUNT='32', DOTNET_gcServer='1', DOTNET_gcConcurrent='0')
        sys.path.insert(0, str(bundle / 'code'))
        import modal_research
        expected_package = (bundle / 'code' / 'modal_research').resolve()
        if Path(modal_research.__file__).resolve().parent != expected_package:
            raise ValueError('Function imported an unpinned worker package')
        import_overlay('native_worker', release['overlay_hashes']['native_worker.py'])
        worker = import_overlay('function_worker', release['overlay_hashes']['function_worker.py'])
        worker.worker(packet, packet_hash, bundle_hash, cutoff, source_root=SOURCE,
                      attempt_root=attempt_root, resume_root=resume_root(resume_location, token), volume=output_volume)
        return {'exit_code': 0, 'attempt': attempt}
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)


def run_entry(modal, **arguments):
    """Return structured execution failures; caller-side RPC uncertainty is separate.

    The attempt UUID exists even if validation/metadata/claim fails before any
    durable handle can be written. No exception text, credentials or private
    data is returned. The worker retains its original failed/partial receipt.
    """
    attempt = uuid4().hex
    try:
        return execute(modal, attempt=attempt, **arguments)
    except Exception as error:
        try:
            log_execution_failure(error)
        except Exception:
            pass  # diagnostics must not change the fixed terminal return
        return {'exit_code': 1, 'attempt': attempt}


def log_execution_failure(error):
    """Public error location only; never messages, source text, locals or values."""
    import traceback
    frames = traceback.extract_tb(error.__traceback__)
    body = {'event':'sts2_function_execution_failed', 'exception_type':type(error).__name__,
            'frames':[{'file':Path(frame.filename).name, 'line':frame.lineno,
                       'function':frame.name} for frame in frames[-16:]]}
    print(json.dumps(body, sort_keys=True, separators=(',',':')), file=sys.stderr, flush=True)


def declare(modal, release):
    """Declare fixed resources; hydrate existing Volume metadata, no paid invocation.

    from_id Volumes must be hydrated before SDK 1.6.3 builds FunctionInfo.
    This performs read-only control-plane requests, never an image build or run.
    """
    if modal.__version__ != '1.6.3':
        raise RuntimeError('Task103 requires the reviewed Modal SDK 1.6.3')
    if not 1 <= release['timeout_seconds'] <= 1500:
        raise ValueError('formal Function timeout exceeds the reviewed 1500-second envelope')
    for key, prefix in (('source_volume_id','vo'),('output_volume_id','vo'),('image_id','im'),('claim_dict_id','di')):
        if not isinstance(release[key],str) or not re.fullmatch(prefix+r'-[A-Za-z0-9]+',release[key]):
            raise ValueError('invalid fixed Modal object ID')
    source = modal.Volume.from_id(release['source_volume_id']).hydrate()
    output = modal.Volume.from_id(release['output_volume_id']).hydrate()
    # SDK 1.6.3 exposes no public per-handle version accessor. Its hydrated
    # metadata is audited here because ready publication uses hard links.
    from modal_proto.api_pb2 import VOLUME_FS_VERSION_V2
    if output._get_metadata().version != VOLUME_FS_VERSION_V2:
        raise ValueError('Function output Volume must be verified V2')
    if release['source_volume_id'] == release['output_volume_id']:
        raise ValueError('source and output Volumes must be distinct')
    volumes = {str(SOURCE): source.with_mount_options(read_only=True), str(OUTPUT): output}
    accepted_id = release.get('accepted_volume_id')
    if accepted_id is not None:
        if not isinstance(accepted_id,str) or not re.fullmatch(r'vo-[A-Za-z0-9]+',accepted_id):
            raise ValueError('invalid accepted Volume ID')
        if accepted_id in {release['source_volume_id'], release['output_volume_id']}:
            raise ValueError('separate accepted mount requires a third distinct Volume')
        volumes[str(ACCEPTED)] = modal.Volume.from_id(accepted_id).hydrate().with_mount_options(read_only=True)
    if __name__ != 'function_app':
        raise ValueError('deploy this module under its fixed name function_app')
    # Serialized Functions have no implicit entrypoint source mount. The
    # closure references execute by this fixed module name on the container.
    image = modal.Image.from_id(release['image_id']).add_local_file(
        Path(__file__), remote_path='/root/function_app.py', copy=False)
    for filename in ('native_worker.py','function_worker.py'):
        local = Path(__file__).parent / 'modal_research' / filename
        if hashlib.sha256(local.read_bytes()).hexdigest() != release['overlay_hashes'][filename]:
            raise ValueError('local overlay differs from frozen deployment')
        image = image.add_local_file(local, remote_path=str(OVERLAY/filename), copy=False)
    claims = modal.Dict.from_id(release['claim_dict_id']).hydrate()
    app = modal.App(release['app_name'])

    @app.function(name='run_function', image=image, gpu='H100!', cpu=(32, 32), memory=(65536, 65536),
                  volumes=volumes, timeout=release['timeout_seconds'], startup_timeout=80,
                  serialized=True, retries=0, min_containers=0, max_containers=1, buffer_containers=0,
                  scaledown_window=2, single_use_containers=True, block_network=True)
    def run_function(packet, packet_hash, bundle_hash, cutoff, resume_location):
        return run_entry(modal, packet=packet, packet_hash=packet_hash, bundle_hash=bundle_hash,
                       cutoff=cutoff, resume_location=resume_location, output_volume=output,
                       claims=claims, release=release)

    return app, run_function
