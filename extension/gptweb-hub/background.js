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

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
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
