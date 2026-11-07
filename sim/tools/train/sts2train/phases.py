"""Phase split of the rollout inference time (wave 21, throughput measurement) and the stacked action readback.

``t_infer_s`` (metrics.jsonl) is one opaque timer over the rollout's model work: per step the observation upload, the
policy forward with masking and sampling, the store writes, the action / log-prob / value readback and the Python
bookkeeping between them, plus the bootstrap forward after the window. ``InferPhases`` splits it into five t_* keys
(per update, seconds) that add up to ``t_infer_s`` (to the few perf_counter calls outside the phase marks):

    t_h2d_s         observations host -> device (``features.obs_to_device``: pinned staging copies + async uploads)
    t_forward_s     policy forward incl. logit adjustment (loop breaker), masking, the categorical draw and the
                    exploration mixture; the first update's compiled-vs-eager check and the aux heads' bootstrap pass
    t_store_s       rollout store writes (``RolloutStore.put`` / ``put_rows`` device copies, the host per-step arrays)
    t_sample_d2h_s  actions / log-probs / values device -> host (``read_back``)
    t_track_s       Python bookkeeping inside the timer (cand_n copy, aux targets, loop breaker / cycle commits,
                    run stats, the exploration bookkeeping arrays)

On the cpu every phase is its host wall time (``time.perf_counter``). On CUDA the host only enqueues work, and the
wait for it happens in the one blocking readback per step, so host wall times alone would charge the whole forward to
``t_sample_d2h_s``. The split therefore records a CUDA event at each phase boundary of a step's device chain (h2d ->
forward -> store -> readback) and one right after the readback returned; the stream is idle there (the readback
synchronised it), so that last event's GPU timestamp is the host time it was recorded at, and each earlier boundary's
GPU completion time is ``host_end - elapsed(event_k, last_event)``. A phase is then charged from the later of (its own
host end, its device work's completion) back to the previous phase's, which keeps the sum equal to the host wall time
and moves the readback's wait onto the phase whose device work it waited for. No sync is added inside the step loop:
the events are read once per update (``flush``) after the bootstrap readback, which synchronises already. mps / other
devices: host wall times only (their waits stay in ``t_sample_d2h_s``).

``read_back(a, logp, value)`` is the per-step readback: on CUDA one device->host copy of the three tensors' bytes
(concatenated as uint8 on the device, split and re-viewed on the host), so the same bytes as three ``.cpu()`` calls
(bit-identical, NaN payloads and signed zeros included) with one blocking transfer instead of three. On the cpu (and
other devices) it is the separate ``.cpu().numpy()`` calls.
"""

from __future__ import annotations

import time

import numpy as np
import torch

PHASES = ("h2d", "forward", "store", "sample_d2h", "track")
METRIC_KEYS = {p: f"t_{p}_s" for p in PHASES}
DEVICE_PHASES = frozenset(("h2d", "forward", "store", "sample_d2h"))

_clk = time.perf_counter


class InferPhases:
    """Per-update accumulator of the rollout inference phases (``PHASES``).

    Protocol per timed block (a rollout step, the bootstrap): ``begin(phase)``, then ``to(phase)`` at every phase
    change, ``to(phase, synced=True)`` right after a blocking readback (closes the device chain), ``end()``. After
    the update's last blocking readback: ``flush()`` (reads the CUDA events), then ``metrics()``. ``reset()`` starts
    the next update.

    Device chain (CUDA): a segment of a device phase (``DEVICE_PHASES``) opened while no chain is open starts one,
    unless opened with ``host=True`` (host-only work charged to that phase, e.g. the store's numpy arrays); every
    segment of an open chain (whatever its phase) gets a boundary event, until the ``synced`` close."""

    def __init__(self, device: torch.device | str = "cpu", *, events: bool | None = None):
        self.device = torch.device(device)
        self.events = (self.device.type == "cuda") if events is None else bool(events)
        self._pool: list = []           # reusable timing events (CUDA)
        self.reset()

    def reset(self) -> None:
        self.acc = dict.fromkeys(PHASES, 0.0)
        self._pending: list = []        # closed device chains: (host times, phases, events)
        self._used: list = []           # events handed out since the last flush (returned to the pool there)
        self._phase = None
        self._t = 0.0
        self._chain = None              # open chain: [host times], [phases], [events]

    # --------------------------------------------------------------------------------------------- step protocol

    def begin(self, phase: str = "track", *, host: bool = False) -> None:
        self._open(phase, _clk(), host)

    def to(self, phase: str, *, synced: bool = False, host: bool = False) -> None:
        """Close the running phase's segment and open `phase`. `synced`: the closed segment ended in a blocking
        device->host read, so the device chain (if any) is complete up to here. `host`: the new segment does no
        device work (it does not open a chain)."""
        now = _clk()
        self._close(now, synced)
        self._open(phase, now, host)

    def _open(self, phase: str, now: float, host: bool) -> None:
        self._phase, self._t = phase, now
        if self.events and self._chain is None and phase in DEVICE_PHASES and not host:
            self._chain = ([now], [], [])

    def end(self, *, synced: bool = False) -> None:
        self._close(_clk(), synced)
        self._phase = None

    def _event(self):
        ev = self._pool.pop() if self._pool else torch.cuda.Event(enable_timing=True)
        ev.record()
        self._used.append(ev)
        return ev

    def _close(self, now: float, synced: bool) -> None:
        ph = self._phase
        if ph is None:
            return
        if self._chain is None:                         # host-only segment (cpu, or outside a device chain)
            self.acc[ph] += now - self._t
            return
        hs, phs, evs = self._chain
        hs.append(now)
        phs.append(ph)
        evs.append(self._event())                       # GPU completion of everything enqueued so far
        if synced:
            self._pending.append(self._chain)
            self._chain = None

    # --------------------------------------------------------------------------------------------- results

    def flush(self) -> None:
        """Charge the closed device chains (call after a blocking readback; the chains' last events were recorded on
        an idle stream). An unclosed chain is charged at host wall times."""
        if self._chain is not None:                     # no synced close (should not happen): host times
            hs, phs, _ = self._chain
            for k, ph in enumerate(phs):
                self.acc[ph] += hs[k + 1] - hs[k]
            self._chain = None
        if self._pending:
            self._pending[-1][2][-1].synchronize()      # recorded right after a sync: completes within microseconds
        for hs, phs, evs in self._pending:
            for ph, dt in zip(phs, charge(hs, [evs[k].elapsed_time(evs[-1]) / 1000.0 for k in range(len(evs))])):
                self.acc[ph] += dt
        self._pending = []
        self._pool.extend(self._used)
        self._used = []

    def metrics(self, ndigits: int = 3) -> dict:
        return {METRIC_KEYS[p]: round(self.acc[p], ndigits) for p in PHASES}

    def total(self) -> float:
        return sum(self.acc.values())


def charge(hs: list[float], to_end: list[float]) -> list[float]:
    """Phase charges of one device chain. `hs` = host times [start, end of segment 1, ..., end of segment n] (the last
    one right after the blocking readback), `to_end[k]` = device time from boundary k+1's event to the last event
    (seconds; to_end[-1] = 0). Boundary k's device work completed at g_k = hs[-1] - to_end[k-1]; segment k is charged
    E_k - E_{k-1} with E_k = clamp(max(hs[k], g_k, E_{k-1}), <= hs[-1]), E_0 = hs[0], E_n = hs[-1]: the sum is the
    host wall time hs[-1] - hs[0], and a wait inside the readback goes to the segment whose device work it waited
    for."""
    n = len(hs) - 1
    end = hs[-1]
    out, prev = [], hs[0]
    for k in range(1, n + 1):
        e = end if k == n else min(end, max(hs[k], end - to_end[k - 1], prev))
        out.append(e - prev)
        prev = e
    return out


# ------------------------------------------------------------------------------------------------ readback

_NP_DTYPES: dict = {}


def _np_dtype(dt: torch.dtype):
    if dt not in _NP_DTYPES:
        try:
            _NP_DTYPES[dt] = torch.empty(0, dtype=dt).numpy().dtype
        except TypeError:                               # bfloat16 & co.: no numpy dtype
            _NP_DTYPES[dt] = None
    return _NP_DTYPES[dt]


def read_back(*ts: torch.Tensor, stacked: bool | None = None) -> tuple[np.ndarray, ...]:
    """Host numpy copies of `ts` (the step's actions, log-probs, values), bit-identical to ``t.cpu().numpy()`` each.

    `stacked` (default: on CUDA): one device->host copy of all the tensors' bytes (uint8 views concatenated on the
    device), split and re-viewed as each tensor's dtype / shape on the host. Off (cpu / mps, or a dtype without a numpy
    equivalent): the separate copies (on the cpu ``.numpy()`` aliases the tensor, as ``.cpu().numpy()`` did)."""
    if stacked is None:
        stacked = bool(ts) and ts[0].device.type == "cuda"
    dts = [_np_dtype(t.dtype) for t in ts]
    if not stacked or any(d is None for d in dts) or len({t.device for t in ts}) != 1:
        return tuple(t.cpu().numpy() for t in ts)
    flat = [t.detach().contiguous().reshape(-1).view(torch.uint8) for t in ts]
    host = torch.cat(flat).cpu().numpy()
    out, o = [], 0
    for t, f, d in zip(ts, flat, dts):
        n = f.numel()
        out.append(host[o:o + n].view(d).reshape(tuple(t.shape)))
        o += n
    return tuple(out)
