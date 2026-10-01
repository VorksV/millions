// Pré-visualização ampliada dos glifos, no tamanho real da navegação (22px)
// e ampliado 4x, para inspeção de silhueta.
// Uso: node scripts/preview-voltris-glyphs.cjs
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const out = 'C:\\Users\\VOLTRIS\\AppData\\Local\\Temp\\opencode\\voltris-glyphs.html';

const tabsSrc = fs.readFileSync(path.join(root, 'components', 'dashboard', 'dashboardTabs.ts'), 'utf8');
const iconsSrc = fs.readFileSync(path.join(root, 'components', 'dashboard', 'VoltrisIcon.tsx'), 'utf8');

const tabs = [];
const tabRe = /label:\s*'([^']+)',[\s\S]*?icon:\s*'([^']+)',[\s\S]*?color:\s*'([^']+)'/g;
let m;
while ((m = tabRe.exec(tabsSrc)) !== null) tabs.push({ label: m[1], icon: m[2], color: m[3] });

const paths = {};
const block = iconsSrc.slice(iconsSrc.indexOf('VOLTRIS_ICON_PATHS'), iconsSrc.indexOf('export interface'));
const pathRe = /^\s{2}([A-Za-z][A-Za-z0-9]*):\s*\n?\s*((?:'[^']*'\s*\+?\s*)+),?\s*$/gm;
while ((m = pathRe.exec(block)) !== null) {
  paths[m[1]] = (m[2].match(/'([^']*)'/g) || []).map((l) => l.slice(1, -1)).join('');
}

const cell = (t, size) => `
  <div class="cell">
    <div class="ico" style="width:${size}px;height:${size}px;color:${t.color}">
      <svg viewBox="0 0 24 24" fill="currentColor" fill-rule="nonzero" style="width:100%;height:100%"><path d="${paths[t.icon]}"/></svg>
    </div>
    <div class="lbl">${t.label}</div>
    <div class="ico-name">${t.icon}</div>
  </div>`;

const html = `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8">
<title>Voltris — Glifos</title><style>
 body{margin:0;background:#0a0a0f;color:#fff;font:14px "Segoe UI",system-ui,sans-serif;padding:28px}
 h3{font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:#6b6b80;margin:0 0 14px}
 .row{display:flex;gap:8px;flex-wrap:wrap}
 .cell{width:120px;background:#121218;border:1px solid #2a2a3a;border-radius:12px;padding:14px 8px;text-align:center;margin-bottom:8px}
 .ico{margin:0 auto 10px;filter:drop-shadow(0 0 6px currentColor)}
 .lbl{font-size:10px;color:#B4B4C0;line-height:1.3}
 .ico-name{font-size:9px;color:#6b6b80;font-family:ui-monospace,monospace;margin-top:3px}
 .gridline{stroke:#2a2a3a;stroke-width:.3}
</style></head><body>
<h3>Tamanho real — 22px (sidebar) e 20px (rail recolhida)</h3>
<div class="row">${tabs.map((t) => cell(t, 22)).join('')}</div>
<h3 style="margin-top:26px">Ampliado 4x — inspeção de silhueta</h3>
<div class="row">${tabs.map((t) => cell(t, 88)).join('')}</div>
</body></html>`;

fs.writeFileSync(out, html, 'utf8');
console.log(out);
