// These functions are serialized by executeScript into the isolated world of the top frame.
export function readLogin(expectedUrl) {
  if (window.top !== window || location.href !== expectedUrl || location.protocol !== "https:")
    throw new Error("A página mudou. Abra a extensão novamente.");
  const visible = el => !el.disabled && !el.readOnly && el.getClientRects().length > 0
    && getComputedStyle(el).visibility !== "hidden" && getComputedStyle(el).display !== "none";
  const passwords = [...document.querySelectorAll('input[type="password"]')].filter(visible);
  if (passwords.length !== 1) throw new Error("Use um formulário com um único campo de senha visível.");
  const password = passwords[0];
  const form = password.form;
  if (form && new URL(form.action || location.href, location.href).origin !== location.origin)
    throw new Error("O formulário envia dados para outra origem. Operação bloqueada.");
  const users = [...(form || document).querySelectorAll('input')].filter(el => visible(el)
    && ["text", "email", "tel"].includes(el.type) && (el.compareDocumentPosition(password) & Node.DOCUMENT_POSITION_FOLLOWING));
  const user = users.find(el => el.autocomplete === "username") || users.find(el => el.type === "email") || users.at(-1);
  return { username: user?.value || "", password: password.value };
}

export function fillLogin(expectedUrl, username, password) {
  if (window.top !== window || location.href !== expectedUrl || location.protocol !== "https:")
    throw new Error("A página mudou. Solicite o preenchimento novamente.");
  const visible = el => !el.disabled && !el.readOnly && el.getClientRects().length > 0
    && getComputedStyle(el).visibility !== "hidden" && getComputedStyle(el).display !== "none";
  const passwords = [...document.querySelectorAll('input[type="password"]')].filter(visible);
  if (passwords.length !== 1 || passwords[0].autocomplete === "new-password")
    throw new Error("Não encontrei um formulário de login com um único campo de senha.");
  const field = passwords[0];
  const form = field.form;
  if (form && new URL(form.action || location.href, location.href).origin !== location.origin)
    throw new Error("O formulário aponta para outra origem. Preenchimento bloqueado.");
  const users = [...(form || document).querySelectorAll('input')].filter(el => visible(el)
    && ["text", "email", "tel"].includes(el.type) && (el.compareDocumentPosition(field) & Node.DOCUMENT_POSITION_FOLLOWING));
  const user = users.find(el => el.autocomplete === "username") || users.find(el => el.type === "email") || users.at(-1);
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value").set;
  const set = (el, value) => {
    if (location.href !== expectedUrl || !el.isConnected) throw new Error("O formulário mudou.");
    setter.call(el, value);
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new Event("change", { bubbles: true }));
  };
  if (user && username) set(user, username);
  // Input handlers can replace the form; re-check before releasing the password into the page.
  if (form && new URL(form.action || location.href, location.href).origin !== location.origin)
    throw new Error("A origem do formulário mudou.");
  set(field, password);
  return { filled: true }; // Never submit a login automatically.
}
