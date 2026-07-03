import { staticClasses } from "@decky/ui";
import { definePlugin } from "@decky/api";
import { useEffect, useState } from "react";
import { FaBoxOpen } from "react-icons/fa";

import { getStatus, StatusResponse } from "./api";
import { ConnectSection } from "./components/ConnectSection";
import { DevSection } from "./components/DevSection";
import { StatusSection } from "./components/StatusSection";
import { StorageSection } from "./components/StorageSection";
import { SyncSection } from "./components/SyncSection";

function ShelfboundPanel() {
  const [status, setStatus] = useState<StatusResponse | null>(null);

  const refresh = () => {
    void getStatus().then(setStatus);
  };

  useEffect(refresh, []);

  return (
    <>
      <StatusSection status={status} />
      <ConnectSection status={status} onChanged={refresh} />
      <SyncSection connected={status?.connection?.connected ?? false} onSynced={refresh} />
      <StorageSection />
      <DevSection serverUrl={status?.connection?.serverUrl} onChanged={refresh} />
    </>
  );
}

export default definePlugin(() => {
  return {
    name: "Shelfbound",
    titleView: <div className={staticClasses.Title}>Shelfbound</div>,
    content: <ShelfboundPanel />,
    icon: <FaBoxOpen />,
  };
});
