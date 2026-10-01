'use client';

import React from 'react';
import VoltrisIcon, { type VoltrisIconName } from './VoltrisIcon';

/**
 * Acentos do dashboard = paleta exata do app desktop
 * (D:\APLICATIVO VOLTRIS\UI\Themes\VoltrisDesignSystem.xaml:20-28 e a rampa de
 * acentos de ícone usada em MainWindow.xaml:536-711).
 *
 * Substitui o `indigo-*` do Tailwind que aparecia em ~30 ladrilhos do site.
 */
export const DASHBOARD_ACCENT = {
  /** Roxo elétrico da marca — gradiente central. */
  brand: '#8B31FF',
  /** Azul da marca — fim do gradiente. */
  brandBlue: '#31A8FF',
  /** Rosa/avermelhado da marca — início do gradiente. */
  brandPink: '#FF4B6B',
  /** Verde neon — sucesso. */
  success: '#00FF94',
  /** Âmbar — atenção. */
  warning: '#F59E0B',
  /** Ciano — informação. */
  info: '#00D4FF',
  /** Vermelho — erro/destruição. */
  danger: '#EF4444',
} as const;

export type DashboardAccent = (typeof DASHBOARD_ACCENT)[keyof typeof DASHBOARD_ACCENT];

/**
 * Proporções do ladrilho no app (DeviceInfoView.xaml:130, PerformanceView.xaml:61,
 * VoltrisDesignSystem.xaml:506): o glifo ocupa sempre 50% do ladrilho e o raio
 * acompanha o lado em ~25%.
 */
const TILE_METRICS = {
  8: { box: 32, radius: 8, glyph: 16 },
  9: { box: 36, radius: 9, glyph: 18 },
  10: { box: 40, radius: 10, glyph: 20 },
  12: { box: 48, radius: 12, glyph: 24 },
  14: { box: 56, radius: 14, glyph: 28 },
  16: { box: 64, radius: 16, glyph: 32 },
  20: { box: 80, radius: 20, glyph: 40 },
} as const;

export type TileSize = keyof typeof TILE_METRICS;
export type TileTone = 'tint' | 'gradient';

export interface VoltrisIconTileProps {
  icon: VoltrisIconName;
  /** Acento do glifo. Ignorado quando `tone="gradient"`. */
  accent?: string;
  /** `tint` = ladrilho a 10% de alfa (padrão do app). `gradient` = ladrilho de marca. */
  tone?: TileTone;
  size?: TileSize;
  /** Borda na cor do acento. O ladrilho do app não tem; no site mantida por consistência visual. */
  bordered?: boolean;
  className?: string;
  title?: string;
}

/**
 * Contêiner de ícone do dashboard.
 *
 * Substitui os ~30 ladrilhos duplicados no padrão
 * `w-N h-N rounded-xl bg-{cor}/10 border border-{cor}/20 text-{cor}-400`
 * que estavam espalhados por todas as tabs.
 */
export default function VoltrisIconTile({
  icon,
  accent = DASHBOARD_ACCENT.brand,
  tone = 'tint',
  size = 10,
  bordered = true,
  className = '',
  title,
}: VoltrisIconTileProps) {
  const m = TILE_METRICS[size];

  const style: React.CSSProperties =
    tone === 'gradient'
      ? {
          width: m.box,
          height: m.box,
          borderRadius: m.radius,
          background:
            'linear-gradient(135deg, #FF4B6B 0%, #8B31FF 55%, #31A8FF 100%)',
          // DropShadowEffect Blur=12 Color=#FF4B6B Opacity=0.4 (PerformanceView.xaml:73)
          boxShadow: '0 0 12px rgba(255, 75, 107, 0.4)',
        }
      : {
          width: m.box,
          height: m.box,
          borderRadius: m.radius,
          backgroundColor: `color-mix(in srgb, ${accent} 10%, transparent)`,
          border: bordered ? `1px solid color-mix(in srgb, ${accent} 20%, transparent)` : 'none',
        };

  return (
    <div
      style={style}
      title={title}
      className={`flex items-center justify-center shrink-0 ${className}`}
    >
      <VoltrisIcon
        name={icon}
        size={m.glyph}
        className={tone === 'gradient' ? 'text-white' : ''}
        style={tone === 'gradient' ? undefined : { color: accent }}
      />
    </div>
  );
}
