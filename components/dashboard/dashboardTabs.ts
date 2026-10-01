import type { VoltrisIconName } from './VoltrisIcon';

/**
 * Fonte única de verdade das tabs do dashboard do usuário.
 *
 * Cada entrada replica um item da navegação do app desktop
 * (D:\APLICATIVO VOLTRIS\UI\MainWindow.xaml:536-711): mesma geometria de ícone
 * e mesmo acento de cor do `Fill` inline que o app renderiza em runtime.
 *
 * A coluna `appSource` registra de qual item do sidebar do desktop a cor veio,
 * para que a correspondência seja auditável e não pareça arbitrária.
 */

export interface DashboardTab {
  /** Rótulo exibido na navegação. */
  label: string;
  /** Identificador estável. */
  value: string;
  /** Geometria do ícone (24x24, somente preenchimento). */
  icon: VoltrisIconName;
  /** Acento do ícone — preenchimento + glow. */
  color: string;
  /** Rota de destino. */
  path: string;
  /** Aba virtual via query string (usada nas rotas reais do app). */
  query?: { tab: string };
  /** Item correspondente no sidebar do app desktop. */
  appSource: string;
}

export const DASHBOARD_TABS: DashboardTab[] = [
  {
    label: 'Visão Geral',
    value: 'overview',
    icon: 'home',
    color: '#31A8FF',
    path: '/dashboard',
    query: { tab: 'overview' },
    appSource: 'NavDashboard',
  },
  {
    label: 'Minhas Licenças',
    value: 'licenses',
    icon: 'license',
    color: '#8B31FF',
    path: '/dashboard',
    query: { tab: 'licenses' },
    appSource: 'NavShield',
  },
  {
    label: 'Pedidos',
    value: 'orders',
    icon: 'orders',
    color: '#00D4FF',
    path: '/dashboard',
    query: { tab: 'orders' },
    appSource: 'NetworkIcon / InfoColor',
  },
  {
    label: 'Meu Computador',
    value: 'pc',
    icon: 'system',
    color: '#6366F1',
    path: '/dashboard',
    query: { tab: 'pc' },
    appSource: 'NavDiagnostics',
  },
  {
    label: 'Segurança',
    value: 'security',
    icon: 'security',
    color: '#EF4444',
    path: '/dashboard',
    query: { tab: 'security' },
    appSource: 'NavSecurity',
  },
  {
    label: 'Novos Pedidos',
    value: 'new-order',
    icon: 'bolt',
    color: '#FB923C',
    path: '/dashboard/new-order',
    appSource: 'NavRepair',
  },
  {
    label: 'Empresas',
    value: 'companies',
    icon: 'streamHub',
    color: '#9146FF',
    path: '/dashboard/companies',
    appSource: 'NavStreamHub',
  },
  {
    label: 'Meu Perfil',
    value: 'profile',
    icon: 'person',
    color: '#EC4899',
    path: '/dashboard/profile',
    appSource: 'DashboardView.xaml:2764',
  },
  {
    label: 'Suporte',
    value: 'tickets',
    icon: 'support',
    color: '#F59E0B',
    path: '/dashboard/tickets',
    appSource: 'NavPersonalize / NavShortcuts',
  },
  // ── Tabs que existiam como página mas não tinham entrada na navegação ──
  {
    label: 'Notificações',
    value: 'notifications',
    icon: 'bell',
    color: '#10B981',
    path: '/dashboard/notifications',
    appSource: 'NavRecovery',
  },
  {
    label: 'Dispositivos',
    value: 'devices',
    icon: 'deviceInfo',
    color: '#06B6D4',
    path: '/dashboard/companies/devices',
    appSource: 'NavDrivers / NavDisplay',
  },
  {
    label: 'Gamer',
    value: 'gamer',
    icon: 'gamer',
    color: '#FF6B35',
    path: '/dashboard/gamer',
    appSource: 'NavGamer',
  },
];

/**
 * Resolve se a tab está ativa. Preserva exatamente a lógica já usada pela
 * Sidebar: abas virtuais casam por query string na rota `/dashboard`, e as
 * demais por prefixo de rota.
 */
export function isDashboardTabActive(
  tab: DashboardTab,
  pathname: string,
  searchParams: URLSearchParams | null
): boolean {
  if (tab.path === '/dashboard') {
    const current = searchParams?.get('tab');
    if (tab.query?.tab === 'overview') {
      return pathname === '/dashboard' && (!current || current === 'overview');
    }
    return pathname === '/dashboard' && current === tab.query?.tab;
  }
  return pathname.startsWith(tab.path);
}
