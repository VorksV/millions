'use client';

import React, { useMemo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import Image from 'next/image';
import Link from 'next/link';
import { usePathname, useSearchParams } from 'next/navigation';
import { createClient } from '@/utils/supabase/client';
import { useAuth } from '@/app/hooks/useAuth';
import VoltrisIcon from './VoltrisIcon';
import { DASHBOARD_TABS, isDashboardTabActive } from './dashboardTabs';

interface SidebarProps {
  mobileOpen?: boolean;
  setMobileOpen?: (open: boolean) => void;
  collapsed?: boolean;
  setCollapsed?: (collapsed: boolean) => void;
}

export default function Sidebar({ mobileOpen = false, setMobileOpen, collapsed, setCollapsed }: SidebarProps) {
  const { user } = useAuth();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const supabase = useMemo(() => createClient(), []);

  const handleLogout = async () => {
    await supabase.auth.signOut();
    window.location.href = '/login';
  };

  const SidebarContent = ({ isMobile = false }) => (
    <div className={`h-full flex flex-col transition-all duration-300 ${!isMobile && collapsed ? 'items-center px-3' : 'px-5'} py-6 relative`}>
      
      {/* Sidebar Header / Logo */}
      <div className={`flex items-center gap-3.5 mb-8 transition-all duration-300 ${!isMobile && collapsed ? 'justify-center' : ''}`}>
        <Link href="/" className="relative flex items-center justify-center shrink-0 group">
          <Image
            src="/logo.png"
            alt="Voltris"
            width={40}
            height={40}
            className="w-10 h-10 object-contain transition-transform duration-300 group-hover:scale-105"
            priority
          />
        </Link>
        {(!collapsed || isMobile) && (
          <Link href="/" className="flex flex-col group">
            <span className="font-bold text-sm tracking-wider uppercase text-white leading-tight group-hover:text-indigo-300 transition-colors">Voltris</span>
            <span className="text-[10px] font-medium text-indigo-400 uppercase tracking-widest leading-none">Console</span>
          </Link>
        )}
      </div>

      {/* Desktop Collapse Toggle */}
      {!isMobile && (
        <button 
          onClick={() => setCollapsed?.(!collapsed)}
          className="absolute -right-3 top-8 w-6 h-6 rounded-full bg-slate-900 hover:bg-indigo-600 border border-slate-700/80 flex items-center justify-center text-slate-400 hover:text-white transition-all z-50 shadow-md group hover:scale-105"
          title={collapsed ? "Expandir" : "Recolher"}
        >
          {collapsed ? <VoltrisIcon name="chevronRight" size={14} /> : <VoltrisIcon name="chevronLeft" size={14} />}
        </button>
      )}

      {/* Profile Section */}
      <div className={`mb-6 transition-all duration-300 ${!isMobile && collapsed ? 'w-10 mx-auto overflow-hidden' : 'w-full'}`}>
        <div className={`flex items-center gap-3 p-2.5 rounded-xl bg-slate-900/60 border border-slate-800/80 transition-all group overflow-hidden ${!isMobile && collapsed ? 'justify-center p-2' : ''}`}>
           <div className="w-8 h-8 rounded-lg bg-slate-800 border border-slate-700/70 flex items-center justify-center text-white flex-shrink-0 font-semibold text-xs">
             {user?.email?.[0]?.toUpperCase() || '?'}
           </div>
           {(!collapsed || isMobile) && (
             <div className="min-w-0 flex-1 flex flex-col">
                <h3 className="text-slate-200 font-semibold text-xs truncate">
                  {user?.user_metadata?.full_name?.split(' ')[0] || user?.email?.split('@')[0] || 'Usuário'}
                </h3>
                <div className="flex items-center gap-1.5 mt-0.5">
                  <div className="w-1.5 h-1.5 rounded-full bg-emerald-400"></div>
                  <span className="text-[10px] font-medium text-slate-400 uppercase tracking-wider">Conectado</span>
                </div>
             </div>
           )}
        </div>
      </div>

      {/* Navigation Menu */}
      <nav className="flex-1 space-y-1 overflow-y-auto no-scrollbar relative z-10">
         {DASHBOARD_TABS.map((tab) => {
            const isActive = isDashboardTabActive(tab, pathname, searchParams);
            const isRail = !isMobile && collapsed;

            return (
              <Link
                key={tab.value}
                href={tab.query ? { pathname: tab.path, query: tab.query } : tab.path}
                onClick={() => setMobileOpen?.(false)}
                aria-current={isActive ? 'page' : undefined}
                title={isRail ? tab.label : ''}
                style={{ '--vtab-color': tab.color } as React.CSSProperties}
                className={`vtab text-xs font-medium tracking-wide ${isRail ? 'vtab--rail p-3' : 'px-3.5 py-2.5'}`}
              >
                <VoltrisIcon name={tab.icon} className="vtab__ico" />
                {!isRail && (
                  <span className="vtab__label truncate">
                    {tab.label}
                  </span>
                )}
              </Link>
            );
         })}
      </nav>

      {/* Logout / Bottom */}
      <div className="pt-4 mt-2 border-t border-slate-800/80 flex-shrink-0">
        <button
          onClick={handleLogout}
          className={`w-full flex items-center gap-3 px-3.5 py-2.5 rounded-xl text-xs font-medium text-slate-400 hover:text-rose-400 hover:bg-rose-500/10 border border-transparent hover:border-rose-500/20 transition-all group overflow-hidden ${!isMobile && collapsed ? 'justify-center p-3' : ''}`}
        >
          <VoltrisIcon name="logout" size={16} className="group-hover:-translate-x-0.5 transition-transform flex-shrink-0" />
          {(!collapsed || isMobile) && (
            <span>Sair da Conta</span>
          )}
        </button>
      </div>

    </div>
  );

  return (
    <>
      {/* Desktop Sidebar Shell */}
      <div className="hidden lg:block h-full w-full">
        <SidebarContent />
      </div>

      {/* Mobile Drawer Shell */}
      <AnimatePresence>
        {mobileOpen && (
          <div className="fixed inset-0 z-[150] lg:hidden">
            {/* Dark Backdrop */}
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="absolute inset-0 bg-black/80 backdrop-blur-md"
              onClick={() => setMobileOpen?.(false)}
            />
            
            {/* Drawer Surface */}
            <motion.div
              initial={{ x: '-100%' }}
              animate={{ x: 0 }}
              exit={{ x: '-100%' }}
              transition={{ type: "spring", damping: 25, stiffness: 200 }}
              className="absolute top-0 left-0 h-full w-[85%] max-w-sm bg-[#0a0a0f] border-r border-white/5 shadow-xl"
            >
              <SidebarContent isMobile />
              {/* Mobile Swipe-Close Handle */}
              <div className="absolute top-1/2 -right-4 w-12 h-20 flex items-center justify-center lg:hidden">
                <div className="w-1.5 h-12 rounded-full bg-white/20"></div>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </>
  );
}
