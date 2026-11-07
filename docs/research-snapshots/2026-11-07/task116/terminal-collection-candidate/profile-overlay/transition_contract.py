"""Task116 isolated M profile of Task089 contract; only capacity constants/shapes differ, no provider/actor authority."""
import hashlib
import json
from pathlib import Path

VERSION = 'task089-public-nextframe-v1'
INTEGRATION = '75c4394dc3ee68a0b64444d862f1c150ae93b6b941664e73567b321b0ab28c75'
ROLE = 'transition_aux'
NAME = 'transition_aux.json'
MAX_BYTES = 128 * 1024
BASE_PARAMS, HEAD_PARAMS = 16106128, 74052
REWARD = 'protocol-v0.2-delta-win1-noacq-cycle-0p01-stepcap-bootstrap-v1'
MODEL = dict(d=384,s=768,depth=6,heads=8,ff_mult=4,deck_counts=True,aux_heads=True)
CONTRACT = dict(version=VERSION,channels=['player_hp','player_block','player_energy','sum_enemy_hp'],
                coefficient=.1,scales=[100.,100.,10.,100.],seed=89,hidden_width=64,
                loss='SmoothL1-beta1-perchannel-mean-div4',mask='same-combat-public-proxy-v1-owned-terminal-hp-only',
                encoder_version='3b73317739c650bd',spec_sha256='1891c314a6c7ddbfd5b7843194dd679538f82b579cdce4283ce7a5c53e145683')
IDENTITY = dict(version=VERSION,coefficient=.1,integration_sha256=INTEGRATION,head_params=HEAD_PARAMS)
REASONS = {'invalid_sample_or_index','noncombat_action_kind','pre_error_or_native_overflow','pre_trainer_width_overflow',
           'terminal_ownership','done_or_post_error_reset_overflow','post_trainer_width_overflow','episode_or_step_discontinuity',
           'room_discontinuity','combat_identity_proxy','combat_phase','enemy_membership_or_invalid','player_component_invalid'}


def canonical(obj):return json.dumps(obj,sort_keys=True,separators=(',',':'),allow_nan=False)
def digest(obj):return hashlib.sha256(canonical(obj).encode()).hexdigest()
INPUT_ARCH = 'reward_choices_v1'
PUBLIC_HAND_INPUT_ARCH = 'reward_choices_public_handselect_v1'
PUBLIC_HAND_MODEL_SPEC_SHA256 = '63e9cc460886b2b26836cff57514c24dfb39253b87cd31eea746fb0b061ef10b'
REWARD_MODEL_SPEC_SHA256 = '38c768172a5378c5e4795db9bfa5ea50adc7fe8218eff65b81d4b1eb71547973'


def input_arch(document):
    marker = document.get('input_arch', '')
    if marker not in ('', INPUT_ARCH, PUBLIC_HAND_INPUT_ARCH) or ('input_arch' in document and marker not in (INPUT_ARCH, PUBLIC_HAND_INPUT_ARCH)):
        raise ValueError('unknown or empty explicit input architecture')
    return marker


CRITIC_ARCH = 'state_legal_candidates_detached_mean_v1'
CRITIC_BASE_PARAMS = 16253584


def critic_arch(document):
    model = document.get('model_config', document)
    marker = model.get('critic_arch', '')
    if marker not in ('', CRITIC_ARCH) or ('critic_arch' in model and marker != CRITIC_ARCH):
        raise ValueError('unknown or empty explicit critic architecture')
    return marker


def base_params(document):
    return CRITIC_BASE_PARAMS if critic_arch(document) else BASE_PARAMS


def input_contract(document):
    marker = input_arch(document)
    if marker == PUBLIC_HAND_INPUT_ARCH:
        return dict(CONTRACT, spec_sha256=PUBLIC_HAND_MODEL_SPEC_SHA256)
    return dict(CONTRACT, spec_sha256=REWARD_MODEL_SPEC_SHA256) if marker else CONTRACT


def validate_critic_state(data, spec):
    expected = critic_arch(spec)
    if (data.get('arch', {}).get('critic_arch', '') != expected
            or data.get('config', {}).get('critic_arch', '') != expected):
        raise ValueError('checkpoint critic architecture differs from immutable experiment')
    state = data.get('model', {})
    key = 'value_candidate_proj.weight'
    if bool(expected) != (key in state):
        raise ValueError('critic checkpoint marker/projection differs')
    if not expected:return
    import torch
    value = state[key]
    if (not torch.is_tensor(value) or tuple(value.shape)!=(384,384)
            or value.dtype!=torch.float32 or not bool(torch.isfinite(value).all())
            or list(state)[-1]!=key):
        raise ValueError('critic projection schema, finiteness or append-only binding differs')
    optimizer=data.get('optimizer', {})
    groups=optimizer.get('param_groups', []);moments=optimizer.get('state', {})
    if (type(groups) is not list or len(groups)!=1 or type(moments) is not dict
            or groups[0].get('params')!=list(range(len(state)))
            or any(type(i) is not int for i in groups[0].get('params', []))):
        raise ValueError('critic optimizer canonical parameter IDs differ')
    ids=groups[0]['params']
    if any(type(i) is not int or i not in ids for i in moments):
        raise ValueError('critic optimizer contains unbound parameters')
    saved=moments.get(ids[-1])
    required={'step','exp_avg','exp_avg_sq'} | ({'max_exp_avg_sq'} if groups[0].get('amsgrad',False) else set())
    if type(saved) is not dict or set(saved)!=required:
        raise ValueError('critic projection Adam state incomplete')
    for name in required-{'step'}:
        v=saved[name]
        if not torch.is_tensor(v) or tuple(v.shape)!=(384,384) or v.dtype!=torch.float32 or not bool(torch.isfinite(v).all()):
            raise ValueError('critic projection Adam moment differs')
    step=saved['step']
    if (not torch.is_tensor(step) or step.ndim!=0 or step.dtype!=torch.float32
            or not bool(torch.isfinite(step)) or float(step)<1 or float(step)!=int(float(step))):
        raise ValueError('critic projection Adam step differs')
    all_steps=[float(v['step']) for v in moments.values() if type(v) is dict and torch.is_tensor(v.get('step')) and v['step'].numel()==1]
    if not all_steps or float(step)!=max(all_steps):
        raise ValueError('critic projection Adam step trails base')


def science_files(spec):
    if input_arch(spec) == PUBLIC_HAND_INPUT_ARCH:
        if critic_arch(spec) != CRITIC_ARCH:
            raise ValueError("public HandSelect input requires the frozen candidate critic profile")
        return dict(science_files(dict(spec, input_arch=INPUT_ARCH)), **{'code/sts2train/features.py': '316e969717284df7aefc1ac459b02b508097db0e2b755476ba42337476529030', 'code/sts2train/model.py': '1c0f48bbe45154417f5952d734673aa96874098c57010b765cdfa0b190becb21', 'code/sts2train/ppo.py': '97059b209603b556de165d137043a2a25f2ace7cab9bb3450891f309fbe76ea3', 'code/sts2train/evaluate.py': 'd4db3106a8018d7fe1dd9ecbeaa4384cc5528a650500f8dc76fa24161ce57f98'})
    if critic_arch(spec):
        if input_arch(spec) != INPUT_ARCH:
            raise ValueError('critic profile requires reward-choice inputs')
        return dict(SCIENCE_FILES, **{
            'code/sts2train/features.py': '10babafe5b54bf7fedf04cfc21e9acd92e0843154fbd45efd953b17240c47ad3',
            'code/sts2train/model.py': '4702c3ad3659209b11c7359d1943ca337de67bba0f37324e12ef17842056bd35',
            'code/sts2train/ppo.py': 'c66539a1564f3e20a1cec394b0bd42dda0951ccbf281c5d4810b00c8e1124e9e',
            'code/sts2train/critic_diagnostics.py': '0df080b9448177a58695b7ac5c1b76c82f758a0dd9261c860ddf8557796b1f54',
            'code/sts2train/transition_gpu_gate.py': '663a19669c7f34583a279cb44e1029055db437ed020225eb83e6cc4420589b8b',
        })
    if input_arch(spec):
        return dict(SCIENCE_FILES, **{
            'code/sts2train/features.py': '10babafe5b54bf7fedf04cfc21e9acd92e0843154fbd45efd953b17240c47ad3',
            'code/sts2train/model.py': '6a84612735aace38b168aebfb1adb137c9fe6c24ad7a3d28c3f4571d22af4f4a',
        })
    return SCIENCE_FILES


def positive(spec):return 'transition_coef' in spec['ppo_config']
def total_params(spec):return spec['actual_param_count'] + (HEAD_PARAMS if positive(spec) else 0)


def validate_recipe(spec):
    if not positive(spec):return
    input_arch(spec)
    critic = critic_arch(spec)
    if critic and input_arch(spec) not in (INPUT_ARCH, PUBLIC_HAND_INPUT_ARCH):raise ValueError('critic profile requires reward-choice inputs')
    expected_model = dict(MODEL, critic_arch=CRITIC_ARCH) if critic else MODEL
    p=spec['ppo_config']
    if any(spec[k]!=v for k,v in BASE_ENV.items()):raise ValueError('Task089 requires frozen084 native/schema/dependencies')
    if (type(p['transition_coef']) not in (int,float) or p['transition_coef']!=.1
            or spec['reward_version']!=REWARD or spec['model_config']!=expected_model
            or spec['actual_param_count']!=base_params(spec) or spec['parent_id'] is not None
            or spec['rollout_shape']!={'envs':2048,'rollout':64}
            or p.get('amp') is not False or p.get('tf32') is not False or p.get('compile')!='off'
            or p.get('epochs')!=1 or p.get('minibatch')!=4096):
        raise ValueError('Task089 requires exact original S, fresh root, fixed .1 eager FP32 recipe')
    expected=BASE_PPO | {'transition_coef':.1}
    if canonical(p)!=canonical(expected):raise ValueError('Task089 science recipe differs from frozen084')


def _integer(v,maximum):
    if type(v) is not int or not 0<=v<=maximum:raise ValueError('Task089 count must be a bounded integer')
    return v


def validate_counts(counts,steps):
    if type(steps) is not int or steps<1:raise ValueError('transition count decisions must be positive integer')
    fields={'terminal_hp_labeled','terminal_ownership_rejected','combat_identity_rejected','reasons',
            'valid_per_channel','samples','nonzero_per_channel','by_action_kind'}
    if type(counts) is not dict or set(counts)!=fields or counts['samples']!=steps:
        raise ValueError('Task089 cumulative coverage fields or sample total differ')
    _integer(counts['samples'],steps)
    def vectors(row,maximum):
        for name in ('valid_per_channel','nonzero_per_channel'):
            v=row[name]
            if type(v) is not list or len(v)!=4:raise ValueError('Task089 coverage vector shape differs')
            for n in v:_integer(n,maximum)
        if any(a>b for a,b in zip(row['nonzero_per_channel'],row['valid_per_channel'])):
            raise ValueError('Task089 nonzero count exceeds valid labels')
    vectors(counts,steps)
    for name in ('terminal_hp_labeled','terminal_ownership_rejected','combat_identity_rejected'):_integer(counts[name],steps)
    if counts['terminal_hp_labeled']>counts['valid_per_channel'][0]:raise ValueError('terminal HP exceeds HP coverage')
    reasons=counts['reasons']
    if type(reasons) is not dict or not reasons.keys()<=REASONS:raise ValueError('unknown transition rejection counter')
    for n in reasons.values():_integer(n,steps)
    kinds=counts['by_action_kind']
    if type(kinds) is not dict or set(kinds)!={'PlayCard','UsePotion','EndTurn'}:raise ValueError('action count fields differ')
    for row in kinds.values():
        if type(row) is not dict or set(row)!={'samples','valid_per_channel','nonzero_per_channel'}:
            raise ValueError('action counter schema differs')
        _integer(row['samples'],steps);vectors(row,row['samples'])
    if sum(row['samples'] for row in kinds.values())>steps:raise ValueError('action samples exceed total')
    for field in ('valid_per_channel','nonzero_per_channel'):
        if [sum(row[field][j] for row in kinds.values()) for j in range(4)]!=counts[field]:
            raise ValueError('action/channel coverage aggregate differs')
    if len(canonical(counts).encode())>32*1024:raise ValueError('Task089 cumulative counts exceed bound')


def validate_checkpoint(data,spec):
    """Validate full new-head state before publication OR train/eval loading, preserving RNG."""
    validate_critic_state(data, spec)
    enabled=positive(spec)
    config=data.get('config',{})
    coef=config.get('transition_coef',0.)
    saved=data.get('transition_aux')
    if not enabled:
        if coef!=0 or saved is not None:raise ValueError('baseline worker must not ignore transition training state')
        return None
    validate_recipe(spec)
    expected_contract = input_contract(spec)
    expected_arch = input_arch(spec)
    if (data.get('spec', {}).get('input_arch', '') != expected_arch
            or data.get('arch', {}).get('input_arch', '') != expected_arch
            or (expected_arch == PUBLIC_HAND_INPUT_ARCH and config.get('input_arch') != expected_arch)
            or (expected_arch != PUBLIC_HAND_INPUT_ARCH and config.get('input_arch', expected_arch) != expected_arch)):
        raise ValueError('checkpoint input architecture differs from immutable experiment')
    if (type(coef) not in (int,float) or coef!=.1 or type(saved) is not dict
            or set(saved)!={'contract','head_weights_sha256','head','optimizer','counts'}
            or saved['contract']!=expected_contract or data.get('params')!=base_params(spec)):
        raise ValueError('Task089 checkpoint contract or complete separate training state differs')
    if type(data.get('updates')) is not int or data['updates']<1:raise ValueError('formal transition checkpoint needs completed updates')
    if digest(data['spec'])!=expected_contract['spec_sha256']:raise ValueError('Task089 ModelSpec is not the frozen public schema')
    validate_counts(saved['counts'],data['env_steps'])
    import torch
    from types import SimpleNamespace
    from sts2train import transition_state as ts
    expected={k:torch.empty(shape,dtype=torch.float32) for k,shape in (('layers.0.weight',(64,1152)),('layers.0.bias',(64,)),('layers.2.weight',(4,64)),('layers.2.bias',(4,)))}
    head=SimpleNamespace(parameters=lambda:iter(expected.values()))
    weights=saved['head']
    if (type(weights) is not dict or set(weights)!=set(expected)
            or any(not torch.is_tensor(weights[k]) or weights[k].shape!=v.shape or weights[k].dtype!=v.dtype
                   or not bool(torch.isfinite(weights[k]).all()) for k,v in expected.items())):
        raise ValueError('Task089 head tensor schema or finite state differs')
    base=data.get('optimizer',{});aux=saved['optimizer']
    if type(base) is not dict or type(aux) is not dict:raise ValueError('Task089 optimizer states absent')
    bg=base.get('param_groups',[]);ag=aux.get('param_groups',[])
    if len(bg)!=1 or len(ag)!=1 or canonical({k:v for k,v in bg[0].items() if k!='params'})!=canonical({k:v for k,v in ag[0].items() if k!='params'}):
        raise ValueError('Task089 full head/base Adam group settings differ')
    # The independently accepted frozen validator enforces every Adam moment,
    # float32 scalar synchronized step and base/head step agreement.
    ts.validate_adam_state(saved['optimizer'],head,data['updates'],data['optimizer'])
    if ts.weights_hash(weights)!=saved['head_weights_sha256']:raise ValueError('Task089 head tensor digest differs')
    restored=saved['counts']
    return {'contract':expected_contract,'head_weights_sha256':saved['head_weights_sha256'],
            'base_params':base_params(spec),'head_params':HEAD_PARAMS,'training_total_params':base_params(spec)+HEAD_PARAMS,
            'updates':data['updates'],'cumulative_counts':restored,'integration_sha256':INTEGRATION,
            'head_optimizer_step':int(next(iter(aux['state'].values()))['step'])}


def sidecar(data,spec,checkpoint_sha):
    result=validate_checkpoint(data,spec)
    if result is None:return None
    return dict(version=1,checkpoint_sha256=checkpoint_sha,embedded='transition_aux',**result)


def verify_sidecar(root,identity,metadata,entries):
    enabled=identity.get('training_auxiliary')==IDENTITY and identity['kind']=='train'
    if not enabled:
        if ROLE in entries or 'transition_auxiliary' in metadata:raise ValueError('unexpected transition sidecar on baseline/eval')
        return
    if ROLE not in entries or metadata.get('transition_auxiliary')!=IDENTITY:raise ValueError('missing exact transition manifest metadata')
    entry=entries[ROLE]
    if entry['name']!=NAME or entry['size']>MAX_BYTES:raise ValueError('transition sidecar path or size differs')
    body=json.loads((Path(root)/NAME).read_bytes())
    required={'version','checkpoint_sha256','embedded','contract','head_weights_sha256','base_params','head_params',
              'training_total_params','updates','cumulative_counts','integration_sha256','head_optimizer_step'}
    if (type(body) is not dict or set(body)!=required or type(body['version']) is not int or body['version']!=1
            or body['contract']!=input_contract(identity) or body['embedded']!='transition_aux'
            or body['checkpoint_sha256']!=entries['model']['sha256'] or type(body['updates']) is not int or body['updates']!=metadata['updates']
            or body['base_params']!=base_params(identity) or body['head_params']!=HEAD_PARAMS
            or body['training_total_params']!=base_params(identity)+HEAD_PARAMS or body['integration_sha256']!=INTEGRATION
            or type(body['head_optimizer_step']) is not int or body['head_optimizer_step']<metadata['updates']
            or type(body['head_weights_sha256']) is not str or len(body['head_weights_sha256'])!=64
            or any(c not in '0123456789abcdef' for c in body['head_weights_sha256'])):
        raise ValueError('transition sidecar differs from complete checkpoint or schema')
    validate_counts(body['cumulative_counts'],metadata['decisions'])

BASE_PPO = {'acq_deck_coef': 0.003,
 'acq_relic_coef': 0.01,
 'acq_shaping': False,
 'acq_upgrade_coef': 0.004,
 'amp': False,
 'aux_bootstrap': True,
 'aux_coef': 0.1,
 'clip': 0.2,
 'compile': 'off',
 'cycle_cost': 0.01,
 'ent_coef': 0.02,
 'epochs': 1,
 'floor_step_cap': 80,
 'gamma': 1.0,
 'grad_ckpt': False,
 'lam': 0.99,
 'lr': 0.0003,
 'max_grad_norm': 0.5,
 'max_rows': 192,
 'minibatch': 4096,
 'objective': 'protocol',
 'step_cap': 1200,
 'tf32': False,
 'vf_coef': 0.5,
 'win_bonus': 1.0}

SCIENCE_FILES = {'code/sts2train/__init__.py': '51098b80b81edf0744cda2e2c21b478c3b04f9f7dda8413776c9999b845a12ce',
 'code/sts2train/cycles.py': '7a27ce9185765f38a8fa6bd46e20e7173a4684bd834c4cd451c71f27ff6c8305',
 'code/sts2train/episodes.py': '4c65cb9b35be698331d7e3d1d25cad8aa46971df2de3b42e42be6df859fee1fe',
 'code/sts2train/evaluate.py': '8df68583c711ff1b9eb70ec772b470a34a68375f9054c806a379bb075144e9ee',
 'code/sts2train/features.py': 'd667fa51a5e445163f2fbafba64fb03cc75f8c739c7ea0d190498f92f4d3ba3b',
 'code/sts2train/gae.py': 'a34b95be5c2dd01169a320718892f3cbcf987792cd441d8958f71d0d34dcca8e',
 'code/sts2train/heads.py': 'a46e3f5904cb201d5e18712dc764cb9e1292574bead7cbadef76386422f3a49f',
 'code/sts2train/isolation.py': '4a627ff887f15093344e40219930a5f742776c67dae18d599022d5e5468c4ca7',
 'code/sts2train/loopbreak.py': 'a91edce801a0c159052e2c4c9b9ed2cf62cc9a5fe5f5d76e6686d4e472f47f1a',
 'code/sts2train/model.py': '25c8a80ef32a2b2d035426123dcd582a1075fc08074397d4f9902d1b23fa04df',
 'code/sts2train/objective.py': '9098473be747f5d3d8c2fa5a28769b0f1bf5a0152ac43ec076841cfbc79802a5',
 'code/sts2train/ppo.py': 'b0eea343453c1ebe5f3e831b59688b11b3ba3a6c562f572053478a35d7e8571b',
 'code/sts2train/progress.py': 'c37943665d902e3d52b7e2f8180165aa0ba9edacebf0fdca355df31d95895578',
 'code/sts2train/runlog.py': '614545e8265d2ae129e821408e074025126328947e13cb733d74878eb1fb1716',
 'code/sts2train/seedstream.py': '6f4ac9bb936997c8dc8ae49720dae424ed8996ad0012f3746774b4ea4a23900b',
 'code/sts2train/startpool.py': '9554a7dddf2b58a09f3e5708d05b483d956371aa12aec4ab5e2b5090e0593df1',
 'code/sts2train/transition_aux.py': '8723438ce4f7edd402418e102e5c43b9a7cb8a99badd5858dbaf468f5dd419c8',
 'code/sts2train/transition_state.py': '407fffeafa5f0cebc4a33b54ebfdbe28693efd49c2469335bacdfbf3577664ae',
 'code/sts2train/transition_vector.py': 'faaa9fef0cb6b4da3a58922f19841c0f5868cdddde36c3f5220612601ce5f5bd',
 'code/sts2vec/__init__.py': '017cc4f1e1f744abfcceb4f664afa96e5b08dda2f39ee6acae928db839e187d2',
 'code/sts2vec/env.py': '17d950709ce1b83ef0156f05f095a64ff949fd1879da4732440e07287034082b',
 'code/sts2vec/errors.py': '56828317c3c663dd353a079e456e6e9c44a776a92d85c01bf0b59280e163f3b0',
 'code/sts2vec/evaluate.py': '3f145a7aa01d45362112ae10d581d77599ed9948d0f64b1977fa16f809b8d9f0',
 'code/sts2vec/loader.py': '2d369347079352bd7c7bdbd596d1d8e43672db9bee6d700fb5ce75571d657b88',
 'code/sts2vec/policies.py': 'f4915fd29bfd5093614c7730a9d6cfe3fbdd8b866ad2896055fcd1c37998535d',
 'code/sts2vec/pool.py': '6b0dd82a85b1016267fb1e3df9b17c4ce63b4f410ba0bf0e7fcf947d412413c9',
 'code/sts2vec/protocol.py': '105225d1411d04f1a77cceaab2d5c089489b2de944aff34131e76fc1cfeb3434',
 'code/sts2vec/schema.py': 'e4cafa2415767939d860e3b20f6b883818f1ee103299aef54b6daa512f00c7ff'}


def verify_ancestor_counts(root,job,body):
    """Accepted-reference full-manifest hash pins the prior cumulative coverage tree."""
    if body is None or job.payload['checkpoint'] is None:return
    from .contracts import identifier
    from .manifests import verify, file_digest
    ref=job.payload['checkpoint']
    parent=Path(root)/identifier(ref['job_key'])/identifier(ref['generation'])
    ready=parent/'ready.json'
    if ready.is_symlink() or not ready.is_file() or ready.stat().st_size>32000 or file_digest(ready)!=ref['manifest_hash']:
        raise ValueError('Task089 accepted ancestor ready digest differs')
    doc=json.loads(ready.read_bytes());identity=doc['identity']
    if (identity.get('experiment_id')!=job.payload['spec']['experiment_id']
            or identity.get('config_hash')!=job.payload['config_hash'] or identity.get('training_auxiliary')!=IDENTITY
            or identity.get('kind')!='train'):
        raise ValueError('Task089 cumulative ancestor identity differs')
    verify(parent,ref['manifest_hash'],identity)
    previous=json.loads((parent/doc['files'][ROLE]['name']).read_bytes())
    if body['updates']<=previous['updates']:raise ValueError('transition auxiliary updates must advance')
    def monotonic(current,old):
        if type(old) is dict:
            if type(current) is not dict or not old.keys()<=current.keys():raise ValueError('transition counters dropped ancestor keys')
            for key,value in old.items():monotonic(current[key],value)
        elif type(old) is list:
            if type(current) is not list or len(current)!=len(old):raise ValueError('transition counter shape changed')
            for a,b in zip(current,old):monotonic(a,b)
        elif type(current) is not int or current<old:raise ValueError('transition cumulative counter regressed')
    monotonic(body['cumulative_counts'],previous['cumulative_counts'])

BASE_ENV = {'dependency_hash': '20c3ed41b2f90970ea2523674d165b5810b20993abf54bd923834f9b7233908e',
 'engine_hash': '2d035a9be243fd40b9cef407d31fb46c12b6ca42ff8e39a6f95f15042fa984ce',
 'schema_hash': 'caddf802fff5f221bca659831c148ea7e5b268e014bcbe2b4e517d3e3bb98f96'}

import math
GPU_GATE_VERSION = 'task089-cuda4096-pre-adam0-v1'
GPU_GATE_KIND = 'actual_cuda_first4096_pre_adam0'
GPU_GATE_CONTEXT_KEYS = {'experiment_id', 'config_hash', 'code_commit', 'engine_hash', 'dependency_hash', 'schema_hash', 'first_job_key'}

def validate_gpu_context(context):
    if not isinstance(context, dict) or set(context) != GPU_GATE_CONTEXT_KEYS:
        raise ValueError('transition GPU gate requires exact public job/source context')
    for key, value in context.items():
        if not isinstance(value, str) or not value or len(value) > 192:
            raise ValueError('invalid transition GPU gate context')
    for key in ('config_hash', 'engine_hash', 'dependency_hash', 'schema_hash'):
        if len(context[key]) != 64 or any((c not in '0123456789abcdef' for c in context[key])):
            raise ValueError('invalid transition GPU gate context digest')
    if len(context['code_commit']) != 40 or any((c not in '0123456789abcdef' for c in context['code_commit'])):
        raise ValueError('invalid transition GPU gate source commit')

def validate_gpu_gate(report, expected_context=None, *, spec=None):
    """A structural check under exact worker/checkpoint hashes, not an independent hardware attestation."""
    if not isinstance(report, dict):
        raise ValueError('transition GPU gate receipt absent')
    expected = {'version', 'context', 'kind', 'passed', 'rollout_health', 'runtime', 'optimizer_steps_before', 'head_optimizer_steps_before', 'sample_indices_sha256', 'base_weights_before_sha256', 'head_weights_before_sha256', 'gate_source_sha256', 'p95_abs_delta_logp', 'p99_abs_delta_logp', 'min_signed_delta_logp', 'max_signed_delta_logp', 'positive_deltas', 'negative_deltas', 'zero_deltas', 'samples', 'finite', 'max_abs_delta_logp', 'mean_approx_kl', 'max_abs_delta_limit', 'mean_approx_kl_limit', 'learner_train_mode', 'learner_grad_enabled', 'bf16_autocast', 'same_candidate_mask', 'finite_transition_prediction', 'actor_dtype', 'transition_prediction_dtype', 'finite_loss', 'finite_gradients', 'base_grad_norm', 'head_grad_norm', 'combined_grad_norm', 'loss', 'loss_dtype', 'learner_forward_calls', 'base_gradient_tensors', 'head_gradient_tensors', 'transition_valid_per_channel'}
    if set(report) != expected:
        raise ValueError('transition GPU gate public fields differ')
    if report.get('version') != GPU_GATE_VERSION or report.get('kind') != GPU_GATE_KIND or report.get('passed') is not True or (report.get('samples') != 4096) or (report.get('optimizer_steps_before') != 0) or (report.get('head_optimizer_steps_before') != 0) or (report.get('finite') is not True) or (report.get('finite_gradients') is not True) or (report.get('finite_loss') is not True) or (report.get('finite_transition_prediction') is not True) or (report.get('learner_train_mode') is not True) or (report.get('learner_grad_enabled') is not True) or (report.get('same_candidate_mask') is not True) or (report.get('bf16_autocast') is not False) or (report.get('max_abs_delta_limit') != 0.001) or (report.get('mean_approx_kl_limit') != 1e-06):
        raise ValueError('transition GPU gate receipt is not a successful actual first learner gate')
    if any((type(report[k]) is not int for k in ('samples', 'optimizer_steps_before', 'head_optimizer_steps_before', 'learner_forward_calls'))):
        raise ValueError('transition GPU gate integer witness fields differ')
    for key, limit in [('max_abs_delta_logp', 0.001), ('mean_approx_kl', 1e-06)]:
        value = report.get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or (not 0 <= value <= limit):
            raise ValueError('transition GPU gate parity bound differs')
    for key in ('positive_deltas', 'negative_deltas', 'zero_deltas'):
        if type(report[key]) is not int or not 0 <= report[key] <= 4096:
            raise ValueError('transition GPU gate delta counts differ')
    if sum((report[k] for k in ('positive_deltas', 'negative_deltas', 'zero_deltas'))) != 4096:
        raise ValueError('transition GPU gate delta denominator differs')
    if type(report['base_gradient_tensors']) is not int or not 0 < report['base_gradient_tensors'] < 1000 or report['head_gradient_tensors'] != 4:
        raise ValueError('transition GPU gate gradient scope differs')
    counts = report['transition_valid_per_channel']
    if type(counts) is not list or len(counts) != 4 or any((type(n) is not int or not 0 <= n <= 4096 for n in counts)):
        raise ValueError('transition GPU gate channel coverage differs')
    for key in ('p95_abs_delta_logp', 'p99_abs_delta_logp', 'min_signed_delta_logp', 'max_signed_delta_logp'):
        if type(report[key]) not in (int, float) or not math.isfinite(report[key]):
            raise ValueError('transition GPU gate nonfinite delta distribution')
    runtime = report.get('runtime', {})
    if runtime.get('device_type') != 'cuda' or runtime.get('default_dtype') != 'torch.float32' or runtime.get('float32_matmul_precision') != 'highest' or (runtime.get('cuda_matmul_tf32') is not False) or (runtime.get('cudnn_tf32') is not False) or (runtime.get('ambient_cuda_autocast') is not False) or (not isinstance(runtime.get('device_name'), str)) or (not runtime['device_name']):
        raise ValueError('transition GPU gate runtime is not strict actual CUDA FP32')
    for key in ('sample_indices_sha256', 'base_weights_before_sha256', 'head_weights_before_sha256', 'gate_source_sha256'):
        value = report.get(key)
        if not isinstance(value, str) or len(value) != 64 or any((c not in '0123456789abcdef' for c in value)):
            raise ValueError('transition GPU gate provenance digest absent')
    if report['gate_source_sha256'] != science_files(spec or {})['code/sts2train/transition_gpu_gate.py']:
        raise ValueError('transition GPU gate source witness differs')
    validate_gpu_context(report.get('context'))
    if expected_context is not None:
        validate_gpu_context(expected_context)
        if any((report['context'][k] != v for k, v in expected_context.items() if k != 'first_job_key')):
            raise ValueError('transition GPU gate belongs to another config/source')
    for key in ('base_grad_norm', 'head_grad_norm', 'combined_grad_norm', 'loss'):
        value = report.get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or (key != 'loss' and value < 0):
            raise ValueError('transition GPU gate nonfinite health receipt')
    if any((report.get(k) != 'torch.float32' for k in ('actor_dtype', 'transition_prediction_dtype', 'loss_dtype'))):
        raise ValueError('transition GPU gate learner arithmetic dtype differs')
    if report.get('learner_forward_calls') != 1:
        raise ValueError('transition GPU gate did not use one learner forward')
    validate_gpu_rollout(report.get('rollout_health'))
    return report
GPU_GATE_HEALTH_FIELDS = {'rollout_decisions', 'learning_samples', 'sim_error', 'content_gap', 'illegal', 'cand_over_max_rows', 'prompt_over_max_rows', 'native_candidate_overflow', 'native_table_overflow'}

def validate_gpu_rollout(health):
    if not isinstance(health, dict) or set(health) != GPU_GATE_HEALTH_FIELDS or any((type(v) is not int for v in health.values())):
        raise ValueError('transition GPU gate lacks typed rollout health')
    if health['rollout_decisions'] != 2048 * 64 or health['learning_samples'] != 2048 * 64 or any((v != 0 for k, v in health.items() if k not in ('rollout_decisions', 'learning_samples'))):
        raise ValueError('transition GPU gate rollout is incomplete or unhealthy')


def gate_context(spec,job):
    return dict(experiment_id=spec['experiment_id'],config_hash=job.payload['config_hash'],
        code_commit=spec['code_commit'],engine_hash=spec['engine_hash'],dependency_hash=spec['dependency_hash'],
        schema_hash=spec['schema_hash'],first_job_key=job.key)


def validate_checkpoint_gate(data,spec,job):
    if not positive(spec):return
    report=validate_gpu_gate(data.get('first_transition_gate'),gate_context(spec,job),spec=spec)
    if job.payload['checkpoint'] is None and report['context']['first_job_key']!=job.key:
        raise ValueError('fresh transition GPU gate belongs to another initial job')


def mirror_gate(data,spec,metrics,job):
    if not positive(spec):return
    validate_checkpoint_gate(data,spec,job)
    metrics['first_transition_gate']=data['first_transition_gate']
    if len(canonical(metrics).encode())>64*1024:raise ValueError('transition GPU gate metrics exceed bound')


def verify_gate_ancestor(root,job,report):
    if not positive(job.payload['spec']):return
    validate_gpu_gate(report,gate_context(job.payload['spec'],job),spec=job.payload['spec'])
    ref=job.payload['checkpoint']
    if ref is None:
        if report['context']['first_job_key']!=job.key:raise ValueError('fresh GPU gate initial job differs')
        return
    from .contracts import identifier
    from .manifests import file_digest,verify
    parent=Path(root)/identifier(ref['job_key'])/identifier(ref['generation'])
    ready=parent/'ready.json'
    if ready.is_symlink() or not ready.is_file() or ready.stat().st_size>32000 or file_digest(ready)!=ref['manifest_hash']:
        raise ValueError('GPU gate accepted ancestor ready digest differs')
    doc=json.loads(ready.read_bytes());identity=doc['identity']
    if (identity.get('experiment_id')!=job.payload['spec']['experiment_id']
            or identity.get('config_hash')!=job.payload['config_hash'] or identity.get('training_auxiliary')!=IDENTITY
            or identity.get('kind')!='train'):
        raise ValueError('GPU gate accepted ancestor identity differs')
    verify(parent,ref['manifest_hash'],identity)
    path=parent/doc['files']['metrics']['name']
    if path.is_symlink() or not path.is_file() or not 0<path.stat().st_size<=64*1024:
        raise ValueError('GPU gate ancestor metrics absent or oversized')
    previous=json.loads(path.read_bytes()).get('first_transition_gate')
    validate_gpu_gate(previous,gate_context(job.payload['spec'],job),spec=job.payload['spec'])
    if canonical(previous)!=canonical(report):raise ValueError('GPU gate witness changed across accepted ancestry')

SCIENCE_FILES['code/sts2train/transition_gpu_gate.py']='663a19669c7f34583a279cb44e1029055db437ed020225eb83e6cc4420589b8b'
SCIENCE_FILES['code/sts2train/ppo.py']='eff347e9290a640f125bc74a389c29d6b442bf9cb31ffa06ad132c2f0b95f40c'


def gate_failure_body(job,raw=None):
    expected=gate_context(job.payload['spec'],job)
    if raw is not None:
        if type(raw) is not dict:raise ValueError('invalid gate failure record')
        if raw.get('passed') is True:return None
        if raw.get('version')!=GPU_GATE_VERSION or raw.get('context')!=expected:
            raise ValueError('gate failure source or original job differs')
    code='GATE_EVIDENCE_UNAVAILABLE' if raw is None else raw.get('error_code')
    if code not in ('GATE_REJECTED','NONFINITE','GATE_EVIDENCE_UNAVAILABLE'):
        raise ValueError('unknown fixed gate failure reason')
    body={'version':GPU_GATE_VERSION,'kind':'transition_gpu_gate_failure','passed':False,
          'context':expected,'error_code':code}
    for key in ('optimizer_steps_before','head_optimizer_steps_before','max_abs_delta_logp',
                'mean_approx_kl','base_grad_norm','head_grad_norm','combined_grad_norm','loss'):
        value=None if raw is None else raw.get(key)
        if value is not None and (type(value) not in (int,float) or not math.isfinite(value)):
            value=None
        body[key]=value
    return body


def validate_gate_failure(body,job):
    expected=gate_failure_body(job,None)
    if type(body) is not dict or set(body)!=set(expected):raise ValueError('gate failure schema differs')
    if any(body[k]!=expected[k] for k in ('version','kind','passed','context')):
        raise ValueError('gate failure original job/source/config differs')
    if body['error_code'] not in ('GATE_REJECTED','NONFINITE','GATE_EVIDENCE_UNAVAILABLE'):
        raise ValueError('gate failure fixed reason differs')
    for key in expected.keys()-{'version','kind','passed','context','error_code'}:
        value=body[key]
        if value is not None and (type(value) not in (int,float) or not math.isfinite(value)):
            raise ValueError('gate failure contains nonnumeric diagnostic')
    return body
