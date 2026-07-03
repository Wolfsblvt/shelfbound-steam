from datetime import datetime, timezone

from shelfbound_decky.steam_files import (
    parse_app_manifest,
    parse_library_folders,
    parse_login_users,
    parse_shared_config,
)

LIBRARY_FOLDERS = '''"libraryfolders"
{
    "1"
    {
        "path"  "/run/media/mmcblk0p1"
        "label" "SD Card"
        "apps" { "1145360" "5" }
    }
    "0"
    {
        "path"  "/home/deck/.local/share/Steam"
        "label" ""
        "apps"
        {
            "228980" "1"
            "753640" "2"
            "notanumber" "3"
        }
    }
    "tools" { "path" "/ignored" }
    "2" { "path" "  " }
}
'''


def test_library_folders_sorted_with_label_fallback():
    folders = parse_library_folders(LIBRARY_FOLDERS)

    assert [f.index for f in folders] == [0, 1]  # "tools" and blank-path entries skipped
    assert folders[0].label == "library-0"  # empty label falls back
    assert folders[0].app_ids == (228980, 753640)  # non-numeric app key skipped
    assert folders[1].label == "SD Card"
    assert folders[1].steamapps_path == "/run/media/mmcblk0p1/steamapps"


def test_app_manifest_fields_and_install_state():
    manifest = parse_app_manifest('''"AppState"
{
    "appid" "753640"
    "name" "Outer Wilds"
    "StateFlags" "4"
    "installdir" "Outer Wilds"
    "SizeOnDisk" "11652599956"
    "LastUpdated" "1746100800"
    "LastPlayed" "0"
}
''')
    assert manifest.app_id == 753640
    assert manifest.is_fully_installed
    assert manifest.size_on_disk == 11652599956
    assert manifest.last_updated == datetime(2025, 5, 1, 12, 0, tzinfo=timezone.utc)
    assert manifest.last_played is None  # 0 / negative timestamps are "unknown"


def test_app_manifest_partial_install_and_fallback_name():
    manifest = parse_app_manifest('"AppState" { "StateFlags" "1026" }')
    assert manifest.app_id == 0
    assert manifest.name == "App 0"
    assert not manifest.is_fully_installed
    assert manifest.install_dir is None
    assert manifest.size_on_disk is None


def test_login_users_order_most_recent_and_account_id():
    accounts = parse_login_users('''"users"
{
    "76561198000000002" { "AccountName" "other" "PersonaName" "Other" "MostRecent" "0" }
    "76561198000000001" { "AccountName" "wolf" "PersonaName" "Wolf" "MostRecent" "1" }
}
''')
    assert [a.steam_id64 for a in accounts] == ["76561198000000002", "76561198000000001"]
    assert accounts[1].most_recent and not accounts[0].most_recent
    assert accounts[1].account_id == 39734273
    assert accounts[0].account_id == 39734274


def test_shared_config_tag_order_and_skips():
    categories = parse_shared_config('''"UserRoamingConfigStore"
{
    "Software" { "Valve" { "Steam" { "apps"
    {
        "10" { "tags" { "2" "C" "0" "A" "1" "B" } }
        "20" { "tags" { "0" "  " } }
        "30" { "tags" { } }
        "40" { "cloudenabled" "1" }
    } } } }
}
''')
    assert categories == {10: ["A", "B", "C"]}  # numeric-index order, blanks/empties dropped


def test_shared_config_missing_structure_is_empty():
    assert parse_shared_config('"SomethingElse" { }') == {}
