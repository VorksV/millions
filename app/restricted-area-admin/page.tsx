'use client';

import { useAuth } from '@/app/hooks/useAuth';
import AuthGuard from '@/components/AuthGuard';
import { useState } from 'react';
import {
  FiUsers, FiMail, FiMenu, FiChevronLeft, FiChevronRight,
  FiLogOut, FiHome, FiZap, FiRefreshCw
} from 'react-icons/fi';
import { ClipboardList, TicketCheck } from 'lucide-react';
import AdminOrdersTab from './orders/AdminOrdersTab';
import AdminTicketsTab from './tickets/AdminTicketsTab';
import AdminNewsletterTab from './newsletter/AdminNewsletterTab';
import AdminUsersTab from './users/AdminUsersTab';
import { motion, AnimatePresence } from 'framer-motion';
import Link from 'next/link';
import Image from 'next/image';

const tabs = [
  { label: 'Visão Geral',  value: 'overview',    icon: FiHome,         color: 'text-slate-400' },
  { label: 'Pedidos',      value: 'orders',      icon: ClipboardList,  color: 'text-indigo-400' },
  { label: 'Tickets',      value: 'tickets',     icon: TicketCheck,    color: 'text-violet-400' },
  { label: 'Newsletter',   value: 'newsletter',  icon: FiMail,         color: 'text-amber-400' },
  { label: 'Usuários',     value: 'users',       icon: FiUsers,        color: 'text-emerald-400' },
];

export default function AdminDashboard() {
  const { user, loading, error, signOut } = useAuth();
  const [activeTab, setActiveTab] = useState('orders');
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  if (loading) {
    return (
      <div className="min-h-screen w-full flex items-center justify-center bg-slate-950 text-white">
        <div className="text-center space-y-4">
          <div className="w-10 h-10 border-2 border-indigo-500 border-t-transparent rounded-full animate-spin mx-auto" />
          <p className="text-xs text-slate-500 font-medium uppercase tracking-widest">Verificando acesso…</p>
        </div>
      </div>
    );
  }

  const handleLogout = async () => {
    await signOut();
    window.location.href = '/login';
  };

  const SidebarContent = ({ isMobile = false }) => (
    <div className={`h-full flex flex-col ${!isMobile && sidebarCollapsed ? 'items-center px-3' : 'px-5'} py-6 relative`}>
      {/* Logo */}
      <div className={`flex items-center gap-3 mb-8 ${!isMobile && sidebarCollapsed ? 'justify-center' : ''}`}>
        <div className="w-9 h-9 rounded-lg flex-shrink-0 overflow-hidden">
          <Image src="/logo.png" alt="Voltris" width={36} height={36} className="w-full h-full object-contain" />
        </div>
        {(!sidebarCollapsed || isMobile) && (
          <div className="flex flex-col leading-none">
            <span className="text-xs font-bold text-slate-200 tracking-widest uppercase">Voltris</span>
            <span className="text-[10px] text-indigo-400 font-semibold tracking-widest uppercase mt-0.5">Admin</span>
          </div>
        )}
      </div>

      {/* Collapse Toggle */}
      {!isMobile && (
        <button
          onClick={() => setSidebarCollapsed(!sidebarCollapsed)}
          className="absolute -right-3 top-8 w-6 h-6 rounded-full bg-slate-800 hover:bg-indigo-600 border border-slate-700 flex items-center justify-center text-slate-400 hover:text-white transition-all z-50"
        >
          {sidebarCollapsed ? <FiChevronRight className="w-3 h-3" /> : <FiChevronLeft className="w-3 h-3" />}
        </button>
      )}

      {/* Admin Profile */}
      <div className={`mb-6 ${!isMobile && sidebarCollapsed ? 'w-10 mx-auto' : 'w-full'}`}>
        <div className={`flex items-center gap-3 p-2.5 rounded-xl bg-slate-800/60 border border-slate-700/50 ${!isMobile && sidebarCollapsed ? 'justify-center' : ''}`}>
          <div className="w-8 h-8 rounded-lg bg-indigo-600/20 border border-indigo-500/30 flex items-center justify-center flex-shrink-0">
            <span className="text-xs font-bold text-indigo-300">{user?.email?.[0]?.toUpperCase() || 'A'}</span>
          </div>
          {(!sidebarCollapsed || isMobile) && (
            <div className="min-w-0 flex-1">
              <p className="text-xs font-semibold text-slate-200 truncate">System Admin</p>
              <p className="text-[10px] text-emerald-400 font-medium">Nível Supremo</p>
            </div>
          )}
        </div>
      </div>

      {/* Nav */}
      <nav className="flex-1 space-y-1 overflow-y-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
        {tabs.map((tab) => {
          const isActive = activeTab === tab.value;
          const Icon = tab.icon;
          return (
            <button
              key={tab.value}
              onClick={() => { setActiveTab(tab.value); setMobileMenuOpen(false); }}
              className={`w-full group flex items-center gap-3 transition-all duration-200 rounded-xl relative
                ${!isMobile && sidebarCollapsed ? 'p-3 justify-center' : 'px-3 py-2.5'}
                ${isActive
                  ? 'bg-slate-800 border border-slate-700/80 text-slate-100'
                  : 'text-slate-500 hover:text-slate-300 hover:bg-slate-800/50'}
              `}
              title={sidebarCollapsed && !isMobile ? tab.label : ''}
            >
              {isActive && (
                <motion.div
                  layoutId="admin-active-indicator"
                  className="absolute left-0 top-1/2 -translate-y-1/2 w-0.5 h-5 rounded-r-full bg-indigo-500"
                  transition={{ type: 'spring', bounce: 0.2, duration: 0.5 }}
                />
              )}
              <Icon className={`w-4 h-4 flex-shrink-0 ${isActive ? tab.color : 'opacity-60 group-hover:opacity-100'}`} />
              {(!sidebarCollapsed || isMobile) && (
                <span className="text-xs font-semibold tracking-wide">{tab.label}</span>
              )}
            </button>
          );
        })}
      </nav>

      {/* Bottom Actions */}
      <div className="pt-4 mt-4 border-t border-slate-800 flex-shrink-0 space-y-1">
        <Link
          href="/dashboard"
          className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-slate-500 hover:text-slate-300 hover:bg-slate-800/50 transition-all ${!isMobile && sidebarCollapsed ? 'justify-center' : ''}`}
        >
          <FiZap className="w-4 h-4 flex-shrink-0 text-amber-500" />
          {(!sidebarCollapsed || isMobile) && <span className="text-xs font-semibold">Painel Usuário</span>}
        </Link>
        <button
          onClick={handleLogout}
          className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-slate-500 hover:text-rose-400 hover:bg-rose-500/10 transition-all ${!isMobile && sidebarCollapsed ? 'justify-center' : ''}`}
        >
          <FiLogOut className="w-4 h-4 flex-shrink-0" />
          {(!sidebarCollapsed || isMobile) && <span className="text-xs font-semibold">Sair</span>}
        </button>
      </div>
    </div>
  );

  const currentTab = tabs.find(t => t.value === activeTab);

  return (
    <AuthGuard requireAdmin={true}>
      <div className="flex h-screen w-screen bg-slate-950 text-white overflow-hidden [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">

        {/* Desktop Sidebar */}
        <aside className={`hidden lg:flex flex-col h-full transition-all duration-300 border-r border-slate-800/80 bg-slate-900/80 backdrop-blur-xl z-50 flex-shrink-0 ${sidebarCollapsed ? 'w-[68px]' : 'w-64'}`}>
          <SidebarContent />
        </aside>

        {/* Mobile Drawer */}
        <AnimatePresence>
          {mobileMenuOpen && (
            <div className="fixed inset-0 z-[150] lg:hidden">
              <motion.div
                initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                className="absolute inset-0 bg-black/70 backdrop-blur-sm"
                onClick={() => setMobileMenuOpen(false)}
              />
              <motion.div
                initial={{ x: '-100%' }} animate={{ x: 0 }} exit={{ x: '-100%' }}
                transition={{ type: 'spring', damping: 28, stiffness: 220 }}
                className="absolute top-0 left-0 h-full w-72 bg-slate-900 border-r border-slate-800 shadow-2xl"
              >
                <SidebarContent isMobile />
              </motion.div>
            </div>
          )}
        </AnimatePresence>

        {/* Main Area */}
        <div className="flex flex-1 flex-col overflow-hidden min-w-0">

          {/* Top Header */}
          <header className="h-14 px-4 sm:px-6 flex items-center justify-between border-b border-slate-800/80 bg-slate-900/60 backdrop-blur-xl z-40 flex-shrink-0">
            <div className="flex items-center gap-3">
              <button
                onClick={() => setMobileMenuOpen(true)}
                className="lg:hidden p-2 rounded-lg bg-slate-800 border border-slate-700 text-slate-400 hover:text-white transition"
              >
                <FiMenu className="w-4 h-4" />
              </button>
              <div>
                <h2 className="text-sm font-bold text-slate-100 leading-none">
                  Centro de Operações
                </h2>
                <p className="text-[10px] text-slate-500 font-medium mt-0.5 uppercase tracking-widest">
                  {currentTab?.label}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <Link
                href="/"
                className="flex items-center gap-1.5 px-2.5 sm:px-3 py-1.5 rounded-lg bg-slate-800 border border-slate-700 hover:bg-slate-700 transition text-xs font-medium text-slate-300"
              >
                <span>←</span>
                <span className="hidden xs:inline sm:inline">Voltar ao site</span>
              </Link>
              <button
                onClick={() => window.location.reload()}
                className="p-2 rounded-lg text-slate-500 hover:text-slate-200 hover:bg-slate-800 transition"
                title="Recarregar"
              >
                <FiRefreshCw className="w-4 h-4" />
              </button>
            </div>
          </header>

          {/* Scrollable Content */}
          <main className="flex-1 overflow-y-auto overflow-x-hidden custom-scrollbar px-4 sm:px-6 py-6">
            <motion.div
              key={activeTab}
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ type: 'spring', damping: 22, stiffness: 120, duration: 0.3 }}
              className="max-w-[1400px] mx-auto w-full pb-10"
            >
              {error && (
                <div className="bg-amber-500/10 border border-amber-500/20 rounded-xl p-3 mb-5 flex items-center gap-3">
                  <FiZap className="w-4 h-4 text-amber-400 flex-shrink-0" />
                  <p className="text-amber-300/90 text-xs">{error}</p>
                </div>
              )}

              {activeTab === 'overview' && (
                <div className="space-y-5">
                  {/* Welcome Card */}
                  <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6">
                    <h3 className="text-base font-bold text-slate-100 mb-1">Bem-vindo, {user?.email}</h3>
                    <p className="text-sm text-slate-500 leading-relaxed">
                      Você está no painel administrativo de nível supremo. Utilize a barra lateral para gerenciar pedidos, tickets, newsletter e usuários.
                    </p>
                  </div>
                  {/* Stats Grid */}
                  <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
                    {[
                      { label: 'Pedidos', icon: ClipboardList, color: 'text-indigo-400', bg: 'bg-indigo-500/10 border-indigo-500/20', tab: 'orders' },
                      { label: 'Tickets',  icon: TicketCheck,   color: 'text-violet-400', bg: 'bg-violet-500/10 border-violet-500/20', tab: 'tickets' },
                      { label: 'Newsletter', icon: FiMail,      color: 'text-amber-400',  bg: 'bg-amber-500/10 border-amber-500/20',  tab: 'newsletter' },
                      { label: 'Usuários', icon: FiUsers,       color: 'text-emerald-400',bg: 'bg-emerald-500/10 border-emerald-500/20', tab: 'users' },
                    ].map((item) => {
                      const Icon = item.icon;
                      return (
                        <button
                          key={item.tab}
                          onClick={() => setActiveTab(item.tab)}
                          className="bg-slate-900 border border-slate-800 rounded-xl p-4 text-left hover:border-slate-700 transition group"
                        >
                          <div className={`w-9 h-9 rounded-lg ${item.bg} border flex items-center justify-center mb-3`}>
                            <Icon className={`w-4 h-4 ${item.color}`} />
                          </div>
                          <p className="text-xs font-semibold text-slate-300 group-hover:text-white transition">{item.label}</p>
                          <p className="text-[10px] text-slate-600 mt-0.5">Clique para acessar →</p>
                        </button>
                      );
                    })}
                  </div>

                </div>
              )}

              {activeTab === 'orders'     && <AdminOrdersTab />}
              {activeTab === 'tickets'    && <AdminTicketsTab />}
              {activeTab === 'newsletter' && <AdminNewsletterTab />}
              {activeTab === 'users'      && <AdminUsersTab />}
            </motion.div>
          </main>
        </div>
      </div>
    </AuthGuard>
  );
}
