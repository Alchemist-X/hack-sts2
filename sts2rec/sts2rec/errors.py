"""Exception hierarchy for sts2rec.

Every error carries a human-readable message that names the offending
file/line/key so failures are actionable without a debugger.
"""

from __future__ import annotations


class Sts2RecError(Exception):
    """Base class for all sts2rec errors."""


class ManifestError(Sts2RecError):
    """manifest.json is missing, unreadable, or fails schema validation."""


class JsonlError(Sts2RecError):
    """A JSONL stream file is missing or contains a malformed line."""


class RecordError(Sts2RecError):
    """A parsed JSONL record does not match the envelope contract."""


class NativeRunError(Sts2RecError):
    """The game's native .run history file is missing or malformed."""


class CanonicalError(Sts2RecError):
    """Session streams cannot be merged into a canonical trajectory."""


class ArchiveError(Sts2RecError):
    """Native artifacts could not be archived into the session."""


class PackError(Sts2RecError):
    """A session's streams could not be gzip-packed safely."""
