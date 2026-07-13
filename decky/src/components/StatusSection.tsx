import { Field, PanelSection, PanelSectionRow } from "@decky/ui";
import { StatusResponse } from "../api";
import { formatWhen } from "../format";

const DEVICE_TYPE_LABELS: Record<string, string> = {
  steamDeck: "Steam Deck",
  desktop: "Desktop",
  laptop: "Laptop",
  server: "Server",
  unknown: "Unknown device",
};

export function StatusSection({ status }: { status: StatusResponse | null }) {
  if (status === null) {
    return (
      <PanelSection title="Status">
        <PanelSectionRow>
          <Field label="Loading…" />
        </PanelSectionRow>
      </PanelSection>
    );
  }

  if (!status.ok) {
    return (
      <PanelSection title="Status">
        <PanelSectionRow>
          <Field label="Backend error" description={status.error ?? "Unknown error"} />
        </PanelSectionRow>
      </PanelSection>
    );
  }

  const device = status.device;
  const connection = status.connection;
  const lastSync = status.lastSync;

  return (
    <PanelSection title="Status">
      <PanelSectionRow>
        <Field
          label="This device"
          description={device ? `${device.name} · ${DEVICE_TYPE_LABELS[device.type] ?? device.type}` : "Unknown"}
        />
      </PanelSectionRow>
      <PanelSectionRow>
        <Field label="Steam" description={status.steam?.found ? "Library found" : "No Steam installation found"} />
      </PanelSectionRow>
      <PanelSectionRow>
        <Field
          label="Account"
          description={
            connection?.connected
              ? (connection.account?.displayName ?? "Connected (account unreachable right now)")
              : "Not connected"
          }
        />
      </PanelSectionRow>
      <PanelSectionRow>
        <Field
          label="Last sync"
          description={
            lastSync
              ? `${formatWhen(lastSync.at)} · ${lastSync.status}${
                  lastSync.gameCount ? ` · ${lastSync.gameCount} games` : ""
                }`
              : "never"
          }
        />
      </PanelSectionRow>
    </PanelSection>
  );
}
