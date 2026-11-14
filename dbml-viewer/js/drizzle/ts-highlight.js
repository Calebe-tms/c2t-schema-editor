'use strict';
/* Realce de sintaxe do TypeScript do schema.ts, no mesmo formato de highlight() (DBML): devolve HTML com <span class="t..."> */
const TS_KW = new Set(['import', 'export', 'from', 'const', 'let', 'var', 'new', 'return', 'function', 'type', 'as', 'default']);
const TS_LIT = new Set(['true', 'false', 'null', 'undefined']);
const TS_BUILDERS = new Set(['pgTable', 'pgSchema', 'pgEnum', 'table', 'enum', 'index', 'uniqueIndex', 'unique', 'foreignKey', 'primaryKey', 'check', 'sql', 'relations']);
const TS_TYPES = new Set(['uuid', 'varchar', 'text', 'char', 'integer', 'smallint', 'bigint', 'serial', 'bigserial', 'smallserial', 'boolean',
  'numeric', 'decimal', 'real', 'doublePrecision', 'date', 'time', 'timestamp', 'interval', 'json', 'jsonb', 'inet', 'cidr', 'macaddr',
  'bytea', 'point', 'line', 'vector']);
const TS_MODS = new Set(['notNull', 'primaryKey', 'default', 'defaultNow', 'defaultRandom', 'unique', 'references', 'array', 'onDelete', 'onUpdate',
  'enableRLS', 'on', 'using', 'where', 'asc', 'desc', 'nullsFirst', 'nullsLast', 'op', 'columns', 'foreignColumns', 'name']);
const tsEsc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

function highlightTs(text) {
  const re = /\/\/[^\n]*|\/\*[\s\S]*?(?:\*\/|$)|`(?:\\[\s\S]|[^`\\])*`?|'(?:\\.|[^'\\\n])*'?|"(?:\\.|[^"\\\n])*"?|[A-Za-z_$À-￿][\w$À-￿]*|\d[\w.]*|=>|[\[\]{}()]|\n|\s+|[^\s]/g;
  let out = '', prevDot = false, m;
  const span = (c, s) => `<span class="${c}">${tsEsc(s)}</span>`;
  while ((m = re.exec(text))) {
    const s = m[0], c = s[0];
    if (s === '\n' || /^\s/.test(c)) { out += s; continue; }
    if (c === '/' && (s[1] === '/' || s[1] === '*')) { out += span('tc', s); prevDot = false; continue; }
    if (c === '`') { out += span('tq', s); prevDot = false; continue; } // sql`...`
    if (c === "'" || c === '"') { out += span('ts', s); prevDot = false; continue; }
    if (/[A-Za-z_$À-￿]/.test(c)) {
      if (TS_KW.has(s) && !prevDot) out += span('tk', s);
      else if (TS_LIT.has(s)) out += span('tn', s);
      else if (TS_TYPES.has(s) && !prevDot) out += span('tt', s);
      else if (TS_MODS.has(s) && prevDot) out += span('ta', s);
      else if (TS_BUILDERS.has(s)) out += span('tk', s);
      else out += tsEsc(s);
      prevDot = false; continue;
    }
    if (/\d/.test(c)) { out += span('tn', s); prevDot = false; continue; }
    if (s === '=>') { out += span('to', s); prevDot = false; continue; }
    prevDot = s === '.';
    if (/[\[\]{}()]/.test(c)) { out += span('tp', s); continue; }
    out += tsEsc(s);
  }
  return out;
}
