"""Fake torch for the driver's fork check (`torch.load(...)` of a stub checkpoint, which is JSON)."""
import json


def load(path, map_location=None, weights_only=None):  # noqa: ARG001 - torch.load's signature
    with open(path) as f:
        return json.load(f)
