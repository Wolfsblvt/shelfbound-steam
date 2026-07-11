import json
from pathlib import Path

from shelfbound_decky.device_identity import DEFAULT_DEVICE_NAME
from shelfbound_decky.hosted_projection import FIELD_PURPOSES, prepare_hosted_upload

GOLDEN_PATH = (
    Path(__file__).resolve().parents[2] / "tests" / "Fixtures" / "hosted-snapshot.golden.json"
)


def test_python_projection_matches_the_csharp_golden_and_drops_sensitive_fields():
    local = full_local_snapshot()

    upload = prepare_hosted_upload(local)

    assert upload.body == GOLDEN_PATH.read_text(encoding="utf-8").rstrip("\r\n")
    forbidden = (
        "steamAccounts",
        "synthetic-login",
        "Synthetic Persona",
        "76561198000000001",
        "synthetic-private-host",
        "SERIAL-123",
        "/home/test/private/Steam",
        "synthetic-secret",
        "Windows 10.0.26200",
    )
    for value in forbidden:
        assert value not in upload.body

    assert upload.snapshot["device"]["name"] == "Living room PC"
    assert upload.snapshot["device"]["specs"]["osDescription"] == "Windows 10/11"
    assert upload.snapshot["games"][0]["installDir"] == "Relative Folder"


def test_legacy_machine_hostname_is_neutralized(monkeypatch):
    monkeypatch.setattr(
        "shelfbound_decky.hosted_projection.socket.gethostname",
        lambda: "synthetic-private-host",
    )
    local = full_local_snapshot()
    local["device"]["name"] = "synthetic-private-host"

    upload = prepare_hosted_upload(local)

    assert upload.snapshot["device"]["name"] == DEFAULT_DEVICE_NAME
    assert "synthetic-private-host" not in upload.body


def test_purpose_manifest_covers_every_surviving_golden_leaf():
    upload = prepare_hosted_upload(full_local_snapshot())
    leaves: set[str] = set()

    collect_leaf_paths(upload.snapshot, "", leaves)

    manifest_paths = [path for path, _purpose in FIELD_PURPOSES]
    assert len(manifest_paths) == len(set(manifest_paths))
    assert set(manifest_paths) == leaves


def test_invalid_local_snapshot_fails_before_a_body_can_be_prepared():
    local = full_local_snapshot()
    del local["device"]

    try:
        prepare_hosted_upload(local)
    except ValueError as error:
        assert "device" in str(error)
    else:
        raise AssertionError("invalid snapshot unexpectedly produced an upload body")


def collect_leaf_paths(value, path: str, leaves: set[str]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            child_path = f"{path}.{key}" if path else key
            collect_leaf_paths(child, child_path, leaves)
    elif isinstance(value, list):
        for child in value:
            collect_leaf_paths(child, f"{path}[]", leaves)
    else:
        leaves.add(path)


def full_local_snapshot() -> dict:
    return {
        "schemaVersion": "0.5.0",
        "snapshotId": "11111111-1111-1111-1111-111111111111",
        "createdAt": "2026-07-11T12:34:56+00:00",
        "source": {
            "tool": "contract-test",
            "toolVersion": "1.2.3",
            "platform": "windows",
            "syntheticSecret": "synthetic-secret",
        },
        "device": {
            "id": "22222222-2222-2222-2222-222222222222",
            "name": "Living room PC",
            "hostname": "synthetic-private-host",
            "type": "desktop",
            "os": "windows",
            "specs": {
                "cpu": "Synthetic CPU",
                "logicalCores": 8,
                "totalMemoryBytes": 16_000_000_000,
                "gpu": "Synthetic GPU",
                "osDescription": "Microsoft Windows 10.0.26200",
                "architecture": "X64",
                "serialNumber": "SERIAL-123",
            },
        },
        "steamAccounts": [
            {
                "steamId64": "76561198000000001",
                "accountName": "synthetic-login",
                "personaName": "Synthetic Persona",
                "mostRecent": True,
            }
        ],
        "libraries": [
            {
                "index": 0,
                "label": "Main library",
                "gameCount": 1,
                "path": "/home/test/private/Steam",
                "storage": {
                    "kind": "internal",
                    "freeBytes": 123_456_789,
                    "totalBytes": 987_654_321,
                    "deviceName": "private-block-device",
                },
            }
        ],
        "games": [
            {
                "appId": 42,
                "name": "Private shortcut title",
                "installed": True,
                "libraryIndex": 0,
                "installDir": "Relative Folder",
                "fullPath": "/home/test/private/Steam/steamapps/common/Relative Folder",
                "sizeOnDiskBytes": 12_345,
                "playtimeMinutes": 67,
                "lastUpdated": "2026-07-10T10:00:00+00:00",
                "lastPlayed": "2026-07-11T11:00:00+00:00",
                "categories": ["Deck", "Private collection"],
            }
        ],
        "categories": [
            {"name": "Deck", "gameCount": 1},
            {"name": "Private collection", "gameCount": 1},
        ],
        "stats": {
            "libraryCount": 1,
            "installedGameCount": 1,
            "totalSizeOnDiskBytes": 12_345,
            "scope": "fullLibrary",
        },
    }
