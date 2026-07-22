import asyncio
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from shelfbound_decky.cloud import PairingUnavailableError
from shelfbound_decky.snapshot import ScanOutput

STORED_TOKEN = "stored-token-that-must-stay-backend-side"
CLAIMED_TOKEN = "claimed-token-that-must-stay-backend-side"
ACCOUNT = {"accountId": "acc-1", "displayName": "Wolf"}
DEVICE = {"id": "device-1", "name": "testdeck", "type": "steamDeck", "os": "linux"}
PRIVATE_FIXTURE_ROOT = Path(__file__).resolve().parents[2] / "tests" / "Fixtures"
SNAPSHOT = {
    "schemaVersion": "0.6.0",
    "snapshotId": "77777777-7777-7777-7777-777777777777",
    "createdAt": "2026-07-11T12:00:00+00:00",
    "source": {"tool": "shelfbound-decky", "toolVersion": "0.1.0-test", "platform": "linux"},
    "device": DEVICE,
    "steamAccounts": [
        {
            "steamId64": "76561198000000001",
            "accountName": "synthetic-login",
            "personaName": "Synthetic Persona",
            "mostRecent": True,
        }
    ],
    "libraries": [],
    "games": [],
    "categories": [],
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
    uploaded_bodies: list[str] = []

    def __init__(self, _base_url, token=None, timeout_seconds=15.0):
        self.token = token
        self.timeout_seconds = timeout_seconds

    def get_account(self):
        return ACCOUNT

    def upload_prepared(self, upload):
        type(self).uploaded_bodies.append(upload.body)
        return SimpleNamespace(
            ok=True,
            status="success",
            message=None,
            warning="Synthetic switch warning.",
            error_code="none",
            game_count=0,
            retry_after_seconds=None,
            plan=None,
            max_devices=None,
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
    FakeShelfboundServer.uploaded_bodies = []
    monkeypatch.setattr(decky_plugin_module, "ShelfboundServer", FakeShelfboundServer)
    return FakeShelfboundServer


def assert_tokens_absent(payload, *tokens):
    dumped = json.dumps(payload, sort_keys=True)
    for token in tokens:
        assert token not in dumped


def test_get_settings_returns_success_envelope(plugin):
    response = asyncio.run(plugin.get_settings())

    assert response["ok"] is True
    assert response["serverUrl"] == "http://127.0.0.1:5080"
    assert response["deviceName"] is None
    assert response["excludeSteamPrivateGames"] is False


def test_exception_paths_return_error_envelope(plugin):
    def fail_load():
        raise RuntimeError("settings exploded")

    plugin._settings_store.load = fail_load

    response = asyncio.run(plugin.get_settings())

    assert response == {"ok": False, "error": "settings exploded"}


def test_frontend_callable_methods_never_return_device_tokens(plugin, fake_server):
    plugin._token_store.save(STORED_TOKEN)
    fake_server.pairing_poll_results = [{"status": "claimed", "token": CLAIMED_TOKEN}]

    preview = asyncio.run(plugin.get_privacy_preview())
    responses = [
        asyncio.run(plugin.get_status()),
        asyncio.run(plugin.get_storage_overview()),
        preview,
        asyncio.run(plugin.sync_now(preview["uploadId"])),
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


def test_preview_body_is_the_exact_one_time_body_uploaded(plugin, fake_server):
    plugin._token_store.save(STORED_TOKEN)

    preview = asyncio.run(plugin.get_privacy_preview())
    synced = asyncio.run(plugin.sync_now(preview["uploadId"]))
    reused = asyncio.run(plugin.sync_now(preview["uploadId"]))

    assert preview["ok"] is True
    assert "steamAccounts" not in preview["snapshotJson"]
    assert fake_server.uploaded_bodies == [preview["snapshotJson"]]
    assert synced["ok"] is True
    assert synced["warning"] == "Synthetic switch warning."
    assert reused == {
        "ok": False,
        "status": "previewRequired",
        "error": "Preview this upload again before syncing.",
    }


def test_private_preview_unskip_persists_locally_and_updates_the_exact_one_time_body(
    decky_plugin_module, plugin, fake_server, monkeypatch
):
    local = json.loads(
        (PRIVATE_FIXTURE_ROOT / "private-game-exclusion.input.json").read_text(encoding="utf-8")
    )
    settings = plugin._settings_store.load()
    settings.exclude_steam_private_games = True
    plugin._settings_store.save(settings)
    monkeypatch.setattr(
        plugin,
        "_scan",
        lambda: ScanOutput(local, ["synthetic scan warning"], {}, "fixture-root", []),
    )
    monkeypatch.setattr(
        decky_plugin_module,
        "read_private_apps_evidence",
        lambda _root, _accounts: SimpleNamespace(
            private_app_ids=frozenset({20}),
            describe=lambda: "Synthetic positive local cache evidence.",
        ),
    )
    plugin._token_store.save(STORED_TOKEN)

    preview = asyncio.run(plugin.get_privacy_preview())
    updated = asyncio.run(plugin.unskip_private_game(preview["uploadId"], 20))
    synced = asyncio.run(plugin.sync_now(updated["uploadId"]))

    assert preview["privateGameExclusion"] == {
        "enabled": True,
        "status": (
            "Synthetic positive local cache evidence. 1 matching game(s) will be omitted from "
            "this hosted body."
        ),
        "skippedGames": [{"appId": 20, "name": "Skipped Beta"}],
    }
    assert '"appId":20' not in preview["snapshotJson"]
    assert updated["uploadId"] != preview["uploadId"]
    assert updated["privateGameExclusion"]["skippedGames"] == []
    assert '"appId":20' in updated["snapshotJson"]
    assert plugin._settings_store.load().private_game_unskip_app_ids == {20}
    assert fake_server.uploaded_bodies == [updated["snapshotJson"]]
    assert synced["ok"] is True


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
