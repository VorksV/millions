'use client';

import React from 'react';

/**
 * Voltris Icon System — porta fiel dos `<Geometry>` do app desktop.
 *
 * Fonte: D:\APLICATIVO VOLTRIS\UI\Themes\Icons.xaml
 *        D:\APLICATIVO VOLTRIS\UI\Views\DashboardView.xaml (ícone de pessoa)
 *
 * Invariantes herdadas do XAML (SidebarPremium.xaml:209-256):
 *  1. Viewport 24x24 (viewBox="0 0 24 24").
 *  2. SOMENTE preenchimento — nunca stroke. `fillRule` = nonzero (padrão WPF),
 *     preservando os vazados negativos desenhados pelo próprio path.
 *  3. Escala visual vem de `transform: scale()`, nunca de width/height, para
 *     que o rótulo da tab nunca reflui ao trocar de estado.
 */

export type VoltrisIconName =
  // Navegação do dashboard do site
  | 'home'
  | 'license'
  | 'orders'
  | 'system'
  | 'security'
  | 'bolt'
  | 'streamHub'
  | 'person'
  | 'support'
  | 'bell'
  | 'deviceInfo'
  | 'gamer'
  // Coleção completa do app, disponível para reuso
  | 'performance'
  | 'cleanup'
  | 'network'
  | 'diagnostics'
  | 'history'
  | 'logs'
  | 'services'
  | 'scheduler'
  | 'drivers'
  | 'repair'
  | 'recovery'
  | 'privacy'
  | 'debloat'
  | 'settings'
  | 'ram'
  | 'disk'
  | 'personalize'
  | 'display'
  | 'shortcuts'
  | 'success'
  | 'error'
  | 'warning'
  | 'info'
  | 'processing'
  | 'rocket'
  // Conceitos exclusivos do site, desenhados no mesmo idioma visual do app
  // (Material filled 24x24 — a mesma família de onde saem os glifos do XAML:
  // memory, build, visibility_off, delete, settings, auto_awesome, wifi).
  | 'package'
  | 'creditCard'
  | 'download'
  | 'externalLink'
  | 'copy'
  | 'clock'
  | 'plus'
  | 'close'
  | 'arrowLeft'
  | 'arrowRight'
  | 'search'
  | 'edit'
  | 'save'
  | 'mail'
  | 'phone'
  | 'mapPin'
  | 'calendar'
  | 'key'
  | 'smartphone'
  | 'inbox'
  | 'send'
  | 'filter'
  | 'trendingUp'
  | 'pieChart'
  | 'barChart'
  | 'target'
  | 'boltOff'
  | 'check'
  | 'trash'
  | 'power'
  | 'terminal'
  | 'activity'
  | 'message'
  | 'alertTriangle'
  | 'alertCircle'
  | 'lock'
  | 'database'
  | 'users'
  | 'link'
  | 'eye'
  | 'layout'
  | 'menu'
  | 'logout';

export const VOLTRIS_ICON_PATHS: Record<VoltrisIconName, string> = {
  // ── Dashboard ────────────────────────────────────────────────────────────
  // Icons.xaml:10 — HomeIcon (grade 2x2 de quadrados arredondados)
  home:
    'M3.5,4.5C3.5,3.95 3.95,3.5 4.5,3.5h4C9.05,3.5 9.5,3.95 9.5,4.5v4C9.5,9.05 9.05,9.5 8.5,9.5h-4C3.95,9.5 3.5,9.05 3.5,8.5V4.5z' +
    'M14.5,4.5C14.5,3.95 14.95,3.5 15.5,3.5h4C20.05,3.5 20.5,3.95 20.5,4.5v4C20.5,9.05 20.05,9.5 19.5,9.5h-4C14.95,9.5 14.5,9.05 14.5,8.5V4.5z' +
    'M3.5,15.5C3.5,14.95 3.95,14.5 4.5,14.5h4C9.05,14.5 9.5,14.95 9.5,15.5v4C9.5,20.05 9.05,20.5 8.5,20.5h-4C3.95,20.5 3.5,20.05 3.5,19.5V15.5z' +
    'M14.5,15.5C14.5,14.95 14.95,14.5 15.5,14.5h4C20.05,14.5 20.5,14.95 20.5,15.5v4C20.5,20.05 20.05,20.5 19.5,20.5h-4C14.95,20.5 14.5,20.05 14.5,19.5V15.5z',

  // Icons.xaml:68 — ShieldIcon, silhueta cheia (marca Voltris / licença).
  // Subtrai-se o segundo subpath interno do original: a versão sólida continua
  // legível a 16px, enquanto o vazado de ~2px do original embrulha a 20px.
  license:
    'M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1Z',

  // Icons.xaml:56 — BenchmarkIcon (barras + linha de tendência)
  orders:
    'M5,20h14V18H6v1H5V20zM16.5,18v-5H14v5H16.5zM12.25,18V9H9.5v9H12.25zM8,18V6H5.5v12H8z' +
    'M17,12.5L14,15.5l-2,-2l-2,2v1.5l3,-3l2,2l3,-3V12.5z',

  // Icons.xaml:43 — SystemIcon (chip de CPU com pinos)
  system:
    'M9,3V5H7A2,2 0 0,0 5,7V9H3V11H5V13H3V15H5V17A2,2 0 0,0 7,19H9V21H11V19H13V21H15V19H17A2,2 0 0,0 19,17V15H21V13H19V11H21V9H19V7A2,2 0 0,0 17,5H15V3H13V5H11V3H9M7,7H17V17H7V7M9,9V15H15V9H9Z',

  // Icons.xaml:50 — SecurityIcon (escudo com cadeado, vazado interno)
  security:
    'M12,1L3,5v6c0,5.55 3.84,10.74 9,12c5.16,-1.26 9,-6.45 9,-12V5L12,1z' +
    'M12,7c1.1,0 2,0.9 2,2 0,0.9-0.6,1.66-1.4,1.92L12.8,12h-1.6l-0.2,-1.08C10.2,10.66 10,10.36 10,9c0,-1.1 0.9,-2 2,-2z' +
    'M12,13.5c0.97,0 1.75,0.78 1.75,1.75S12.97,17 12,17s-1.75,-0.78 -1.75,-1.75S11.03,13.5 12,13.5z',

  // Icons.xaml:40 — BoltIcon (raio)
  bolt: 'M7,2v11h3v9l7-12h-4l4-8H7z',

  // Ícones.xaml:96 — StreamHubIcon (ondas de transmissão / hub de nós).
  // O original empilha 4 circunferências concêntricas com ventos mistos e, sob
  // `nonzero`, colapsa num disco sólido. Aqui as circunferências foram
  // re-ordenadas para dois anéis + núcleo, com o vento interno inverso ao
  // externo — que é o que produz a faixa vazada real.
  streamHub:
    'M12,1A11,11 0 0,1 23,12A11,11 0 0,1 12,23A11,11 0 0,1 1,12A11,11 0 0,1 12,1Z' +
    'M12,3.5A8.5,8.5 0 0,0 3.5,12A8.5,8.5 0 0,0 12,20.5A8.5,8.5 0 0,0 20.5,12A8.5,8.5 0 0,0 12,3.5Z' +
    'M12,5A7,7 0 0,1 19,12A7,7 0 0,1 12,19A7,7 0 0,1 5,12A7,7 0 0,1 12,5Z' +
    'M12,7.5A4.5,4.5 0 0,0 7.5,12A4.5,4.5 0 0,0 12,16.5A4.5,4.5 0 0,0 16.5,12A4.5,4.5 0 0,0 12,7.5Z' +
    'M12,9.8A2.2,2.2 0 0,1 14.2,12A2.2,2.2 0 0,1 12,14.2A2.2,2.2 0 0,1 9.8,12A2.2,2.2 0 0,1 12,9.8Z',

  // Sem equivalente no app. Desenhado no mesmo idioma visual (preenchimento
  // sólido, cantos arredondados) — cabeça + ombros.
  person:
    'M12,4.4A3.6,3.6 0 0,1 15.6,8A3.6,3.6 0 0,1 12,11.6A3.6,3.6 0 0,1 8.4,8A3.6,3.6 0 0,1 12,4.4Z' +
    'M12,13.2C7.9,13.2 4.5,15.8 4.5,19C4.5,19.9 5.2,20.6 6.1,20.6H17.9C18.8,20.6 19.5,19.9 19.5,19' +
    'C19.5,15.8 16.1,13.2 12,13.2Z',

  // Sem equivalente no app. Desenhado no mesmo idioma visual — fone de
  // suporte em silhueta única, com o recorte que abre a faixa do arco.
  support:
    'M12,1C6.48,1 2,5.48 2,11v7c0,1.66 1.34,3 3,3h3v-8H5v-2c0-3.87 3.13,-7 7,-7s7,3.13 7,7v2h-3v8h3' +
    'c1.66,0 3,-1.34 3,-3v-7C22,5.48 17.52,1 12,1Z',

  // Icons.xaml:105 — IconBell
  bell:
    'M12 22c1.1 0 2-.9 2-2h-4c0 1.1.9 2 2 2zm6-6v-5c0-3.07-1.63-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68' +
    'C7.64 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z',

  // Icons.xaml:65 — DeviceInfoIcon (monitor com "i")
  deviceInfo:
    'M20,4H4C2.9,4 2,4.9 2,6v10c0,1.1 0.9,2 2,2h6v2H8v2h8v-2h-2v-2h6c1.1,0 2,-0.9 2,-2V6C22,4.9 21.1,4 20,4z' +
    'M4,6h16v10H4V6zM12,9.5c0.97,0 1.75,0.78 1.75,1.75s-0.78,1.75 -1.75,1.75s-1.75,-0.78 -1.75,-1.75S11.03,9.5 12,9.5z' +
    'M11,13.5h2v3h-2v-3zM11,7.5h2v1.5h-2V7.5z',

  // Icons.xaml:22 — GamerIcon (gamepad com d-pad e botões)
  gamer:
    'M18,6H6A4,4 0 0,0 2,10v4A4,4 0 0,0 6,18c1.44,0 2.8,-0.79 3.47,-2h5.06C15.2,17.21 16.56,18 18,18a4,4 0 0,0 4,-4v-4A4,4 0 0,0 18,6z' +
    'M8.5,12A1.5,1.5 0 1,1 8.5,9A1.5,1.5 0 0,1 8.5,12M15.5,12A1.5,1.5 0 1,1 15.5,9A1.5,1.5 0 0,1 15.5,12' +
    'M11,8h2v2h2v2h-2v2h-2v-2H9v-2h2V8z',

  // ── Coleção do app (reuso) ───────────────────────────────────────────────
  // Icons.xaml:13 — PerformanceIcon (velocímetro)
  performance:
    'M13,2.05V5.08C16.39,5.57 19,8.47 19,12C19,12.9 18.82,13.75 18.5,14.54L21.12,16.07C21.68,14.83 22,13.45 22,12C22,6.82 18.05,2.55 13,2.05' +
    'M12,19A7,7 0 0,1 5,12C5,8.47 7.61,5.57 11,5.08V2.05C5.94,2.55 2,6.81 2,12A10,10 0 0,0 12,22C15.3,22 18.23,20.39 20.05,17.91L17.45,16.38C16.17,18 14.21,19 12,19Z',
  // Icons.xaml:16 — CleanupIcon (brilhos)
  cleanup:
    'M10,3l2,5 5,2-5,2-2,5-2-5L3,10l5-2 2-5zM19,4.5l0.75,1.75 1.75,0.75-1.75,0.75L19,9.5l-0.75,-1.75L16.5,7l1.75,-0.75L19,4.5z' +
    'M6,15.5l0.6,1.4L8,17.5l-1.4,0.6L6,19.5l-0.6,-1.4L4,17.5l1.4,-0.6L6,15.5z',
  // Icons.xaml:19 — NetworkIcon (wi-fi 3 arcos)
  network:
    'M1,9l2,2c4.97,-4.97 13.03,-4.97 18,0l2,-2C16.93,2.93 7.07,2.93 1,9zM5,13l2,2c2.76,-2.76 7.24,-2.76 10,0l2,-2C15.14,9.14 8.86,9.14 5,13zM9,17l3,3l3,-3c-1.65,-1.66 -4.34,-1.66 -6,0z',
  // Icons.xaml:25 — DiagnosticsIcon (monitor com pulso)
  diagnostics:
    'M20,4H4C2.9,4 2,4.9 2,6v9c0,1.1 0.9,2 2,2h5v1H7v4h10v-4h-2v-1h5c1.1,0 2,-0.9 2,-2V6C22,4.9 21.1,4 20,4zM19,15H5V6h14V15z' +
    'M11,12.6c-0.09,0-0.18,0-0.27,-0.01l-0.5,-0.13c-1.31,-0.34-2.2,-1.24-2.2,-2.35 0,-1.15 0.83,-2.15 1.99,-2.15 0.44,0 0.87,0.17 1.18,0.46 0.13,0.12 0.35,0.12 0.48,0 0.31,-0.29 0.74,-0.46 1.18,-0.46 1.16,0 1.99,1 1.99,2.15 0,1.11 -0.89,2.01 -2.2,2.35l-0.5,0.13c-0.09,0.01 -0.18,0.01 -0.27,0.01z',
  // Icons.xaml:28 — HistoryIcon (relógio com seta)
  history:
    'M13,3c-4.97,0-9,4.03-9,9H1l3.89,3.89 0.07,0.14L9,12H6c0-3.87,3.13-7,7-7s7,3.13 7,7-3.13,7-7,7c-1.93,0-3.68,-0.78-4.95,-2.05l-1.41,1.41C8.41,19.97 10.63,21 13,21c4.97,0 9-4.03 9,-9s-4.03,-9-9,-9z' +
    'M12,8v5l4.28,2.54 0.72,-1.21 -3.5,-2.08V8H12z',
  // Icons.xaml:31 — LogsIcon (documento)
  logs: 'M19,3H5C3.9,3 3,3.9 3,5v14c0,1.1 0.9,2 2,2h14c1.1,0 2,-0.9 2,-2V5C21,3.9 20.1,3 19,3zM14,17H7v-2h7V17zM17,13H7v-2h10V13zM17,9H7V7h10V9z',
  // Icons.xaml:34 — ServicesIcon (camadas). O 2º subpath do original é aberto
  // (área nula); aqui foi fechado para que a camada de baixo realmente apareça.
  services:
    'M12,2.5L1.5,7.5 12,12.5 22.5,7.5 12,2.5z' +
    'M1.5,12 12,17l10.5,-5 -1.5,-0.75L12,15.5 3,6.25 1.5,7.5V12z' +
    'M1.5,16.5 12,21.5 22.5,16.5 21,15.75 12,20 3,15.75 1.5,16.5z',
  // Icons.xaml:37 — SchedulerIcon (calendário com relógio)
  scheduler:
    'M6,3h12c1.1,0 2,0.9 2,2v14c0,1.1-0.9,2-2,2H6c-1.1,0-2,-0.9-2,-2V5C4,3.9 4.9,3 6,3zM4,8h16v11c0,1.1-0.9,2-2,2H6c-1.1,0-2,-0.9,-2,-2V8z' +
    'M8,4h2v2H8zM12,4h2v2h-2zM16,4h2v2h-2zM13.5,10.5a3.5,3.5 0 1,1 -0.01,0zM13.5,12.5a2,2 0 1,1 0.01,0zM13.15,12.4h0.7v2.4h-0.7zM12.5,14.1h2.3v0.7h-2.3z',
  // Icons.xaml:44 — DriversIcon (drives empilhados)
  drivers:
    'M19,10V7A2,2 0 0,0 17,5H7A2,2 0 0,0 5,7V10A2,2 0 0,0 7,12H17A2,2 0 0,0 19,10M7,7H17V10H7V7' +
    'M19,19V16A2,2 0 0,0 17,14H7A2,2 0 0,0 5,16V19A2,2 0 0,0 7,21H17A2,2 0 0,0 19,19M7,16H17V19H7V16M8,8H10V9H8V8M8,17H10V18H8V17Z',
  // Icons.xaml:47 — RepairIcon (chave inglesa)
  repair:
    'M22.7,19l-9.1,-9.1c0.9,-2.3 0.4,-5 -1.5,-6.9 -2,-2 -5,-2.4 -7.4,-1.3L9,6L6,9L1.6,4.7C0.4,7.1 0.9,10.1 2.9,12.1c1.9,1.9 4.6,2.4 6.9,1.5l9.1,9.1c0.4,0.4 1,0.4 1.4,0l2.3,-2.3c0.4,-0.4 0.4,-1 0,-1.4z',
  // Icons.xaml:53 — RecoveryIcon (seta circular de restauração)
  recovery:
    'M12,5V1L7,6l5,5V7c3.31,0 6,2.69 6,6s-2.69,6-6,6c-2.97,0-5.43,-2.16-5.9,-5H3.03c0.52,3.78 3.72,7 7.97,7c4.42,0 8,-3.58 8,-8s-3.58,-8 -8,-8z',
  // Icons.xaml:59 — PrivacyIcon (olho com risco)
  privacy:
    'M12,7c2.76,0 5,2.24 5,5c0,0.65 -0.13,1.26 -0.36,1.83l2.92,2.92c1.51,-1.26 2.7,-2.89 3.43,-4.75c-1.73,-4.39 -6,-7.5 -11,-7.5c-1.4,0 -2.74,0.25 -3.98,0.7l2.16,2.16C10.74,7.13 11.35,7 12,7z' +
    'M2,4.27l2.28,2.28l0.46,0.46C3.08,8.3 1.78,10.02 1,12c1.73,4.39 6,7.5 11,7.5c1.55,0 3.03,-0.3 4.38,-0.84l0.42,0.42L19.73,22L21,20.73L3.27,3L2,4.27z' +
    'M7.53,9.8l1.55,1.55C9.03,11.56 9,11.77 9,12c0,1.66 1.34,3 3,3c0.23,0 0.44,-0.03 0.65,-0.08l1.55,1.55C13.68,16.8 12.89,17 12,17c-2.76,0-5,-2.24-5,-5c0,-0.89 0.2,-1.68 0.53,-2.2z' +
    'M11.84,9.02l3.15,3.15C14.99,12.11 15,12.06 15,12c0,-1.66 -1.34,-3 -3,-3C11.96,9 11.9,9.01 11.84,9.02z',
  // Icons.xaml:62 — DebloatIcon (lixeira + varredura)
  debloat:
    'M15,16h4v2h-4v-2zM15,8h7v2h-7V8zM15,12h7v2h-7v-2zM3,18c0,1.1 0.9,2 2,2h6c1.1,0 2,-0.9 2,-2V8H3v10zM3,5h4l1,-1h6l1,1h4v2H3V5z',
  // Icons.xaml:70 — SettingsIcon (engrenagem)
  settings:
    'M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.07-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59 8.12,5.92 7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.74,8.87C2.62,9.08 2.66,9.34 2.86,9.48l2.03,1.58C4.84,11.36 4.8,11.69 4.8,12s0.02,0.64 0.07,0.94l-2.03,1.58c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39,-0.96c0.5,0.38 1.03,0.7 1.62,0.94l0.36,2.54c0.05,0.24 0.24,0.41 0.48,0.41h3.84c0.24,0 0.44,-0.17 0.47,-0.41l0.36,-2.54c0.59,-0.24 1.13,-0.56 1.62,-0.94l2.39,0.96c0.22,0.08 0.47,0 0.59,-0.22l1.92,-3.32c0.12,-0.22 0.07,-0.47 -0.12,-0.61L19.14,12.94z' +
    'M12,15.6c-1.98,0-3.6-1.62-3.6-3.6s1.62,-3.6,3.6,-3.6s3.6,1.62 3.6,3.6S13.98,15.6 12,15.6z',
  // Icons.xaml:73 — IconRam (chip de memória)
  ram:
    'M17,17H7V7H17M21,11V9H19V7C19,5.89 18.1,5 17,5H15V3H13V5H11V3H9V5H7C5.89,5 5,5.89 5,7V9H3V11H5V13H3V15H5V17C5,18.1 5.89,19 7,19H9V21H11V19H13V21H15V19H17C18.1,19 19,18.1 19,17V15H21V13H19V11M13,13H11V11H13V13Z',
  // Icons.xaml:76 — IconDisk (HDD)
  disk:
    'M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2M12,17A2,2 0 0,0 14,15A2,2 0 0,0 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M8,5V7H16V5H8Z',
  // Icons.xaml:90 — PersonalizeIcon (sliders)
  personalize:
    'M3,17V19H9V17H3M3,5V7H13V5H3M13,21V19H21V17H13V15H11V21H13M7,9V11H3V13H7V15H9V9H7M21,13V11H11V13H21M15,9H17V7H21V5H17V3H15V9Z',
  // Icons.xaml:93 — DisplayIcon (monitor)
  display:
    'M20,18C21.1,18 22,17.1 22,16V6C22,4.89 21.1,4 20,4H4A2,2 0 0,0 2,6V16A2,2 0 0,0 4,18H0V20H24V18H20M4,6H20V16H4V6Z',
  // Icons.xaml:98 — ShortcutsIcon (chevron duplo)
  shortcuts:
    'M5.6,6L11,12l-5.4,6L4,16.8L8.2,12L4,7.2L5.6,6z M11,6l5.4,6L11,18l-1.4-1.6L13.8,12l-4.2-4.8L11,6z',
  // Icons.xaml:101 — IconSuccess
  success:
    'M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M10,17L5,12L6.41,10.59L10,14.17L17.59,6.58L19,8L10,17Z',
  // Icons.xaml:102 — IconError
  error:
    'M12,2C6.47,2 2,6.47 2,12C2,17.53 6.47,22 12,22C17.53,22 22,17.53 22,12C22,6.47 17.53,2 12,2' +
    'M17,15.59L15.59,17L12,13.41L8.41,17L7,15.59L10.59,12L7,8.41L8.41,7L12,10.59L15.59,7L17,8.41L13.41,12L17,15.59Z',
  // Icons.xaml:103 — IconWarning
  warning:
    'M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,16H13V18H11V16M11,6H13V14H11V6Z',
  // Icons.xaml:104 — IconInfo
  info:
    'M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,11H13V17H11V11M11,7H13V9H11V7Z',
  // Icons.xaml:106 — IconProcessing
  processing:
    'M12,4V1L8,5l4,4V6c3.31,0,6,2.69,6,6c0,1.01-.25,1.97-.7,2.8l1.46,1.46C19.54,15.03,20,13.57,20,12C20,7.58,16.42,4,12,4z' +
    'M12,18c-3.31,0-6-2.69-6-6c0-1.01,.25-1.97,.7-2.8L4.24,7.74C3.46,8.97,3,10.43,3,12c0,4.42,3.58,8,8,8v3l4-4l-4-4V18z',
  // Icons.xaml:107 — RocketIcon
  rocket:
    'M12,2C12,2 9,6 9,11C9,12.1 9.3,13.1 9.8,14H14.2C14.7,13.1 15,12.1 15,11C15,6 12,2 12,2M12,18C11,18 10,17.4 9.4,16.5H14.6C14,17.4 13,18 12,18' +
    'M5,12C3,12 2,14 2,14C2,14 2.8,15.6 5,16V12M19,12C21,12 22,14 22,14C22,14 21.2,15.6 19,16V12Z',

  // ── Conceitos exclusivos do site (Material filled, 24x24) ────────────────
  package:
    'M20 2H4c-1.1 0-2 .9-2 2v3.01c0 .72.43 1.34 1 1.69V20c0 1.1 1.1 2 2 2h14c.9 0 2-.9 2-2V8.7c.57-.35 1-.97 1-1.69V4c0-1.1-1-2-2-2zm-5 12H9v-2h6v2zm5-7H4V4h16v3z',
  creditCard:
    'M20 4H4c-1.11 0-1.99.89-1.99 2L2 18c0 1.11.89 2 2 2h16c1.11 0 2-.89 2-2V6c0-1.11-.89-2-2-2zm0 14H4v-6h16v6zm0-10H4V6h16v2z',
  download: 'M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z',
  externalLink:
    'M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z',
  copy:
    'M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z',
  clock:
    'M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8z' +
    'M12.5 7H11v6l5.25 3.15.75-1.23-4.5-2.67z',
  plus: 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z',
  close:
    'M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z',
  arrowLeft: 'M20 11H7.83l5.59-5.59L12 4l-8 8 8 8 1.41-1.41L7.83 13H20v-2z',
  arrowRight: 'M12 4l-1.41 1.41L16.17 11H4v2h12.17l-5.58 5.59L12 20l8-8z',
  search:
    'M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z',
  edit:
    'M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z',
  save:
    'M17 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V7l-4-4zm-5 16c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm3-10H5V5h10v4z',
  mail:
    'M20 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 4l-8 5-8-5V6l8 5 8-5v2z',
  phone:
    'M6.62 10.79c1.44 2.83 3.76 5.14 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z',
  mapPin:
    'M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z',
  calendar:
    'M19 3h-1V1h-2v2H8V1H6v2H5c-1.11 0-1.99.9-1.99 2L3 19c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V8h14v11zM7 10h5v5H7z',
  key:
    'M12.65 10C11.83 7.67 9.61 6 7 6c-3.31 0-6 2.69-6 6s2.69 6 6 6c2.61 0 4.83-1.67 5.65-4H17v4h4v-4h2v-4H12.65zM7 14c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2z',
  smartphone:
    'M17 1.01L7 1c-1.1 0-2 .9-2 2v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-1.99-2-1.99zM17 19H7V5h10v14z',
  inbox:
    'M19 3H4.99c-1.11 0-1.98.89-1.98 2L3 19c0 1.1.88 2 1.99 2H19c1.1 0 2-.9 2-2V5c0-1.11-.9-2-2-2zm0 12h-4c0 1.66-1.35 3-3 3s-3-1.34-3-3H4.99V5H19v10z',
  send: 'M2.01 21L23 12 2.01 3 2 10l15 2-15 2z',
  filter:
    'M4.25 5.61C6.27 8.2 10 13 10 13v6c0 .55.45 1 1 1h2c.55 0 1-.45 1-1v-6s3.72-4.8 5.74-7.39c.51-.66.04-1.61-.79-1.61H5.04c-.83 0-1.3.95-.79 1.61z',
  trendingUp: 'M16 6l2.29 2.29-4.88 4.88-4-4L2 16.59 3.41 18l6-6 4 4 6.3-6.29L22 12V6z',
  pieChart:
    'M11 2v20c-5.07-.5-9-4.79-9-10s3.93-9.5 9-10zm2.03 0v8.99H22c-.47-4.75-4.25-8.54-8.97-8.99zm0 11.01V22c4.74-.46 8.5-4.25 8.97-8.99h-8.97z',
  barChart: 'M5 9.2h3V19H5zM10.6 5h2.8v14h-2.8zm5.6 8H19v6h-2.8z',
  target:
    'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM3.05 13H1v-2h2.05C3.5 6.83 6.83 3.5 11 3.05V1h2v2.05c4.17.45 7.5 3.83 7.95 7.95H23v2h-2.05c-.45 4.17-3.83 7.5-7.95 7.95V23h-2v-2.05C6.83 20.5 3.5 17.17 3.05 13z' +
    'M12 5c-3.87 0-7 3.13-7 7s3.13 7 7 7 7-3.13 7-7-3.13-7-7-7z',
  boltOff:
    'M3.27 3L2 4.27l5 5V13h3v9l3.58-6.14L17.73 20 19 18.73 3.27 3zM17 10h-4l4-8H7v2.18l8.46 8.46L17 10z',
  check: 'M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z',
  trash: 'M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z',
  power:
    'M13 3h-2v10h2V3zM17.83 5.17l-1.42 1.42C17.99 7.86 19 9.81 19 12c0 3.87-3.13 7-7 7s-7-3.13-7-7c0-2.19 1.01-4.14 2.58-5.42L6.17 5.17C4.23 6.82 3 9.26 3 12c0 4.97 4.03 9 9 9s9-4.03 9-9c0-2.74-1.23-5.18-3.17-6.83z',
  terminal:
    'M20 4H4c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 14H4V8h16v10z' +
    'M9.6 14.4L8.2 13l3.4-3.4-1.4-1.4-4.8 4.8 4.8 4.8 1.4-1.4-3.4-3.4zM16 16h-5v2h5v-2z',
  activity: 'M3.5 18.49l6-6.01 4 4L22 6.92l-1.41-1.41-7.09 7.97-4-4L2 16.99z',
  message: 'M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2z',
  alertTriangle: 'M1 21h22L12 2 1 21zm12-3h-2v-2h2v2zm0-4h-2v-4h2v4z',
  alertCircle:
    'M11 15h2v2h-2v-2zm0-8h2v6h-2V7zm1-5C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z',
  lock:
    'M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3 3.1-3s3.1 1.29 3.1 3v2z',
  database:
    'M12 2C7.58 2 4 3.34 4 5v14c0 1.66 3.58 3 8 3s8-1.34 8-3V5c0-1.66-3.58-3-8-3zm0 2c3.87 0 6 1.05 6 1s-2.13 1-6 1-6-1.05-6-1 2.13-1 6-1zm0 16c-3.87 0-6-1.05-6-1v-3.29c1.61.66 3.79 1.05 6 1.05s4.39-.39 6-1.05V19c0-.05-2.13 1-6 1zm0-5c-3.87 0-6-1.05-6-1v-3.29C7.61 11.37 9.79 12 12 12s4.39-.63 6-1.29V14c0 .05-2.13 1-6 1z',
  users:
    'M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z',
  link:
    'M3.9 12c0-1.71 1.39-3.1 3.1-3.1h4V7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h4v-1.9H7c-1.71 0-3.1-1.39-3.1-3.1zM8 13h8v-2H8v2zm9-6h-4v1.9h4c1.71 0 3.1 1.39 3.1 3.1s-1.39 3.1-3.1 3.1h-4V17h4c2.76 0 5-2.24 5-5s-2.24-5-5-5z',
  eye:
    'M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5z' +
    'M12 9c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z',
  layout: 'M3 3h8v8H3zm10 0h8v8h-8zM3 13h8v8H3zm10 0h8v8h-8z',
  menu: 'M3 18h18v-2H3v2zm0-5h18v-2H3v2zm0-7v2h18V6H3z',
  logout:
    'M17 7l-1.41 1.41L18.17 11H8v2h10.17l-2.58 2.58L17 17l5-5zM4 5h8V3H4c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h8v-2H4V5z',
};

export interface VoltrisIconProps {
  name: VoltrisIconName;
  /** Tamanho da caixa em px. Se omitido, herda do tamanho da fonte via w/h. */
  size?: number;
  className?: string;
  /** Permite sobrescrever a cor (`fill="currentColor"`). */
  style?: React.CSSProperties;
  title?: string;
}

export default function VoltrisIcon({ name, size, className = '', style, title }: VoltrisIconProps) {
  const d = VOLTRIS_ICON_PATHS[name];
  if (!d) return null;

  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      className={className}
      style={style}
      fill="currentColor"
      fillRule="nonzero"
      stroke="none"
      focusable="false"
      aria-hidden={title ? undefined : true}
      role={title ? 'img' : undefined}
      shapeRendering="geometricPrecision"
    >
      {title ? <title>{title}</title> : null}
      <path d={d} />
    </svg>
  );
}
