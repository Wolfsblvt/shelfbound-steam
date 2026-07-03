from shelfbound_decky.overview import build_storage_overview
from shelfbound_decky.storage import MountEntry

SNAPSHOT = {
    "libraries": [
        {"index": 0, "label": "library-0", "gameCount": 2},
        {"index": 1, "label": "SD Card", "gameCount": 2},
    ],
    "games": [
        {"appId": 1, "name": "Small", "installed": True, "libraryIndex": 0, "sizeOnDiskBytes": 100},
        {"appId": 2, "name": "Big", "installed": True, "libraryIndex": 0, "sizeOnDiskBytes": 900},
        {"appId": 3, "name": "SD Game", "installed": True, "libraryIndex": 1, "sizeOnDiskBytes": 500},
        {"appId": 4, "name": "Updating", "installed": False, "libraryIndex": 1, "sizeOnDiskBytes": 300},
    ],
}

LIBRARY_PATHS = {0: "/home/deck/.local/share/Steam", 1: "/run/media/mmcblk0p1/steamlib"}

MOUNTS = [
    MountEntry("/dev/nvme0n1p8", "/home", "ext4"),
    MountEntry("/dev/mmcblk0p1", "/run/media/mmcblk0p1", "ext4"),
]


def fake_usage(path):
    return (10_000, 50_000) if "mmcblk" in path else (111, 999)


def test_groups_by_storage_kind_with_usage_and_largest():
    result = build_storage_overview(
        SNAPSHOT, LIBRARY_PATHS, MOUNTS, usage_fn=fake_usage, home="/home/deck"
    )

    kinds = [group["kind"] for group in result["storages"]]
    assert kinds == ["internal", "sdCard"]  # fixed display order

    internal, sd = result["storages"]
    assert internal["label"] == "Internal SSD"
    assert internal["gameCount"] == 2
    assert internal["sizeOnDiskBytes"] == 1000
    assert (internal["freeBytes"], internal["totalBytes"]) == (111, 999)
    assert [game["name"] for game in internal["largestGames"]] == ["Big", "Small"]

    assert sd["installedGameCount"] == 1  # the updating game isn't fully installed
    assert (sd["freeBytes"], sd["totalBytes"]) == (10_000, 50_000)


def test_missing_path_goes_to_unknown():
    result = build_storage_overview(SNAPSHOT, {0: "/home/deck/x"}, [], usage_fn=lambda _: None, home="/home/deck")
    kinds = {group["kind"] for group in result["storages"]}
    assert kinds == {"internal", "unknown"}
