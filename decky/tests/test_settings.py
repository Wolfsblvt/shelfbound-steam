import os

import pytest

from shelfbound_decky.settings import PluginSettings, SettingsStore, TokenStore


@pytest.mark.skipif(os.name == "nt", reason="Unix file modes are enforced on SteamOS")
def test_secret_and_personal_files_are_owner_only(tmp_path):
    SettingsStore(str(tmp_path)).save(PluginSettings(device_name="Test Deck"))
    TokenStore(str(tmp_path)).save("synthetic-token")

    assert (tmp_path / "settings.json").stat().st_mode & 0o777 == 0o600
    assert (tmp_path / "token").stat().st_mode & 0o777 == 0o600


def test_private_game_setting_defaults_off_and_round_trips_bounded_overrides(tmp_path):
    store = SettingsStore(str(tmp_path))
    assert store.load().exclude_steam_private_games is False

    store.save(
        PluginSettings(
            exclude_steam_private_games=True,
            private_game_unskip_app_ids={40, -1, 20},
        )
    )

    restored = store.load()
    assert restored.exclude_steam_private_games is True
    assert restored.private_game_unskip_app_ids == {20, 40}

    (tmp_path / "settings.json").write_text("{ not-json", encoding="utf-8")
    corrupt = store.load()
    assert corrupt.exclude_steam_private_games is False
    assert not corrupt.private_game_unskip_app_ids

    (tmp_path / "settings.json").write_text("[]", encoding="utf-8")
    wrong_shape = store.load()
    assert wrong_shape.exclude_steam_private_games is False
    assert not wrong_shape.private_game_unskip_app_ids
