import os

import pytest

from shelfbound_decky.settings import PluginSettings, SettingsStore, TokenStore


@pytest.mark.skipif(os.name == "nt", reason="Unix file modes are enforced on SteamOS")
def test_secret_and_personal_files_are_owner_only(tmp_path):
    SettingsStore(str(tmp_path)).save(PluginSettings(device_name="Test Deck"))
    TokenStore(str(tmp_path)).save("synthetic-token")

    assert (tmp_path / "settings.json").stat().st_mode & 0o777 == 0o600
    assert (tmp_path / "token").stat().st_mode & 0o777 == 0o600
