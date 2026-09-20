chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "reload-extension") return;
  sendResponse({ ok: true });
  setTimeout(() => chrome.runtime.reload(), 250);
  return true;
});