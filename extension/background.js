import { formOperation } from "./page.js";
import { defaults, validateOptions, generatePassword } from "./generator.js";

const HOST = "com.cofre.passwords";
let busy = false;
// Register only hosts explicitly granted by the user in the browser's permission prompt.
let registration = Promise.resolve();
function registerSites() {
  registration = registration.catch(() => {}).then(async () => {
    const granted = (await chrome.permissions.getAll()).origins || [];
    const patterns = [...new Set(granted.filter(origin => /^https:\/\/[^*]+\/\*$/.test(origin)))].sort();
    const previous = (await chrome.scripting.getRegisteredContentScripts()).filter(script => script.id.startsWith("cofre-site-"));
    const wanted = [];
    for (const pattern of patterns) {
      const bytes = new TextEncoder().encode(pattern);
      const digest = [...new Uint8Array(await crypto.subtle.digest("SHA-256", bytes))].map(b => b.toString(16).padStart(2, "0")).join("");
      wanted.push({ id: "cofre-site-" + digest.slice(0, 24), matches: [pattern], js: ["content.js"],
        allFrames: false, runAt: "document_idle", persistAcrossSessions: true });
    }
    const removed = previous.filter(script => !wanted.some(item => item.id === script.id)).map(script => script.id);
    if (removed.length) await chrome.scripting.unregisterContentScripts({ ids: removed });
    const added = wanted.filter(script => !previous.some(item => item.id === script.id));
    if (added.length) await chrome.scripting.registerContentScripts(added);
  });
  return registration;
}
function native(request) {
  return new Promise((resolve, reject) => {
    const port = chrome.runtime.connectNative(HOST);
    let settled = false;
    const finish = (error, response) => {
      if (settled) return;
      settled = true; clearTimeout(timeout); port.disconnect();
      error ? reject(error) : resolve(response);
    };
    const timeout = setTimeout(() => finish(new Error("Tempo de confirmação esgotado.")), 85000);
    port.onMessage.addListener(response => finish(response?.ok ? null : new Error(response?.error || "Resposta inválida do Cofre."), response));
    port.onDisconnect.addListener(() => {
      const error = chrome.runtime.lastError;
      finish(new Error(error ? "Conexão indisponível. Abra o Cofre e registre a integração nativa." : "O Cofre encerrou a conexão."));
    });
    try { port.postMessage(request); } catch (error) { finish(error); }
  });
}
function callerKind(sender) {
  if (sender.id !== chrome.runtime.id) return null;
  if (!sender.tab && sender.url === chrome.runtime.getURL("popup.html")) return "popup";
  if (sender.tab?.id && sender.frameId === 0 && sender.url?.startsWith("https://")
    && (!sender.origin || sender.origin === new URL(sender.url).origin)) return "inline";
  return null;
}
async function activePage(sender, kind) {
  const tab = kind === "inline" ? await chrome.tabs.get(sender.tab.id)
    : (await chrome.tabs.query({ active: true, currentWindow: true }))[0];
  if (!tab?.id || !tab.url || new URL(tab.url).protocol !== "https:")
    throw new Error("Abra uma página HTTPS para usar o Cofre.");
  if (kind === "inline" && (sender.url !== tab.url || tab.active === false))
    throw new Error("A página mudou ou não está ativa. Clique no campo novamente.");
  if (kind === "inline" && !await chrome.permissions.contains({ origins: ["https://" + new URL(tab.url).hostname + "/*"] }))
    throw new Error("O acesso a este site foi removido. Ative os ícones pela extensão novamente.");
  return tab;
}
async function handle(message, sender, kind) {
  const tab = await activePage(sender, kind);
  const url = tab.url, origin = new URL(url).origin;
  const inline = kind === "inline";
  const allowed = inline ? ["inline.inspect", "inline.list", "inline.fill", "inline.generate", "inline.apply", "inline.save"]
    : ["list", "read", "fill", "save", "attach"];
  if (!allowed.includes(message.action)) throw new Error("Ação inválida para esta origem.");
  if ((inline || !["list", "attach"].includes(message.action)) && message.pageUrl !== url)
    throw new Error("A página mudou. Atualize a lista antes de continuar.");
  if (inline && (typeof message.field !== "string" || message.field.length > 100))
    throw new Error("Campo inválido.");
  const target = inline && sender.documentId ? { tabId: tab.id, documentIds: [sender.documentId] } : { tabId: tab.id, frameIds: [0] };
  const operate = async (operation, values = {}) => {
    const current = await chrome.tabs.get(tab.id);
    if (current.url !== url) throw new Error("A página mudou durante a confirmação.");
    const [result] = await chrome.scripting.executeScript({ target, func: formOperation,
      args: [url, operation, { ...values, ...(inline ? { field: message.field } : {}) }] });
    if (!result?.result) throw new Error("Não foi possível acessar este formulário.");
    return result.result;
  };
  if (message.action === "attach") {
    const pattern = "https://" + new URL(url).hostname + "/*";
    if (!await chrome.permissions.contains({ origins: [pattern] }))
      throw new Error("Autorize este site no botão Ativar ícones para continuar.");
    await registerSites();
    await chrome.scripting.executeScript({ target, files: ["content.js"] });
    const [result] = await chrome.scripting.executeScript({ target, func: () => ({
      fieldCount: [...(globalThis.__cofreInlineFields?.values() || [])].filter(field =>
        field.isConnected && field.type === "password" && !field.disabled && !field.readOnly
        && field.getClientRects().length > 0 && getComputedStyle(field).visibility === "visible").length
    }) });
    return { ok: true, fieldCount: result?.result?.fieldCount ?? 0 };
  }
  if (message.action === "inline.inspect") {
    const info = await operate("inspect");
    let options = defaults;
    try { options = validateOptions((await chrome.storage.local.get("generatorOptions")).generatorOptions); } catch { /* Restore defaults if settings are invalid. */ }
    return { ok: true, ...info, options, origin };
  }
  if (["list", "inline.list"].includes(message.action)) {
    const response = await native({ action: "list", url: origin });
    return { ...response, origin, pageUrl: url };
  }
  if (message.action === "read") return { ok: true, ...await operate("read") };
  if (["fill", "inline.fill"].includes(message.action)) {
    const info = await operate("inspect");
    if (info.kind !== "login") throw new Error("Este formulário é de cadastro ou troca de senha. Use o gerador.");
    const response = await native({ action: "fill", url: origin, id: message.id });
    if (response.origin !== origin) throw new Error("A resposta não corresponde ao site.");
    if (!(await operate("fill", { username: response.username, password: response.password })).filled)
      throw new Error("Não foi possível preencher este formulário.");
    return { ok: true }; // Never send vault secrets to the content script or popup.
  }
  if (message.action === "inline.generate") {
    await operate("inspect");
    const options = validateOptions(message.options);
    const password = generatePassword(options);
    await chrome.storage.local.set({ generatorOptions: options }); // Preferences only, never passwords.
    return { ok: true, password };
  }
  if (message.action === "inline.apply") {
    if (typeof message.password !== "string" || message.password.length < 12 || message.password.length > 128)
      throw new Error("Senha gerada inválida.");
    await operate("generated", { password: message.password });
    return { ok: true };
  }
  if (message.action === "inline.save") {
    const values = await operate("read");
    const id = typeof message.id === "string" ? message.id : "";
    return await native({ action: id ? "update" : "save", url: origin, id,
      title: new URL(url).hostname, username: values.username, password: values.password });
  }
  if (message.action === "save") return await native({ action: "save", url: origin, title: message.title,
    username: message.username, password: message.password });
  throw new Error("Ação inválida.");
}
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  const kind = callerKind(sender);
  if (!kind || !message || typeof message.action !== "string") return false;
  const independent = message.action === "attach";
  if (busy && !independent) { reply({ ok: false, error: "Já existe uma operação em andamento no Cofre." }); return false; }
  if (!independent) busy = true;
  chrome.action.setBadgeText({ text: "" });
  handle(message, sender, kind).then(result => {
    if (["fill", "save", "inline.fill", "inline.save", "inline.apply"].includes(message.action)) {
      chrome.action.setBadgeBackgroundColor({ color: "#4d613b" });
      chrome.action.setBadgeText({ text: "OK" });
      chrome.action.setTitle({ title: "Cofre — operação concluída" });
    }
    reply(result);
  }).catch(error => {
    chrome.action.setBadgeBackgroundColor({ color: "#ad4646" });
    chrome.action.setBadgeText({ text: "!" });
    chrome.action.setTitle({ title: "Cofre — " + error.message });
    reply({ ok: false, error: error.message });
  }).finally(() => { if (!independent) busy = false; });
  return true;
});
// Content scripts request preferences through the worker; they cannot read storage directly.
chrome.storage.local.setAccessLevel({ accessLevel: "TRUSTED_CONTEXTS" }).catch(() => {});
chrome.permissions.onAdded.addListener(() => { registerSites().catch(() => {}); });
chrome.permissions.onRemoved.addListener(() => { registerSites().catch(() => {}); });
registerSites().catch(() => {});
