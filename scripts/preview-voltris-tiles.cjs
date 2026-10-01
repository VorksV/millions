// Pré-visualização dos ladrilhos de ícone, nos tamanhos usados pelas tabs.
// Uso: node scripts/preview-voltris-tiles.cjs
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const out = 'C:\\Users\\VOLTRIS\\AppData\\Local\\Temp\\opencode\\voltris-tiles.html';

const iconSrc = fs.readFileSync(path.join(root, 'components', 'dashboard', 'VoltrisIcon.tsx'), 'utf8');
const block = iconSrc.slice(iconSrc.indexOf('VOLTRIS_ICON_PATHS'), iconSrc.indexOf('export interface'));
const paths = {};
const pathRe = /^\s{2}([A-Za-z][A-Za-z0-9]*):\s*\n?\s*((?:'[^']*'\s*\+?\s*)+),?\s*$/gm;
let m;
while ((m = pathRe.exec(block)) !== null) {
  paths[m[1]] = (m[2].match(/'([^']*)'/g) || []).map((l) => l.slice(1, -1)).join('');
}

const METRICS = {
  8: [32, 8, 16], 9: [36, 9, 18], 10: [40, 10, 20],
  12: [48, 12, 24], 14: [56, 14, 28], 16: [64, 16, 32], 20: [80, 20, 40],
};

const accents = { brand: '#8B31FF', brandBlue: '#31A8FF', brandPink: '#FF4B6B', success: '#00FF94', warning: '#F59E0B', info: '#00D4FF', danger: '#EF4444' };

const tintTile = (icon, accent, size) => {
  const [box, radius, glyph] = METRICS[size];
  return `<div style="width:${box}px;height:${box}px;border-radius:${radius}px;
    background-color:color-mix(in srgb, ${accent} 10%, transparent);
    border:1px solid color-mix(in srgb, ${accent} 20%, transparent);
    display:flex;align-items:center;justify-content:center;flex-shrink:0">
    <svg viewBox="0 0 24 24" width="${glyph}" height="${glyph}" fill="${accent}" fill-rule="nonzero" style="display:block"><path d="${paths[icon]}"/></svg></div>`;
};

const gradTile = (icon, size) => {
  const [box, radius, glyph] = METRICS[size];
  return `<div style="width:${box}px;height:${box}px;border-radius:${radius}px;
    background:linear-gradient(135deg,#FF4B6B 0%,#8B31FF 55%,#31A8FF 100%);
    box-shadow:0 0 12px rgba(255,75,107,.4);
    display:flex;align-items:center;justify-content:center;flex-shrink:0">
    <svg viewBox="0 0 24 24" width="${glyph}" height="${glyph}" fill="#fff" fill-rule="nonzero" style="display:block"><path d="${paths[icon]}"/></svg></div>`;
};

const row = (title, items) => `
  <h3>${title}</h3>
  <div class="row">${items.map((i) => `<div class="cell">${i.t}<div class="lbl">${i.l}</div></div>`).join('')}</div>`;

const html = `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8">
<title>Voltris — Ladrilhos</title><style>
 body{margin:0;background:#0a0a0f;color:#fff;font:14px "Segoe UI",system-ui,sans-serif;padding:28px}
 h3{font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:#6b6b80;margin:26px 0 12px}
 .row{display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end}
 .cell{background:#121218;border:1px solid #2a2a3a;border-radius:12px;padding:14px 10px;text-align:center}
 .lbl{font-size:9px;color:#6b6b80;font-family:ui-monospace,monospace;margin-top:9px}
</style></head><body>
<h3 style="margin-top:0">Ladrilho de marca (tone="gradient") — cabeçalho de página, 40px</h3>
<div class="row">
  ${['home', 'license', 'system', 'security', 'gamer', 'bell', 'streamHub', 'person'].map((i) => `<div class="cell">${gradTile(i, 10)}<div class="lbl">${i}</div></div>`).join('')}
</div>

${row('Escada de tamanhos (tone="tint", accent=brand)', [8, 9, 10, 12, 14, 16, 20].map((s) => ({ t: tintTile('deviceInfo', accents.brand, s), l: `size ${s}` })))}

${row('Acentos da paleta do app', Object.entries(accents).map(([k, v]) => ({ t: tintTile('settings', v, 10), l: `${k}<br>${v}` })))}

${row('Glifos do site, tamanho 10', Object.keys(paths).map((k) => ({ t: tintTile(k, accents.brand, 10), l: k })))}
</body></html>`;

fs.writeFileSync(out, html, 'utf8');
console.log(out);
