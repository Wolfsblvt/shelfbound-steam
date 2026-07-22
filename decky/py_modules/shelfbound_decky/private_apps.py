"""Best-effort, positive-only reader for Steam's local Private-app fallback cache.

The account-scoped VDF value has no freshness or completeness marker. Only positive
membership may drive hosted omission; every other outcome remains explicit uncertainty.
Raw account ids and app ids stay in backend memory and never enter logs or hosted JSON.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from typing import Callable

from . import limits, vdf
from .steam_files import SteamAccount
from .vdf import VdfFormatError, VdfScalarSelection

POSITIVE = "positive"
ABSENT = "absent"
EMPTY = "empty"
UNREADABLE = "unreadable"
MALFORMED = "malformed"
ACCOUNT_MISMATCH = "accountMismatch"


@dataclass(frozen=True)
class PrivateAppsEvidenceOutcome:
    state: str
    private_app_ids: frozenset[int] = frozenset()


@dataclass(frozen=True)
class PrivateAppsEvidenceResult:
    outcomes: tuple[PrivateAppsEvidenceOutcome, ...]
    private_app_ids: frozenset[int]

    def describe(self) -> str:
        positive_accounts = sum(1 for outcome in self.outcomes if outcome.state == POSITIVE)
        uncertain_accounts = len(self.outcomes) - positive_accounts
        if positive_accounts == 0:
            if not self.outcomes:
                return "No local Steam account was available for Private-game evidence. No games were omitted."
            return (
                "Steam's local cache supplied no positive Private-game evidence. Missing, empty, "
                "unreadable, malformed, or account-mismatched cache data proves nothing, so no "
                "games were omitted from it."
            )

        uncertainty = (
            f" {uncertain_accounts} other local account cache(s) were inconclusive and did not "
            "authorize omission."
            if uncertain_accounts
            else ""
        )
        return (
            f"Steam's local cache last marked {len(self.private_app_ids)} game(s) Private across "
            f"{positive_accounts} local account(s). The cache may be stale.{uncertainty}"
        )


def read_private_apps_evidence(
    steam_root: str,
    accounts: list[SteamAccount],
    *,
    file_exists: Callable[[str], bool] = os.path.isfile,
    select_cache_value: Callable[[str, str], VdfScalarSelection] | None = None,
) -> PrivateAppsEvidenceResult:
    """Unions positive membership across every known account on this device."""
    value_selector = select_cache_value or _select_private_apps_value
    outcomes: list[PrivateAppsEvidenceOutcome] = []
    private_app_ids: set[int] = set()
    for account in accounts:
        outcome = _read_account(steam_root, account, file_exists, value_selector)
        outcomes.append(outcome)
        if outcome.state == POSITIVE:
            private_app_ids.update(outcome.private_app_ids)
    return PrivateAppsEvidenceResult(tuple(outcomes), frozenset(private_app_ids))


def _read_account(
    steam_root: str,
    account: SteamAccount,
    file_exists: Callable[[str], bool],
    select_cache_value: Callable[[str, str], VdfScalarSelection],
) -> PrivateAppsEvidenceOutcome:
    account_id = account.account_id
    if account_id is None or account_id <= 0 or account_id > 4_294_967_295:
        return PrivateAppsEvidenceOutcome(ACCOUNT_MISMATCH)

    account_text = str(account_id)
    path = os.path.join(steam_root, "userdata", account_text, "config", "localconfig.vdf")
    if not file_exists(path):
        return PrivateAppsEvidenceOutcome(ABSENT)

    expected_key = f"PrivateApps_{account_text}"
    try:
        selection = select_cache_value(path, expected_key)
    except OSError:
        return PrivateAppsEvidenceOutcome(UNREADABLE)
    except VdfFormatError:
        return PrivateAppsEvidenceOutcome(MALFORMED)

    raw_value = selection.value
    if raw_value is None:
        return PrivateAppsEvidenceOutcome(
            ACCOUNT_MISMATCH if selection.has_matching_sibling else ABSENT
        )
    if not raw_value.strip():
        return PrivateAppsEvidenceOutcome(EMPTY)

    app_ids = _parse_private_app_ids(raw_value)
    if app_ids is None:
        return PrivateAppsEvidenceOutcome(MALFORMED)
    return PrivateAppsEvidenceOutcome(
        POSITIVE if app_ids else EMPTY,
        app_ids,
    )


def _select_private_apps_value(path: str, expected_key: str) -> VdfScalarSelection:
    return vdf.select_file_value(
        path,
        ("UserLocalConfigStore", "WebStorage"),
        expected_key,
        "PrivateApps_",
    )


def _parse_private_app_ids(raw_value: str) -> frozenset[int] | None:
    """Parse exactly one flat JSON integer array with entry limits enforced during the scan."""
    index = _skip_json_whitespace(raw_value, 0)
    if index >= len(raw_value) or raw_value[index] != "[":
        return None
    index = _skip_json_whitespace(raw_value, index + 1)
    if index < len(raw_value) and raw_value[index] == "]":
        index = _skip_json_whitespace(raw_value, index + 1)
        return frozenset() if index == len(raw_value) else None

    app_ids: set[int] = set()
    entry_count = 0
    while index < len(raw_value):
        if not raw_value[index].isdigit() or not raw_value[index].isascii():
            return None

        first_digit = raw_value[index]
        app_id = 0
        while (
            index < len(raw_value)
            and raw_value[index].isascii()
            and raw_value[index].isdigit()
        ):
            if first_digit == "0" and app_id == 0 and index + 1 < len(raw_value):
                next_char = raw_value[index + 1]
                if next_char.isascii() and next_char.isdigit():
                    return None
            app_id = (app_id * 10) + (ord(raw_value[index]) - ord("0"))
            if app_id > 2_147_483_647:
                return None
            index += 1

        if app_id <= 0:
            return None
        entry_count += 1
        if entry_count > limits.MAX_PRIVATE_APP_ENTRIES:
            return None
        app_ids.add(app_id)

        index = _skip_json_whitespace(raw_value, index)
        if index >= len(raw_value):
            return None
        if raw_value[index] == "]":
            index = _skip_json_whitespace(raw_value, index + 1)
            return frozenset(app_ids) if index == len(raw_value) else None
        if raw_value[index] != ",":
            return None
        index = _skip_json_whitespace(raw_value, index + 1)
    return None


def _skip_json_whitespace(value: str, index: int) -> int:
    while index < len(value) and value[index] in " \t\r\n":
        index += 1
    return index
