"""Transport for a verified asynchronous job gateway. Importing opens no connections."""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import HTTPRedirectHandler, Request, build_opener

from .adapters import Outcome
from .contracts import bounded_text, canonical, identifier

MAX_REPLY_BYTES = 128_000


@dataclass(frozen=True)
class AgentBinding:
    model_id: str
    verification_ref: str
    max_output_bytes: int
    scope: str

    def validate(self, scope):
        bounded_text(self.model_id, 256)
        identifier(self.verification_ref)
        if self.scope != scope or type(self.max_output_bytes) is not int or not 1 <= self.max_output_bytes <= 16_000:
            raise ValueError("invalid agent binding scope or output limit")


class _NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


class GatewayBackend:
    """Provider job IDs must survive process death; the server enforces idempotency and admission.

    The SOL binding identifies a sandbox with scoped file/test tools. A text-only
    model endpoint cannot implement its contract.
    """

    fixture = False
    tool_enabled = True

    def __init__(self, url, token, research, sol, provider_verification):
        endpoint = urlsplit(url)
        if (endpoint.scheme != "https" or not endpoint.hostname or endpoint.username or endpoint.password
                or endpoint.query or endpoint.fragment):
            raise ValueError("gateway requires an HTTPS URL without embedded credentials")
        identifier(provider_verification)
        bounded_text(token, 4096)
        research.validate("research-decision")
        sol.validate("sol-isolated-files-and-tests")
        self.url = url.rstrip("/")
        self.token = token
        self.agents = {"research": research, "sol": sol}
        self.opener = build_opener(_NoRedirect)

    def _request(self, method, path, payload=None, authoritative_absence=False):
        data = canonical(payload).encode() if payload is not None else None
        request = Request(f"{self.url}{path}", data=data, method=method, headers={
            "Authorization": f"Bearer {self.token}", "Accept": "application/json", "Content-Type": "application/json",
        })
        try:
            with self.opener.open(request, timeout=5) as response:
                raw = response.read(MAX_REPLY_BYTES + 1)
        except HTTPError as error:
            if authoritative_absence and error.code == 404:
                raw = error.read(1024)
                try:
                    if json.loads(raw) == {"status": "absent", "billable": False}:
                        return None
                except ValueError:
                    pass
            raise RuntimeError("job gateway unavailable or ambiguous") from None
        if len(raw) > MAX_REPLY_BYTES:
            raise ValueError("job gateway reply exceeded limit")
        return json.loads(raw)

    def submit_admitted(self, job, admission):
        payload = {"job": asdict(job), "admission": admission}
        binding = self.agents.get(job.kind)
        if binding:
            payload["agent"] = asdict(binding)
        value = self._request("PUT", f"/jobs/by-key/{quote(job.key, safe='')}", payload)
        return identifier(value["job_id"])

    def lookup(self, job):
        value = self._request("GET", f"/jobs/by-key/{quote(job.key, safe='')}", authoritative_absence=True)
        return None if value is None else identifier(value["job_id"])

    def poll(self, remote_id):
        value = self._request("GET", f"/jobs/{quote(identifier(remote_id), safe='')}")
        if type(value) is not dict or set(value) - {"status", "actual_cents", "output", "error"}:
            raise ValueError("invalid job response")
        return Outcome(**value)
