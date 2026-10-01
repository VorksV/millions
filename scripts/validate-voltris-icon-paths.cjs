// Validador de path data: confere aridade de argumentos por comando SVG.
// Uso: node scripts/validate-voltris-icon-paths.cjs
const fs = require('fs');
const path = require('path');

const src = fs.readFileSync(
  path.join(__dirname, '..', 'components', 'dashboard', 'VoltrisIcon.tsx'),
  'utf8'
);

const ARITY = {
  M: 2, L: 2, H: 1, V: 1, C: 6, S: 4, Q: 4, T: 2,
  A: 7, Z: 0, z: 0, m: 2, l: 2, h: 1, v: 1, c: 6, s: 4, q: 4, t: 2, a: 7,
};

const start = src.indexOf('VOLTRIS_ICON_PATHS');
const block = src.slice(start, src.indexOf('export interface VoltrisIconProps'));

const entryRe = /^\s{2}([A-Za-z][A-Za-z0-9]*):\s*\n?\s*((?:'[^']*'\s*\+?\s*)+),?\s*$/gm;

let match;
let checked = 0;
let failed = 0;
let count = 0;

while ((match = entryRe.exec(block)) !== null) {
  const name = match[1];
  const literals = match[2].match(/'([^']*)'/g) || [];
  const d = literals.map((l) => l.slice(1, -1)).join('');
  count++;

  // Divide o path em comandos, contando os argumentos de cada um.
  const tokens = d.match(/[MLHVCSQTAZmlhvcsqtaz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?/g);
  if (!tokens) {
    console.log(`  ✗ ${name}: nenhum token reconhecido`);
    failed++;
    continue;
  }

  let cmd = null;
  let i = 0;
  let ok = true;
  let cmds = 0;

  while (i < tokens.length) {
    const t = tokens[i];
    if (/^[MLHVCSQTAZmlhvcsqtaz]$/.test(t)) {
      cmd = t;
      i++;
      cmds++;
    } else if (cmd === null) {
      console.log(`  ✗ ${name}: argumento antes de qualquer comando`);
      ok = false;
      break;
    }

    const need = ARITY[cmd];
    let got = 0;
    while (i < tokens.length && !/^[MLHVCSQTAZmlhvcsqtaz]$/.test(tokens[i])) {
      const v = tokens[i];
      if (!/^-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?$/.test(v)) {
        console.log(`  ✗ ${name}: token inválido "${v}"`);
        ok = false;
        i++;
        continue;
      }
      got++;
      i++;
    }

    if (got !== 0 && got % need !== 0) {
      console.log(`  ✗ ${name}: comando ${cmd} com ${got} argumentos (múltiplo de ${need} esperado)`);
      ok = false;
    }
  }

  if (ok) {
    checked++;
  } else {
    failed++;
  }
  void cmds;
}

console.log(`\nÍcones: ${count} declarados, ${checked} válidos, ${failed} com erro`);
process.exit(failed > 0 ? 1 : 0);
