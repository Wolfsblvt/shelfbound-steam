from shelfbound_decky.overview import build_storage_overview

# The snapshot now carries per-library storage (kind + free/total); the overview reads
# it directly instead of re-classifying from paths + a mount table.
SNAPSHOT = {
    "libraries": [
        {"index": 0, "label": "library-0", "gameCount": 2,
         "storage": {"kind": "internal", "freeBytes": 111, "totalBytes": 999}},
        {"index": 1, "label": "SD Card", "gameCount": 2,
         "storage": {"kind": "sdCard", "freeBytes": 10_000, "totalBytes": 50_000}},
    ],
    "games": [
        {"appId": 1, "name": "Small", "installed": True, "libraryIndex": 0, "sizeOnDiskBytes": 100},
        {"appId": 2, "name": "Big", "installed": True, "libraryIndex": 0, "sizeOnDiskBytes": 900},
        {"appId": 3, "name": "SD Game", "installed": True, "libraryIndex": 1, "sizeOnDiskBytes": 500},
        {"appId": 4, "name": "Updating", "installed": False, "libraryIndex": 1, "sizeOnDiskBytes": 300},
    ],
}


def test_groups_by_storage_kind_with_usage_and_largest():
    result = build_storage_overview(SNAPSHOT)

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


def test_missing_storage_field_falls_back_to_unknown():
    # A lenient consumer: a library without a storage field groups under "unknown".
    snapshot = {
        "libraries": [
            {"index": 0, "label": "library-0", "gameCount": 1, "storage": {"kind": "internal"}},
            {"index": 1, "label": "mystery", "gameCount": 1},  # no storage field at all
        ],
        "games": [
            {"appId": 1, "name": "A", "installed": True, "libraryIndex": 0, "sizeOnDiskBytes": 100},
            {"appId": 2, "name": "B", "installed": True, "libraryIndex": 1, "sizeOnDiskBytes": 200},
        ],
    }

    result = build_storage_overview(snapshot)
    kinds = {group["kind"] for group in result["storages"]}
    assert kinds == {"internal", "unknown"}

    unknown = next(group for group in result["storages"] if group["kind"] == "unknown")
    assert unknown["label"] == "Unknown storage"
    assert unknown["freeBytes"] is None  # no sizes available for an unclassified library
