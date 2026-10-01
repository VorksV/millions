// Gera uma pré-visualização estática da sidebar com o novo sistema de ícones.
// Uso: node scripts/preview-dashboard-icons.cjs
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const out = 'C:\\Users\\VOLTRIS\\AppData\\Local\\Temp\\opencode\\voltris-tabs-preview.html';

const tabsSrc = fs.readFileSync(path.join(root, 'components', 'dashboard', 'dashboardTabs.ts'), 'utf8');
const iconsSrc = fs.readFileSync(path.join(root, 'components', 'dashboard', 'VoltrisIcon.tsx'), 'utf8');
const cssSrc = fs.readFileSync(path.join(root, 'app', 'globals.css'), 'utf8');

const vtabCss = (cssSrc.match(/\.vtab \{[\s\S]*?prefers-reduced-motion[\s\S]*?\}\s*\n\s*\}/) || [''])[0];

const tabs = [];
const tabRe = /label:\s*'([^']+)',[\s\S]*?icon:\s*'([^']+)',[\s\S]*?color:\s*'([^']+)',[\s\S]*?appSource:\s*'([^']*)'/g;
let m;
while ((m = tabRe.exec(tabsSrc)) !== null) tabs.push({ label: m[1], icon: m[2], color: m[3], src: m[4] });

const paths = {};
const pathRe = /^\s{2}([A-Za-z][A-Za-z0-9]*):\s*\n?\s*((?:'[^']*'\s*\+?\s*)+),?\s*$/gm;
while ((m = pathRe.exec(iconsSrc.slice(iconsSrc.indexOf('VOLTRIS_ICON_PATHS'), iconsSrc.indexOf('export interface')))) !== null) {
  paths[m[1]] = (m[2].match(/'([^']*)'/g) || []).map((l) => l.slice(1, -1)).join('');
}

const svg = (name, color) =>
  `<svg viewBox="0 0 24 24" fill="${color}" fill-rule="nonzero" style="width:100%;height:100%;display:block"><path d="${paths[name]}"/></svg>`;

const rail = (t, active, isRail) => `
  <a class="vtab ${isRail ? 'vtab--rail' : ''}" aria-current="${active ? 'page' : undefined}" style="--vtab-color:${t.color}">
    <span class="vtab__ico">${svg(t.icon, 'currentColor')}</span>
    ${isRail ? '' : `<span class="vtab__label">${t.label}</span>`}
  </a>`;

const html = `<!doctype html>
<html lang="pt-BR"><head><meta charset="utf-8"><title>Voltris — Ícones das Tabs</title>
<style>
  *{box-sizing:border-box}
  body{margin:0;background:#0a0a0f;color:#fff;font:15px/1.5 "Segoe UI",system-ui,sans-serif;padding:32px}
  h2{font-size:12px;letter-spacing:.14em;text-transform:uppercase;color:#6b6b80;margin:0 0 14px;font-weight:700}
  .wrap{display:flex;gap:32px;align-items:flex-start;flex-wrap:wrap}
  .panel{background:#121218;border:1px solid #2a2a3a;border-radius:16px;padding:16px;width:290px}
  .panel.rail{width:76px;display:flex;flex-direction:column;gap:4px}
  .brand{display:flex;gap:10px;align-items:center;margin-bottom:20px;padding-left:6px}
  .brand i{width:34px;height:34px;border-radius:9px;display:block;background:linear-gradient(135deg,#FF4B6B 0%,#8B31FF 55%,#31A8FF 100%);box-shadow:0 0 12px rgba(255,75,107,.4)}
  .brand b{font-size:12px;letter-spacing:.16em}
  .brand span{display:block;font-size:9px;letter-spacing:.22em;color:#8B31FF}
  .rail .vtab--rail{justify-content:center;padding:11px 0}
  .rail .vtab--rail .vtab__ico{width:20px;height:20px}
  .rail .vtab--rail .vtab__ico svg{width:100%;height:100%}
  table{width:100%;border-collapse:collapse;font-size:12px}
  td,th{padding:6px 8px;border-bottom:1px solid #1e1e28;text-align:left}
  th{color:#6b6b80;font-size:10px;letter-spacing:.1em;text-transform:uppercase}
  code{font-family:ui-monospace,monospace;color:#B4B4C0}
  .sw{display:inline-block;width:11px;height:11px;border-radius:3px;vertical-align:-1px;margin-right:6px}

${vtabCss}
  .vtab{margin-bottom:4px;padding:11px 14px;text-decoration:none}
  .vtab__ico{display:block}
</style></head><body>
<h2>Sidebar — estado inativo</h2>
<h2 style="margin-top:26px">Sidebar — aba ativa</h2>
<div class="wrap">
  <div>
    <div class="panel">
      <div class="brand"><i></i><div><b>VOLTRIS</b><span>CONSOLE</span></div></div>
      ${tabs.map((t, i) => rail(t, i === 1, false)).join('')}
    </div>
    <h2 style="margin-top:22px">Recolhida (rail)</h2>
    <div class="panel rail">
      ${tabs.map((t, i) => rail(t, i === 1, true)).join('')}
    </div>
  </div>
  <div>
    <h2>Mapa de correspondência</h2>
    <table>
      <tr><th>Tab</th><th>Ícone</th><th>Cor</th><th>Origem no app desktop</th></tr>
      ${tabs.map((t) => `<tr>
        <td><span class="sw" style="background:${t.color}"></span>${t.label}</td>
        <td><code>${t.icon}</code></td>
        <td><code>${t.color}</code></td>
        <td>${t.src}</td></tr>`).join('')}
    </table>
  </div>
</div>
</body></html>`;

fs.writeFileSync(out, html, 'utf8');
console.log(`Pré-visualização gerada: ${out}`);
