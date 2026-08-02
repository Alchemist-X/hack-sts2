from pathlib import Path

from sts2rec.audit import audit_lineage


def test_audit_fixture_lineage_is_structured(session_dir: Path) -> None:
    report = audit_lineage(session_dir)
    assert report["meta"]["part_count"] == 1
    assert report["decision_snapshots"] == 2
    assert isinstance(report["state_types"], dict)
