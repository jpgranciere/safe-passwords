import { readLogin, fillLogin } from "./page.js";

const HOST = "com.cofre.passwords";
let busy = false;

function native(request) {
  return new Promise((resolve, reject) => {
    const port = chrome.runtime.connectNative(HOST); // Keeps the worker alive during desktop consent.
    let settled = false;
    const timeout = setTimeout(() => { if (!settled) { settled = true; port.disconnect(); reject(new Error("Tempo de confirmação esgotado.")); } }, 85000);
    port.onMessage.addListener(response => {
      if (settled) return;
      settled = true; clearTimeout(timeout); port.disconnect();
      if (!response?.ok) reject(new Error(response?.error || "Resposta inválida do Cofre."));
      else resolve(response);
    });
    port.onDisconnect.addListener(() => {
      const error = chrome.runtime.lastError;
      if (settled) return;
      settled = true; clearTimeout(timeout);
      reject(new Error(error ? "Conexão indisponível. Abra o Cofre e registre a integração nativa." : "O Cofre encerrou a conexão."));
    });
    port.postMessage(request);
  });
}

async function activePage() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !tab.url || new URL(tab.url).protocol !== "https:")
    throw new Error("Abra uma página HTTPS para usar o Cofre.");
  return tab;
}

async function handle(message) {
  const tab = await activePage();
  const url = tab.url;
  const origin = new URL(url).origin;
  if (message.action === "list") {
    const response = await native({ action: "list", url: origin });
    return { ...response, origin, pageUrl: url };
  }
  if (message.pageUrl !== url) throw new Error("A página mudou. Atualize a lista antes de continuar.");
  if (message.action === "read") {
    const [result] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: readLogin, args: [url] });
    if (!result?.result) throw new Error("Não foi possível ler o formulário.");
    return { ok: true, ...result.result };
  }
  if (message.action === "fill") {
    const response = await native({ action: "fill", url: origin, id: message.id });
    if (response.origin !== origin) throw new Error("A resposta não corresponde ao site.");
    const current = await chrome.tabs.get(tab.id);
    if (current.url !== url) throw new Error("A página mudou durante a confirmação.");
    const [result] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: fillLogin,
      args: [url, response.username, response.password] });
    if (!result?.result?.filled) throw new Error("Não foi possível preencher este formulário.");
    return { ok: true };
  }
  if (message.action === "save") {
    return await native({ action: "save", url: origin, title: message.title,
      username: message.username, password: message.password });
  }
  throw new Error("Ação inválida.");
}

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  // Only our action popup can request operations. No website or content-script command channel.
  if (sender.id !== chrome.runtime.id || sender.url !== chrome.runtime.getURL("popup.html")) return false;
  if (busy) { reply({ ok: false, error: "Já existe uma operação em andamento no Cofre." }); return false; }
  busy = true;
  chrome.action.setBadgeText({ text: "" });
  handle(message).then(result => {
    if (["fill", "save"].includes(message.action)) {
      chrome.action.setBadgeBackgroundColor({ color: "#3d8060" });
      chrome.action.setBadgeText({ text: "OK" });
      chrome.action.setTitle({ title: message.action === "fill" ? "Cofre — login preenchido" : "Cofre — entrada salva" });
    }
    reply(result);
  }).catch(error => {
    chrome.action.setBadgeBackgroundColor({ color: "#ad4646" });
    chrome.action.setBadgeText({ text: "!" });
    chrome.action.setTitle({ title: "Cofre — " + error.message });
    reply({ ok: false, error: error.message });
  }).finally(() => { busy = false; });
  return true;
});
