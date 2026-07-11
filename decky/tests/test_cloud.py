"""Exercises the HTTP client against a real local server (stdlib http.server),
covering the exact status mapping the C# ShelfboundClient uses."""

import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest

from shelfbound_decky.cloud import PairingUnavailableError, ShelfboundServer, UploadResponseV1
from shelfbound_decky.hosted_projection import prepare_hosted_upload

SNAPSHOT = {
    "schemaVersion": "0.5.0",
    "snapshotId": "55555555-5555-5555-5555-555555555555",
    "createdAt": "2026-07-11T12:00:00+00:00",
    "source": {"tool": "test", "toolVersion": "1.0.0", "platform": "linux"},
    "device": {
        "id": "66666666-6666-6666-6666-666666666666",
        "name": "Test device",
        "type": "steamDeck",
        "os": "linux",
    },
    "steamAccounts": [
        {
            "steamId64": "76561198000000001",
            "accountName": "synthetic-login",
            "personaName": "Synthetic Persona",
            "mostRecent": True,
        }
    ],
    "libraries": [],
    "games": [
        {"appId": 1, "name": "One", "installed": True, "categories": []},
        {"appId": 2, "name": "Two", "installed": False, "categories": []},
    ],
    "categories": [],
    "stats": {
        "libraryCount": 0,
        "installedGameCount": 1,
        "totalSizeOnDiskBytes": 0,
        "scope": "fullLibrary",
    },
}


class _Handler(BaseHTTPRequestHandler):
    # Configured per-test via class attributes.
    ingest_status = 200
    ingest_body = {"schemaVersion": "0.5.0", "summary": {}, "warning": None}
    retry_after = None
    seen: list[dict] = []

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length).decode("utf-8") if length else ""
        type(self).seen.append({
            "path": self.path,
            "authorization": self.headers.get("Authorization"),
            "body": body,
        })

        if self.path == "/ingest":
            self.send_response(type(self).ingest_status)
            if type(self).retry_after is not None:
                self.send_header("Retry-After", str(type(self).retry_after))
            payload = type(self).ingest_body
            encoded = (
                json.dumps(payload).encode()
                if isinstance(payload, dict)
                else str(payload or "").encode()
            )
            if encoded:
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(encoded)))
            self.end_headers()
            if encoded:
                self.wfile.write(encoded)
        elif self.path.startswith("/devices/pair"):
            self.send_response(404)  # pairing endpoints are a proposal — today's server has none
            self.end_headers()
        else:
            self.send_response(404)
            self.end_headers()

    def do_GET(self):
        if self.path == "/auth/me":
            payload = json.dumps({"accountId": "acc-1", "displayName": "Wolf"}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(payload)
        else:
            self.send_response(404)
            self.end_headers()

    def log_message(self, *_args):  # keep test output clean
        pass


@pytest.fixture()
def server():
    _Handler.ingest_status = 200
    _Handler.ingest_body = {"schemaVersion": "0.5.0", "summary": {}, "warning": None}
    _Handler.retry_after = None
    _Handler.seen = []
    httpd = HTTPServer(("127.0.0.1", 0), _Handler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{httpd.server_address[1]}"
    httpd.shutdown()
    thread.join(timeout=5)


def test_upload_success_counts_games_and_sends_bearer(server):
    _Handler.ingest_body = {
        "schemaVersion": "0.5.0",
        "summary": {},
        "warning": "Switching devices deleted the previous Free snapshot.",
    }
    outcome = ShelfboundServer(server, token="tok-123").upload_snapshot(SNAPSHOT)

    assert outcome.ok
    assert outcome.status == "success"
    assert outcome.error_code == "none"
    assert outcome.game_count == 2
    assert outcome.warning == "Switching devices deleted the previous Free snapshot."
    assert isinstance(outcome.response, UploadResponseV1)
    assert outcome.response.schema_version == "0.5.0"
    request = _Handler.seen[0]
    assert request["authorization"] == "Bearer tok-123"
    assert request["body"] == prepare_hosted_upload(SNAPSHOT).body
    assert "steamAccounts" not in request["body"]
    assert "synthetic-login" not in request["body"]
    assert ": " not in request["body"]  # compact separators, like the C# client


def test_upload_throttled_maps_retry_after(server):
    _Handler.ingest_status = 429
    _Handler.retry_after = 60
    _Handler.ingest_body = {
        "error": "Upload rejected: too soon for your plan.",
        "plan": "Free",
        "retryAfterSeconds": 42,
        "hint": "Wait before retrying.",
    }

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "throttled"
    assert outcome.error_code == "tooSoon"
    assert outcome.retry_after_seconds == 42
    assert outcome.plan == "Free"
    assert "Wait before retrying." in outcome.message


def test_upload_unauthorized(server):
    _Handler.ingest_status = 401
    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)
    assert outcome.status == "unauthorized"
    assert outcome.error_code == "unauthorized"
    assert outcome.message == "Invalid or missing API token."


def test_upload_forbidden_scope_is_distinct(server):
    _Handler.ingest_status = 403
    _Handler.ingest_body = {"error": "This API token cannot upload snapshots."}

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "forbidden"
    assert outcome.error_code == "insufficientScope"


def test_upload_device_mismatch_is_distinct(server):
    _Handler.ingest_status = 403
    _Handler.ingest_body = {
        "error": "This device token is bound to a different snapshot device."
    }

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "forbidden"
    assert outcome.error_code == "deviceMismatch"


def test_upload_device_limit_surfaces_plan_cap_and_hint(server):
    _Handler.ingest_status = 409
    _Handler.ingest_body = {
        "error": "Device limit reached for your plan.",
        "plan": "Pro",
        "maxDevices": 3,
        "hint": "Remove a device.",
    }

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "deviceLimited"
    assert outcome.error_code == "deviceLimitReached"
    assert outcome.plan == "Pro"
    assert outcome.max_devices == 3
    assert "Remove a device." in outcome.message


def test_upload_invalid_snapshot_is_distinct(server):
    _Handler.ingest_status = 400
    _Handler.ingest_body = {"error": "Invalid snapshot."}

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "invalidSnapshot"
    assert outcome.error_code == "invalidSnapshot"
    assert outcome.message == "Invalid snapshot."


def test_upload_payload_too_large_is_distinct(server):
    _Handler.ingest_status = 413
    _Handler.ingest_body = None

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "payloadTooLarge"
    assert outcome.error_code == "payloadTooLarge"
    assert "payload-size limit" in outcome.message


def test_upload_other_status_is_error(server):
    _Handler.ingest_status = 500
    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)
    assert outcome.status == "error"
    assert outcome.error_code == "serverError"
    assert outcome.message == "Server returned 500."


def test_upload_unreachable_server_is_error_not_exception():
    outcome = ShelfboundServer("http://127.0.0.1:9", token="t", timeout_seconds=0.5).upload_snapshot(SNAPSHOT)
    assert outcome.status == "error"
    assert outcome.error_code == "networkError"
    assert outcome.message


def test_upload_projection_failure_sends_no_request(server):
    invalid = dict(SNAPSHOT)
    invalid.pop("device")

    outcome = ShelfboundServer(server, token="t").upload_snapshot(invalid)

    assert outcome.status == "error"
    assert outcome.error_code == "projectionFailed"
    assert _Handler.seen == []


def test_get_account(server):
    account = ShelfboundServer(server, token="t").get_account()
    assert account == {"accountId": "acc-1", "displayName": "Wolf"}


def test_pairing_reports_unavailable_on_todays_server(server):
    client = ShelfboundServer(server)
    with pytest.raises(PairingUnavailableError):
        client.pairing_start("testdeck", "steamDeck", "device-1")
    with pytest.raises(PairingUnavailableError):
        client.pairing_poll("poll-token")
