function toBase64(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  const chunk = 0x8000;
  for (let index = 0; index < bytes.length; index += chunk)
    binary += String.fromCharCode(...bytes.subarray(index, index + chunk));
  return btoa(binary);
}

async function sha256Hex(buffer) {
  const digest = await crypto.subtle.digest("SHA-256", buffer);
  return [...new Uint8Array(digest)]
    .map(value => value.toString(16).padStart(2, "0"))
    .join("")
    .toUpperCase();
}

function allowedResourceUrl(value) {
  try {
    const url = new URL(value);
    if (url.protocol !== "https:") return false;
    const host = url.hostname.toLowerCase();
    return host === "chatgpt.com" ||
      host.endsWith(".oaiusercontent.com") ||
      host.endsWith(".openai.com");
  } catch {
    return false;
  }
}

let managedTabCleanupQueue = Promise.resolve();

function isChatGptPageUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "https:" &&
      (url.hostname.toLowerCase() === "chatgpt.com" ||
       url.hostname.toLowerCase() === "www.chatgpt.com");
  } catch {
    return false;
  }
}

function conversationIdFromTabUrl(value) {
  try {
    const match = new URL(value).pathname.match(/^\/c\/([a-zA-Z0-9-]+)/);
    return match ? match[1] : "";
  } catch {
    return "";
  }
}

function managedConversationUrl(conversationId, role) {
  const suffix = "?projecthub-managed-role=" + encodeURIComponent(role);
  return conversationId
    ? "https://chatgpt.com/c/" + encodeURIComponent(conversationId) + suffix
    : "https://chatgpt.com/" + suffix;
}

async function ensureSingleManagedChatGptTab(message, sender) {
  const role = String(message?.role || "").trim().toUpperCase();
  if (role !== "HQ" && role !== "RESOURCE")
    throw new Error("MANAGED_ROLE_INVALID");

  const requestedConversationId = String(message?.conversationId || "").trim();
  const senderTab = sender?.tab && isChatGptPageUrl(sender.tab.url || "")
    ? sender.tab
    : null;
  const tabs = await chrome.tabs.query({});
  const chatTabs = tabs.filter(tab => isChatGptPageUrl(tab.url || ""));

  let target = null;
  if (requestedConversationId) {
    target = chatTabs.find(tab =>
      conversationIdFromTabUrl(tab.url || "") === requestedConversationId) || null;
  }

  if (!target && senderTab)
    target = senderTab;
  if (!target && chatTabs.length)
    target = chatTabs[0];

  const targetUrl = managedConversationUrl(requestedConversationId, role);
  if (!target) {
    target = await chrome.tabs.create({ url: targetUrl, active: true });
  } else if (requestedConversationId &&
             conversationIdFromTabUrl(target.url || "") !== requestedConversationId) {
    target = await chrome.tabs.update(target.id, { url: targetUrl, active: true });
  } else {
    target = await chrome.tabs.update(target.id, { active: true });
  }

  const removeIds = chatTabs
    .filter(tab => tab.id !== target?.id)
    .map(tab => tab.id)
    .filter(id => Number.isInteger(id));

  if (removeIds.length)
    await chrome.tabs.remove(removeIds);

  return {
    ok: true,
    role,
    tabId: target?.id ?? null,
    removedTabs: removeIds.length,
    conversationId: conversationIdFromTabUrl(target?.url || "") || requestedConversationId || null
  };
}

function queueManagedTabCleanup(message, sender, sendResponse) {
  managedTabCleanupQueue = managedTabCleanupQueue
    .catch(() => {})
    .then(() => ensureSingleManagedChatGptTab(message, sender));

  managedTabCleanupQueue.then(
    result => sendResponse(result),
    error => sendResponse({ ok: false, error: error?.message || String(error) })
  );
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type === "ensure-single-chatgpt-tab") {
    queueManagedTabCleanup(message, sender, sendResponse);
    return true;
  }

  if (message?.type === "reload-extension") {
    sendResponse({ ok: true });
    setTimeout(() => chrome.runtime.reload(), 250);
    return true;
  }

  if (message?.type === "fetch-resource-file" || message?.type === "fetch-resource-image") {
    (async () => {
      if (!allowedResourceUrl(message.url)) {
        sendResponse({ ok: false, error: "RESOURCE_URL_NOT_ALLOWED" });
        return;
      }
      try {
        const response = await fetch(message.url, { credentials: "include" });
        if (!response.ok) {
          sendResponse({ ok: false, error: "RESOURCE_DOWNLOAD_HTTP_" + response.status });
          return;
        }
        const buffer = await response.arrayBuffer();
        if (!buffer.byteLength) {
          sendResponse({ ok: false, error: "RESOURCE_FILE_EMPTY" });
          return;
        }
        sendResponse({
          ok: true,
          base64: toBase64(buffer),
          sha256: await sha256Hex(buffer),
          mimeType: response.headers.get("content-type") || "application/octet-stream",
          contentDisposition: response.headers.get("content-disposition") || ""
        });
      } catch (error) {
        sendResponse({ ok: false, error: "RESOURCE_BACKGROUND_FETCH: " + (error?.message || error) });
      }
    })();
    return true;
  }
});
