#!/usr/bin/env python3
"""Send one projected payload through the existing owner-private service boundary."""
import argparse
import json
import os
from pathlib import Path
import re
import ssl
import sys
import urllib.error
import urllib.request

from project_live import read_json

URL = "https://sts2-modal-research-console.crazy-kfc.chatgpt.site/api/research/live"
MAX_PAYLOAD = 16 * 1024


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    parser.add_argument("--credential-fd", type=int, required=True,
                        help="existing authorized service credential as inherited file descriptor")
    args = parser.parse_args()
    payload = read_json(args.payload, MAX_PAYLOAD)
    if not isinstance(payload, dict) or set(payload) != {"schema_version", "version", "observed_at", "runs"}:
        raise ValueError("expected projected payload")
    body = json.dumps(payload, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode()
    if len(body) > MAX_PAYLOAD:
        raise ValueError("payload exceeds bound")
    with os.fdopen(os.dup(args.credential_fd), "rb") as credential:
        token = credential.read(8193).strip()
    if not re.fullmatch(rb"[A-Za-z0-9._~+/-]{1,8192}=*", token):
        raise ValueError("invalid service credential format")
    request = urllib.request.Request(URL, data=body, method="POST", headers={
        "Content-Type": "application/json",
        "OAI-Sites-Authorization": "Bearer " + token.decode("ascii"),
    })
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(),
                                      urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    with opener.open(request, timeout=15) as response:
        result = json.loads(response.read(2049))
        if response.status != 200 or result != {"ok": True, "version": payload["version"]}:
            raise ValueError("unexpected live receipt")
    print(json.dumps({"ok": True, "version": payload["version"]}))


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as error:
        print(f"monitor_push_http={error.code}", file=sys.stderr)
        raise SystemExit(1) from None
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(f"monitor_push_failed={type(error).__name__}", file=sys.stderr)
        raise SystemExit(1) from None
