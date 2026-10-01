// Confere a cobertura das tabs do dashboard: rota existe, ícone é único e a cor
// é válida. Uso: node scripts/verify-dashboard-tabs.cjs
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const src = fs.readFileSync(
  path.join(root, 'components', 'dashboard', 'dashboardTabs.ts'),
  'utf8'
);
const iconSrc = fs.readFileSync(
  path.join(root, 'components', 'dashboard', 'VoltrisIcon.tsx'),
  'utf8'
);

const tabRe = /label:\s*'([^']+)',[\s\S]*?value:\s*'([^']+)',[\s\S]*?icon:\s*'([^']+)',[\s\S]*?color:\s*'([^']+)',[\s\S]*?path:\s*'([^']+)'/g;

const tabs = [];
let m;
while ((m = tabRe.exec(src)) !== null) {
  tabs.push({ label: m[1], value: m[2], icon: m[3], color: m[4], route: m[5] });
}

let problems = 0;
const seenIcon = new Map();
const seenColor = new Map();

for (const t of tabs) {
  const issues = [];

  // 1. A rota precisa existir de verdade no app router.
  const routeDir = t.route === '/dashboard' ? 'app/dashboard' : `app${t.route}`;
  if (!fs.existsSync(path.join(root, routeDir, 'page.tsx'))) {
    issues.push(`rota inexistente: ${t.route}`);
  }

  // 2. O ícone precisa existir na biblioteca.
  if (!new RegExp(`^\\s{2}${t.icon}:`, 'm').test(iconSrc)) {
    issues.push(`ícone ausente na biblioteca: ${t.icon}`);
  }

  // 3. Cor em hex de 6 dígitos.
  if (!/^#[0-9A-Fa-f]{6}$/.test(t.color)) {
    issues.push(`cor inválida: ${t.color}`);
  }

  // 4. Sem ícones repetidos (o app tinha FiShoppingBag em 2 abas).
  if (seenIcon.has(t.icon)) {
    issues.push(`ícone duplicado com "${seenIcon.get(t.icon)}"`);
  }
  seenIcon.set(t.icon, t.label);

  // 5. Cores adjacentes não podem ser da mesma família de matiz.
  const prev = tabs[tabs.indexOf(t) - 1];
  if (prev) {
    const hue = (hex) => {
      const r = parseInt(hex.slice(1, 3), 16) / 255;
      const g = parseInt(hex.slice(3, 5), 16) / 255;
      const b = parseInt(hex.slice(5, 7), 16) / 255;
      const max = Math.max(r, g, b);
      const min = Math.min(r, g, b);
      if (max === min) return -1;
      const d = max - min;
      let h;
      if (max === r) h = ((g - b) / d + 6) % 6;
      else if (max === g) h = (b - r) / d + 2;
      else h = (r - g) / d + 4;
      return h * 60;
    };
    const a = hue(prev.color);
    const b = hue(t.color);
    if (a >= 0 && b >= 0) {
      const diff = Math.min(Math.abs(a - b), 360 - Math.abs(a - b));
      if (diff < 25) {
        issues.push(`matiz muito próximo do anterior (${Math.round(diff)}°): ${prev.color} → ${t.color}`);
      }
    }
  }
  seenColor.set(t.color, t.label);

  if (issues.length) {
    problems++;
    console.log(`✗ ${t.label} (${t.value})`);
    issues.forEach((i) => console.log(`    - ${i}`));
  } else {
    console.log(`✓ ${t.label.padEnd(18)} ${t.icon.padEnd(11)} ${t.color}  → ${t.route}`);
  }
}

console.log(`\n${tabs.length} tabs verificadas, ${problems} com problema.`);
process.exit(problems > 0 ? 1 : 0);
