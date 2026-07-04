import asyncio
import json
from types import SimpleNamespace

import pytest

from shelfbound_decky.cloud import PairingUnavailableError
from shelfbound_decky.snapshot import ScanOutput

STORED_TOKEN = "stored-token-that-must-stay-backend-side"
CLAIMED_TOKEN = "claimed-token-that-must-stay-backend-side"
ACCOUNT = {"accountId": "acc-1", "displayName": "Wolf"}
DEVICE = {"id": "device-1", "name": "testdeck", "type": "steamDeck", "os": "linux"}
SNAPSHOT = {
    "schemaVersion": "0.5.0",
    "source": {"tool": "shelfbound-decky", "toolVersion": "0.1.0-test", "platform": "linux"},
    "libraries": [],
    "games": [],
    "stats": {"libraryCount": 0, "installedGameCount": 0, "totalSizeOnDiskBytes": 0, "scope": "installedOnly"},
}


@pytest.fixture()
def plugin(decky_plugin_module, monkeypatch):
    module = decky_plugin_module
    monkeypatch.setattr(module.locator, "locate", lambda: "/home/deck/.local/share/Steam")
    monkeypatch.setattr(module.device_identity, "resolve_device", lambda _name, _type: DEVICE)
    monkeypatch.setattr(module.Plugin, "_scan", lambda _self: ScanOutput(SNAPSHOT, ["test warning"], {}))
    return module.Plugin()


class FakeShelfboundServer:
    pairing_start_result = {
        "code": "PAIR-123",
        "claimUrl": "https://shelfbound.test/claim/PAIR-123",
        "pollToken": "poll-token-1",
        "expiresInSeconds": 600,
    }
    pairing_poll_results: list[dict] = []

    def __init__(self, _base_url, token=None, timeout_seconds=15.0):
        self.token = token
        self.timeout_seconds = timeout_seconds

    def get_account(self):
        return ACCOUNT

    def upload_snapshot(self, _snapshot):
        return SimpleNamespace(
            ok=True,
            status="success",
            message=None,
            game_count=0,
            retry_after_seconds=None,
        )

    def pairing_start(self, _device_name, _device_type, _device_id):
        return dict(type(self).pairing_start_result)

    def pairing_poll(self, _poll_token):
        return type(self).pairing_poll_results.pop(0)


class PairingUnavailableServer(FakeShelfboundServer):
    def pairing_start(self, _device_name, _device_type, _device_id):
        raise PairingUnavailableError("This server doesn't support device pairing yet.")


@pytest.fixture()
def fake_server(decky_plugin_module, monkeypatch):
    FakeShelfboundServer.pairing_start_result = {
        "code": "PAIR-123",
        "claimUrl": "https://shelfbound.test/claim/PAIR-123",
        "pollToken": "poll-token-1",
        "expiresInSeconds": 600,
    }
    FakeShelfboundServer.pairing_poll_results = []
    monkeypatch.setattr(decky_plugin_module, "ShelfboundServer", FakeShelfboundServer)
    return FakeShelfboundServer


def assert_tokens_absent(payload, *tokens):
    dumped = json.dumps(payload, sort_keys=True)
    for token in tokens:
        assert token not in dumped


def test_get_settings_returns_success_envelope(plugin):
    response = asyncio.run(plugin.get_settings())

    assert response["ok"] is True
    assert response["serverUrl"] == "http://localhost:5080"
    assert response["deviceName"] is None


def test_exception_paths_return_error_envelope(plugin):
    def fail_load():
        raise RuntimeError("settings exploded")

    plugin._settings_store.load = fail_load

    response = asyncio.run(plugin.get_settings())

    assert response == {"ok": False, "error": "settings exploded"}


def test_frontend_callable_methods_never_return_device_tokens(plugin, fake_server):
    plugin._token_store.save(STORED_TOKEN)
    fake_server.pairing_poll_results = [{"status": "claimed", "token": CLAIMED_TOKEN}]

    responses = [
        asyncio.run(plugin.get_status()),
        asyncio.run(plugin.get_storage_overview()),
        asyncio.run(plugin.get_privacy_preview()),
        asyncio.run(plugin.sync_now()),
        asyncio.run(plugin.get_settings()),
        asyncio.run(plugin.update_settings(server_url=" https://shelfbound.test ", device_name=" Deck ")),
        asyncio.run(plugin.pairing_cancel()),
        asyncio.run(plugin.pairing_poll()),
        asyncio.run(plugin.pairing_start()),
        asyncio.run(plugin.pairing_poll()),
        asyncio.run(plugin.disconnect()),
    ]

    for response in responses:
        assert_tokens_absent(response, STORED_TOKEN, CLAIMED_TOKEN)
    assert plugin._token_store.load() is None


def test_pairing_claimed_flow_persists_token_and_clears_session(plugin, fake_server):
    fake_server.pairing_poll_results = [
        {"status": "pending"},
        {"status": "claimed", "token": CLAIMED_TOKEN},
    ]

    unpaired = asyncio.run(plugin.pairing_poll())
    started = asyncio.run(plugin.pairing_start())
    assert plugin._pairing_session["pollToken"] == "poll-token-1"

    pending = asyncio.run(plugin.pairing_poll())
    assert plugin._pairing_session["pollToken"] == "poll-token-1"

    claimed = asyncio.run(plugin.pairing_poll())

    assert unpaired == {"ok": False, "error": "No pairing in progress."}
    assert started == {
        "ok": True,
        "code": "PAIR-123",
        "claimUrl": "https://shelfbound.test/claim/PAIR-123",
        "expiresInSeconds": 600,
    }
    assert pending == {"ok": True, "status": "pending"}
    assert claimed == {"ok": True, "status": "claimed", "account": ACCOUNT}
    assert plugin._pairing_session is None
    assert plugin._token_store.load() == CLAIMED_TOKEN
    assert_tokens_absent(claimed, CLAIMED_TOKEN)


def test_pairing_denied_clears_session_without_storing_token(plugin, fake_server):
    fake_server.pairing_poll_results = [{"status": "denied"}]

    started = asyncio.run(plugin.pairing_start())
    denied = asyncio.run(plugin.pairing_poll())

    assert started["ok"] is True
    assert denied == {"ok": True, "status": "denied"}
    assert plugin._pairing_session is None
    assert plugin._token_store.load() is None


def test_pairing_start_reports_unavailable_server(decky_plugin_module, plugin, monkeypatch):
    monkeypatch.setattr(decky_plugin_module, "ShelfboundServer", PairingUnavailableServer)

    response = asyncio.run(plugin.pairing_start())

    assert response["ok"] is False
    assert response["pairingUnavailable"] is True
    assert "doesn't support device pairing" in response["error"]
    assert plugin._pairing_session is None
