import { Field, PanelSection, PanelSectionRow } from "@decky/ui";
import { useEffect, useState } from "react";
import { getStorageOverview, StorageGroup, StorageOverviewResponse } from "../api";
import { formatBytes } from "../format";

/**
 * The Deck-specific view: installed games grouped by internal SSD vs microSD, with
 * free space and the biggest installs. All of this is on-device intelligence — none
 * of it is part of the uploaded snapshot.
 */
export function StorageSection() {
  const [overview, setOverview] = useState<StorageOverviewResponse | null>(null);

  useEffect(() => {
    void getStorageOverview().then(setOverview);
  }, []);

  return (
    <PanelSection title="Storage">
      {overview === null ? (
        <PanelSectionRow>
          <Field label="Scanning libraries…" />
        </PanelSectionRow>
      ) : !overview.ok ? (
        <PanelSectionRow>
          <Field label="Scan failed" description={overview.error ?? "Unknown error"} />
        </PanelSectionRow>
      ) : (
        (overview.storages ?? []).map((group) => <StorageRow key={group.kind} group={group} />)
      )}
    </PanelSection>
  );
}

function StorageRow({ group }: { group: StorageGroup }) {
  const space =
    group.freeBytes != null && group.totalBytes != null
      ? ` · ${formatBytes(group.freeBytes)} free of ${formatBytes(group.totalBytes)}`
      : "";
  const largest = group.largestGames
    .map((game) => `${game.name} (${formatBytes(game.sizeOnDiskBytes)})`)
    .join(", ");

  return (
    <PanelSectionRow>
      <Field
        label={group.label}
        description={
          `${group.installedGameCount} installed · ${formatBytes(group.sizeOnDiskBytes)} used${space}` +
          (largest ? ` — largest: ${largest}` : "")
        }
      />
    </PanelSectionRow>
  );
}
