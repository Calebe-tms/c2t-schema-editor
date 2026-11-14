'use strict';
/* Compara o modelo lido do schema.ts com o snapshot mais recente do Drizzle (drizzle/meta/NNNN_snapshot.json),
   ou seja, com a última migration já GERADA. Marca nos nós: mig = 'new' | 'changed', migCols (por coluna) e migGone (colunas removidas).
   Não diz se a migration foi aplicada no banco: isso só o banco sabe. */

// Notações diferentes do mesmo tipo: 'timestamp with time zone' (snapshot) x 'timestamptz' (schema.ts), 'numeric(10, 2)' x 'numeric(10,2)'
const snapType = t => String(t || '').toLowerCase().replace(/\s+/g, '')
  .replace('timestampwithtimezone', 'timestamptz').replace('timewithtimezone', 'timetz');

function diffSnapshot(model, snap) {
  const out = { new: 0, changed: 0, removed: [], found: !!(snap && snap.tables) };
  for (const n of model.nodes) { n.mig = null; n.migCols = {}; n.migGone = []; }
  if (!out.found) return out;

  const seenTables = new Set(), seenEnums = new Set();
  for (const n of model.nodes) {
    if (n.kind === 'enum') {
      seenEnums.add(n.full);
      const old = (snap.enums || {})[n.full];
      if (!old) { n.mig = 'new'; out.new++; continue; }
      const now = n.values.map(v => v.name), was = old.values || [];
      const add = now.filter(v => !was.includes(v)), del = was.filter(v => !now.includes(v));
      for (const v of add) n.migCols[v] = 'new';
      n.migGone = del;
      if (add.length || del.length) { n.mig = 'changed'; out.changed++; }
      continue;
    }
    seenTables.add(n.full);
    const old = snap.tables[n.full];
    if (!old) { n.mig = 'new'; out.new++; for (const c of n.cols) n.migCols[c.name] = 'new'; continue; }
    const oldCols = old.columns || {};
    // chave primária composta fica em compositePrimaryKeys, não em primaryKey da coluna
    const compositePk = new Set(Object.values(old.compositePrimaryKeys || {}).flatMap(k => k.columns || []));
    for (const c of n.cols) {
      const o = oldCols[c.name];
      if (!o) n.migCols[c.name] = 'new';
      else if (snapType(o.type) !== snapType(c.type) || !!o.notNull !== !!c.notnull || (!!o.primaryKey || compositePk.has(c.name)) !== !!c.pk) n.migCols[c.name] = 'changed';
    }
    n.migGone = Object.keys(oldCols).filter(k => !n.cols.some(c => c.name === k));
    if (n.migGone.length || Object.keys(n.migCols).length) { n.mig = 'changed'; out.changed++; }
  }
  for (const k of Object.keys(snap.tables)) if (!seenTables.has(k)) out.removed.push(k);
  for (const k of Object.keys(snap.enums || {})) if (!seenEnums.has(k)) out.removed.push(k + ' (enum)');
  return out;
}
