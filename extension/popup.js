const $ = id => document.getElementById(id);
let pageUrl = "", sitePattern = "", attaching = false;
const status = text => { $("status").textContent = text; };
const fieldStatus = text => { $("field-status").textContent = text; };
const contextReady = chrome.tabs.query({ active: true, currentWindow: true }).then(([tab]) => {
  if (!tab?.url || new URL(tab.url).protocol !== "https:") throw new Error("Abra uma página HTTPS para usar o Cofre.");
  const url = new URL(tab.url);
  pageUrl = tab.url; sitePattern = "https://" + url.hostname + "/*";
  $("site").textContent = url.origin; $("title").value = url.hostname;
});
async function request(payload) {
  const response = await chrome.runtime.sendMessage({ ...payload, pageUrl });
  if (!response?.ok) throw new Error(response?.error || "Falha na conexão com o Cofre.");
  return response;
}
async function run(work) {
  // A Windows approval must never disable site activation, which does not require the vault.
  document.querySelectorAll("button:not(#attach)").forEach(b => { b.disabled = true; });
  try { await work(); } catch (error) { status(error.message); }
  finally { document.querySelectorAll("button:not(#attach)").forEach(b => { b.disabled = false; }); }
}
async function attachIcons() {
  const response = await request({ action: "attach" });
  $("attach").textContent = "Reativar ícones neste site";
  fieldStatus(response.fieldCount > 0
    ? `Ícones ativos em ${response.fieldCount} campo(s) de senha. Feche este popup para vê-los na página.`
    : "Acesso ativado, mas nenhum campo de senha visível foi encontrado. Campos dentro de iframe não são atendidos.");
}
async function syncIcons() {
  try {
    await contextReady;
    if (await chrome.permissions.contains({ origins: [sitePattern] })) await attachIcons();
    else fieldStatus("Os ícones ainda não estão ativados. Clique no botão acima e autorize este site.");
  } catch (error) { fieldStatus(error.message); }
}
async function refresh() {
  await contextReady;
  $("entries").replaceChildren();
  const response = await request({ action: "list" });
  pageUrl = response.pageUrl;
  for (const entry of response.entries) {
    const button = document.createElement("button");
    const title = document.createElement("strong"); title.textContent = entry.title;
    const user = document.createElement("span"); user.textContent = entry.username || "Sem usuário";
    button.append(title, user);
    button.addEventListener("click", () => run(async () => {
      status("Preenchendo… Se o cofre estiver bloqueado, desbloqueie no aplicativo para continuar.");
      await request({ action: "fill", id: entry.id }); status("Login preenchido. Volte à página para entrar.");
    }));
    $("entries").append(button);
  }
  status(response.entries.length ? "Selecione uma conta para preencher o login." : "Nenhum login cadastrado para esta origem exata.");
}
$("refresh").addEventListener("click", () => run(refresh));
$("attach").addEventListener("click", async () => {
  if (attaching) return;
  if (!sitePattern) { fieldStatus("Abra uma página HTTPS e abra a extensão novamente."); return; }
  attaching = true; $("attach").disabled = true;
  try {
    // Called directly in the user's click handler to preserve Chrome's permission-request gesture.
    const granted = await chrome.permissions.request({ origins: [sitePattern] });
    if (!granted) throw new Error("Acesso ao site não autorizado. Clique novamente quando quiser ativar os ícones.");
    await attachIcons();
  } catch (error) { fieldStatus(error.message); }
  finally { attaching = false; $("attach").disabled = false; }
});
$("read").addEventListener("click", () => run(async () => {
  const result = await request({ action: "read" });
  $("username").value = result.username; $("password").value = result.password;
  status("Formulário lido. Confira os campos e clique em salvar.");
}));
$("save").addEventListener("submit", event => {
  event.preventDefault();
  run(async () => {
    status("Confirme no aplicativo Cofre. Uma nova entrada será criada.");
    await request({ action: "save", title: $("title").value, username: $("username").value, password: $("password").value });
    $("password").value = ""; await refresh(); status("Entrada salva.");
  });
});
window.addEventListener("pagehide", () => { $("password").value = ""; });
syncIcons();
run(refresh);
