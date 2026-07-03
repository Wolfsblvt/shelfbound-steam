"""HTTP client for a Shelfbound server (stdlib urllib only).

The live endpoints mirror the C# ShelfboundClient (src/Shelfbound.Client/):

- ``POST {server}/ingest``            Bearer device token, compact snapshot JSON.
- ``GET  {server}/auth/me``           the signed-in account behind the token.
- ``GET  {server}/auth/entitlements`` plan limits (display only; server enforces).

PAIRING IS A PROPOSAL. The pairing endpoints below do NOT exist server-side yet; they
sketch the claim flow the Decky plan calls for (pairing-code / QR / portal claim —
never the tray's loopback callback, which is wrong for Gaming Mode). The flow:

1. ``POST {server}/devices/pair`` with ``{deviceName, deviceType, deviceId}``
   → ``{code, claimUrl, pollToken, expiresInSeconds, pollIntervalSeconds}``
2. The user opens ``claimUrl`` on any signed-in browser (phone/desktop) and confirms
   the code shown on the Deck.
3. ``POST {server}/devices/pair/poll`` with ``{pollToken}``
   → ``{status: "pending" | "claimed" | "expired" | "denied", token?}``
   The plugin polls user-triggered (once per button press), not on a hot loop.

Against today's server these return 404 and the plugin reports pairing as
not-available — honest failure, no faked success. Revocation stays in the dashboard
(post-M-4 the ``/auth/tokens*`` management endpoints are cookie-only).
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass

from . import TOOL_NAME, TOOL_VERSION

# UploadOutcome.status values (mirror the C# UploadStatus enum, camelCased).
UPLOAD_SUCCESS = "success"
UPLOAD_THROTTLED = "throttled"
UPLOAD_UNAUTHORIZED = "unauthorized"
UPLOAD_ERROR = "error"


class PairingUnavailableError(Exception):
    """The server does not implement the (proposed) pairing endpoints yet."""


class ServerError(Exception):
    def __init__(self, status: int, message: str | None = None) -> None:
        super().__init__(message or f"Server returned {status}.")
        self.status = status


@dataclass(frozen=True)
class UploadOutcome:
    status: str
    game_count: int
    message: str | None
    retry_after_seconds: int | None

    @property
    def ok(self) -> bool:
        return self.status == UPLOAD_SUCCESS


class ShelfboundServer:
    def __init__(self, base_url: str, token: str | None = None, timeout_seconds: float = 15.0) -> None:
        self._base = base_url.rstrip("/")
        self._token = token
        self._timeout = timeout_seconds

    def upload_snapshot(self, snapshot: dict) -> UploadOutcome:
        """POST /ingest; maps responses exactly like the C# client."""
        body = json.dumps(snapshot, separators=(",", ":"), ensure_ascii=False)
        try:
            status, headers, _ = self._request("POST", "ingest", body=body)
        except OSError as error:
            return UploadOutcome(UPLOAD_ERROR, 0, str(error), None)

        if 200 <= status < 300:
            return UploadOutcome(UPLOAD_SUCCESS, len(snapshot.get("games", [])), None, None)
        if status == 429:
            return UploadOutcome(
                UPLOAD_THROTTLED, 0,
                "Too soon for your plan. Automatic/frequent sync is a Pro/Lifetime feature.",
                _parse_retry_after(headers.get("retry-after")),
            )
        if status == 401:
            return UploadOutcome(UPLOAD_UNAUTHORIZED, 0, "Invalid or missing API token.", None)
        return UploadOutcome(UPLOAD_ERROR, 0, f"Server returned {status}.", None)

    def get_account(self) -> dict | None:
        """GET /auth/me — {accountId, steamId?, displayName?} or None."""
        return self._get_json("auth/me")

    def get_entitlements(self) -> dict | None:
        """GET /auth/entitlements — {plan, autoSync, minUploadIntervalSeconds, maxDevices} or None."""
        return self._get_json("auth/entitlements")

    def pairing_start(self, device_name: str, device_type: str, device_id: str) -> dict:
        """PROPOSED endpoint — see the module docstring. Raises PairingUnavailableError on 404/405/501."""
        body = json.dumps({"deviceName": device_name, "deviceType": device_type, "deviceId": device_id})
        status, _, text = self._request("POST", "devices/pair", body=body)
        if status in (404, 405, 501):
            raise PairingUnavailableError(
                "This server doesn't support device pairing yet (the endpoint is a Shelfbound proposal)."
            )
        if not 200 <= status < 300:
            raise ServerError(status)
        return json.loads(text)

    def pairing_poll(self, poll_token: str) -> dict:
        """PROPOSED endpoint — one user-triggered poll of the pairing session."""
        body = json.dumps({"pollToken": poll_token})
        status, _, text = self._request("POST", "devices/pair/poll", body=body)
        if status in (404, 405, 501):
            raise PairingUnavailableError(
                "This server doesn't support device pairing yet (the endpoint is a Shelfbound proposal)."
            )
        if not 200 <= status < 300:
            raise ServerError(status)
        return json.loads(text)

    def _get_json(self, path: str) -> dict | None:
        try:
            status, _, text = self._request("GET", path)
            if not 200 <= status < 300:
                return None
            parsed = json.loads(text)
            return parsed if isinstance(parsed, dict) else None
        except (OSError, ValueError):
            return None

    def _request(self, method: str, path: str, *, body: str | None = None) -> tuple[int, dict, str]:
        """Returns (status, lowercase headers, response text). Network failures raise OSError."""
        headers = {
            "Accept": "application/json",
            "User-Agent": f"{TOOL_NAME}/{TOOL_VERSION}",
        }
        if self._token:
            headers["Authorization"] = f"Bearer {self._token}"
        if body is not None:
            headers["Content-Type"] = "application/json; charset=utf-8"

        request = urllib.request.Request(
            f"{self._base}/{path}",
            data=body.encode("utf-8") if body is not None else None,
            headers=headers,
            method=method,
        )
        try:
            with urllib.request.urlopen(request, timeout=self._timeout) as response:
                return (
                    response.status,
                    {key.lower(): value for key, value in response.headers.items()},
                    response.read().decode("utf-8", errors="replace"),
                )
        except urllib.error.HTTPError as error:
            # Non-2xx is a *response*, not a transport failure — return it for status mapping.
            with error:
                return (
                    error.code,
                    {key.lower(): value for key, value in error.headers.items()},
                    error.read().decode("utf-8", errors="replace"),
                )


def _parse_retry_after(value: str | None) -> int | None:
    if value is None:
        return None
    try:
        return int(value)
    except ValueError:
        return None  # HTTP-date form not needed for this prototype
