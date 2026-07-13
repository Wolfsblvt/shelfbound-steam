import { ButtonItem, Field, PanelSection, PanelSectionRow } from "@decky/ui";
import { useState } from "react";
import { disconnect, pairingCancel, pairingPoll, pairingStart, PairingStartResponse, StatusResponse } from "../api";

/**
 * Account claim via pairing code — deliberately NOT the tray's loopback-callback
 * OAuth (wrong tool inside Gaming Mode; Decky documents local-port conflicts).
 * Flow: request a code, the user enters it at the claim URL on any signed-in
 * browser (phone/desktop), then presses "I've entered the code" — the plugin
 * polls once per press, no hot polling loop.
 *
 * A QR code for the claim URL is a planned nicety (e.g. via the `react-qr-code`
 * package); the prototype keeps dependencies at zero and shows the URL as text.
 *
 * The server endpoints behind this are a PROPOSAL (see py_modules/shelfbound_decky/cloud.py);
 * against today's server this reports "pairing not available" — honestly, not fake-success.
 */
export function ConnectSection({ status, onChanged }: { status: StatusResponse | null; onChanged: () => void }) {
  const [session, setSession] = useState<PairingStartResponse | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const connected = status?.connection?.connected ?? false;

  const start = async () => {
    setBusy(true);
    setMessage(null);
    const result = await pairingStart();
    setBusy(false);
    if (result.ok) {
      setSession(result);
    } else {
      setMessage(result.error ?? "Pairing failed.");
    }
  };

  const checkClaimed = async () => {
    setBusy(true);
    const result = await pairingPoll();
    setBusy(false);
    if (!result.ok) {
      setMessage(result.error ?? "Pairing check failed.");
      return;
    }
    if (result.status === "claimed") {
      setSession(null);
      setMessage(result.account?.displayName ? `Connected as ${result.account.displayName}.` : "Connected.");
      onChanged();
    } else if (result.status === "expired" || result.status === "denied") {
      setSession(null);
      setMessage(`Pairing ${result.status}. Start again to get a new code.`);
      onChanged(); // backend dropped its session — refresh so the UI leaves the resume branch
    } else {
      setMessage("Not claimed yet — finish the code entry in your browser, then check again.");
    }
  };

  const cancel = async () => {
    await pairingCancel();
    setSession(null);
    setMessage(null);
  };

  const signOut = async () => {
    setBusy(true);
    await disconnect();
    setBusy(false);
    setMessage("Disconnected on this device. Manage or revoke tokens in the dashboard.");
    onChanged();
  };

  return (
    <PanelSection title="Account">
      {session ? (
        <>
          <PanelSectionRow>
            <Field label="Pairing code" description="Enter this code at the claim page using any signed-in browser.">
              <div style={{ fontSize: "22px", fontWeight: 700, letterSpacing: "2px" }}>{session.code ?? "—"}</div>
            </Field>
          </PanelSectionRow>
          <PanelSectionRow>
            <Field label="Claim page" description={session.claimUrl ?? "—"} />
          </PanelSectionRow>
          <PanelSectionRow>
            <ButtonItem layout="below" disabled={busy} onClick={() => void checkClaimed()}>
              I've entered the code
            </ButtonItem>
          </PanelSectionRow>
          <PanelSectionRow>
            <ButtonItem layout="below" onClick={() => void cancel()}>
              Cancel pairing
            </ButtonItem>
          </PanelSectionRow>
        </>
      ) : connected ? (
        <PanelSectionRow>
          <ButtonItem layout="below" disabled={busy} onClick={() => void signOut()}>
            Disconnect this device
          </ButtonItem>
        </PanelSectionRow>
      ) : status?.pairingInProgress ? (
        // The QAM unmounts whenever it closes, so resuming a backend-held pairing
        // session (started before the user grabbed their phone) is the normal path.
        <>
          <PanelSectionRow>
            <Field label="Pairing in progress" description="Finish entering the code from earlier, then check below." />
          </PanelSectionRow>
          <PanelSectionRow>
            <ButtonItem layout="below" disabled={busy} onClick={() => void checkClaimed()}>
              I've entered the code
            </ButtonItem>
          </PanelSectionRow>
          <PanelSectionRow>
            <ButtonItem
              layout="below"
              onClick={() => {
                void cancel();
                onChanged();
              }}
            >
              Cancel pairing
            </ButtonItem>
          </PanelSectionRow>
        </>
      ) : (
        <PanelSectionRow>
          <ButtonItem layout="below" disabled={busy} onClick={() => void start()}>
            Connect Shelfbound account
          </ButtonItem>
        </PanelSectionRow>
      )}
      {message && (
        <PanelSectionRow>
          <Field label="" description={message} />
        </PanelSectionRow>
      )}
    </PanelSection>
  );
}
