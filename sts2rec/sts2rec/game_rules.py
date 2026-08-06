"""Versioned game-rule constants shared by data and benchmark contracts."""

from __future__ import annotations


MIN_ASCENSION = 0
MAX_ASCENSION = 10


def validate_ascension(value: object, name: str = "ascension") -> int:
    """Return an A0-A10 level or reject an invalid benchmark/data label."""

    if (
        isinstance(value, bool)
        or not isinstance(value, int)
        or value < MIN_ASCENSION
        or value > MAX_ASCENSION
    ):
        raise ValueError(
            f"{name} must be an integer from {MIN_ASCENSION} through "
            f"{MAX_ASCENSION}"
        )
    return value
