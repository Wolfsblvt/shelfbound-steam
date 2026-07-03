"""Exercises the HTTP client against a real local server (stdlib http.server),
covering the exact status mapping the C# ShelfboundClient uses."""

import json
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer

import pytest

from shelfbound_decky.cloud import PairingUnavailableError, ShelfboundServer

SNAPSHOT = {"schemaVersion": "0.5.0", "games": [{"appId": 1}, {"appId": 2}]}


class _Handler(BaseHTTPRequestHandler):
    # Configured per-test via class attributes.
    ingest_status = 200
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
            self.end_headers()
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
    _Handler.retry_after = None
    _Handler.seen = []
    httpd = HTTPServer(("127.0.0.1", 0), _Handler)
    thread = threading.Thread(target=httpd.serve_forever, daemon=True)
    thread.start()
    yield f"http://127.0.0.1:{httpd.server_address[1]}"
    httpd.shutdown()
    thread.join(timeout=5)


def test_upload_success_counts_games_and_sends_bearer(server):
    outcome = ShelfboundServer(server, token="tok-123").upload_snapshot(SNAPSHOT)

    assert outcome.ok
    assert outcome.status == "success"
    assert outcome.game_count == 2
    request = _Handler.seen[0]
    assert request["authorization"] == "Bearer tok-123"
    assert json.loads(request["body"]) == SNAPSHOT
    assert ": " not in request["body"]  # compact separators, like the C# client


def test_upload_throttled_maps_retry_after(server):
    _Handler.ingest_status = 429
    _Handler.retry_after = 60

    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)

    assert outcome.status == "throttled"
    assert outcome.retry_after_seconds == 60
    assert "Pro/Lifetime" in outcome.message


def test_upload_unauthorized(server):
    _Handler.ingest_status = 401
    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)
    assert outcome.status == "unauthorized"
    assert outcome.message == "Invalid or missing API token."


def test_upload_other_status_is_error(server):
    _Handler.ingest_status = 500
    outcome = ShelfboundServer(server, token="t").upload_snapshot(SNAPSHOT)
    assert outcome.status == "error"
    assert outcome.message == "Server returned 500."


def test_upload_unreachable_server_is_error_not_exception():
    outcome = ShelfboundServer("http://127.0.0.1:9", token="t", timeout_seconds=0.5).upload_snapshot(SNAPSHOT)
    assert outcome.status == "error"
    assert outcome.message


def test_get_account(server):
    account = ShelfboundServer(server, token="t").get_account()
    assert account == {"accountId": "acc-1", "displayName": "Wolf"}


def test_pairing_reports_unavailable_on_todays_server(server):
    client = ShelfboundServer(server)
    with pytest.raises(PairingUnavailableError):
        client.pairing_start("testdeck", "steamDeck", "device-1")
    with pytest.raises(PairingUnavailableError):
        client.pairing_poll("poll-token")
