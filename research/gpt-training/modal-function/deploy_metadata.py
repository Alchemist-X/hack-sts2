"""Exact metadata deployment only. Default dry run; never invokes a Function."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import os
import platform
import sys
import time

ROOT = Path(__file__).resolve().parent


def sha(path):
    with path.open('rb') as handle:
        return hashlib.file_digest(handle,'sha256').hexdigest()


def canonical(value):
    return json.dumps(value,sort_keys=True,separators=(',',':'),allow_nan=False)


def verify_package(expected):
    manifest_path=ROOT/'package-manifest.json'
    manifest_raw=manifest_path.read_bytes()
    if expected is not None and hashlib.sha256(manifest_raw).hexdigest()!=expected:
        raise ValueError('package manifest differs from exact operator release')
    manifest=json.loads(manifest_raw)
    for name,expected_hash in manifest['files'].items():
        path=ROOT/name
        if path.is_symlink() or not path.is_file() or sha(path)!=expected_hash:
            raise ValueError('formal package member differs: '+name)
    return manifest


def validate_release(release,contract):
    required={'app_name','source_volume_id','output_volume_id','accepted_volume_id','claim_dict_id',
              'image_id','timeout_seconds','absolute_deadline','overlay_hashes','bundle_hashes',
              'environment_name','workspace'}
    if type(release)is not dict or set(release)!=required:
        raise ValueError('formal release requires the exact metadata schema')
    for key in ('app_name','source_volume_id','image_id','timeout_seconds','absolute_deadline',
                'overlay_hashes','bundle_hashes','accepted_volume_id','environment_name','workspace'):
        if release[key]!=contract['fixed_release'][key]:
            raise ValueError('formal release changes pinned source/resource field: '+key)
    missing=[key for key in ('output_volume_id','claim_dict_id') if release[key] is None]
    if missing:return missing
    import re
    for key,prefix in (('output_volume_id','vo'),('claim_dict_id','di')):
        if type(release[key])is not str or not re.fullmatch(prefix+r'-[A-Za-z0-9]+',release[key]):
            raise ValueError('invalid exact resource ID: '+key)
    if release['output_volume_id']==release['source_volume_id']:
        raise ValueError('formal source must remain a distinct read-only Volume')
    return []


def write_receipt(path,value,*,exclusive=False):
    path=Path(path)
    raw=(canonical(value)+'\n').encode()
    if exclusive:
        fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
        with os.fdopen(fd,'wb')as handle:handle.write(raw);handle.flush();os.fsync(handle.fileno())
    else:
        temporary=path.with_name(path.name+'.completed.'+str(os.getpid()))
        fd=os.open(temporary,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
        with os.fdopen(fd,'wb')as handle:handle.write(raw);handle.flush();os.fsync(handle.fileno())
        os.replace(temporary,path)
    directory=os.open(path.parent,os.O_RDONLY|os.O_DIRECTORY)
    try:os.fsync(directory)
    finally:os.close(directory)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release',type=Path,default=ROOT/'release.DISABLED-TEMPLATE.json')
    parser.add_argument('--release-sha256')
    parser.add_argument('--package-sha256')
    parser.add_argument('--deploy',action='store_true')
    parser.add_argument('--receipt',type=Path)
    args=parser.parse_args()
    manifest=verify_package(args.package_sha256)
    release_raw=args.release.read_bytes()
    release=json.loads(release_raw)
    if args.release_sha256 is not None and hashlib.sha256(release_raw).hexdigest()!=args.release_sha256:
        raise ValueError('release document differs from exact operator release')
    contract=json.loads((ROOT/'deployment-contract.json').read_bytes())
    missing=validate_release(release,contract)
    if not args.deploy:
        print(canonical({'status':'disabled_template'if missing else'ready_metadata_only',
                         'arm':contract['arm'],'missing_resource_ids':missing,
                         'package_sha256':sha(ROOT/'package-manifest.json'),
                         'source_commit':contract['source_commit'],'compute_invocations':0,
                         'provider_calls':0,'production_mutations':0}))
        return 0
    if missing or args.receipt is None or args.release_sha256 is None or args.package_sha256 is None:
        raise ValueError('deployment requires exact package/release hashes, resource IDs, and exclusive receipt path')
    if sys.version_info[:2]!=(3,11):
        raise RuntimeError('deployment Python must match the immutable GPU image Python3.11')
    import modal
    if modal.__version__!='1.6.3':raise RuntimeError('formal deployment requires Modal1.6.3')
    intent={'status':'metadata_attempt_unknown_until_reconciled','at':time.time(),
            'package_sha256':args.package_sha256,'release_sha256':args.release_sha256,
            'app_name':release['app_name'],'compute_invocations':0,'source_writes':0}
    # Raven local one-shot intent only. A crash must be reconciled by the same
    # app/name; it never authorizes a new namespace, deployment, or paid input.
    write_receipt(args.receipt,intent,exclusive=True)
    workspace=modal.Workspace.from_context().hydrate()
    if workspace.name!=release['workspace']:raise ValueError('workspace differs from exact release')
    output=modal.Volume.from_name(contract['output_volume_name'],environment_name=release['environment_name'],
                                  create_if_missing=False,version=2).hydrate()
    claims=modal.Dict.from_name(contract['claim_dict_name'],environment_name=release['environment_name'],
                               create_if_missing=False).hydrate()
    if output.object_id!=release['output_volume_id'] or claims.object_id!=release['claim_dict_id']:
        raise ValueError('existing metadata resource IDs/names differ from release')
    sys.dont_write_bytecode=True
    if 'function_app'in sys.modules:raise RuntimeError('formal module name is already occupied')
    module_spec=importlib.util.spec_from_file_location('function_app',ROOT/'function_app.py')
    module=importlib.util.module_from_spec(module_spec);sys.modules['function_app']=module
    module_spec.loader.exec_module(module)
    app,function=module.declare(modal,release)
    app.deploy(environment_name=release['environment_name'])
    receipt={**intent,'status':'metadata_deployed_no_invocation','completed_at':time.time(),
             'app_id':app.app_id,'function_id':function.object_id,'output_volume_id':output.object_id,
             'claim_dict_id':claims.object_id,'python':platform.python_version(),'sdk':modal.__version__,
             'source_commit':contract['source_commit'],'native_overlay_sha256':release['overlay_hashes']['native_worker.py']}
    write_receipt(args.receipt,receipt)
    print(canonical(receipt))
    return 0


if __name__=='__main__':raise SystemExit(main())
