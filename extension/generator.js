export const defaults = Object.freeze({ length: 20, upper: true, lower: true, digits: true,
  symbols: true, avoidAmbiguous: true, symbolChars: "!@#$%&*_-+?" });
export function validateOptions(value = {}) {
  const options = { ...defaults, ...value };
  if (!Number.isInteger(options.length) || options.length < 12 || options.length > 128)
    throw new Error("Escolha de 12 a 128 caracteres.");
  for (const key of ["upper", "lower", "digits", "symbols", "avoidAmbiguous"])
    if (typeof options[key] !== "boolean") throw new Error("Opções do gerador inválidas.");
  if (typeof options.symbolChars !== "string" || !/^[!@#$%^&*()_+\-=\[\]{};:,.?/|~]{1,32}$/.test(options.symbolChars))
    throw new Error("Use de 1 a 32 símbolos, sem letras, números ou espaços.");
  if (![options.upper, options.lower, options.digits, options.symbols].some(Boolean))
    throw new Error("Selecione pelo menos um tipo de caractere.");
  return Object.fromEntries(Object.keys(defaults).map(key => [key, options[key]]));
}
export function generatePassword(value) {
  const o = validateOptions(value);
  const sets = [[o.upper, "ABCDEFGHIJKLMNOPQRSTUVWXYZ"], [o.lower, "abcdefghijklmnopqrstuvwxyz"],
    [o.digits, "0123456789"], [o.symbols, [...new Set(o.symbolChars)].join("")]]
    .filter(([enabled]) => enabled).map(([, chars]) => o.avoidAmbiguous ? chars.replace(/[O0Il1|]/g, "") : chars);
  if (sets.some(set => !set.length)) throw new Error("Os símbolos escolhidos foram removidos pelo filtro de ambíguos.");
  const random = limit => {
    const range = 0x100000000, bound = range - range % limit;
    const buffer = new Uint32Array(1);
    do { crypto.getRandomValues(buffer); } while (buffer[0] >= bound);
    return buffer[0] % limit;
  };
  const pool = sets.join("");
  const chars = sets.map(set => set[random(set.length)]);
  while (chars.length < o.length) chars.push(pool[random(pool.length)]);
  for (let i = chars.length - 1; i > 0; i--) {
    const j = random(i + 1); [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join("");
}
