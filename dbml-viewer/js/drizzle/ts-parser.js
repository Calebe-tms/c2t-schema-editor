'use strict';
/* Leitor do subconjunto de TypeScript usado em schemas do Drizzle: tokenizer + árvore de expressões.
   Não é um parser completo de TS: entende declarações `export const x = <expressão>`, chamadas encadeadas,
   objetos, arrays, arrow functions, strings e templates (sql`...`). O que não reconhece vira {t:'other'}. */
class TsError extends Error { constructor(line, msg) { super(msg); this.line = line; } }

/* ============================ tokenizer ============================ */
function tsTokenize(src) {
  const T = [], stack = []; let i = 0, line = 1; const n = src.length;
  const isIdStart = c => /[A-Za-z_$À-￿]/.test(c), isId = c => /[\w$À-￿]/.test(c);
  const closer = { '(': ')', '[': ']', '{': '}' };
  while (i < n) {
    const c = src[i];
    if (c === '\n') { line++; i++; continue; }
    if (/\s/.test(c)) { i++; continue; }
    if (c === '/' && src[i + 1] === '/') { while (i < n && src[i] !== '\n') i++; continue; }
    if (c === '/' && src[i + 1] === '*') {
      const e = src.indexOf('*/', i + 2), end = e < 0 ? n : e + 2;
      for (let k = i; k < end; k++) if (src[k] === '\n') line++;
      i = end; continue;
    }
    if (c === "'" || c === '"') {
      let j = i + 1, v = '';
      while (j < n && src[j] !== c) {
        if (src[j] === '\n') throw new TsError(line, 'String não foi fechada');
        if (src[j] === '\\' && j + 1 < n) { v += src[j + 1]; j += 2; continue; }
        v += src[j++];
      }
      if (j >= n) throw new TsError(line, 'String não foi fechada');
      T.push({ t: 'str', v, line }); i = j + 1; continue;
    }
    if (c === '`') {
      const start = line; let j = i + 1, depth = 0, v = '';
      while (j < n && !(src[j] === '`' && depth === 0)) {
        if (src[j] === '\\' && j + 1 < n) { v += src[j] + src[j + 1]; j += 2; continue; }
        if (src[j] === '$' && src[j + 1] === '{') { depth++; v += '${'; j += 2; continue; }
        if (src[j] === '}' && depth > 0) depth--;
        if (src[j] === '\n') line++;
        v += src[j++];
      }
      if (j >= n) throw new TsError(start, 'Template ` não foi fechado');
      T.push({ t: 'tpl', v, line: start, endLine: line }); i = j + 1; continue;
    }
    if (/\d/.test(c)) {
      let j = i; while (j < n && /[\w.]/.test(src[j])) j++;
      T.push({ t: 'num', v: src.slice(i, j), line }); i = j; continue;
    }
    if (isIdStart(c)) {
      let j = i; while (j < n && isId(src[j])) j++;
      T.push({ t: 'id', v: src.slice(i, j), line }); i = j; continue;
    }
    if (c === '=' && src[i + 1] === '>') { T.push({ t: 'p', v: '=>', line }); i += 2; continue; }
    if (c === '.' && src[i + 1] === '.' && src[i + 2] === '.') { T.push({ t: 'p', v: '...', line }); i += 3; continue; }
    if (c === '?' && src[i + 1] === '.' && !/\d/.test(src[i + 2] || '')) { T.push({ t: 'p', v: '?.', line }); i += 2; continue; }
    if (closer[c]) stack.push({ c, line });
    else if (c === ')' || c === ']' || c === '}') {
      const top = stack.pop();
      if (!top) throw new TsError(line, `"${c}" sem abertura correspondente`);
      if (closer[top.c] !== c) throw new TsError(line, `Esperava "${closer[top.c]}" (aberto na linha ${top.line}) mas encontrei "${c}"`);
    }
    T.push({ t: 'p', v: c, line }); i++;
  }
  if (stack.length) { const top = stack[stack.length - 1]; throw new TsError(top.line, `"${top.c}" não foi fechado`); }
  return T;
}

/* ============================== parser ============================== */
const TS_STMT_START = new Set(['export', 'import', 'const', 'let', 'var', 'function', 'type', 'interface', 'class', 'enum']);

function tsParse(src) {
  const toks = tsTokenize(src); let p = 0;
  const lastLine = () => toks.length ? toks[toks.length - 1].line : 1;
  const peek = (k = 0) => toks[p + k];
  const next = () => { if (p >= toks.length) throw new TsError(lastLine(), 'Fim inesperado do arquivo'); return toks[p++]; };
  const isP = (v, k = 0) => { const t = toks[p + k]; return !!t && t.t === 'p' && t.v === v; };
  const isId = (v, k = 0) => { const t = toks[p + k]; return !!t && t.t === 'id' && t.v === v; };
  const expectP = v => { const t = next(); if (t.t !== 'p' || t.v !== v) throw new TsError(t.line, `Esperava "${v}" mas encontrei "${t.v}"`); return t; };
  const OPEN = { '(': 1, '[': 1, '{': 1 }, CLOSE = { ')': 1, ']': 1, '}': 1 };
  const prevLine = () => p > 0 ? (toks[p - 1].endLine || toks[p - 1].line) : 1;

  // consome tokens até um terminador no nível atual (`,` `)` `]` `}` `;`), respeitando aninhamento
  function skipToTerminator() {
    let depth = 0;
    while (peek()) {
      const t = peek();
      if (t.t === 'p') {
        if (OPEN[t.v]) depth++;
        else if (CLOSE[t.v]) { if (depth === 0) return; depth--; }
        else if (depth === 0 && (t.v === ',' || t.v === ';')) return;
      }
      if (depth === 0 && t.t === 'id' && TS_STMT_START.has(t.v) && t.line > prevLine()) return;
      next();
    }
  }
  const skipBalanced = () => { let d = 0; do { const t = next(); if (t.t === 'p') { if (OPEN[t.v]) d++; else if (CLOSE[t.v]) d--; } } while (d > 0); };

  function expr() {
    const start = peek(); if (!start) throw new TsError(lastLine(), 'Expressão incompleta');
    let node = postfix(primary());
    const t = peek();
    const ends = !t || (t.t === 'p' && (t.v === ',' || t.v === ')' || t.v === ']' || t.v === '}' || t.v === ';'))
      || (t.t === 'id' && TS_STMT_START.has(t.v) && t.line > prevLine());
    if (!ends) { skipToTerminator(); node = { t: 'other', line: start.line }; }
    return node;
  }

  function primary() {
    const t = next();
    if (t.t === 'str') return { t: 'str', v: t.v, line: t.line };
    if (t.t === 'num') return { t: 'num', v: t.v, line: t.line };
    if (t.t === 'tpl') return { t: 'tpl', v: t.v, tag: null, line: t.line };
    if (t.t === 'id') {
      if (t.v === 'true' || t.v === 'false' || t.v === 'null' || t.v === 'undefined') return { t: 'lit', v: t.v, line: t.line };
      if (isP('=>')) { next(); return arrowBody([t.v], t.line); }
      return { t: 'id', name: t.v, line: t.line };
    }
    if (t.t === 'p') {
      if (t.v === '(') {
        // procura o ")" que fecha para saber se é arrow function
        let d = 1, k = p; while (k < toks.length && d > 0) { const x = toks[k++]; if (x.t === 'p') { if (OPEN[x.v]) d++; else if (CLOSE[x.v]) d--; } }
        let after = k; if (toks[after] && toks[after].t === 'p' && toks[after].v === ':') { // tipo de retorno: (): AnyPgColumn =>
          while (after < toks.length && !(toks[after].t === 'p' && toks[after].v === '=>')) after++;
        }
        if (toks[after] && toks[after].t === 'p' && toks[after].v === '=>') {
          const params = []; let depth = 0, expectName = true;
          for (let j = p; j < k - 1; j++) {
            const x = toks[j];
            if (x.t === 'p') { if (OPEN[x.v]) depth++; else if (CLOSE[x.v]) depth--; else if (x.v === ',' && depth === 0) expectName = true; }
            else if (x.t === 'id' && expectName && depth === 0) { params.push(x.v); expectName = false; }
          }
          p = after + 1; return arrowBody(params, t.line);
        }
        const e = expr(); expectP(')'); return e;
      }
      if (t.v === '[') {
        const items = [];
        while (!isP(']')) {
          if (isP(',')) { next(); continue; }
          if (isP('...')) { next(); skipToTerminator(); items.push({ t: 'other', line: t.line }); continue; }
          items.push(expr());
        }
        next(); return { t: 'arr', items, line: t.line };
      }
      if (t.v === '{') {
        const props = [];
        while (!isP('}')) {
          if (isP(',')) { next(); continue; }
          if (isP('...')) { next(); skipToTerminator(); continue; }
          const k = next(); let key;
          if (k.t === 'id' || k.t === 'str' || k.t === 'num') key = k.v;
          else if (k.t === 'p' && k.v === '[') { p--; skipBalanced(); if (isP(':')) { next(); expr(); } continue; }
          else throw new TsError(k.line, `Chave de objeto inválida: "${k.v}"`);
          if (isP(':')) { next(); props.push({ key, value: expr(), line: k.line }); }
          else if (isP('(')) { skipBalanced(); if (isP('{')) skipBalanced(); } // método: ignora
          else props.push({ key, value: { t: 'id', name: key, line: k.line }, line: k.line });
        }
        next(); return { t: 'obj', props, line: t.line };
      }
    }
    // qualquer outra coisa (new, function, operadores unários…): vira "other" até o terminador
    const s = --p; skipToTerminator(); if (p === s) next(); // garante avanço
    return { t: 'other', line: t.line };
  }

  function arrowBody(params, line) {
    if (isP('{')) { skipBalanced(); return { t: 'arrow', params, body: { t: 'block', line }, line }; }
    return { t: 'arrow', params, body: expr(), line };
  }

  function postfix(node) {
    for (;;) {
      if (isP('.') || isP('?.')) {
        next(); const k = next();
        if (k.t !== 'id') throw new TsError(k.line, `Nome esperado depois do "." mas encontrei "${k.v}"`);
        node = { t: 'member', obj: node, prop: k.v, line: k.line };
      } else if (isP('(')) {
        const open = next(), args = [];
        while (!isP(')')) { if (isP(',')) { next(); continue; } args.push(expr()); }
        next(); node = { t: 'call', callee: node, args, line: open.line };
      } else if (isP('[')) {
        next(); expr(); expectP(']'); node = { t: 'other', line: node.line };
      } else if (peek() && peek().t === 'tpl') {
        const k = next(); node = { t: 'tpl', v: k.v, tag: node, line: k.line };
      } else if (isP('!') && !isP('=', 1)) { next(); }
      else return node;
    }
  }

  /* ---- declarações do arquivo ---- */
  const stmts = [];
  while (p < toks.length) {
    const start = peek();
    if (start.t === 'id' && start.v === 'import') {
      next(); while (peek() && peek().t !== 'str') next(); if (peek()) next(); if (isP(';')) next(); continue;
    }
    let exported = false;
    if (isId('export')) { next(); exported = true; }
    if (isId('const') || isId('let') || isId('var')) {
      next(); const name = peek();
      if (name && name.t === 'id' && (isP('=', 1) || isP(':', 1))) {
        next(); if (isP(':')) { while (peek() && !isP('=')) next(); }
        expectP('=');
        const init = expr(); if (isP(';')) next();
        stmts.push({ name: name.v, init, line: start.line, endLine: prevLine(), exported });
        continue;
      }
    }
    // qualquer outra instrução (export default, function, type, relations()…): ignora até o próximo comando
    next();
    for (;;) {
      skipToTerminator();
      if (!peek()) break;
      if (isP(';')) { next(); break; }
      if (isP(',') || (peek().t === 'p' && CLOSE[peek().v])) { next(); continue; }
      break; // próximo comando
    }
  }
  return stmts;
}
