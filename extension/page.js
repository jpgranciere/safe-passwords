// Serialized into Chrome's ISOLATED world. Keep this function self-contained.
export function formOperation(expectedUrl, operation, data = {}) {
  const fail = text => { throw new Error(text); };
  const page = () => {
    if (window.top !== window || location.href !== expectedUrl || location.protocol !== "https:")
      fail("A página mudou. Abra o Cofre novamente.");
  };
  page();
  const visible = el => el && el.isConnected && !el.disabled && !el.readOnly
    && el.getClientRects().length > 0 && !["hidden", "collapse"].includes(getComputedStyle(el).visibility)
    && getComputedStyle(el).display !== "none" && getComputedStyle(el).opacity !== "0";
  const all = [...document.querySelectorAll('input[type="password"]')].filter(visible);
  const target = data.field ? globalThis.__cofreInlineFields?.get(data.field) : null;
  if (data.field && !visible(target)) fail("O campo mudou. Clique no ícone novamente.");
  const field = target || (all.length === 1 ? all[0] : null);
  if (!field) fail("Escolha um campo usando o ícone do Cofre ao lado da senha.");
  const form = field.form;
  const scope = form || document;
  const passwords = all.filter(el => el.form === form);
  const ac = el => (el.autocomplete || "").toLowerCase().split(/\s+/);
  const newFields = passwords.filter(el => ac(el).includes("new-password"));
  const currentFields = passwords.filter(el => ac(el).includes("current-password")
    || /(?:old|current|atual|antiga)/i.test((el.name || "") + " " + (el.id || "")));
  const hints = [...scope.querySelectorAll('button, input[type="submit"], h1, h2, h3')]
    .map(el => el.textContent || el.value || "").join(" ").normalize("NFD").replace(/[\u0300-\u036f]/g, "");
  const signupHint = /(?:sign\s*up|register|create\s+(?:an?\s+)?account|cadastr|criar\s+(?:uma\s+)?conta|registrar)/i
    .test(hints + " " + new URL(expectedUrl).pathname);
  const kind = newFields.length ? (currentFields.length ? "change" : "signup")
    : passwords.length > 1 ? (currentFields.length ? "change" : "signup")
    : ac(field).includes("current-password") ? "login" : signupHint ? "signup" : "login";
  if (!passwords.length) fail("Não encontrei um campo de senha neste formulário.");
  const users = [...scope.querySelectorAll("input")].filter(el => visible(el)
    && ["text", "email", "tel"].includes(el.type)
    && (el.compareDocumentPosition(passwords[0]) & Node.DOCUMENT_POSITION_FOLLOWING));
  const user = users.find(el => ac(el).includes("username")) || users.find(el => el.type === "email") || users.at(-1);
  const output = kind === "login" ? passwords : passwords.filter(el => !currentFields.includes(el));
  const safe = () => {
    page();
    if (!visible(field) || field.form !== form) fail("O formulário mudou.");
    if (form && new URL(form.action || location.href, location.href).origin !== location.origin)
      fail("O formulário envia para outro domínio. Operação bloqueada.");
    for (const button of scope.querySelectorAll("button[formaction], input[formaction]")) {
      const action = button.getAttribute?.("formaction");
      if (action && new URL(action, location.href).origin !== location.origin)
        fail("Um botão do formulário envia para outro domínio. Operação bloqueada.");
    }
  };
  safe();
  if (operation === "inspect") {
    return { kind, username: user?.value || "", minLength: Math.max(0, ...output.map(el => el.minLength || 0)),
      maxLength: Math.min(128, ...output.map(el => el.maxLength > 0 ? el.maxLength : 128)),
      hasPattern: output.some(el => !!el.pattern) };
  }
  if (operation === "read") {
    const source = kind === "login" ? passwords[0] : output[0];
    if (!source || !visible(source)) fail("Escolha a nova senha antes de salvar.");
    if (output.some(el => el.value !== source.value)) fail("A senha e a confirmação precisam ser iguais.");
    return { username: user?.value || "", password: source.value };
  }
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
  const set = (el, value) => {
    safe();
    if (!visible(el) || el.form !== form) fail("O formulário mudou. Solicite novamente.");
    setter.call(el, value);
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
  };
  if (operation === "fill") {
    if (kind !== "login" || passwords.length !== 1 || newFields.length)
      fail("Este é um cadastro ou troca de senha. Use Gerar senha.");
    if (user && data.username) set(user, data.username);
    if (passwords[0].type !== "password" || ac(passwords[0]).includes("new-password"))
      fail("O campo mudou durante o preenchimento.");
    set(passwords[0], data.password);
  } else if (operation === "generated") {
    if (!output.length || output.length > 2 || (kind === "login" && passwords.length !== 1))
      fail("Não foi possível identificar onde colocar a nova senha.");
    if (output.some(el => el.maxLength > 0 && data.password.length > el.maxLength
      || el.minLength > data.password.length)) fail("Ajuste o tamanho às regras do site.");
    for (const el of output) set(el, data.password); // Preserve the old/current password.
  } else fail("Ação de formulário inválida.");
  return { filled: true }; // The user submits the form.
}
export function readLogin(url) { return formOperation(url, "read"); }
export function fillLogin(url, username, password) { return formOperation(url, "fill", { username, password }); }
