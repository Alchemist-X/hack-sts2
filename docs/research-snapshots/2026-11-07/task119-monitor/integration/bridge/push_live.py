#!/usr/bin/env python3
"""Bounded owner-private HTTPS client derived from the Task119 one-shot sender."""
import json
import os
from pathlib import Path
import re
import signal
import ssl
import urllib.request

from project_live import RUNS, epoch, payload_for, utc

RESEARCH_URL = "https://sts2-modal-research-console.crazy-kfc.chatgpt.site/api/research"
URL = RESEARCH_URL + "/live"
MAX_PAYLOAD = 16 * 1024
MAX_RESEARCH = 2 * 1024 * 1024
RUN_FIELDS = {
    "id", "label", "status", "observed_at", "started_at", "deadline_at", "decisions",
    "target_decisions", "new_decisions", "updates", "checkpoint_evidence", "resources",
    "progress_health", "stale",
}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def read_service_token(fd=None):
    if fd is None:
        path = Path(os.environ["CREDENTIALS_DIRECTORY"]) / "site_access"
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    else:
        fd = os.dup(fd)
    with os.fdopen(fd, "rb") as credential:
        token = credential.read(8193).strip()
    if len(token) > 8192 or not re.fullmatch(rb"[A-Za-z0-9._~+/-]+=*", token):
        raise ValueError("invalid service credential format")
    return token.decode("ascii")


def alarm_expired(signum, frame):
    raise TimeoutError("Site HTTP wall timeout")


class SiteClient:
    def __init__(self, token):
        self._authorization = "Bearer " + token
        self._opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}), NoRedirect(),
            urllib.request.HTTPSHandler(context=ssl.create_default_context()),
        )

    def _request(self, url, limit, timeout, data=None):
        headers = {"OAI-Sites-Authorization": self._authorization}
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(url, data=data, headers=headers)
        old_handler = signal.signal(signal.SIGALRM, alarm_expired)
        signal.setitimer(signal.ITIMER_REAL, timeout)
        try:
            with self._opener.open(request, timeout=timeout) as response:
                result = response.read(limit + 1)
                if response.status != 200 or len(result) > limit:
                    raise ValueError("unexpected Site response")
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
            signal.signal(signal.SIGALRM, old_handler)
        return json.loads(result)

    def post(self, payload, timeout):
        body = json.dumps(payload, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode()
        if len(body) > MAX_PAYLOAD:
            raise ValueError("payload exceeds bound")
        result = self._request(URL, 2048, timeout, body)
        if result != {"ok": True, "version": payload["version"]}:
            raise ValueError("unexpected live receipt")

    def get(self, timeout):
        result = self._request(RESEARCH_URL, MAX_RESEARCH, timeout)
        live = result["live"]
        if result["schema_version"] != 1 or live["schema_version"] != 1:
            raise ValueError("Site schema differs")
        runs = live["runs"]
        if (not isinstance(runs, list) or len(runs) != 2
                or not all(isinstance(run, dict) and set(run) == RUN_FIELDS for run in runs)
                or {run["id"] for run in runs} != set(RUNS)):
            raise ValueError("Site does not expose both configured observations")
        for run in runs:
            observed = epoch(run["observed_at"])
            if (observed is None or utc(observed) != run["observed_at"]
                    or run["status"] not in ("running", "terminal", "unknown")
                    or run["checkpoint_evidence"] not in ("none", "writer_metadata", "body_verified")
                    or not all(run[key] is None or type(run[key]) is int and 0 <= run[key] <= 2**53 - 1
                               for key in ("decisions", "updates"))):
                raise ValueError("invalid Site run readback")
        projected = [
            {key: value for key, value in run.items() if key != "stale"}
            for run in runs
        ]
        snapshot = payload_for(sorted(projected, key=lambda run: run["id"]))
        if snapshot["observed_at"] != live["observed_at"]:
            raise ValueError("Site observation time disagrees with run observations")
        return snapshot
