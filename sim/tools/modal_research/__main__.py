"""Operator CLI for local state. No network calls are made by this module."""

import argparse
import json
from pathlib import Path
from tempfile import NamedTemporaryFile

from .store import Store


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", type=Path, required=True)
    parser.add_argument("--study-id", default="silent-a10-modal")
    commands = parser.add_subparsers(dest="command", required=True)
    status = commands.add_parser("status")
    status.add_argument("--output", type=Path)
    approve = commands.add_parser("approve")
    approve.add_argument("amount")
    approve.add_argument("--reference", required=True)
    approve.add_argument("--execution-cap", required=True)
    platform = commands.add_parser("platform-snapshot")
    for name in ("gross-limit", "net-limit", "credit", "gross-used", "net-spent", "reference"):
        platform.add_argument(f"--{name}", required=True)
    probe = commands.add_parser("reserve-access-probe")
    for name in ("key", "decision-id", "category", "reserve-usd"):
        probe.add_argument(f"--{name}", required=True)
    probe.add_argument("--seconds", type=int, required=True)
    commands.add_parser("stop").add_argument("reason")
    resume = commands.add_parser("resume")
    resume.add_argument("reason")
    resume.add_argument("--reference", required=True)
    args = parser.parse_args()
    store = Store(args.state, args.study_id)
    if args.command == "approve":
        store.approve(args.amount, args.reference, args.execution_cap)
    elif args.command == "platform-snapshot":
        store.record_platform(gross_limit=args.gross_limit, net_limit=args.net_limit, credit=args.credit,
                              gross_used=args.gross_used, net_spent=args.net_spent, reference=args.reference)
    elif args.command == "reserve-access-probe":
        store.reserve_access_probe(args.key, args.decision_id, args.category, args.reserve_usd, args.seconds)
    elif args.command == "stop":
        store.stop(args.reason)
    elif args.command == "resume":
        store.resume(args.reason, args.reference)
    output = json.dumps(store.status(), indent=2, sort_keys=True) + "\n"
    if args.command == "status" and args.output:
        with NamedTemporaryFile(dir=args.output.parent, mode="w", delete=False) as handle:
            handle.write(output)
        Path(handle.name).replace(args.output)
    print(output, end="")


if __name__ == "__main__":
    main()
