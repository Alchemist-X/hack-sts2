#!/usr/bin/env python3
"""Push one already-curated snapshot to the owner-private Sites endpoint.

Input is a bounded JSON file and a systemd LoadCredential token file. No raw
server data, credential, or response body is ever logged.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import ssl
import sys
import urllib.error
import urllib.request

URL = "https://sts2-modal-research-console.crazy-kfc.chatgpt.site/api/snapshot"
LIMIT = 1024 * 1024


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def send(source: Path, token_file: Path, verify: bool = True) -> int:
    if source.stat().st_size > LIMIT:
        raise ValueError("snapshot exceeds size limit")
    body = source.read_bytes()
    record = json.loads(body)
    if not isinstance(record, dict) or record.get("schema_version") != 1 or not isinstance(record.get("version"), int):
        raise ValueError("invalid snapshot header")
    token = token_file.read_text(encoding="utf-8").strip()
    if not token or len(token) > 4096 or "\n" in token or "\r" in token:
        raise ValueError("invalid service credential")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(), urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    headers = {"Content-Type": "application/json", "OAI-Sites-Authorization": "Bearer " + token}
    req = urllib.request.Request(URL, data=body, method="POST", headers=headers)
    with opener.open(req, timeout=25) as response:
        if response.status != 200:
            raise RuntimeError("push rejected")
        ack = json.loads(response.read(4096))
        if ack != {"ok": True, "version": record["version"]}:
            raise RuntimeError("unexpected push acknowledgement")
    if verify:
        req = urllib.request.Request(URL, method="GET", headers={"OAI-Sites-Authorization": "Bearer " + token})
        with opener.open(req, timeout=25) as response:
            if response.status != 200:
                raise RuntimeError("snapshot verification failed")
            remote = json.loads(response.read(LIMIT + 1))
            if not isinstance(remote, dict) or remote.get("schema_version") != 1 or remote.get("version") != record["version"] or remote.get("generated_at") != record.get("generated_at"):
                raise RuntimeError("snapshot verification mismatch")
    return record["version"]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True)
    parser.add_argument("--token-file", required=True)
    parser.add_argument("--no-verify", action="store_true")
    args = parser.parse_args()
    try:
        version = send(Path(args.source), Path(args.token_file), not args.no_verify)
    except (OSError, ValueError, RuntimeError, json.JSONDecodeError, urllib.error.URLError, urllib.error.HTTPError):
        print("monitor push failed; check private service logs", file=sys.stderr)
        return 1
    print(f"monitor snapshot v{version} pushed and verified" if not args.no_verify else f"monitor snapshot v{version} pushed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
