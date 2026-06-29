"use client";
import { SITE } from "@/lib/site";
import { Icon } from "./Icons";
import CopyButton from "./CopyButton";

// Glass bar: server IP (copyable) + "Connect via Steam" button.
export default function ConnectBar() {
  return (
    <div className="connect-bar glass reveal">
      <div className="connect-ip">
        <span className="connect-ip-label">SERVER IP</span>
        <code>{SITE.serverIp}</code>
        <CopyButton text={SITE.serverIp} />
      </div>
      <a className="btn btn-primary connect-go" href={SITE.connectUrl}>
        <Icon.arrow style={{ width: 18, height: 18 }} />
        Connect via Steam
      </a>
    </div>
  );
}
