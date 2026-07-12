"""HTTP client for a Shelfbound server (stdlib urllib only).

The live endpoints mirror the C# ShelfboundClient (src/Shelfbound.Client/):

- ``POST {server}/ingest``            Bearer device token, compact hosted-projection JSON.
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

import ipaddress
import json
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass

from . import TOOL_NAME, TOOL_VERSION
from .hosted_projection import HostedUpload, prepare_hosted_upload

# UploadOutcome.status values (mirror the C# UploadStatus enum, camelCased).
UPLOAD_SUCCESS = "success"
UPLOAD_THROTTLED = "throttled"
UPLOAD_UNAUTHORIZED = "unauthorized"
UPLOAD_FORBIDDEN = "forbidden"
UPLOAD_DEVICE_LIMITED = "deviceLimited"
UPLOAD_INVALID_SNAPSHOT = "invalidSnapshot"
UPLOAD_PAYLOAD_TOO_LARGE = "payloadTooLarge"
UPLOAD_ERROR = "error"

ERROR_NONE = "none"
ERROR_PROJECTION_FAILED = "projectionFailed"
ERROR_NETWORK = "networkError"
ERROR_INVALID_RESPONSE = "invalidResponse"
ERROR_UNAUTHORIZED = "unauthorized"
ERROR_INSUFFICIENT_SCOPE = "insufficientScope"
ERROR_DEVICE_MISMATCH = "deviceMismatch"
ERROR_FORBIDDEN = "forbidden"
ERROR_DEVICE_LIMIT = "deviceLimitReached"
ERROR_INVALID_SNAPSHOT = "invalidSnapshot"
ERROR_PAYLOAD_TOO_LARGE = "payloadTooLarge"
ERROR_TOO_SOON = "tooSoon"
ERROR_SERVER = "serverError"


class PairingUnavailableError(Exception):
    """The server does not implement the (proposed) pairing endpoints yet."""


class ServerError(Exception):
    def __init__(self, status: int, message: str | None = None) -> None:
        super().__init__(message or f"Server returned {status}.")
        self.status = status


class _SameOriginRedirectHandler(urllib.request.HTTPRedirectHandler):
    """Allows redirects only when the authenticated request origin is unchanged."""

    def redirect_request(self, request, file_pointer, code, message, headers, new_url):
        resolved_url = urllib.parse.urljoin(request.full_url, new_url)
        if _origin(request.full_url) != _origin(resolved_url):
            raise urllib.error.HTTPError(
                request.full_url,
                code,
                "Shelfbound refused a cross-origin or scheme-changing redirect.",
                headers,
                file_pointer,
            )
        return super().redirect_request(
            request, file_pointer, code, message, headers, resolved_url
        )


@dataclass(frozen=True)
class UploadResponseV1:
    """Version 1 of the tolerant success/error body returned by ``POST /ingest``."""

    schema_version: str | None = None
    summary: dict | None = None
    warning: str | None = None
    error: str | None = None
    plan: str | None = None
    retry_after_seconds: int | None = None
    max_devices: int | None = None
    hint: str | None = None


@dataclass(frozen=True)
class UploadOutcome:
    status: str
    error_code: str
    game_count: int = 0
    message: str | None = None
    warning: str | None = None
    retry_after_seconds: int | None = None
    plan: str | None = None
    max_devices: int | None = None
    response: UploadResponseV1 | None = None

    @property
    def ok(self) -> bool:
        return self.status == UPLOAD_SUCCESS


class ShelfboundServer:
    def __init__(self, base_url: str, token: str | None = None, timeout_seconds: float = 15.0) -> None:
        self._base = _validate_base_url(base_url)
        self._token = token
        self._timeout = timeout_seconds
        self._opener = urllib.request.build_opener(_SameOriginRedirectHandler())

    def upload_snapshot(self, snapshot: dict) -> UploadOutcome:
        """Projects a complete local snapshot before POSTing; invalid projection never sends."""
        try:
            upload = prepare_hosted_upload(snapshot)
        except (TypeError, ValueError) as error:
            return UploadOutcome(
                status=UPLOAD_ERROR,
                error_code=ERROR_PROJECTION_FAILED,
                message=f"Could not create the privacy-minimized upload: {error}",
            )
        return self.upload_prepared(upload)

    def upload_prepared(self, upload: HostedUpload) -> UploadOutcome:
        """POSTs the exact canonical body held by a previously previewed upload."""
        try:
            status, headers, text = self._request("POST", "ingest", body=upload.body)
        except OSError as error:
            return UploadOutcome(
                status=UPLOAD_ERROR,
                error_code=ERROR_NETWORK,
                message=str(error),
            )

        response = _parse_response(text)

        if 200 <= status < 300:
            if response is None or not response.schema_version:
                return UploadOutcome(
                    status=UPLOAD_ERROR,
                    error_code=ERROR_INVALID_RESPONSE,
                    message="The server returned an invalid upload response.",
                )
            return UploadOutcome(
                status=UPLOAD_SUCCESS,
                error_code=ERROR_NONE,
                game_count=upload.game_count,
                warning=response.warning,
                response=response,
            )
        if status == 429:
            body_retry_after = response.retry_after_seconds if response else None
            return UploadOutcome(
                status=UPLOAD_THROTTLED,
                error_code=ERROR_TOO_SOON,
                message=_response_message(response, "Upload rejected: too soon for your plan."),
                retry_after_seconds=body_retry_after
                if body_retry_after is not None
                else _parse_retry_after(headers.get("retry-after")),
                plan=response.plan if response else None,
                response=response,
            )
        if status == 401:
            return UploadOutcome(
                status=UPLOAD_UNAUTHORIZED,
                error_code=ERROR_UNAUTHORIZED,
                message=_response_message(response, "Invalid or missing API token."),
                response=response,
            )
        if status == 403:
            error = response.error if response else None
            error_code = ERROR_FORBIDDEN
            if error and "cannot upload" in error.casefold():
                error_code = ERROR_INSUFFICIENT_SCOPE
            elif error and "bound to a different" in error.casefold():
                error_code = ERROR_DEVICE_MISMATCH
            return UploadOutcome(
                status=UPLOAD_FORBIDDEN,
                error_code=error_code,
                message=_response_message(
                    response, "This token is not allowed to upload this device snapshot."
                ),
                response=response,
            )
        if status == 409:
            return UploadOutcome(
                status=UPLOAD_DEVICE_LIMITED,
                error_code=ERROR_DEVICE_LIMIT,
                message=_response_message(response, "Device limit reached for your plan."),
                plan=response.plan if response else None,
                max_devices=response.max_devices if response else None,
                response=response,
            )
        if status == 400:
            return UploadOutcome(
                status=UPLOAD_INVALID_SNAPSHOT,
                error_code=ERROR_INVALID_SNAPSHOT,
                message=_response_message(
                    response, "The server rejected the snapshot schema or payload."
                ),
                response=response,
            )
        if status == 413:
            return UploadOutcome(
                status=UPLOAD_PAYLOAD_TOO_LARGE,
                error_code=ERROR_PAYLOAD_TOO_LARGE,
                message=_response_message(
                    response, "The hosted upload exceeds the server's payload-size limit."
                ),
                response=response,
            )
        return UploadOutcome(
            status=UPLOAD_ERROR,
            error_code=ERROR_SERVER,
            message=_response_message(response, f"Server returned {status}."),
            response=response,
        )

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
            with self._opener.open(request, timeout=self._timeout) as response:
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


def _validate_base_url(base_url: str) -> str:
    if not isinstance(base_url, str) or not base_url.strip():
        raise ValueError("Shelfbound server URL is required.")
    parsed = urllib.parse.urlsplit(base_url.strip())
    if parsed.scheme not in ("http", "https") or not parsed.hostname:
        raise ValueError("Shelfbound server URL must be an absolute HTTP(S) URL.")
    if parsed.username is not None or parsed.password is not None:
        raise ValueError("Shelfbound server URL must not contain credentials.")
    if parsed.query or parsed.fragment:
        raise ValueError("Shelfbound server URL must not contain a query or fragment.")
    try:
        parsed.port
    except ValueError as error:
        raise ValueError("Shelfbound server URL has an invalid port.") from error

    if parsed.scheme == "http" and not _is_literal_loopback(parsed.hostname):
        raise ValueError("Shelfbound server URL must use HTTPS except for a literal loopback address.")
    return urllib.parse.urlunsplit(parsed).rstrip("/")


def _is_literal_loopback(host: str) -> bool:
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


def _origin(url: str) -> tuple[str, str, int]:
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme not in ("http", "https") or not parsed.hostname:
        raise ValueError("Redirect target must be an absolute HTTP(S) URL.")
    try:
        port = parsed.port
    except ValueError as error:
        raise ValueError("Redirect target has an invalid port.") from error
    return parsed.scheme, parsed.hostname.casefold(), port or (443 if parsed.scheme == "https" else 80)


def _parse_retry_after(value: str | None) -> int | None:
    if value is None:
        return None
    try:
        return int(value)
    except ValueError:
        return None  # HTTP-date form not needed for this prototype


def _parse_response(text: str) -> UploadResponseV1 | None:
    if not text.strip():
        return None
    try:
        parsed = json.loads(text)
    except ValueError:
        return None
    if not isinstance(parsed, dict):
        return None
    summary = parsed.get("summary")
    return UploadResponseV1(
        schema_version=_optional_text(parsed.get("schemaVersion")),
        summary=summary if isinstance(summary, dict) else None,
        warning=_optional_text(parsed.get("warning")),
        error=_optional_text(parsed.get("error")),
        plan=_optional_text(parsed.get("plan")),
        retry_after_seconds=_optional_int(parsed.get("retryAfterSeconds")),
        max_devices=_optional_int(parsed.get("maxDevices")),
        hint=_optional_text(parsed.get("hint")),
    )


def _response_message(response: UploadResponseV1 | None, fallback: str) -> str:
    if response is None:
        return fallback
    error = response.error
    hint = response.hint
    if error and hint:
        return f"{error} {hint}"
    return error or hint or fallback


def _optional_text(value: object) -> str | None:
    return value if isinstance(value, str) and value.strip() else None


def _optional_int(value: object) -> int | None:
    return value if isinstance(value, int) and not isinstance(value, bool) else None
