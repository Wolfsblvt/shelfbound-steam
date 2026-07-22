"""Shelfbound Decky plugin backend — thin controller over py_modules/shelfbound_decky.

Every method returns an envelope dict ({"ok": true, ...} / {"ok": false, "error"})
rather than raising across the bridge. Filesystem/network work runs in a worker
thread so the loader's event loop never blocks. The device API token never crosses
the bridge to the frontend — it lives backend-side only.

[NEEDS-DECK] Everything below runs "in theory": the bridge call conventions, settings
dir, and event loop behaviour follow the current decky-loader docs/template but have
not been exercised on real hardware. See the README's validation list.
"""

import asyncio
import os
import sys
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timezone

# The loader puts py_modules on sys.path; insert defensively so local smoke tests work too.
_PY_MODULES = os.path.join(os.path.dirname(os.path.realpath(__file__)), "py_modules")
if _PY_MODULES not in sys.path:
    sys.path.insert(0, _PY_MODULES)

import decky  # noqa: E402 — injected by Decky Loader at runtime
from shelfbound_decky import SCHEMA_VERSION, TOOL_NAME, TOOL_VERSION  # noqa: E402
from shelfbound_decky import device_identity, locator, overview, privacy  # noqa: E402
from shelfbound_decky.cloud import PairingUnavailableError, ShelfboundServer  # noqa: E402
from shelfbound_decky.hosted_projection import HostedUpload, prepare_hosted_upload  # noqa: E402
from shelfbound_decky.private_apps import read_private_apps_evidence  # noqa: E402
from shelfbound_decky.settings import SettingsStore, TokenStore  # noqa: E402
from shelfbound_decky.snapshot import ScanOutput, build_snapshot  # noqa: E402

# Short timeout for the opportunistic account lookup in get_status so an unreachable
# server can't make the panel feel dead.
_STATUS_TIMEOUT_SECONDS = 4.0


@dataclass(frozen=True)
class _PendingUpload:
    upload_id: str
    upload: HostedUpload
    warnings: list[str]
    snapshot: dict = field(repr=False)
    positive_private_app_ids: frozenset[int] = field(repr=False)
    evidence_message: str
    private_game_enabled: bool


class Plugin:
    def __init__(self) -> None:
        self._settings_store = SettingsStore(decky.DECKY_PLUGIN_SETTINGS_DIR)
        self._token_store = TokenStore(decky.DECKY_PLUGIN_SETTINGS_DIR)
        self._pairing_session: dict | None = None
        self._pending_upload: _PendingUpload | None = None

    # ---- panel data ------------------------------------------------------------

    async def get_status(self) -> dict:
        try:
            settings = self._settings_store.load()
            steam_root = await asyncio.to_thread(locator.locate)
            device = await asyncio.to_thread(
                device_identity.resolve_device, settings.device_name, None
            )

            token = self._token_store.load()
            account = None
            if token:
                server = ShelfboundServer(
                    settings.server_url, token, timeout_seconds=_STATUS_TIMEOUT_SECONDS
                )
                account = await asyncio.to_thread(server.get_account)

            return {
                "ok": True,
                "plugin": {
                    "toolName": TOOL_NAME,
                    "version": self._tool_version(),
                    "schemaVersion": SCHEMA_VERSION,
                },
                # steam.root is shown on-device for trust/debug; it is never uploaded.
                "steam": {"found": steam_root is not None, "root": steam_root},
                "device": device,
                "connection": {
                    "connected": token is not None,
                    "serverUrl": settings.server_url,
                    "account": account,
                },
                "lastSync": settings.last_sync,
                "pairingInProgress": self._pairing_session is not None,
                "privateGameExclusion": {
                    "enabled": settings.exclude_steam_private_games,
                    "status": (
                        "Enabled — checked at preview time; missing or uncertain cache data fails open."
                        if settings.exclude_steam_private_games
                        else "Off"
                    ),
                },
            }
        except Exception as error:  # noqa: BLE001
            return self._fail("get_status", error)

    async def get_storage_overview(self) -> dict:
        try:
            # The snapshot already carries per-library storage (kind + free/total); the
            # overview just groups it for display — no second classification pass.
            scan = await asyncio.to_thread(self._scan)
            result = await asyncio.to_thread(overview.build_storage_overview, scan.snapshot)
            return {"ok": True, **result, "warnings": scan.warnings}
        except Exception as error:  # noqa: BLE001
            return self._fail("get_storage_overview", error)

    async def get_privacy_preview(self) -> dict:
        try:
            scan = await asyncio.to_thread(self._scan)
            settings = self._settings_store.load()
            pending = await asyncio.to_thread(self._prepare_upload, scan, settings)
            self._pending_upload = pending
            preview = self._preview_payload(pending)
            return {"ok": True, "uploadId": pending.upload_id, **preview}
        except Exception as error:  # noqa: BLE001
            self._pending_upload = None
            return self._fail("get_privacy_preview", error)

    # ---- sync ------------------------------------------------------------------

    async def sync_now(self, upload_id: str | None = None) -> dict:
        try:
            token = self._token_store.load()
            if not token:
                return {
                    "ok": False,
                    "status": "notConnected",
                    "error": "Not connected — pair this device with your Shelfbound account first.",
                }

            pending = self._pending_upload
            if pending is None or upload_id != pending.upload_id:
                return {
                    "ok": False,
                    "status": "previewRequired",
                    "error": "Preview this upload again before syncing.",
                }

            # One confirmation authorizes one exact body. Consume it before the network call so a
            # retry always requires a fresh preview rather than accidentally double-uploading.
            self._pending_upload = None
            settings = self._settings_store.load()
            server = ShelfboundServer(settings.server_url, token)
            outcome = await asyncio.to_thread(server.upload_prepared, pending.upload)

            synced_at = datetime.now(timezone.utc).isoformat()
            settings.last_sync = {
                "at": synced_at,
                "status": outcome.status,
                "message": outcome.message,
                "warning": outcome.warning,
                "errorCode": outcome.error_code,
                "gameCount": outcome.game_count,
            }
            self._settings_store.save(settings)

            return {
                "ok": outcome.ok,
                "status": outcome.status,
                "message": outcome.message,
                "warning": outcome.warning,
                "errorCode": outcome.error_code,
                "gameCount": outcome.game_count,
                "retryAfterSeconds": outcome.retry_after_seconds,
                "plan": outcome.plan,
                "maxDevices": outcome.max_devices,
                "syncedAt": synced_at,
                "warnings": pending.warnings,
            }
        except Exception as error:  # noqa: BLE001
            return self._fail("sync_now", error)

    async def unskip_private_game(self, upload_id: str, app_id: int) -> dict:
        """Persist one local override and rebuild bytes from the same scan/evidence."""
        try:
            pending = self._pending_upload
            if pending is None or upload_id != pending.upload_id:
                return {
                    "ok": False,
                    "status": "previewRequired",
                    "error": "Preview this upload again before changing skipped games.",
                }
            if not isinstance(app_id, int) or isinstance(app_id, bool) or not any(
                game.app_id == app_id for game in pending.upload.skipped_games
            ):
                return {"ok": False, "error": "That game is not skipped by this preview."}

            settings = self._settings_store.load()
            overrides = set(settings.private_game_unskip_app_ids or set())
            overrides.add(app_id)
            excluded = set(pending.positive_private_app_ids) - overrides
            upload = prepare_hosted_upload(pending.snapshot, excluded)
            settings.private_game_unskip_app_ids = overrides
            self._settings_store.save(settings)

            updated = _PendingUpload(
                upload_id=str(uuid.uuid4()),
                upload=upload,
                warnings=pending.warnings,
                snapshot=pending.snapshot,
                positive_private_app_ids=pending.positive_private_app_ids,
                evidence_message=pending.evidence_message,
                private_game_enabled=pending.private_game_enabled,
            )
            self._pending_upload = updated
            return {"ok": True, "uploadId": updated.upload_id, **self._preview_payload(updated)}
        except Exception as error:  # noqa: BLE001
            return self._fail("unskip_private_game", error)

    # ---- account pairing (PROPOSED server endpoints — see cloud.py) --------------

    async def pairing_start(self) -> dict:
        try:
            settings = self._settings_store.load()
            device = await asyncio.to_thread(
                device_identity.resolve_device, settings.device_name, None
            )
            server = ShelfboundServer(settings.server_url)
            try:
                session = await asyncio.to_thread(
                    server.pairing_start, device["name"], device["type"], device["id"]
                )
            except PairingUnavailableError as error:
                return {"ok": False, "pairingUnavailable": True, "error": str(error)}

            self._pairing_session = session
            return {
                "ok": True,
                "code": session.get("code"),
                "claimUrl": session.get("claimUrl"),
                "expiresInSeconds": session.get("expiresInSeconds"),
            }
        except Exception as error:  # noqa: BLE001
            return self._fail("pairing_start", error)

    async def pairing_poll(self) -> dict:
        try:
            session = self._pairing_session
            if session is None:
                return {"ok": False, "error": "No pairing in progress."}

            settings = self._settings_store.load()
            server = ShelfboundServer(settings.server_url)
            result = await asyncio.to_thread(server.pairing_poll, session.get("pollToken", ""))

            status = result.get("status")
            if status == "claimed" and result.get("token"):
                self._token_store.save(result["token"])
                self._pairing_session = None
                authed = ShelfboundServer(
                    settings.server_url, result["token"], timeout_seconds=_STATUS_TIMEOUT_SECONDS
                )
                account = await asyncio.to_thread(authed.get_account)
                return {"ok": True, "status": "claimed", "account": account}

            if status in ("expired", "denied"):
                self._pairing_session = None
            return {"ok": True, "status": status or "pending"}
        except Exception as error:  # noqa: BLE001
            return self._fail("pairing_poll", error)

    async def pairing_cancel(self) -> dict:
        self._pairing_session = None
        return {"ok": True}

    async def disconnect(self) -> dict:
        # Local-only, mirroring the tray post-M-4: clears the stored token; server-side
        # tokens expire at 90 days or are revoked from the dashboard.
        try:
            self._token_store.clear()
            self._pairing_session = None
            self._pending_upload = None
            return {"ok": True}
        except Exception as error:  # noqa: BLE001
            return self._fail("disconnect", error)

    # ---- settings ----------------------------------------------------------------

    async def get_settings(self) -> dict:
        try:
            settings = self._settings_store.load()
            return {
                "ok": True,
                "serverUrl": settings.server_url,
                "deviceName": settings.device_name,
                "excludeSteamPrivateGames": settings.exclude_steam_private_games,
            }
        except Exception as error:  # noqa: BLE001
            return self._fail("get_settings", error)

    async def update_settings(
        self,
        server_url: str | None = None,
        device_name: str | None = None,
        exclude_steam_private_games: bool | None = None,
    ) -> dict:
        try:
            settings = self._settings_store.load()
            if server_url is not None and server_url.strip():
                settings.server_url = server_url.strip()
            if device_name is not None:
                settings.device_name = device_name.strip() or None
            if isinstance(exclude_steam_private_games, bool):
                settings.exclude_steam_private_games = exclude_steam_private_games
            self._settings_store.save(settings)
            self._pending_upload = None
            return {
                "ok": True,
                "serverUrl": settings.server_url,
                "deviceName": settings.device_name,
                "excludeSteamPrivateGames": settings.exclude_steam_private_games,
            }
        except Exception as error:  # noqa: BLE001
            return self._fail("update_settings", error)

    # ---- lifecycle -----------------------------------------------------------------

    async def _main(self) -> None:
        decky.logger.info(
            "Shelfbound Decky plugin %s loaded (snapshot contract %s).",
            self._tool_version(), SCHEMA_VERSION,
        )

    async def _unload(self) -> None:
        decky.logger.info("Shelfbound Decky plugin unloading.")

    # ---- helpers -------------------------------------------------------------------

    def _scan(self) -> ScanOutput:
        """Blocking scan; always call via asyncio.to_thread."""
        steam_root = locator.locate()
        if steam_root is None:
            raise RuntimeError(
                "Could not find a Steam installation. Set the Steam path or SHELFBOUND_STEAM_PATH."
            )
        settings = self._settings_store.load()
        device = device_identity.resolve_device(settings.device_name, None)
        return build_snapshot(steam_root, device, self._tool_version())

    def _prepare_upload(self, scan: ScanOutput, settings) -> _PendingUpload:
        positive_app_ids: frozenset[int] = frozenset()
        if settings.exclude_steam_private_games and scan.steam_root is not None:
            evidence = read_private_apps_evidence(scan.steam_root, scan.accounts)
            positive_app_ids = evidence.private_app_ids
            evidence_message = evidence.describe()
        elif settings.exclude_steam_private_games:
            evidence_message = (
                "Steam's local Private-game cache was unavailable. No games were omitted."
            )
        else:
            evidence_message = "Private-game exclusion is off."

        overrides = set(settings.private_game_unskip_app_ids or set())
        excluded = set(positive_app_ids) - overrides
        upload = prepare_hosted_upload(scan.snapshot, excluded)
        return _PendingUpload(
            upload_id=str(uuid.uuid4()),
            upload=upload,
            warnings=list(scan.warnings),
            snapshot=scan.snapshot,
            positive_private_app_ids=positive_app_ids,
            evidence_message=evidence_message,
            private_game_enabled=settings.exclude_steam_private_games,
        )

    @staticmethod
    def _preview_payload(pending: _PendingUpload) -> dict:
        status = pending.evidence_message
        if pending.private_game_enabled and pending.upload.skipped_games:
            status += (
                f" {len(pending.upload.skipped_games)} matching game(s) will be omitted from "
                "this hosted body."
            )
        elif pending.private_game_enabled and pending.positive_private_app_ids:
            status += " No matching game will be omitted after device-local overrides."
        return privacy.build_privacy_preview(
            pending.upload,
            pending.warnings,
            private_game_enabled=pending.private_game_enabled,
            private_game_status=status,
        )

    def _tool_version(self) -> str:
        return getattr(decky, "DECKY_PLUGIN_VERSION", None) or TOOL_VERSION

    @staticmethod
    def _fail(operation: str, error: Exception) -> dict:
        decky.logger.exception("%s failed", operation)
        return {"ok": False, "error": str(error)}
