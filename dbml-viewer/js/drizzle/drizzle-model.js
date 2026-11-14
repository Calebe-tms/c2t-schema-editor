'use strict';
/* Converte as declarações lidas por ts-parser.js no mesmo modelo que o diagrama já desenha para DBML:
   { nodes: [tabelas e enums], edges: [relações e ligações com enum], groups: [], warnings: [] } */

// Texto curto de uma expressão, para mostrar defaults e checks
function tsText(n) {
  if (!n) return '';
  switch (n.t) {
    case 'str': return "'" + n.v + "'";
    case 'num': case 'lit': return n.v;
    case 'tpl': return n.v.trim();
    case 'id': return n.name;
    case 'member': return tsText(n.obj) + '.' + n.prop;
    case 'call': return tsText(n.callee) + '(' + (n.args.length ? '…' : '') + ')';
    default: return '…';
  }
}

// Desmonta uma cadeia a().b().c() em { base: a(), mods: [{name:'b', args}, {name:'c', args}] } (na ordem escrita)
function tsChain(node) {
  const mods = [];
  while (node && node.t === 'call' && node.callee.t === 'member' && node.callee.obj.t === 'call') {
    mods.unshift({ name: node.callee.prop, args: node.args, line: node.line });
    node = node.callee.obj;
  }
  return { base: node, mods };
}
const tsObjProp = (obj, key) => { const p = obj && obj.t === 'obj' ? obj.props.find(x => x.key === key) : null; return p ? p.value : null; };
// corpo de uma arrow function (() => x) ou o próprio valor
const tsUnwrap = n => n && n.t === 'arrow' ? n.body : n;

// Chave da coluna em `table.userId` / `table.userId.asc().nullsLast()` / `usersInEqualizagro.id`: devolve { owner, key }
function tsColumnRef(node) {
  node = tsUnwrap(node);
  while (node && node.t === 'call' && node.callee.t === 'member') node = node.callee.obj;
  if (node && node.t === 'member' && node.obj.t === 'id') return { owner: node.obj.name, key: node.prop };
  return null;
}

const TS_SERIAL = new Set(['serial', 'bigserial', 'smallserial']);

function parseDrizzle(src) {
  const stmts = tsParse(src);
  const warnings = [], edges = [];
  const schemaVars = new Map(), enumByVar = new Map(), tableByVar = new Map();
  const tables = [], enums = [], pendingFks = [];

  // Passagem 1: schemas, enums e o esqueleto das tabelas (uma tabela pode referenciar outra declarada depois)
  const defs = [];
  for (const s of stmts) {
    const { base } = tsChain(s.init);
    if (!base || base.t !== 'call') continue;
    const c = base.callee;
    if (c.t === 'id' && c.name === 'pgSchema' && base.args[0] && base.args[0].t === 'str') { schemaVars.set(s.name, base.args[0].v); continue; }
    defs.push({ s, base, c });
  }
  for (const { s, base, c } of defs) {
    let kind = null, schema = 'public';
    if (c.t === 'id' && c.name === 'pgTable') kind = 'table';
    else if (c.t === 'id' && c.name === 'pgEnum') kind = 'enum';
    else if (c.t === 'member' && c.obj.t === 'id' && schemaVars.has(c.obj.name) && (c.prop === 'table' || c.prop === 'enum')) { kind = c.prop; schema = schemaVars.get(c.obj.name); }
    if (!kind) continue;
    const name = base.args[0] && base.args[0].t === 'str' ? base.args[0].v : null;
    if (name == null) { warnings.push(`Linha ${s.line}: ${kind === 'table' ? 'tabela' : 'enum'} sem nome em texto`); continue; }
    const node = { kind, name, schema, full: schema + '.' + name, alias: null, color: null, note: null, line: s.line, endLine: s.endLine, varName: s.name };
    if (kind === 'enum') {
      const arr = base.args[1];
      node.values = (arr && arr.t === 'arr' ? arr.items : []).filter(v => v.t === 'str').map(v => ({ name: v.v, note: null }));
      enums.push(node); enumByVar.set(s.name, node);
    } else {
      Object.assign(node, { cols: [], indexes: [], checks: [], keyToCol: {} });
      tables.push(node); tableByVar.set(s.name, { node, base });
    }
  }

  // Passagem 2: colunas e extras de cada tabela
  for (const { node: t, base } of tableByVar.values()) {
    const colsObj = base.args[1];
    if (!colsObj || colsObj.t !== 'obj') { warnings.push(`Linha ${t.line}: colunas de "${t.name}" não estão num objeto literal`); }
    else for (const prop of colsObj.props) readColumn(t, prop);
    const extra = tsUnwrap(base.args[2]);
    const items = extra && extra.t === 'arr' ? extra.items : extra && extra.t === 'obj' ? extra.props.map(p => p.value) : [];
    for (const it of items) readExtra(t, it);
    // dicas no cabeçalho: índices e checks
    const lines = [];
    if (t.indexes.length) lines.push('Índices:', ...t.indexes.map(i => `• ${i.name || '(sem nome)'} (${i.cols.join(', ')})${i.unique ? ' único' : ''}${i.partial ? ' parcial' : ''}`));
    if (t.checks.length) lines.push('Checks:', ...t.checks.map(k => `• ${k.name}: ${k.expr}`));
    if (lines.length) t.note = lines.join('\n');
  }

  function readColumn(t, prop) {
    const { base, mods } = tsChain(prop.value);
    if (!base || base.t !== 'call') { warnings.push(`Linha ${prop.line}: coluna "${prop.key}" não reconhecida`); return; }
    const callee = base.callee, typeName = callee.t === 'id' ? callee.name : tsText(callee);
    const nameArg = base.args.find(a => a.t === 'str'), cfg = base.args.find(a => a.t === 'obj');
    const col = { name: nameArg ? nameArg.v : prop.key, type: typeName, pk: false, notnull: false, unique: false, inc: false, def: null, note: null, fk: false, line: prop.line };
    const cfgVal = k => { const v = tsObjProp(cfg, k); return v ? tsText(v) : null; };

    const en = callee.t === 'id' ? enumByVar.get(callee.name) : null;
    if (en) { col.type = en.name; edges.push({ a: t, aCols: [col.name], b: en, bCols: [], ca: null, cb: null, kind: 'enum' }); }
    else if (cfg) {
      const len = cfgVal('length'), prec = cfgVal('precision'), scale = cfgVal('scale');
      if (len) col.type = `${typeName}(${len})`;
      else if (prec) col.type = `${typeName}(${prec}${scale ? ',' + scale : ''})`;
      if ((typeName === 'timestamp' || typeName === 'time') && cfgVal('withTimezone') === 'true') col.type = typeName + 'tz';
    }
    if (TS_SERIAL.has(typeName)) { col.inc = true; col.notnull = true; }

    for (const m of mods) {
      switch (m.name) {
        case 'notNull': col.notnull = true; break;
        case 'primaryKey': col.pk = true; col.notnull = true; break;
        case 'unique': col.unique = true; break;
        case 'array': col.type += '[]'; break;
        case 'default': col.def = tsText(m.args[0]); break;
        case 'defaultNow': col.def = 'now()'; break;
        case 'defaultRandom': col.def = 'gen_random_uuid()'; break;
        case '$defaultFn': case '$default': col.def = 'fn()'; break;
        case 'generatedAlwaysAsIdentity': case 'generatedByDefaultAsIdentity': col.inc = true; break;
        case 'references': {
          const ref = tsColumnRef(m.args[0]);
          if (ref) pendingFks.push({ t, childKeys: [prop.key], parentVar: ref.owner, parentKeys: [ref.key], line: m.line });
          else warnings.push(`Linha ${m.line}: references() de "${prop.key}" não reconhecido`);
          break;
        }
        default: break;
      }
    }
    if (t.cols.some(c => c.name === col.name)) warnings.push(`Linha ${prop.line}: coluna "${col.name}" repetida em "${t.name}"`);
    t.cols.push(col); t.keyToCol[prop.key] = col;
  }

  function readExtra(t, item) {
    const { base, mods } = tsChain(item);
    if (!base || base.t !== 'call' || base.callee.t !== 'id') return;
    const fn = base.callee.name, nameArg = base.args.find(a => a.t === 'str');
    // coluna (nome real no banco) ou expressão sql`...` do índice
    const colsOf = args => args.map(a => {
      const r = tsColumnRef(a);
      return r ? (t.keyToCol[r.key] ? t.keyToCol[r.key].name : r.key) : a.t === 'tpl' ? a.v.trim() : null;
    }).filter(Boolean);
    const keysOf = n => (n && n.t === 'arr' ? n.items : []).map(tsColumnRef).filter(Boolean);
    switch (fn) {
      case 'index': case 'uniqueIndex': {
        const cols = [];
        for (const m of mods) if (m.name === 'on' || m.name === 'using') cols.push(...colsOf(m.args));
        const partial = mods.some(m => m.name === 'where');
        const ix = { name: nameArg ? nameArg.v : null, cols, unique: fn === 'uniqueIndex', partial };
        t.indexes.push(ix);
        if (ix.unique && !partial && cols.length === 1) { const c = t.cols.find(c => c.name === cols[0]); if (c) c.unique = true; }
        break;
      }
      case 'unique': {
        const cols = []; for (const m of mods) if (m.name === 'on') cols.push(...colsOf(m.args));
        t.indexes.push({ name: nameArg ? nameArg.v : null, cols, unique: true, partial: false });
        if (cols.length === 1) { const c = t.cols.find(c => c.name === cols[0]); if (c) c.unique = true; }
        break;
      }
      case 'primaryKey': {
        const keys = keysOf(tsObjProp(base.args[0], 'columns'));
        for (const k of keys) { const c = t.keyToCol[k.key]; if (c) { c.pk = true; c.notnull = true; } }
        break;
      }
      case 'foreignKey': {
        const cfg = base.args[0], child = keysOf(tsObjProp(cfg, 'columns')), parent = keysOf(tsObjProp(cfg, 'foreignColumns'));
        if (!child.length || !parent.length) { warnings.push(`Linha ${base.line}: foreignKey sem columns/foreignColumns reconhecíveis`); break; }
        pendingFks.push({ t, childKeys: child.map(k => k.key), parentVar: parent[0].owner, parentKeys: parent.map(k => k.key), line: base.line });
        break;
      }
      case 'check': t.checks.push({ name: nameArg ? nameArg.v : '(sem nome)', expr: tsText(base.args[1]) }); break;
      default: break;
    }
  }

  // Resolve as relações: a chave de cada coluna (userId) vira o nome real no banco (user_id)
  for (const f of pendingFks) {
    const parentEntry = tableByVar.get(f.parentVar);
    if (!parentEntry) { warnings.push(`Linha ${f.line}: relação ignorada (tabela "${f.parentVar}" não encontrada)`); continue; }
    const P = parentEntry.node, name = (tbl, k) => (tbl.keyToCol[k] ? tbl.keyToCol[k].name : k);
    for (const k of f.childKeys) if (f.t.keyToCol[k]) f.t.keyToCol[k].fk = true;
    edges.push({ a: f.t, aCols: f.childKeys.map(k => name(f.t, k)), b: P, bCols: f.parentKeys.map(k => name(P, k)), ca: '*', cb: '1', kind: 'ref', line: f.line });
  }

  const nodes = [...tables, ...enums];
  for (const n of nodes) n.id = n.kind + ':' + n.full;
  return { nodes, edges, groups: [], warnings };
}
