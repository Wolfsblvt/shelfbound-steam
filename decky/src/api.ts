/**
 * Typed bridge to the Python backend (main.py). Every call returns an envelope
 * ({ ok, ...} | { ok: false, error }) instead of throwing across the bridge.
 * Shapes mirror the backend dicts 1:1 — keep both sides in sync.
 */
import { callable } from "@decky/api";

export interface PluginInfo {
  toolName: string;
  version: string;
  schemaVersion: string;
}

export interface DeviceSpecs {
  cpu?: string;
  logicalCores?: number;
  totalMemoryBytes?: number;
  gpu?: string;
  osDescription?: string;
  architecture?: string;
}

export interface DeviceInfo {
  id: string;
  name: string;
  type: string;
  os: string;
  specs?: DeviceSpecs;
}

/** Mirrors the server's /auth/me shape (Steam fields may be null over a device token). */
export interface AccountInfo {
  accountId?: string;
  steamId?: string | null;
  displayName?: string | null;
}

export interface LastSync {
  at: string;
  status: string;
  message?: string | null;
  warning?: string | null;
  errorCode?: string | null;
  gameCount?: number | null;
}

export interface StatusResponse {
  ok: boolean;
  error?: string;
  plugin?: PluginInfo;
  steam?: { found: boolean; root: string | null };
  device?: DeviceInfo;
  connection?: { connected: boolean; serverUrl: string; account: AccountInfo | null };
  lastSync?: LastSync | null;
  pairingInProgress?: boolean;
}

export interface StorageLibrary {
  index: number;
  label: string;
  gameCount: number;
}

export interface LargestGame {
  appId: number;
  name: string;
  sizeOnDiskBytes: number;
  installed: boolean;
}

export interface StorageGroup {
  kind: "internal" | "sdCard" | "external" | "unknown";
  label: string;
  libraries: StorageLibrary[];
  gameCount: number;
  installedGameCount: number;
  sizeOnDiskBytes: number;
  freeBytes: number | null;
  totalBytes: number | null;
  largestGames: LargestGame[];
}

export interface StorageOverviewResponse {
  ok: boolean;
  error?: string;
  storages?: StorageGroup[];
  warnings?: string[];
}

export interface PrivacySummary {
  deviceName?: string;
  deviceType?: string;
  libraryCount: number;
  gameCount: number;
  installedGameCount: number;
  categoryCount: number;
  totalSizeOnDiskBytes: number;
  scope: string;
  included: string[];
  neverIncluded: string[];
}

export interface PrivacyPreviewResponse {
  ok: boolean;
  error?: string;
  uploadId?: string;
  summary?: PrivacySummary;
  snapshotJson?: string;
  warnings?: string[];
}

export interface SyncResponse {
  ok: boolean;
  error?: string;
  status?: string;
  message?: string | null;
  warning?: string | null;
  errorCode?: string;
  gameCount?: number;
  retryAfterSeconds?: number | null;
  plan?: string | null;
  maxDevices?: number | null;
  syncedAt?: string;
  warnings?: string[];
}

export interface PairingStartResponse {
  ok: boolean;
  error?: string;
  pairingUnavailable?: boolean;
  code?: string;
  claimUrl?: string;
  expiresInSeconds?: number;
}

export interface PairingPollResponse {
  ok: boolean;
  error?: string;
  status?: "pending" | "claimed" | "expired" | "denied" | string;
  account?: AccountInfo | null;
}

export interface SettingsResponse {
  ok: boolean;
  error?: string;
  serverUrl?: string;
  deviceName?: string | null;
}

export const getStatus = callable<[], StatusResponse>("get_status");
export const getStorageOverview = callable<[], StorageOverviewResponse>("get_storage_overview");
export const getPrivacyPreview = callable<[], PrivacyPreviewResponse>("get_privacy_preview");
export const syncNow = callable<[uploadId: string], SyncResponse>("sync_now");
export const pairingStart = callable<[], PairingStartResponse>("pairing_start");
export const pairingPoll = callable<[], PairingPollResponse>("pairing_poll");
export const pairingCancel = callable<[], { ok: boolean }>("pairing_cancel");
export const disconnect = callable<[], { ok: boolean; error?: string }>("disconnect");
export const getSettings = callable<[], SettingsResponse>("get_settings");
export const updateSettings = callable<[serverUrl: string | null, deviceName: string | null], SettingsResponse>(
  "update_settings",
);
