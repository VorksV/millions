'use client';

import React from 'react';
import { motion } from 'framer-motion';
import Link from 'next/link';
import { usePathname, useSearchParams } from 'next/navigation';
import VoltrisIcon from './VoltrisIcon';
import { DASHBOARD_TABS, isDashboardTabActive } from './dashboardTabs';

/**
 * Barra de abas horizontal para telas pequenas.
 *
 * Compartilha `DASHBOARD_TABS` com a Sidebar, então o ícone e o acento de cada
 * aba são exatamente os mesmos — e as rotas passam a ser as reais
 * (`/dashboard?tab=...` para as abas virtuais, rota própria nas demais).
 */
export default function MobileTabs() {
  const pathname = usePathname();
  const searchParams = useSearchParams();

  return (
    <div className="bg-[#1E1E1E]/95 backdrop-blur-xl border border-gray-800/30 rounded-xl shadow-lg w-full overflow-hidden">
      <div className="px-4 py-4 w-full">
        <div className="flex space-x-3 overflow-x-auto scrollbar-hide pb-2 w-full -webkit-overflow-scrolling-touch">
          {DASHBOARD_TABS.map((tab) => {
            const isActive = isDashboardTabActive(tab, pathname, searchParams);

            return (
              <Link
                key={tab.value}
                href={tab.query ? { pathname: tab.path, query: tab.query } : tab.path}
                aria-current={isActive ? 'page' : undefined}
                style={{ '--vtab-color': tab.color } as React.CSSProperties}
                className={`vtab vtab--bar shrink-0 gap-3 px-5 py-4 min-h-[52px] rounded-xl text-base whitespace-nowrap border ${
                  isActive ? 'shadow-lg' : 'hover:bg-[#171313]/50'
                }`}
              >
                <VoltrisIcon name={tab.icon} className="vtab__ico" />
                <span className={`vtab__label font-medium ${isActive ? 'text-gradient-premium' : ''}`}>
                  {tab.label}
                </span>
                {/* Indicador ativo */}
                {isActive && (
                  <motion.div
                    layoutId="mobileActiveTab"
                    className="absolute bottom-0 left-0 right-0 h-1 rounded-full"
                    style={{
                      background:
                        'linear-gradient(90deg, #FF4B6B 0%, #8B31FF 50%, #31A8FF 100%)',
                    }}
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    transition={{ duration: 0.3 }}
                  />
                )}
              </Link>
            );
          })}
        </div>
      </div>
    </div>
  );
}
