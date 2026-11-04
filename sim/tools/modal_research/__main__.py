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
    platform.add_argument("--observed-at", type=float)
    platform.add_argument("--cycle-start")
    probe = commands.add_parser("reserve-access-probe")
    for name in ("key", "decision-id", "category", "reserve-usd"):
        probe.add_argument(f"--{name}", required=True)
    probe.add_argument("--seconds", type=int, required=True)
    probe.add_argument("--prepare-only", action="store_true")
    probe.add_argument("--child-job", action="append")
    probe.add_argument("--research-event")
    probe.add_argument("--isolated-gpu-namespace")
    probe.add_argument("--billing-vendor", choices=("modal", "non_modal"))
    probe.add_argument("--billing-reference")
    probe.add_argument("--worker-seconds", type=int)
    classify = commands.add_parser("classify-billing")
    classify.add_argument("--mapping-json", type=json.loads, required=True)
    classify.add_argument("--reference", required=True)
    commands.add_parser("activate-access-probe").add_argument("key")
    finish_probe = commands.add_parser("finish-access-probe")
    finish_probe.add_argument("key")
    finish_probe.add_argument("--status", required=True)
    finish_probe.add_argument("--session-id", action="append", default=[])
    finish_probe.add_argument("--report-ref", required=True)
    finish_probe.add_argument("--details-json", type=json.loads)
    pause_access = commands.add_parser("pause-access")
    pause_access.add_argument("reason")
    pause_access.add_argument("--next-wake", required=True)
    resume_access = commands.add_parser("resume-access")
    resume_access.add_argument("reason")
    resume_access.add_argument("--verification-reference", required=True)
    for command in ("resume-provider", "resume-research"):
        scoped = commands.add_parser(command)
        scoped.add_argument("scope")
        scoped.add_argument("reason")
        scoped.add_argument("--reference", required=True)
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
                              gross_used=args.gross_used, net_spent=args.net_spent, reference=args.reference,
                              observed_at=args.observed_at, cycle_start=args.cycle_start)
    elif args.command == "reserve-access-probe":
        store.reserve_access_probe(args.key, args.decision_id, args.category, args.reserve_usd, args.seconds,
                                   prepare_only=args.prepare_only, authorized_jobs=args.child_job,
                                   authorized_event=args.research_event,
                                   isolated_gpu_namespace=args.isolated_gpu_namespace,
                                   billing_vendor=args.billing_vendor, billing_reference=args.billing_reference,
                                   worker_seconds=args.worker_seconds)
    elif args.command == "classify-billing":
        store.classify_billing(args.mapping_json, args.reference)
    elif args.command == "activate-access-probe":
        store.activate_access_probe(args.key)
    elif args.command == "finish-access-probe":
        evidence = {"status": args.status, "session_ids": args.session_id, "report_ref": args.report_ref}
        if args.details_json is not None:
            evidence["details"] = args.details_json
        store.finish_access_probe(args.key, evidence)
    elif args.command == "pause-access":
        store.pause_access(args.reason, args.next_wake)
    elif args.command == "resume-access":
        store.resume_access(args.reason, args.verification_reference)
    elif args.command == "resume-provider":
        store.resume_provider(args.scope, args.reason, args.reference)
    elif args.command == "resume-research":
        store.resume_research(args.scope, args.reason, args.reference)
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
