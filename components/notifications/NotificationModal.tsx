'use client';

import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useNotificationContext } from './NotificationContext';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import { useDashboard } from '@/app/context/DashboardContext';

export default function NotificationModal() {
  const { transparencyMode } = useDashboard();
  const { showPermissionModal, setShowPermissionModal, updateSettings } = useNotificationContext();

  if (!showPermissionModal) return null;

  const handleEnable = async () => {
    await updateSettings({ notifications_enabled: true });
    setShowPermissionModal(false);
  };

  const handleDismiss = () => {
    setShowPermissionModal(false);
  };

  return (
    <AnimatePresence>
      <div className="fixed inset-0 z-[10000] flex items-center justify-center p-6">
        <motion.div
          className="absolute inset-0 bg-black/90 backdrop-blur-md"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          onClick={handleDismiss}
        />
        
        <motion.div
          className={`relative w-full max-w-md p-6 sm:p-8 rounded-2xl border border-slate-800 shadow-2xl overflow-hidden
            ${transparencyMode ? 'voltris-glass' : 'bg-slate-900'}
          `}
          initial={{ y: 20, opacity: 0, scale: 0.95 }}
          animate={{ y: 0, opacity: 1, scale: 1 }}
          exit={{ y: 20, opacity: 0, scale: 0.95 }}
          transition={{ type: 'spring', stiffness: 200, damping: 24 }}
          onClick={(e) => e.stopPropagation()}
        >
          {/* Visual Accents */}
          <div className="absolute top-0 left-0 w-full h-[2px] bg-gradient-to-r from-[#31A8FF] via-[#8B31FF] to-[#FF4B6B]"></div>
          <div className="absolute -right-20 -top-20 w-48 h-48 bg-[#31A8FF]/10 blur-[80px] rounded-full pointer-events-none"></div>
          
          <div className="flex flex-col items-center text-center gap-6 relative z-10">
            <div className="relative mt-2">
              <div className="w-16 h-16 rounded-2xl bg-gradient-to-br from-[#31A8FF]/15 to-[#8B31FF]/15 border border-[#31A8FF]/30 flex items-center justify-center text-[#31A8FF] shadow-xl relative z-10">
                <VoltrisIcon name="bell" size={32} className="text-[#31A8FF]" />
              </div>
              <div className="absolute inset-0 rounded-2xl bg-[#31A8FF] blur-2xl opacity-20 animate-pulse"></div>
            </div>

            <div className="space-y-2">
              <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight leading-tight">
                Ativar <span className="text-[#31A8FF]">Notificações</span>
              </h2>
              <div className="flex flex-col gap-3">
                 <p className="text-slate-400 text-xs sm:text-sm leading-relaxed px-2">
                   Receba atualizações em tempo real sobre seus pedidos, computadores conectados e alertas críticos do sistema.
                 </p>
                 <div className="flex items-center justify-center gap-3 pt-1">
                    <div className="flex items-center gap-1.5 px-2.5 py-1 bg-slate-800/80 rounded-lg border border-slate-700/60">
                       <VoltrisIcon name="security" size={12} className="text-[#00FF94]" />
                       <span className="text-[10px] font-semibold text-slate-300 uppercase tracking-wider">Criptografado</span>
                    </div>
                    <div className="flex items-center gap-1.5 px-2.5 py-1 bg-slate-800/80 rounded-lg border border-slate-700/60">
                       <VoltrisIcon name="system" size={12} className="text-[#31A8FF]" />
                       <span className="text-[10px] font-semibold text-slate-300 uppercase tracking-wider">Baixo Consumo</span>
                    </div>
                 </div>
              </div>
            </div>

            <div className="flex flex-col gap-2.5 w-full pt-2">
              <button
                className="w-full py-3 px-4 bg-gradient-to-r from-[#31A8FF] to-[#8B31FF] hover:opacity-95 text-white font-semibold rounded-xl shadow-lg shadow-indigo-500/20 active:scale-95 transition-all flex items-center justify-center gap-2 text-xs"
                onClick={handleEnable}
              >
                <VoltrisIcon name="check" size={16} />
                <span>Ativar Notificações</span>
              </button>
              <button
                className="w-full py-2.5 text-slate-400 hover:text-slate-200 font-medium text-xs transition-colors"
                onClick={handleDismiss}
              >
                Agora não
              </button>
            </div>
          </div>
        </motion.div>
      </div>
    </AnimatePresence>
  );
} 
