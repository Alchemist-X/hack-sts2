"""Parse the sts2vec schema JSON (CONTRACTS.md §D3.2) and allocate the per-env buffers it describes.

Nothing here hard-codes offsets: buffer order, dtypes, shapes and field lists all come from the JSON.
"""

from __future__ import annotations

import ctypes
import json
from dataclasses import dataclass, field

import numpy as np

DTYPES = {"int32": np.int32, "float32": np.float32, "int16": np.int16, "int64": np.int64, "uint8": np.uint8}

# status bit flags (§D3.1 #30)
STATUS = {
    "OK": 0, "NOOP": 1, "ILLEGAL_ACTION": 2, "CONTENT_GAP": 4, "SIM_ERROR": 8,
    "CANDIDATE_OVERFLOW": 16, "TABLE_OVERFLOW": 32, "RESET": 64, "POOL_EMPTY": 128,
}

# buffers returned separately from obs by VecEnv.step
SIGNAL_BUFFERS = ("reward", "terminated", "truncated", "status", "episode", "last_episode")


def status_names(bits: int) -> list[str]:
    return [n for n, b in STATUS.items() if b and bits & b] or ["OK"]


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
        if self.fields is None:
            raise KeyError(f"buffer {self.name} has no fields")
        return self.fields.index(name)


class VocabList(list):
    """A vocabulary: index 0 = "" (none), 1 = "?" (unknown); ``.index_of(id)`` maps unknown ids to 1."""

    def __init__(self, kind: str, names):
        super().__init__(names)
        self.kind = kind
        self.lookup = {n: i for i, n in enumerate(self)}

    def index_of(self, name: str | None) -> int:
        if not name:
            return 0
        return self.lookup.get(name, 1)


@dataclass
class Vocab:
    lists: dict[str, VocabList]
    enums: dict[str, list[str]]

    def __getattr__(self, kind: str) -> VocabList:
        try:
            return self.__dict__["lists"][kind]
        except KeyError:
            raise AttributeError(kind) from None


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
    by_name: dict[str, BufferSpec] = field(default_factory=dict)

    @staticmethod
    def from_json(text: str) -> "Schema":
        raw = json.loads(text)
        buffers = [
            BufferSpec(i, b["name"], np.dtype(DTYPES[b["dtype"]]), tuple(int(x) for x in b["shape"]),
                       tuple(b["fields"]) if b.get("fields") is not None else None)
            for i, b in enumerate(raw["buffers"])
        ]
        v = raw["vocab"]
        lists = {k: VocabList(k, names) for k, names in v.items() if k != "enums"}
        s = Schema(raw=raw, text=text, encoder_version=raw["encoder_version"], buffers=buffers,
                   vocab=Vocab(lists, dict(v.get("enums", {}))), caps=dict(raw["caps"]),
                   card_vars=VocabList("card_vars", raw.get("card_vars", [])),
                   playable_reasons=list(raw.get("playable_reasons", [])))
        s.by_name = {b.name: b for b in buffers}
        return s

    def __getitem__(self, name: str) -> BufferSpec:
        return self.by_name[name]

    @property
    def bytes_per_env(self) -> int:
        return sum(b.bytes_per_env for b in self.buffers)

    def fields(self, name: str) -> tuple[str, ...] | None:
        return self.by_name[name].fields

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
    """Env-owned numpy arrays (N, *shape) in schema order, and the void* table passed to the exports."""

    def __init__(self, schema: Schema, num_envs: int):
        self.schema = schema
        self.num_envs = num_envs
        self.arrays: dict[str, np.ndarray] = {}
        self.table = (ctypes.c_void_p * len(schema.buffers))()
        for b in schema.buffers:
            a = np.zeros((num_envs, *b.shape), dtype=b.dtype)
            assert a.flags.c_contiguous
            self.arrays[b.name] = a
            self.table[b.index] = a.ctypes.data

    @property
    def ptr(self) -> int:
        return ctypes.addressof(self.table)

    def __getitem__(self, name: str) -> np.ndarray:
        return self.arrays[name]

    @property
    def nbytes(self) -> int:
        return sum(a.nbytes for a in self.arrays.values())


# ---- XXH64 (seed 0), pure Python; only used to verify encoder_version ----

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
