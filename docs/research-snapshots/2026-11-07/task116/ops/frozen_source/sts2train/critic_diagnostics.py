"""Bounded read-only old-critic diagnostics. No model, environment, RNG or learning inputs."""
import json
import numpy as np

VERSION = 'task099-prelearner-oldcritic-aggregate-v1'
MAX_BYTES = 32 * 1024
MOMENTS = ('sumV', 'sumR', 'sumA', 'sumA2', 'sumV2', 'sumR2', 'sumVR')
DERIVED = ('oldV_mean', 'Rlambda_mean', 'residual_mean', 'residual_mse', 'raw_adv_mean', 'raw_adv_std')


def _stats(v, r, a, selected):
    v, r, a = v[selected], r[selected], a[selected]
    n = int(selected.sum())
    finite = np.isfinite(v) & np.isfinite(r) & np.isfinite(a)
    row = dict(n=n, positive_adv_n=int(np.count_nonzero(a > 0)), nonfinite_n=int(n - finite.sum()))
    # Keep denominator=n valid samples; do not silently replace it with a finite-only subset.
    if n == 0 or not finite.all():
        return {**row, **dict.fromkeys(MOMENTS + DERIVED)}
    residual = r - v
    return {**row, 'sumV': float(v.sum()), 'sumR': float(r.sum()), 'sumA': float(a.sum()),
            'sumA2': float(np.dot(a, a)), 'sumV2': float(np.dot(v, v)),
            'sumR2': float(np.dot(r, r)), 'sumVR': float(np.dot(v, r)),
            'oldV_mean': float(v.mean()), 'Rlambda_mean': float(r.mean()),
            'residual_mean': float(residual.mean()), 'residual_mse': float(np.mean(residual * residual)),
            'raw_adv_mean': float(a.mean()), 'raw_adv_std': float(a.std(ddof=0))}


def summarize(old_values, returns, raw_advantages, valid, phase, act, dones, final_values,
              last_values, phase_names):
    """Pure numpy reduction over a fixed rollout before any learner step; <=32KiB JSON.

    Rlambda includes existing bootstraps. Residual is float64(Rlambda-oldV), whereas
    rawA is the actual pre-normalization adv array (not reconstructed by subtraction).
    Merged done is not evidence of an owned true terminal. No MC/terminal-distance label.
    """
    shape = np.shape(old_values)
    if len(shape) != 2 or shape[0] < 1 or shape[1] < 1:
        raise ValueError('diagnostic rollout shape')
    if any(np.shape(x) != shape for x in (returns, raw_advantages, valid, phase, act, dones)):
        raise ValueError('diagnostic input shape mismatch')
    if final_values is not None and np.shape(final_values) != shape:
        raise ValueError('diagnostic cutoff shape mismatch')
    if np.shape(last_values) != (shape[1],):
        raise ValueError('diagnostic last-value shape mismatch')
    if len(phase_names) > 32 or shape[0] > 64:
        return {'version': VERSION, 'status': 'unavailable_schema_outside_fixed_64window_contract'}
    mask = np.asarray(valid, dtype=bool).reshape(-1)
    v, r, a = (np.asarray(x, dtype=np.float64).reshape(-1) for x in (old_values, returns, raw_advantages))
    ph, ac = np.asarray(phase).reshape(-1), np.asarray(act).reshape(-1)
    positions = np.broadcast_to(np.arange(shape[0])[:, None], shape).reshape(-1)
    done = np.asarray(dones, dtype=bool)
    final_finite = np.zeros(shape, dtype=bool) if final_values is None else np.isfinite(final_values)
    valid_2d = mask.reshape(shape)
    tail = valid_2d[-1] & ~done[-1]
    last_finite = np.isfinite(last_values)
    result = {'version': VERSION, 'status': 'ok', 'timing': 'fixed_rollout_pre_learner',
              'target': 'bootstrapped_Rlambda_not_ground_truth',
              'raw_advantage_source': 'actual_adv_before_global_standardization',
              'sample_slots_n': int(mask.size), 'valid_n': int(mask.sum()),
              'global': _stats(v, r, a, mask),
              'phase': {str(i) + ':' + str(name): _stats(v, r, a, mask & (ph == i))
                        for i, name in enumerate(phase_names)},
              'act': {str(i): _stats(v, r, a, mask & (ac == i)) for i in (1, 2, 3)},
              'window_position': {str(lo) + '_' + str(hi): _stats(v, r, a, mask & (positions >= lo) & (positions <= hi))
                                  for lo, hi in ((0, 15), (16, 31), (32, 47), (48, 63))},
              'boundary': {'terminal_known': None,
                           'terminal_unavailable_reason': 'merged_done_has_no_owned_true_terminal_proof',
                           'valid_done_n': int(np.count_nonzero(valid_2d & done)),
                           'valid_done_finite_final_bootstrap_n': int(np.count_nonzero(valid_2d & done & final_finite)),
                           'valid_done_no_finite_final_n': int(np.count_nonzero(valid_2d & done & ~final_finite)),
                           'last_step_continuing_n': int(tail.sum()),
                           'last_step_finite_window_bootstrap_n': int(np.count_nonzero(tail & last_finite)),
                           'last_step_nonfinite_window_bootstrap_n': int(np.count_nonzero(tail & ~last_finite))}}
    result['phase']['unknown'] = _stats(v, r, a, mask & ~np.isin(ph, np.arange(len(phase_names))))
    result['act']['unknown'] = _stats(v, r, a, mask & ~np.isin(ac, (1, 2, 3)))
    if result['global']['nonfinite_n']:
        result['status'] = 'nonfinite_present_original_learner_gate_unchanged'
    if len(json.dumps({'critic_diagnostics': result}, allow_nan=False).encode()) > MAX_BYTES:
        return {'version': VERSION, 'status': 'unavailable_size_limit', 'valid_n': int(mask.sum()), 'max_bytes': MAX_BYTES}
    return result


def safe_summarize(*args, **kwargs):
    """A logging problem cannot replace the existing learner/gate failure policy."""
    try:
        return summarize(*args, **kwargs)
    except Exception:
        return {'version': VERSION, 'status': 'unavailable_diagnostic_error'}
