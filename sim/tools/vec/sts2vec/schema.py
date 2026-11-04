"""Parse the sts2vec schema JSON (CONTRACTS.md §D3.2) and allocate the per-env buffers it describes.

Nothing here hard-codes offsets: buffer order, dtypes, shapes, field lists, vocabularies and enum lists all come from the
JSON. The only constants are the status bit flags (§D3.1 ``status``, frozen in ``Status.cs``) and the names of the
signal buffers that ``VecEnv.step`` returns outside ``obs``.
"""

from __future__ import annotations

import ctypes
import enum
import json
from dataclasses import dataclass, field

import numpy as np

DTYPES = {"int32": np.int32, "float32": np.float32, "int16": np.int16, "int64": np.int64, "uint8": np.uint8}


class Status(enum.IntFlag):
    """Per-slot status bits (§D3.1 ``status``; ``Sts2Sim.Vec.SlotStatus``)."""

    OK = 0
    NOOP = 1
    ILLEGAL_ACTION = 2
    CONTENT_GAP = 4
    SIM_ERROR = 8
    CANDIDATE_OVERFLOW = 16
    TABLE_OVERFLOW = 32
    RESET = 64
    POOL_EMPTY = 128
    FLOOR_STALL = 256  # appended: truncated by floor_step_cap (non-combat decisions on one floor)


# name -> bit (kept as a plain dict for numpy-friendly use: ``status & STATUS["RESET"]``)
STATUS: dict[str, int] = {s.name: int(s.value) for s in Status}

# buffers returned separately from obs by VecEnv.step (signals + episode bookkeeping)
SIGNAL_BUFFERS = ("reward", "terminated", "truncated", "status", "episode", "last_episode")


def status_names(bits: int) -> list[str]:
    """``status_names(65) == ['NOOP', 'RESET']``; ``['OK']`` for 0."""
    bits = int(bits)
    return [n for n, b in STATUS.items() if b and bits & b] or ["OK"]


def status_counts(status: np.ndarray) -> dict[str, int]:
    """Number of slots with each status bit set (``OK`` = slots with no bit set)."""
    s = np.asarray(status).astype(np.int64, copy=False).ravel()
    out = {"OK": int(np.count_nonzero(s == 0))}
    for n, b in STATUS.items():
        if b:
            out[n] = int(np.count_nonzero(s & b))
    return out


@dataclass(frozen=True)
class BufferSpec:
    index: int
    name: str
    dtype: np.dtype
    shape: tuple[int, ...]
    fields: tuple[str, ...] | None

    @property
    def elements(self) -> int:
        return int(np.prod(self.shape, dtype=np.int64)) if self.shape else 1

    @property
    def bytes_per_env(self) -> int:
        return self.elements * self.dtype.itemsize

    def col(self, name: str) -> int:
        """Index of a field along the last axis."""
        if self.fields is None:
            raise KeyError(f"buffer {self.name} has no fields")
        try:
            return self.fields.index(name)
        except ValueError:
            raise KeyError(f"buffer {self.name} has no field {name!r} (fields: {', '.join(self.fields)})") from None

    def cols(self) -> "Columns":
        if self.fields is None:
            raise KeyError(f"buffer {self.name} has no fields")
        return Columns(self.name, self.fields)


class Columns(dict):
    """Field name -> column index for one buffer; also readable as attributes: ``cols.card_id``."""

    def __init__(self, buffer: str, fields):
        super().__init__((f, i) for i, f in enumerate(fields))
        self.buffer = buffer

    def __getattr__(self, name: str) -> int:
        try:
            return self[name]
        except KeyError:
            raise AttributeError(f"buffer {self.buffer} has no field {name!r}") from None


class VocabList(list):
    """A vocabulary (index 0 = "" none/padding, 1 = "?" unknown, real ids from 2) or an enum list (index = value).

    ``index_of(name)`` is the reverse map (unknown names -> 1 for vocabularies, KeyError for enums);
    ``name(i)`` the forward map, tolerant of out-of-range ids (returns ``"<i>"``).
    """

    def __init__(self, kind: str, names, *, is_enum: bool = False):
        super().__init__(names)
        self.kind = kind
        self.is_enum = is_enum
        self.lookup: dict[str, int] = {n: i for i, n in enumerate(self)}

    def index_of(self, name: str | None) -> int:
        if self.is_enum:
            try:
                return self.lookup[name]
            except KeyError:
                raise KeyError(f"{name!r} is not a {self.kind} value") from None
        if not name:
            return 0
        return self.lookup.get(name, 1)

    def name(self, i) -> str:
        i = int(i)
        return self[i] if 0 <= i < len(self) else f"<{i}>"


@dataclass
class Vocab:
    """``vocab.cards[i]`` / ``vocab.cards.index_of("STRIKE_IRONCLAD")``; ``vocab.enums["RunActionKind"]``."""

    lists: dict[str, VocabList]
    enums: dict[str, VocabList]

    def __getattr__(self, kind: str) -> VocabList:
        try:
            return self.__dict__["lists"][kind]
        except KeyError:
            raise AttributeError(kind) from None

    def enum(self, name: str) -> VocabList:
        return self.enums[name]

    @property
    def kinds(self) -> list[str]:
        return list(self.lists)


@dataclass
class Schema:
    raw: dict
    text: str
    encoder_version: str
    buffers: list[BufferSpec]
    vocab: Vocab
    caps: dict[str, int]
    card_vars: VocabList
    playable_reasons: list[str]
    playable_reason_bits: dict[str, int] = field(default_factory=dict)
    by_name: dict[str, BufferSpec] = field(default_factory=dict)
    # static card features (schema append 2026-11-04): row = cards vocab id, columns = card_static_fields; empty
    # ((0, 0), ()) for a library built before the append
    card_static: np.ndarray = field(default_factory=lambda: np.zeros((0, 0), dtype=np.int32))
    card_static_fields: tuple[str, ...] = ()

    @staticmethod
    def from_json(text: str) -> "Schema":
        raw = json.loads(text)
        buffers = []
        for i, b in enumerate(raw["buffers"]):
            if b["dtype"] not in DTYPES:
                raise ValueError(f"buffer {b['name']}: unknown dtype {b['dtype']!r}")
            buffers.append(BufferSpec(i, b["name"], np.dtype(DTYPES[b["dtype"]]), tuple(int(x) for x in b["shape"]),
                                      tuple(b["fields"]) if b.get("fields") is not None else None))
        v = raw["vocab"]
        lists = {k: VocabList(k, names) for k, names in v.items() if k != "enums"}
        enums = {k: VocabList(k, names, is_enum=True) for k, names in v.get("enums", {}).items()}
        s = Schema(raw=raw, text=text, encoder_version=raw["encoder_version"], buffers=buffers,
                   vocab=Vocab(lists, enums), caps={k: int(x) for k, x in raw["caps"].items()},
                   card_vars=VocabList("card_vars", raw.get("card_vars", [])),
                   playable_reasons=list(raw.get("playable_reasons", [])),
                   playable_reason_bits={k: int(x) for k, x in raw.get("playable_reason_bits", {}).items()})
        s.by_name = {b.name: b for b in buffers}
        if len(s.by_name) != len(buffers):
            raise ValueError("schema has duplicate buffer names")
        cs = raw.get("card_static")
        if cs is not None:
            s.card_static_fields = tuple(cs["fields"])
            s.card_static = np.asarray(cs["rows"], dtype=np.int32).reshape(len(cs["rows"]), len(s.card_static_fields))
            if s.card_static.shape[0] != len(s.vocab.cards):
                raise ValueError(f"card_static has {s.card_static.shape[0]} rows, the cards vocabulary {len(s.vocab.cards)}")
        return s

    def __getitem__(self, name: str) -> BufferSpec:
        try:
            return self.by_name[name]
        except KeyError:
            raise KeyError(f"no buffer {name!r} in the schema") from None

    def __contains__(self, name: str) -> bool:
        return name in self.by_name

    @property
    def names(self) -> list[str]:
        return [b.name for b in self.buffers]

    @property
    def observation_names(self) -> list[str]:
        """Buffers returned in ``obs`` (everything but the signal buffers), in schema order."""
        return [b.name for b in self.buffers if b.name not in SIGNAL_BUFFERS]

    @property
    def bytes_per_env(self) -> int:
        return sum(b.bytes_per_env for b in self.buffers)

    @property
    def observation_bytes_per_env(self) -> int:
        return sum(self[n].bytes_per_env for n in self.observation_names)

    def fields(self, name: str) -> tuple[str, ...] | None:
        return self[name].fields

    def col(self, buffer: str, field_name: str) -> int:
        """``schema.col("hand", "card_id") == 0``."""
        return self[buffer].col(field_name)

    def cols(self, buffer: str) -> Columns:
        """``H = schema.cols("hand"); obs["hand"][..., H.card_id]``."""
        return self[buffer].cols()

    def card_static_cols(self) -> Columns:
        """``K = schema.card_static_cols(); schema.card_static[obs["deck"][..., D.card_id], K.damage]`` (static card table)."""
        if not self.card_static_fields:
            raise KeyError("this library publishes no card_static table (built before 2026-11-04)")
        return Columns("card_static", self.card_static_fields)

    @property
    def supports_floor_step_cap(self) -> bool:
        """The library implements ``floor_step_cap`` (its ``EndKind`` enum lists ``FloorStall``; builds before it do not)."""
        return "FloorStall" in self.vocab.enums.get("EndKind", ())

    @property
    def v_cards(self) -> int:
        return len(self.vocab.cards)

    @staticmethod
    def count_index(card_id: int, upgrade: int) -> int:
        """Index into draw/discard/exhaust_counts for (card_id, upgrade): ``card_id * 2 + min(upgrade, 1)``."""
        return int(card_id) * 2 + min(int(upgrade), 1)

    def playable_reason_names(self, bits: int) -> list[str]:
        bits = int(bits)
        return [n for n, b in self.playable_reason_bits.items() if b and bits & b] or ["None"]

    def verify_version(self) -> bool:
        """encoder_version == XXH64 of the JSON text without the leading encoder_version member."""
        member = '"encoder_version":"' + self.encoder_version + '",'
        if not self.text.startswith("{" + member):
            return False
        body = "{" + self.text[1 + len(member):]
        return f"{xxh64(body.encode('utf-8')):016x}" == self.encoder_version

    def allocate(self, num_envs: int) -> "Buffers":
        return Buffers(self, num_envs)


class Buffers:
    """Env-owned numpy arrays ``(N, *shape)`` in schema order, and the ``void*`` table passed to the exports.

    The arrays are allocated once and written in place by the native side; ``table[k]`` is the data pointer of
    buffer ``k``. Keep this object alive as long as the table may be used.
    """

    def __init__(self, schema: Schema, num_envs: int):
        if num_envs < 1:
            raise ValueError(f"num_envs must be >= 1 (got {num_envs})")
        self.schema = schema
        self.num_envs = int(num_envs)
        self.arrays: dict[str, np.ndarray] = {}
        self.table = (ctypes.c_void_p * len(schema.buffers))()
        for b in schema.buffers:
            a = np.zeros((self.num_envs, *b.shape), dtype=b.dtype)
            assert a.flags.c_contiguous and a.flags.writeable
            self.arrays[b.name] = a
            self.table[b.index] = a.ctypes.data

    @property
    def ptr(self) -> int:
        """Address of the ``void*[len(buffers)]`` table (the ``buffers`` argument of reset/step/restore)."""
        return ctypes.addressof(self.table)

    def pointers(self) -> list[int]:
        return [int(self.table[i] or 0) for i in range(len(self.schema.buffers))]

    def __getitem__(self, name: str) -> np.ndarray:
        return self.arrays[name]

    def __iter__(self):
        return iter(self.arrays)

    def items(self):
        return self.arrays.items()

    @property
    def nbytes(self) -> int:
        return sum(a.nbytes for a in self.arrays.values())

    @property
    def observation_nbytes(self) -> int:
        return sum(self.arrays[n].nbytes for n in self.schema.observation_names)

    def slot(self, i: int) -> dict[str, np.ndarray]:
        """Views of slot ``i``'s rows of every buffer."""
        return {k: v[i] for k, v in self.arrays.items()}

    def check(self) -> list[str]:
        """Consistency problems between the arrays, the pointer table and the schema (empty when consistent)."""
        problems = []
        for b in self.schema.buffers:
            a = self.arrays.get(b.name)
            if a is None:
                problems.append(f"{b.name}: missing")
                continue
            if a.shape != (self.num_envs, *b.shape):
                problems.append(f"{b.name}: shape {a.shape} != {(self.num_envs, *b.shape)}")
            if a.dtype != b.dtype:
                problems.append(f"{b.name}: dtype {a.dtype} != {b.dtype}")
            if a.nbytes != self.num_envs * b.bytes_per_env:
                problems.append(f"{b.name}: {a.nbytes} bytes != {self.num_envs} x {b.bytes_per_env}")
            if not a.flags.c_contiguous:
                problems.append(f"{b.name}: not C-contiguous")
            if self.table[b.index] != a.ctypes.data:
                problems.append(f"{b.name}: pointer table[{b.index}] does not point at the array")
        return problems


# ---- XXH64 (seed 0), pure Python; used to verify encoder_version ----

_M = (1 << 64) - 1
_P1, _P2, _P3, _P4, _P5 = (0x9E3779B185EBCA87, 0xC2B2AE3D27D4EB4F, 0x165667B19E3779F9,
                           0x85EBCA77C2B2AE63, 0x27D4EB2F165667C5)


def _rotl(x: int, r: int) -> int:
    return ((x << r) | (x >> (64 - r))) & _M


def _round(acc: int, lane: int) -> int:
    acc = (acc + lane * _P2) & _M
    return (_rotl(acc, 31) * _P1) & _M


def _merge(acc: int, val: int) -> int:
    acc ^= _round(0, val)
    return (acc * _P1 + _P4) & _M


def xxh64(data: bytes, seed: int = 0) -> int:
    n = len(data)
    p = 0
    if n >= 32:
        v1, v2, v3, v4 = (seed + _P1 + _P2) & _M, (seed + _P2) & _M, seed & _M, (seed - _P1) & _M
        while p <= n - 32:
            v1 = _round(v1, int.from_bytes(data[p:p + 8], "little"))
            v2 = _round(v2, int.from_bytes(data[p + 8:p + 16], "little"))
            v3 = _round(v3, int.from_bytes(data[p + 16:p + 24], "little"))
            v4 = _round(v4, int.from_bytes(data[p + 24:p + 32], "little"))
            p += 32
        h = (_rotl(v1, 1) + _rotl(v2, 7) + _rotl(v3, 12) + _rotl(v4, 18)) & _M
        for v in (v1, v2, v3, v4):
            h = _merge(h, v)
    else:
        h = (seed + _P5) & _M
    h = (h + n) & _M
    while p + 8 <= n:
        h ^= _round(0, int.from_bytes(data[p:p + 8], "little"))
        h = (_rotl(h, 27) * _P1 + _P4) & _M
        p += 8
    if p + 4 <= n:
        h ^= (int.from_bytes(data[p:p + 4], "little") * _P1) & _M
        h = (_rotl(h, 23) * _P2 + _P3) & _M
        p += 4
    while p < n:
        h ^= (data[p] * _P5) & _M
        h = (_rotl(h, 11) * _P1) & _M
        p += 1
    h ^= h >> 33
    h = (h * _P2) & _M
    h ^= h >> 29
    h = (h * _P3) & _M
    h ^= h >> 32
    return h


def slot_seed(seed_text: str, slot: int) -> int:
    """``XXH64(UTF-8(seed_text) || int32 LE slot)`` (VecEnv.SlotSeedOf) — the slot's start-sequence seed."""
    return xxh64(seed_text.encode("utf-8") + int(slot).to_bytes(4, "little", signed=True))
