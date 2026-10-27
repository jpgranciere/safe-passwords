const $ = id => document.getElementById(id);
let pageUrl = "";
const status = text => { $("status").textContent = text; };
async function request(payload) {
  const response = await chrome.runtime.sendMessage({ ...payload, pageUrl });
  if (!response?.ok) throw new Error(response?.error || "Falha na conexão com o Cofre.");
  return response;
}
async function run(work) {
  document.querySelectorAll("button").forEach(b => { b.disabled = true; });
  try { await work(); } catch (error) { status(error.message); }
  finally { document.querySelectorAll("button").forEach(b => { b.disabled = false; }); }
}
async function refresh() {
  pageUrl = ""; $("entries").replaceChildren();
  const response = await request({ action: "list" });
  pageUrl = response.pageUrl;
  $("site").textContent = response.origin;
  $("title").value = new URL(response.origin).hostname;
  for (const entry of response.entries) {
    const button = document.createElement("button");
    const title = document.createElement("strong"); title.textContent = entry.title;
    const user = document.createElement("span"); user.textContent = entry.username || "Sem usuário";
    button.append(title, user);
    button.addEventListener("click", () => run(async () => {
      status("Confirme o preenchimento no aplicativo Cofre. Depois volte ao navegador.");
      await request({ action: "fill", id: entry.id }); status("Login preenchido.");
    }));
    $("entries").append(button);
  }
  status(response.entries.length ? "Selecione um login para preencher." : "Nenhum login cadastrado para esta origem exata.");
}
$("refresh").addEventListener("click", () => run(refresh));
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
run(refresh);
