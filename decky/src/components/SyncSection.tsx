import { ButtonItem, ConfirmModal, PanelSection, PanelSectionRow, showModal } from "@decky/ui";
import { useState } from "react";
import { getPrivacyPreview, PrivacySummary, syncNow } from "../api";
import { formatBytes } from "../format";

// Rendering a full multi-hundred-KB snapshot in the quick-access panel would crawl;
// the modal shows the real upload body up to this many characters. The summary above
// it is complete either way.
const PREVIEW_JSON_MAX_CHARS = 6000;

/**
 * Manual "sync now" gated behind the privacy preview: the modal shows exactly what
 * would leave the device (the real upload body) before anything is sent. No
 * background or automatic sync in this prototype — every upload is user-triggered.
 */
export function SyncSection({
  connected,
  onSynced,
}: {
  connected: boolean;
  onSynced: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);

  const runSync = async () => {
    setBusy(true);
    const outcome = await syncNow();
    setBusy(false);
    if (outcome.ok) {
      setResult(`Synced ${outcome.gameCount ?? 0} games.`);
    } else {
      setResult(outcome.message ?? outcome.error ?? "Sync failed.");
    }
    onSynced();
  };

  const openPreview = async () => {
    setBusy(true);
    const preview = await getPrivacyPreview();
    setBusy(false);
    if (!preview.ok || !preview.summary) {
      setResult(preview.error ?? "Could not build the privacy preview.");
      return;
    }
    showModal(
      <PreviewModal
        summary={preview.summary}
        snapshotJson={preview.snapshotJson ?? ""}
        connected={connected}
        onConfirm={() => void runSync()}
      />
    );
  };

  return (
    <PanelSection title="Sync">
      <PanelSectionRow>
        <ButtonItem layout="below" disabled={busy} onClick={() => void openPreview()}>
          Preview upload…
        </ButtonItem>
      </PanelSectionRow>
      {result && (
        <PanelSectionRow>
          <div style={{ fontSize: "12px", opacity: 0.8 }}>{result}</div>
        </PanelSectionRow>
      )}
    </PanelSection>
  );
}

function PreviewModal({
  summary,
  snapshotJson,
  connected,
  onConfirm,
  closeModal,
}: {
  summary: PrivacySummary;
  snapshotJson: string;
  connected: boolean;
  onConfirm: () => void;
  closeModal?: () => void;
}) {
  const truncated = snapshotJson.length > PREVIEW_JSON_MAX_CHARS;
  const shownJson = truncated ? snapshotJson.slice(0, PREVIEW_JSON_MAX_CHARS) : snapshotJson;

  return (
    <ConfirmModal
      strTitle="What will be uploaded"
      strOKButtonText={connected ? "Sync now" : "Not connected"}
      strCancelButtonText="Close"
      bOKDisabled={!connected}
      onOK={() => {
        onConfirm();
        closeModal?.();
      }}
      onCancel={() => closeModal?.()}
    >
      <div style={{ fontSize: "13px", lineHeight: 1.5 }}>
        <div>
          <b>{summary.deviceName}</b> · {summary.gameCount} games (
          {summary.installedGameCount} installed) · {summary.libraryCount} libraries ·{" "}
          {summary.categoryCount} categories · {formatBytes(summary.totalSizeOnDiskBytes)}
        </div>
        <div style={{ marginTop: "6px" }}>
          Accounts:{" "}
          {summary.accounts.length === 0
            ? "none"
            : summary.accounts
                .map((account) => account.personaName ?? account.steamId64 ?? "?")
                .join(", ")}
        </div>
        <div style={{ marginTop: "8px" }}>
          <b>Included:</b> {summary.included.join("; ")}
        </div>
        <div style={{ marginTop: "6px" }}>
          <b>Never included:</b> {summary.neverIncluded.join("; ")}
        </div>
        <pre
          style={{
            marginTop: "10px",
            maxHeight: "220px",
            overflowY: "auto",
            fontSize: "10px",
            background: "rgba(0,0,0,0.35)",
            padding: "8px",
            whiteSpace: "pre-wrap",
            wordBreak: "break-all",
          }}
        >
          {shownJson}
          {truncated && "\n… display truncated — the actual upload is this full document, nothing more."}
        </pre>
      </div>
    </ConfirmModal>
  );
}
