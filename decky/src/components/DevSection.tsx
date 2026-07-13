import { ButtonItem, ConfirmModal, Field, PanelSection, PanelSectionRow, showModal, TextField } from "@decky/ui";
import { useState } from "react";
import { updateSettings } from "../api";

/**
 * Developer section: the server URL defaults to localhost (nothing is deployed yet;
 * production URLs are deliberately not committed — same posture as the tray). Editing
 * it here lets a dev Deck point at a machine on the LAN.
 *
 * [NEEDS-DECK] TextField + virtual keyboard behaviour inside a modal in Gaming Mode
 * is exactly the kind of thing that must be verified on hardware.
 */
export function DevSection({ serverUrl, onChanged }: { serverUrl: string | undefined; onChanged: () => void }) {
  const openEditor = () => {
    showModal(<ServerUrlModal initial={serverUrl ?? ""} onSaved={onChanged} />);
  };

  return (
    <PanelSection title="Developer">
      <PanelSectionRow>
        <Field label="Server" description={serverUrl ?? "—"} />
      </PanelSectionRow>
      <PanelSectionRow>
        <ButtonItem layout="below" onClick={openEditor}>
          Edit server URL
        </ButtonItem>
      </PanelSectionRow>
    </PanelSection>
  );
}

function ServerUrlModal({
  initial,
  onSaved,
  closeModal,
}: {
  initial: string;
  onSaved: () => void;
  closeModal?: () => void;
}) {
  const [value, setValue] = useState(initial);

  return (
    <ConfirmModal
      strTitle="Shelfbound server URL"
      strOKButtonText="Save"
      onOK={() => {
        void updateSettings(value, null).then(() => onSaved());
        closeModal?.();
      }}
      onCancel={() => closeModal?.()}
    >
      <TextField label="Server URL" value={value} onChange={(event) => setValue(event.target.value)} />
    </ConfirmModal>
  );
}
