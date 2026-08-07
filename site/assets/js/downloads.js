import { detectPlatform } from "./platform.js";

const basePath = document.documentElement.dataset.basePath || "/";
const platform = detectPlatform();
const platformLabels = {
  windows: "Windows x64",
  macos: "macOS",
  linux: "Linux x64"
};
const actionLabels = {
  windows: "Download Windows x64",
  macos: "Download macOS — Apple Silicon",
  linux: "Download for Linux — x64"
};

function localPath(path) {
  return `${basePath}${path.replace(/^\//, "")}`;
}

function isAvailable(target) {
  return target?.availability === "available" && /^https:\/\//i.test(target.url || "");
}

function updateHero(manifest) {
  const targets = document.querySelectorAll("[data-download-action]");
  const selected = manifest.platforms?.[platform];

  for (const target of targets) {
    if (platform === "unknown") {
      target.textContent = "View desktop downloads";
      target.href = localPath("download/");
    }
    else if (isAvailable(selected)) {
      target.textContent = actionLabels[platform];
      target.href = selected.url;
      target.rel = "noopener noreferrer";
    }
    else {
      target.textContent = selected?.availability === "verified"
        ? actionLabels[platform]
        : `View ${platformLabels[platform]} availability`;
      target.href = localPath(`download/#${platform}`);
    }
  }

  const releaseStatus = document.querySelector("[data-release-status]");
  if (releaseStatus && manifest.release?.version) {
    const channelLabel = manifest.selectedChannel === "preview" ? "Preview" : "Stable";
    releaseStatus.textContent = `${channelLabel} ${manifest.release.version}`;
  }
}

function updateRecommendation(manifest) {
  const panel = document.querySelector("[data-recommendation-panel]");
  if (!panel) return;

  const title = panel.querySelector("[data-recommendation-title]");
  const detail = panel.querySelector("[data-recommendation-detail]");
  const action = panel.querySelector("[data-recommendation-action]");

  if (platform === "unknown") {
    title.textContent = "Choose your desktop platform";
    detail.textContent = "Platform detection stays on this device. Every supported option remains visible below.";
    action.textContent = "Compare availability";
    action.href = "#platforms-title";
    return;
  }

  const selected = manifest.platforms?.[platform];
  title.textContent = `${platformLabels[platform]} detected`;
  detail.textContent = isAvailable(selected)
    ? selected.note
    : selected?.reason || "This package is not publicly available yet.";
  action.textContent = isAvailable(selected) ? actionLabels[platform] : "View current status";
  action.href = isAvailable(selected) ? selected.url : `#${platform}`;
  if (isAvailable(selected)) action.rel = "noopener noreferrer";
}

function updateLaunchStatus(manifest) {
  const status = document.querySelector("[data-launch-status]");
  if (!status || !manifest.advertising || !manifest.release?.version) return;

  const title = status.querySelector("[data-launch-title]");
  const detail = status.querySelector("[data-launch-detail]");
  const channelLabel = manifest.selectedChannel === "preview" ? "Preview" : "Stable release";
  title.textContent = `${channelLabel} ${manifest.release.version}`;
  detail.textContent = "Public destinations and release metadata verified";
}

function updatePlatformCards(manifest) {
  for (const card of document.querySelectorAll("[data-platform-card]")) {
    const id = card.dataset.platformCard;
    const target = manifest.platforms?.[id];
    if (!target) continue;

    const status = card.querySelector("[data-platform-status]");
    const detail = card.querySelector("[data-platform-detail]");
    const note = card.querySelector("[data-platform-note]");
    const action = card.querySelector("[data-platform-action]");

    if (isAvailable(target)) {
      status.textContent = target.channel === "preview" ? "PREVIEW AVAILABLE" : "AVAILABLE";
      status.classList.remove("status-pending", "status-progress");
      status.classList.add("status-available");
      detail.textContent = target.detail;
      note.textContent = target.note;
      action.textContent = target.actionLabel;
      action.href = target.url;
      action.rel = "noopener noreferrer";
      action.hidden = false;
    }
    else if (target.reason) {
      detail.textContent = target.reason;
      note.textContent = target.note;
    }
  }
}

async function initializeDownloads() {
  try {
    const response = await fetch(localPath("downloads.json"), { headers: { Accept: "application/json" } });
    if (!response.ok) return;
    const manifest = await response.json();
    if (manifest.schemaVersion !== 1 || !manifest.platforms) return;
    updateHero(manifest);
    updateLaunchStatus(manifest);
    updateRecommendation(manifest);
    updatePlatformCards(manifest);
  }
  catch {
    // Static HTML remains the safe, useful fallback.
  }
}

initializeDownloads();
