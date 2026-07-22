"""Best-effort, positive-only reader for Steam's local Private-app fallback cache.

The account-scoped VDF value has no freshness or completeness marker. Only positive
membership may drive hosted omission; every other outcome remains explicit uncertainty.
Raw account ids and app ids stay in backend memory and never enter logs or hosted JSON.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Callable

from . import limits, vdf
from .steam_files import SteamAccount
from .vdf import VdfFormatError, VdfObject

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
    parse_file: Callable[[str], VdfObject] = vdf.parse_file,
) -> PrivateAppsEvidenceResult:
    """Unions positive membership across every known account on this device."""
    outcomes: list[PrivateAppsEvidenceOutcome] = []
    private_app_ids: set[int] = set()
    for account in accounts:
        outcome = _read_account(steam_root, account, file_exists, parse_file)
        outcomes.append(outcome)
        if outcome.state == POSITIVE:
            private_app_ids.update(outcome.private_app_ids)
    return PrivateAppsEvidenceResult(tuple(outcomes), frozenset(private_app_ids))


def _read_account(
    steam_root: str,
    account: SteamAccount,
    file_exists: Callable[[str], bool],
    parse_file: Callable[[str], VdfObject],
) -> PrivateAppsEvidenceOutcome:
    account_id = account.account_id
    if account_id is None or account_id <= 0 or account_id > 4_294_967_295:
        return PrivateAppsEvidenceOutcome(ACCOUNT_MISMATCH)

    account_text = str(account_id)
    path = os.path.join(steam_root, "userdata", account_text, "config", "localconfig.vdf")
    if not file_exists(path):
        return PrivateAppsEvidenceOutcome(ABSENT)

    try:
        root = parse_file(path)
    except OSError:
        return PrivateAppsEvidenceOutcome(UNREADABLE)
    except VdfFormatError:
        return PrivateAppsEvidenceOutcome(MALFORMED)

    local_store = root.get_object("UserLocalConfigStore")
    web_storage = local_store.get_object("WebStorage") if local_store is not None else None
    if web_storage is None:
        return PrivateAppsEvidenceOutcome(ABSENT)

    expected_key = f"PrivateApps_{account_text}"
    raw_value = web_storage.get_value(expected_key)
    if raw_value is None:
        has_other_account_key = any(
            key.casefold().startswith("privateapps_") for key in web_storage.values
        )
        return PrivateAppsEvidenceOutcome(
            ACCOUNT_MISMATCH if has_other_account_key else ABSENT
        )
    if not raw_value.strip():
        return PrivateAppsEvidenceOutcome(EMPTY)

    try:
        values = json.loads(raw_value)
    except ValueError:
        return PrivateAppsEvidenceOutcome(MALFORMED)
    if not isinstance(values, list) or len(values) > limits.MAX_PRIVATE_APP_ENTRIES:
        return PrivateAppsEvidenceOutcome(MALFORMED)

    app_ids: set[int] = set()
    for value in values:
        if not isinstance(value, int) or isinstance(value, bool) or value <= 0 or value > 2_147_483_647:
            return PrivateAppsEvidenceOutcome(MALFORMED)
        app_ids.add(value)
    return PrivateAppsEvidenceOutcome(
        POSITIVE if app_ids else EMPTY,
        frozenset(app_ids),
    )
