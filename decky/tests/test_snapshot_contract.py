"""The heart of the prototype's verification: the emitted snapshot must validate
against the real, unmodified contract (schema/snapshot.v0.schema.json) and match the
C# scanner's semantics. Runnable on any machine — no Deck required."""

import json
from datetime import datetime, timezone
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator, FormatChecker

from shelfbound_decky import SCHEMA_VERSION
from shelfbound_decky.snapshot import build_snapshot
from steamroot_fixture import MISSING_MANIFEST_APP_ID, make_steam_root

SCHEMA_PATH = Path(__file__).resolve().parents[2] / "schema" / "snapshot.v0.schema.json"

DEVICE = {
    "id": "b54997ab-0000-0000-0000-000000000000",
    "name": "testdeck",
    "type": "steamDeck",
    "os": "linux",
    "specs": {"cpu": "AMD Custom APU 0405", "logicalCores": 8, "totalMemoryBytes": 16000000000},
}


@pytest.fixture()
def scan(tmp_path):
    paths = make_steam_root(tmp_path)
    return build_snapshot(paths["steam_root"], DEVICE, "0.1.0"), paths


def test_snapshot_validates_against_the_real_schema(scan):
    output, _ = scan
    schema = json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))
    validator = Draft202012Validator(schema, format_checker=FormatChecker())

    errors = sorted(validator.iter_errors(output.snapshot), key=lambda e: list(e.absolute_path))
    assert not errors, "\n".join(f"{list(e.absolute_path)}: {e.message}" for e in errors)


def test_round_trips_through_json(scan):
    output, _ = scan
    assert json.loads(json.dumps(output.snapshot)) == output.snapshot


def test_header_and_source(scan):
    output, _ = scan
    snapshot = output.snapshot
    assert snapshot["schemaVersion"] == SCHEMA_VERSION == "0.4.0"
    assert snapshot["source"] == {"tool": "shelfbound-decky", "toolVersion": "0.1.0", "platform": "linux"}
    # createdAt parses as an aware UTC timestamp.
    created_at = datetime.fromisoformat(snapshot["createdAt"])
    assert created_at.tzinfo is not None
    assert created_at.utcoffset().total_seconds() == 0
    assert (datetime.now(timezone.utc) - created_at).total_seconds() < 60


def test_games_libraries_and_stats_mirror_the_scanner_semantics(scan):
    output, _ = scan
    snapshot = output.snapshot

    libraries = snapshot["libraries"]
    assert [lib["label"] for lib in libraries] == ["library-0", "SD Card"]
    # gameCount counts parsed manifests, not listed app ids (the missing one is excluded).
    assert [lib["gameCount"] for lib in libraries] == [2, 2]

    games = {game["appId"]: game for game in snapshot["games"]}
    assert set(games) == {228980, 753640, 1145360, 1091500}

    outer_wilds = games[753640]
    assert outer_wilds["installed"] is True
    assert outer_wilds["libraryIndex"] == 0
    assert outer_wilds["installDir"] == "Outer Wilds"
    assert outer_wilds["categories"] == ["Directly Choice", "Deck"]  # user's tag order preserved
    assert "lastPlayed" in outer_wilds and "lastUpdated" in outer_wilds

    cyberpunk = games[1091500]
    assert cyberpunk["installed"] is False  # StateFlags 1026: updating, not fully installed
    assert "lastPlayed" not in cyberpunk  # 0 timestamp omitted, never null

    stats = snapshot["stats"]
    assert stats["libraryCount"] == 2
    assert stats["installedGameCount"] == 3
    assert stats["totalSizeOnDiskBytes"] == sum(
        game.get("sizeOnDiskBytes", 0) for game in snapshot["games"]
    )
    assert stats["scope"] == "installedOnly"  # no web enrichment in this producer


def test_accounts_and_categories(scan):
    output, _ = scan
    snapshot = output.snapshot

    accounts = snapshot["steamAccounts"]
    assert [account["steamId64"] for account in accounts] == [
        "76561198000000001", "76561198000000002",
    ]
    assert accounts[0]["mostRecent"] is True
    assert accounts[0]["accountName"] == "wolftest"

    # Count desc, then ordinal name asc — mirrors the C# summary.
    assert snapshot["categories"] == [
        {"name": "Deck", "gameCount": 2},
        {"name": "Directly Choice", "gameCount": 1},
    ]


def test_no_null_values_anywhere(scan):
    """The contract omits absent fields instead of writing null."""
    output, _ = scan

    def walk(node):
        if isinstance(node, dict):
            for value in node.values():
                assert value is not None
                walk(value)
        elif isinstance(node, list):
            for item in node:
                walk(item)

    walk(output.snapshot)


def test_no_filesystem_paths_leak_into_the_snapshot(scan):
    output, paths = scan
    dumped = json.dumps(output.snapshot)
    assert paths["steam_root"].replace("\\", "/") not in dumped.replace("\\\\", "/")
    assert "steamapps" not in dumped
    # Library paths exist ONLY in the local-only side channel for the storage view.
    assert set(output.library_paths) == {0, 1}


def test_warnings_report_missing_manifest_and_prototype_limits(scan):
    output, _ = scan
    assert f"Missing manifest for app {MISSING_MANIFEST_APP_ID} in library 1." in output.warnings
    assert any("Modern Steam collections are not read" in warning for warning in output.warnings)


def test_missing_libraryfolders_falls_back_to_primary_library(tmp_path):
    steam_root = tmp_path / "bare-steam"
    (steam_root / "steamapps").mkdir(parents=True)

    output = build_snapshot(str(steam_root), DEVICE, "0.1.0")

    assert [lib["label"] for lib in output.snapshot["libraries"]] == ["library-0"]
    assert any("libraryfolders.vdf not found" in warning for warning in output.warnings)
    assert any("loginusers.vdf not found" in warning for warning in output.warnings)
