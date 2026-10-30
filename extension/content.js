(() => {
  if (window.top !== window || location.protocol !== "https:" || globalThis.__cofreInlineStarted) return;
  globalThis.__cofreInlineStarted = true;
  const fields = globalThis.__cofreInlineFields = new Map();
  const items = new Map();
  const host = document.createElement("cofre-assistant");
  host.style.cssText = "all:initial!important;position:fixed!important;inset:0!important;z-index:2147483647!important;pointer-events:none!important;display:block!important;";
  const root = host.attachShadow({ mode: "closed" });
  // Static, extension-owned markup only. Site/account strings are always assigned through textContent.
  root.innerHTML = `
    <style>
      :host { all: initial; } * { box-sizing: border-box; }
      button, input, select { font: inherit; } button { cursor: pointer; }
      .field { position: fixed; width: 26px; height: 26px; padding: 3px; border: 0; background: #242a20;
        color: #c4d3a8; pointer-events: auto; display: grid; place-items: center; box-shadow: 0 0 0 1px #ffffff38; }
      .field:hover, .field:focus-visible { background: #4d613b; color: white; outline: 2px solid #bac99a; }
      svg { width: 20px; height: 20px; fill: currentColor; flex: none; }
      .panel { position: fixed; z-index: 1; width: 344px; max-height: min(620px, calc(100vh - 24px)); overflow: auto;
        background: #1a1c18; color: #f1f1e9; border: 1px solid #4d5545; box-shadow: 0 12px 44px #0006;
        padding: 18px; pointer-events: auto; font: 13px/1.5 'Segoe UI', system-ui, sans-serif; color-scheme: dark; }
      [hidden] { display: none !important; } header { display: flex; align-items: center; gap: 10px; }
      header svg { width: 30px; height: 30px; color: #bac99a; } strong { font-size: 16px; letter-spacing: -.3px; }
      .close { margin-left: auto; border: 0; color: #a8aea1; background: transparent; font-size: 23px; padding: 0 4px; }
      .site { font-size: 11px; color: #bac99a; overflow-wrap: anywhere; margin: 8px 0 4px; }
      .hint, .status { color: #a8aea1; font-size: 12px; margin: 8px 0; }
      .status { color: #d4dfc2; min-height: 18px; } .status.error { color: #ffb4ab; }
      nav { display: flex; gap: 4px; border-bottom: 1px solid #353930; margin: 14px 0; }
      nav button { flex: 1; padding: 9px 4px; border: 0; border-bottom: 2px solid transparent; background: transparent; color: #a8aea1; }
      nav button[aria-selected=true] { border-color: #bac99a; color: #e6efda; }
      .entry { width: 100%; display: block; text-align: left; padding: 10px; background: #242820; border: 1px solid #353930; color: #f1f1e9; margin: 6px 0; }
      .entry:hover { border-color: #bac99a; } .entry b, .entry span { display: block; overflow-wrap: anywhere; }
      .entry span { color: #a8aea1; font-size: 12px; } .preview { width: 100%; background: #242820; color: #e6efda;
        border: 1px solid #353930; padding: 10px; font: 14px/1.6 Consolas, monospace; margin: 0 0 10px; }
      .preview-row { display: flex; align-items: stretch; margin: 0 0 10px; }
      .preview-row .preview { flex: 1; min-width: 0; margin: 0; }
      .visibility { width: 42px; border: 1px solid #454c3e; border-left: 0; background: #242820; color: #e6efda; display: grid; place-items: center; }
      .visibility:hover { background: #4d613b; }
      .actions { display: flex; gap: 8px; } .actions button, .save { flex: 1; padding: 10px; background: #bac99a; color: #192111; border: 0; font-weight: 600; }
      .actions .secondary { background: #2a2f25; color: #e6efda; } button:disabled { opacity: .45; cursor: wait; }
      details { margin: 12px 0; } summary { cursor: pointer; color: #c4d3a8; } .settings { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 10px; }
      label { font-size: 12px; color: #c6cbbc; } label.full { grid-column: 1/-1; } input[type=checkbox] { accent-color: #bac99a; }
      input[type=number], input[type=text], select { width: 100%; background: #242820; border: 1px solid #454c3e; color: #f1f1e9; padding: 8px; }
      .savebox { border-top: 1px solid #353930; margin-top: 16px; padding-top: 12px; } .save { width: 100%; margin-top: 8px; }
      button:focus-visible, input:focus-visible, select:focus-visible, summary:focus-visible { outline: 2px solid #bac99a; outline-offset: 2px; }
    </style>
    <section class="panel" hidden role="dialog" aria-label="Cofre — assistente de senhas">
      <header><svg viewBox="0 0 32 32" aria-hidden="true"><path d="M9 3h17v6H12l-3 3v8l3 3h14v6H9l-6-6V9z"/><path d="M17 13h12v6H17z"/></svg><strong>Cofre</strong><button class="close" title="Fechar" aria-label="Fechar">×</button></header>
      <p class="site"></p><p class="hint"></p>
      <nav role="tablist" aria-label="Ações"><button id="accounts-tab" role="tab" aria-controls="accounts" aria-selected="true">Contas do site</button><button id="generator-tab" role="tab" aria-controls="generator" aria-selected="false">Gerar senha</button></nav>
      <section id="accounts" role="tabpanel" aria-labelledby="accounts-tab"><div id="entries"></div><button id="refresh" class="entry">Atualizar contas</button></section>
      <section id="generator" role="tabpanel" aria-labelledby="generator-tab" hidden>
        <div class="preview-row"><input class="preview" type="password" aria-label="Senha sugerida" readonly spellcheck="false" autocomplete="off"><button id="toggle-preview" class="visibility" type="button" aria-label="Mostrar senha" title="Mostrar senha" aria-pressed="false"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5c-5 0-9 3.5-11 7 2 3.5 6 7 11 7s9-3.5 11-7c-2-3.5-6-7-11-7zm0 12a5 5 0 1 1 0-10 5 5 0 0 1 0 10zm0-2a3 3 0 1 0 0-6 3 3 0 0 0 0 6z"/></svg></button></div>
        <div class="actions"><button id="use">Usar esta senha</button><button id="regenerate" class="secondary">Gerar senha</button></div>
        <details><summary>Personalizar senha</summary><div class="settings">
          <label class="full">Tamanho <input id="length" type="number" min="12" max="128" value="20"></label>
          <label><input id="upper" type="checkbox" checked> Maiúsculas</label><label><input id="lower" type="checkbox" checked> Minúsculas</label>
          <label><input id="digits" type="checkbox" checked> Números</label><label><input id="symbols" type="checkbox" checked> Símbolos</label>
          <label class="full"><input id="avoidAmbiguous" type="checkbox" checked> Evitar O, 0, I, l e 1</label>
          <label class="full">Símbolos permitidos<input id="symbolChars" type="text" maxlength="32" value="!@#$%&*_-+?"></label>
        </div><p class="hint">As preferências ficam salvas neste navegador.</p></details>
        <p id="rules" class="hint"></p>
      </section>
      <div class="savebox"><label>Salvar o valor deste formulário como<select id="save-target"><option value="">Criar nova conta no Cofre</option><option value="new-token">Criar token no Cofre</option></select></label><button class="save" id="save">Salvar no Cofre</button></div>
      <p class="status" role="status" aria-live="polite"></p>
    </section>`;
  const $ = selector => root.querySelector(selector);
  const panel = $(".panel"), status = $(".status"), preview = $(".preview");
  let active = null, pageUrl = "", generation = 0, working = false, scanPending = false, positioned = false;
  let kind = "login", options = {}, timer;
  const visible = field => field.isConnected && !field.disabled && !field.readOnly && field.type === "password"
    && field.getClientRects().length && getComputedStyle(field).visibility === "visible" && getComputedStyle(field).display !== "none"
    && getComputedStyle(field).opacity !== "0";
  const say = (text, error = false) => { status.textContent = text; status.classList.toggle("error", error); };
  function close() {
    generation++; active = null; panel.hidden = true; preview.value = ""; clearTimeout(timer); setPreviewVisibility(false);
    $("#entries").replaceChildren(); $("#save-target").replaceChildren(new Option("Criar nova conta no Cofre", ""), new Option("Criar token no Cofre", "new-token"));
  }
  async function request(action, extra = {}) {
    const context = generation;
    let response;
    try {
      if (!globalThis.chrome?.runtime?.sendMessage || !chrome.runtime.id) throw new Error("context invalidated");
      response = await chrome.runtime.sendMessage({ action: "inline." + action, pageUrl, field: active?.token, ...extra });
    } catch (error) {
      if (!globalThis.chrome?.runtime?.id || /context invalidated/i.test(error?.message || ""))
        throw new Error("A extensão foi atualizada. Recarregue esta página e abra o Cofre novamente.");
      throw error;
    }
    if (context !== generation || !active || location.href !== pageUrl) throw new Error("O formulário mudou. Abra o Cofre novamente.");
    if (!response?.ok) throw new Error(response?.error || "Recarregue a página e abra o Cofre.");
    return response;
  }
  async function run(work) {
    if (working) return;
    working = true;
    const context = generation;
    root.querySelectorAll(".panel button:not(.close), .settings input, select").forEach(el => { el.disabled = true; });
    try { await work(); } catch (error) { if (context === generation) say(error.message, true); }
    finally {
      working = false;
      root.querySelectorAll(".panel button, .settings input, select").forEach(el => { el.disabled = false; });
      if (kind !== "login") root.querySelectorAll("#entries button").forEach(el => { el.disabled = true; });
      reposition();
    }
  }
  function positionPanel() {
    if (!active) return;
    const rect = active.field.getBoundingClientRect();
    if (location.href !== pageUrl || !visible(active.field) || rect.bottom < 0 || rect.top > innerHeight) { close(); return; }
    const width = Math.min(344, innerWidth - 24);
    panel.style.width = width + "px";
    panel.style.left = Math.max(12, Math.min(rect.right - width, innerWidth - width - 12)) + "px";
    const height = panel.getBoundingClientRect().height;
    panel.style.top = Math.max(12, Math.min(rect.bottom + 8, innerHeight - height - 12)) + "px";
  }
  function reposition() {
    positioned = false;
    for (const [field, item] of items) {
      const r = field.getBoundingClientRect();
      item.button.hidden = !visible(field) || r.width < 70 || r.height < 24 || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth;
      // Some sites put a wide "show password" button over the right end of the input.
      // Keep our icon before that button instead of covering its eye icon.
      let right = r.right - 8;
      for (const control of (field.parentElement?.parentElement || field.parentElement)?.querySelectorAll("*") || []) {
        if (control === field || control.contains(field)) continue;
        const b = control.getBoundingClientRect();
        const middle = r.top + r.height / 2;
        if (b.width > 0 && b.width <= Math.min(160, r.width / 2)
          && b.left > r.left + r.width / 2 && b.left < r.right && b.right <= r.right + 12
          && b.right > r.right - Math.min(160, r.width * 0.4)
          && b.bottom > middle && b.top < middle) right = Math.min(right, b.left - 6);
      }
      item.button.style.left = Math.max(r.left + 4, Math.min(innerWidth - 28, right - 26)) + "px";
      item.button.style.top = (r.top + (r.height - 26) / 2) + "px";
    }
    positionPanel();
  }
  function schedulePosition() { if (!positioned) { positioned = true; requestAnimationFrame(reposition); } }
  function readOptions() {
    return { length: Number($("#length").value), symbolChars: $("#symbolChars").value,
      ...Object.fromEntries(["upper", "lower", "digits", "symbols", "avoidAmbiguous"].map(key => [key, $("#" + key).checked])) };
  }
  function setPreviewVisibility(show) {
    preview.type = show ? "text" : "password";
    const button = $("#toggle-preview");
    const label = show ? "Ocultar senha" : "Mostrar senha";
    button.setAttribute("aria-label", label); button.setAttribute("aria-pressed", String(show)); button.title = label;
  }
  async function generate() {
    preview.value = "";
    const response = await request("generate", { options: readOptions() });
    preview.value = response.password;
    clearTimeout(timer); timer = setTimeout(() => { preview.value = ""; say("Sugestão expirada. Clique em Gerar senha."); }, 120000);
    say("Confira as regras do site antes de usar a sugestão.");
  }
  function tab(name) {
    $("#accounts").hidden = name !== "accounts"; $("#generator").hidden = name !== "generator";
    $("#accounts-tab").setAttribute("aria-selected", String(name === "accounts"));
    $("#generator-tab").setAttribute("aria-selected", String(name === "generator"));
    reposition();
  }
  async function loadAccounts() {
    $("#entries").replaceChildren();
    const response = await request("list");
    const selection = $("#save-target");
    const previous = selection.value;
    selection.replaceChildren(new Option("Criar nova conta no Cofre", ""), new Option("Criar token no Cofre", "new-token"));
    for (const entry of response.entries) {
      const option = new Option("Atualizar: " + entry.title + " — " + entry.username, entry.id); selection.add(option);
      const button = document.createElement("button"); button.className = "entry";
      const title = document.createElement("b"); title.textContent = entry.title;
      const user = document.createElement("span"); user.textContent = entry.username || "Sem usuário";
      button.append(title, user);
      button.addEventListener("click", event => {
        if (!event.isTrusted) return;
        run(async () => {
          say("Preenchendo… Se o cofre estiver bloqueado, desbloqueie no aplicativo para continuar.");
          await request("fill", { id: entry.id }); selection.value = entry.id;
          say("Login preenchido. Agora você pode entrar no site.");
        });
      });
      if (kind !== "login") { button.disabled = true; user.textContent += " · use Contas ao fazer login"; }
      $("#entries").append(button);
    }
    if ([...selection.options].some(option => option.value === previous)) selection.value = previous;
    if (!response.entries.length) {
      const text = document.createElement("p"); text.className = "hint";
      text.textContent = "Nenhuma conta salva para este domínio exato."; $("#entries").append(text);
    }
    if (kind === "login") say(response.entries.length ? "Escolha a conta para preencher." : "Você pode salvar o login preenchido usando o botão abaixo.");
  }
  async function open(item) {
    if (working) return;
    close(); active = item; pageUrl = location.href;
    panel.hidden = false; $(".site").textContent = location.origin;
    say("Identificando formulário…"); positionPanel();
    run(async () => {
      const info = await request("inspect"); kind = info.kind; options = info.options;
      $(".hint").textContent = kind === "login" ? "Login identificado · selecione uma conta."
        : kind === "change" ? "Troca de senha · a senha atual será preservada." : "Cadastro identificado · crie uma senha exclusiva.";
      for (const key of ["upper", "lower", "digits", "symbols", "avoidAmbiguous"]) $("#" + key).checked = options[key];
      $("#length").value = Math.max(12, info.minLength, Math.min(options.length, info.maxLength));
      $("#symbolChars").value = options.symbolChars;
      $("#rules").textContent = `Site: mínimo ${info.minLength || "não informado"}, máximo ${info.maxLength} caracteres.`
        + (info.hasPattern ? " O site também exige um formato específico; confira as instruções dele." : "");
      tab(kind === "login" ? "accounts" : "generator");
      if (kind !== "login") await generate();
      try { await loadAccounts(); } catch (error) { say(error.message + (kind !== "login" ? " A geração continua disponível." : ""), true); }
    });
  }
  function scan() {
    scanPending = false;
    if (!host.isConnected && document.documentElement) document.documentElement.append(host);
    for (const [field, item] of items) {
      if (!field.isConnected) { item.button.remove(); items.delete(field); fields.delete(item.token); if (active === item) close(); }
    }
    for (const field of document.querySelectorAll('input[type="password"]')) {
      if (items.has(field)) continue;
      const token = crypto.randomUUID();
      const button = document.createElement("button"); button.className = "field"; button.type = "button";
      button.title = "Cofre — contas e senhas fortes"; button.setAttribute("aria-label", button.title);
      button.innerHTML = '<svg viewBox="0 0 32 32" aria-hidden="true"><path d="M9 3h17v6H12l-3 3v8l3 3h14v6H9l-6-6V9z"/><path d="M17 13h12v6H17z"/></svg>';
      const item = { token, field, button }; items.set(field, item); fields.set(token, field); root.append(button);
      button.addEventListener("click", event => { if (event.isTrusted) active === item && !panel.hidden ? close() : open(item); });
    }
    reposition();
  }
  function scheduleScan() { if (!scanPending) { scanPending = true; setTimeout(scan, 120); } }
  $(".close").addEventListener("click", close);
  $("#accounts-tab").addEventListener("click", event => { if (event.isTrusted) run(async () => { tab("accounts"); await loadAccounts(); }); });
  $("#generator-tab").addEventListener("click", event => { if (event.isTrusted) run(async () => { tab("generator"); await generate(); }); });
  $("#regenerate").addEventListener("click", event => { if (event.isTrusted) run(generate); });
  $("#toggle-preview").addEventListener("click", event => { if (event.isTrusted) setPreviewVisibility(preview.type === "password"); });
  $(".settings").addEventListener("change", event => { if (event.isTrusted) run(generate); });
  $("#refresh").addEventListener("click", event => { if (event.isTrusted) run(loadAccounts); });
  $("#use").addEventListener("click", event => {
    if (!event.isTrusted) return;
    run(async () => {
      if (!preview.value) await generate();
      await request("apply", { password: preview.value }); preview.value = ""; clearTimeout(timer);
      say("Senha preenchida, incluindo a confirmação. Salve no Cofre antes de enviar o cadastro para guardar a senha.");
    });
  });
  $("#save").addEventListener("click", event => {
    if (!event.isTrusted) return;
    run(async () => {
      say("Confirme os dados no aplicativo Cofre.");
      const target = $("#save-target").value;
      await request("save", { id: target === "new-token" ? "" : target, kind: target === "new-token" ? "token" : "login" });
      say(target === "new-token" ? "Token salvo em Tokens e chaves." : "Conta salva no Cofre."); preview.value = "";
    });
  });
  document.addEventListener("pointerdown", event => {
    if (event.composedPath().includes(host)) return;
    if (event.target === active?.field) return;
    close();
  }, true);
  document.addEventListener("keydown", event => { if (event.key === "Escape") close(); });
  window.addEventListener("pagehide", close);
  window.addEventListener("hashchange", close);
  window.addEventListener("popstate", close);
  window.addEventListener("scroll", schedulePosition, true);
  window.addEventListener("resize", schedulePosition);
  // Watch page forms, including React/Vue forms inserted after navigation. Shadow UI changes do not feed this observer.
  new MutationObserver(scheduleScan).observe(document.documentElement, { childList: true, subtree: true,
    attributes: true, attributeFilter: ["type", "disabled", "readonly", "hidden", "style", "class", "autocomplete"] });
  new ResizeObserver(schedulePosition).observe(document.documentElement);
  scan();
})();
