import { ButtonItem, ConfirmModal, PanelSection, PanelSectionRow, showModal } from "@decky/ui";
import { useEffect, useState } from "react";
import {
  getPrivacyPreview,
  getSettings,
  PrivacyPreviewResponse,
  PrivacySummary,
  syncNow,
  unskipPrivateGame,
  updateSettings,
} from "../api";
import { formatBytes } from "../format";

/**
 * Manual "sync now" gated behind the privacy preview: the modal shows exactly what
 * would leave the device (the real upload body) before anything is sent. No
 * background or automatic sync in this prototype — every upload is user-triggered.
 */
export function SyncSection({ connected, onSynced }: { connected: boolean; onSynced: () => void }) {
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<string | null>(null);
  const [privateGameExclusionEnabled, setPrivateGameExclusionEnabled] = useState<boolean | null>(null);

  useEffect(() => {
    void getSettings().then((settings) => {
      if (settings.ok) {
        setPrivateGameExclusionEnabled(settings.excludeSteamPrivateGames ?? false);
      }
    });
  }, []);

  const runSync = async (uploadId: string) => {
    setBusy(true);
    const outcome = await syncNow(uploadId);
    setBusy(false);
    if (outcome.ok) {
      const warning = outcome.warning ? ` Warning: ${outcome.warning}` : "";
      setResult(`Synced ${outcome.gameCount ?? 0} games.${warning}`);
    } else {
      setResult(outcome.message ?? outcome.error ?? "Sync failed.");
    }
    onSynced();
  };

  const openPreview = async () => {
    setBusy(true);
    const preview = await getPrivacyPreview();
    setBusy(false);
    if (!preview.ok || !preview.summary || !preview.uploadId) {
      setResult(preview.error ?? "Could not build the privacy preview.");
      return;
    }
    showModal(
      <PreviewModal initialPreview={preview} connected={connected} onConfirm={(uploadId) => void runSync(uploadId)} />,
    );
  };

  const togglePrivateGameExclusion = async () => {
    if (privateGameExclusionEnabled === null) return;
    setBusy(true);
    const enabled = !privateGameExclusionEnabled;
    const updated = await updateSettings(null, null, enabled);
    setBusy(false);
    if (!updated.ok) {
      setResult(updated.error ?? "Could not save the Private-game exclusion setting.");
      return;
    }
    setPrivateGameExclusionEnabled(updated.excludeSteamPrivateGames ?? enabled);
    setResult(
      enabled
        ? "Private-game exclusion enabled. Missing or uncertain local cache data will fail open visibly."
        : "Private-game exclusion disabled.",
    );
  };

  return (
    <PanelSection title="Sync">
      <PanelSectionRow>
        <ButtonItem
          layout="below"
          disabled={busy || privateGameExclusionEnabled === null}
          onClick={() => void togglePrivateGameExclusion()}
        >
          {privateGameExclusionEnabled ? "Private-game exclusion: On" : "Private-game exclusion: Off"}
        </ButtonItem>
      </PanelSectionRow>
      <PanelSectionRow>
        <div style={{ fontSize: "11px", opacity: 0.75 }}>
          Don't sync games marked Private in Steam. Best effort: Steam's local cache can be stale, and missing or
          unreadable data never proves a game is Public.
        </div>
      </PanelSectionRow>
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
  initialPreview,
  connected,
  onConfirm,
  closeModal,
}: {
  initialPreview: PrivacyPreviewResponse;
  connected: boolean;
  onConfirm: (uploadId: string) => void;
  closeModal?: () => void;
}) {
  const [preview, setPreview] = useState(initialPreview);
  const [updatingAppId, setUpdatingAppId] = useState<number | null>(null);
  const [overrideError, setOverrideError] = useState<string | null>(null);
  const summary = preview.summary as PrivacySummary;
  const privateGameExclusion = preview.privateGameExclusion;

  const unskip = async (appId: number) => {
    if (!preview.uploadId) return;
    setUpdatingAppId(appId);
    setOverrideError(null);
    const updated = await unskipPrivateGame(preview.uploadId, appId);
    setUpdatingAppId(null);
    if (!updated.ok || !updated.summary || !updated.uploadId) {
      setOverrideError(updated.error ?? "Could not save the device-local override.");
      return;
    }
    setPreview(updated);
  };

  return (
    <ConfirmModal
      strTitle="What will be uploaded"
      strOKButtonText={connected ? "Sync now" : "Not connected"}
      strCancelButtonText="Close"
      bOKDisabled={!connected || updatingAppId !== null}
      onOK={() => {
        if (preview.uploadId) onConfirm(preview.uploadId);
        closeModal?.();
      }}
      onCancel={() => closeModal?.()}
    >
      <div style={{ fontSize: "13px", lineHeight: 1.5 }}>
        <div>
          <b>{summary.deviceName}</b> · {summary.gameCount} games ({summary.installedGameCount} installed) ·{" "}
          {summary.libraryCount} libraries · {summary.categoryCount} categories ·{" "}
          {formatBytes(summary.totalSizeOnDiskBytes)}
        </div>
        <div style={{ marginTop: "8px" }}>
          <b>Included:</b> {summary.included.join("; ")}
        </div>
        <div style={{ marginTop: "6px" }}>
          <b>Never included:</b> {summary.neverIncluded.join("; ")}
        </div>
        {privateGameExclusion?.enabled && (
          <div style={{ marginTop: "8px", padding: "8px", background: "rgba(30,41,59,0.7)" }}>
            <b>Private-game exclusion:</b> {privateGameExclusion.status}
            {privateGameExclusion.skippedGames.map((game) => (
              <div
                key={game.appId}
                style={{ display: "flex", justifyContent: "space-between", gap: "8px", marginTop: "6px" }}
              >
                <span>{game.name}</span>
                <button type="button" disabled={updatingAppId !== null} onClick={() => void unskip(game.appId)}>
                  {updatingAppId === game.appId ? "Saving…" : "Sync this game"}
                </button>
              </div>
            ))}
            {overrideError && <div style={{ marginTop: "6px", color: "#fca5a5" }}>{overrideError}</div>}
          </div>
        )}
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
          {preview.snapshotJson ?? ""}
        </pre>
      </div>
    </ConfirmModal>
  );
}
