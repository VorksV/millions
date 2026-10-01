'use client';

import React, { Suspense, useState } from 'react';
import Sidebar from '@/components/dashboard/Sidebar';
import ClientNotificationModal from './ClientNotificationModal';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import { motion } from 'framer-motion';
import { DashboardProvider, useDashboard } from '@/app/context/DashboardContext';
import Link from 'next/link';
import Image from 'next/image';
import UISettingsModal from './UISettingsModal';

// Inner component to access context
function DashboardLayoutInner({ children }: { children: React.ReactNode }) {
  const { transparencyMode, toggleTransparency, sidebarCollapsed, setSidebarCollapsed } = useDashboard();
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);

  return (
    <div className={`flex h-screen w-screen overflow-hidden selection:bg-[#8B31FF]/30 font-sans transition-all duration-700 bg-[#0a0a0f] text-white`}
    style={{
      '--v-glass-bg': transparencyMode ? 'rgba(255, 255, 255, 0.03)' : 'rgba(10, 10, 15, 0.95)',
      '--v-glass-blur': transparencyMode ? '40px' : '0px',
      '--v-glass-border': transparencyMode ? 'rgba(255, 255, 255, 0.06)' : 'rgba(255, 255, 255, 0.02)',
    } as React.CSSProperties}>
      
      {/* BACKGROUND LAYER */}
      <div className="fixed inset-0 z-[-1] overflow-hidden pointer-events-none">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_80%_80%_at_50%_-20%,rgba(99,102,241,0.12),rgba(255,255,255,0))]"></div>
        <div className="absolute -bottom-[20%] -left-[10%] w-[600px] h-[600px] bg-indigo-950/20 rounded-full blur-[100px] pointer-events-none" />
        <div className="absolute inset-0 opacity-[0.015]" 
          style={{ backgroundImage: 'linear-gradient(rgba(255, 255, 255, 0.05) 1px, transparent 1px), linear-gradient(90deg, rgba(255, 255, 255, 0.05) 1px, transparent 1px)', backgroundSize: '60px 60px' }}
        />
      </div>

      {/* Sidebar - Fixa lateral idêntica ao Restaurante */}
      <aside className={`hidden lg:block h-full transition-all duration-500 ease-in-out border-r border-white/5 z-50 ${sidebarCollapsed ? 'w-24' : 'w-72 xl:w-80'} ${transparencyMode ? 'voltris-glass' : 'bg-[#12121A]'}`}>
        <Suspense fallback={<div className="w-full h-full animate-pulse bg-white/5" />}>
          <Sidebar mobileOpen={mobileMenuOpen} setMobileOpen={setMobileMenuOpen} collapsed={sidebarCollapsed} setCollapsed={setSidebarCollapsed} />
        </Suspense>
      </aside>

      {/* Mobile Drawer Hook */}
      <div className="lg:hidden">
        <Sidebar mobileOpen={mobileMenuOpen} setMobileOpen={setMobileMenuOpen} collapsed={sidebarCollapsed} setCollapsed={setSidebarCollapsed} />
      </div>

      {/* Main Body */}
      <div className="flex flex-1 flex-col min-h-0 overflow-hidden relative">
        
        {/* Mobile Topbar */}
        <div className="lg:hidden flex items-center justify-between px-5 py-3.5 bg-[#0a0a0f]/90 backdrop-blur-xl border-b border-slate-800/80 z-[60] sticky top-0">
           <Link href="/" className="flex items-center gap-2.5">
             <Image src="/logo.png" alt="Voltris" width={32} height={32} className="w-8 h-8 object-contain shrink-0" priority />
             <div className="flex flex-col">
               <span className="font-bold text-xs tracking-wider uppercase text-white leading-none">Voltris</span>
               <span className="text-[10px] font-medium text-indigo-400 uppercase tracking-widest leading-tight mt-0.5">Console</span>
             </div>
          </Link>
           <button onClick={() => setMobileMenuOpen(true)} className="w-9 h-9 rounded-lg bg-slate-900 border border-slate-800 flex items-center justify-center active:scale-95 transition-all hover:bg-slate-800 text-slate-300">
               <VoltrisIcon name="menu" size={16} />
           </button>
        </div>

        {/* Scrollable Main Content */}
        <main id="main-dashboard-scroll" className={`flex-1 overflow-y-auto px-4 sm:px-8 py-6 relative z-10 scroll-smooth custom-scrollbar ${transparencyMode ? 'voltris-glass' : 'bg-transparent'}`}>
           <div className="mx-auto max-w-7xl h-full min-h-fit">
             {children}
           </div>
        </main>

        {/* Floating UI Elements (Settings) */}
        <div className="absolute bottom-8 right-8 z-[70] hidden xl:flex flex-col gap-3">
          <motion.button 
            whileHover={{ scale: 1.1 }}
            whileTap={{ scale: 0.9 }}
            onClick={toggleTransparency}
            className="p-3 rounded-2xl bg-[#12121A] shadow-xl flex items-center justify-center text-slate-400 hover:text-indigo-400 transition-all group border border-white/10 hover:border-indigo-500/30"
          >
            {transparencyMode ? <VoltrisIcon name="eye" size={16} /> : <VoltrisIcon name="display" size={16} />}
          </motion.button>
          
          <motion.button 
            whileHover={{ scale: 1.1 }}
            whileTap={{ scale: 0.9 }}
            onClick={() => setIsSettingsOpen(true)}
            className="p-3 rounded-2xl bg-[#12121A] shadow-xl flex items-center justify-center text-slate-400 hover:text-indigo-400 transition-all group border border-white/10 hover:border-indigo-500/30"
          >
            <VoltrisIcon name="settings" size={16} />
          </motion.button>
        </div>
      </div>

      <ClientNotificationModal />
      <UISettingsModal isOpen={isSettingsOpen} onClose={() => setIsSettingsOpen(false)} />
    </div>
  );
}

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <DashboardProvider>
      <DashboardLayoutInner>
        {children}
      </DashboardLayoutInner>
    </DashboardProvider>
  );
}
