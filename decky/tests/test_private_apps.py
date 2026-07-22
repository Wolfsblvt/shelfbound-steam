import json
import os
from pathlib import Path

from shelfbound_decky import limits, vdf
from shelfbound_decky.private_apps import MALFORMED, read_private_apps_evidence
from shelfbound_decky.steam_files import STEAM_ID64_BASE, SteamAccount

FIXTURE_PATH = (
    Path(__file__).resolve().parents[2]
    / "tests"
    / "Fixtures"
    / "private-game-exclusion.cases.json"
)


def test_shared_fixture_covers_positive_and_every_ambiguous_outcome():
    fixture = json.loads(FIXTURE_PATH.read_text(encoding="utf-8"))

    for evidence_case in fixture["evidenceCases"]:
        steam_root = "synthetic-steam-root"
        inputs_by_path = {
            localconfig_path(steam_root, account["accountId"]): account
            for account in evidence_case["accounts"]
        }
        accounts = [
            SteamAccount(str(STEAM_ID64_BASE + account["accountId"]), None, None, False)
            for account in evidence_case["accounts"]
        ]

        def file_exists(path: str) -> bool:
            return inputs_by_path[path]["action"] != "missing"

        def parse_file(path: str):
            account = inputs_by_path[path]
            if account["action"] == "unreadable":
                raise OSError("Synthetic unreadable fixture.")
            return vdf.parse(account["vdf"])

        result = read_private_apps_evidence(
            steam_root,
            accounts,
            file_exists=file_exists,
            parse_file=parse_file,
        )

        assert [outcome.state for outcome in result.outcomes] == evidence_case["expectedStates"]
        assert sorted(result.private_app_ids) == evidence_case["expectedPrivateAppIds"]
        for account in evidence_case["accounts"]:
            assert str(account["accountId"]) not in result.describe()
        for app_id in evidence_case["expectedPrivateAppIds"]:
            assert str(app_id) not in result.describe()


def test_rejects_an_unbounded_private_app_array():
    oversized = json.dumps([1] * (limits.MAX_PRIVATE_APP_ENTRIES + 1), separators=(",", ":"))
    payload = f'"UserLocalConfigStore" {{ "WebStorage" {{ "PrivateApps_10" "{oversized}" }} }}'
    account = SteamAccount(str(STEAM_ID64_BASE + 10), None, None, False)

    result = read_private_apps_evidence(
        "fixture",
        [account],
        file_exists=lambda _path: True,
        parse_file=lambda _path: vdf.parse(payload),
    )

    assert result.outcomes[0].state == MALFORMED
    assert not result.private_app_ids


def localconfig_path(steam_root: str, account_id: int) -> str:
    return os.path.join(
        steam_root,
        "userdata",
        str(account_id),
        "config",
        "localconfig.vdf",
    )
