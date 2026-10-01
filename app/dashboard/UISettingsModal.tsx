'use client';

import { motion, AnimatePresence } from 'framer-motion';
import { FiX, FiCheck, FiMaximize2, FiMinimize2, FiSettings, FiLayout, FiShield } from 'react-icons/fi';
import { useDashboard } from '@/app/context/DashboardContext';

interface UISettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function UISettingsModal({ isOpen, onClose }: UISettingsModalProps) {
  const {
    transparencyMode,
    toggleTransparency,
    sidebarCollapsed,
    setSidebarCollapsed,
    hardwareIDProtection,
    toggleHardwareIDProtection
  } = useDashboard();

  const options = [
    {
      id: 'transparent',
      label: 'Modo Transparente',
      description: 'Fundo translúcido (Glassmorphism)',
      icon: FiMaximize2,
      active: transparencyMode,
      onClick: toggleTransparency,
      activeColor: 'border-indigo-500/40 bg-indigo-500/10',
      iconColor: 'text-indigo-400',
      checkColor: 'text-indigo-400',
    },
    {
      id: 'solid',
      label: 'Modo Sólido',
      description: 'Fundo opaco de alto contraste',
      icon: FiMinimize2,
      active: !transparencyMode,
      onClick: toggleTransparency,
      activeColor: 'border-violet-500/40 bg-violet-500/10',
      iconColor: 'text-violet-400',
      checkColor: 'text-violet-400',
    },
    {
      id: 'compact',
      label: 'Sidebar Compacto',
      description: 'Reduz o menu lateral apenas a ícones',
      icon: FiLayout,
      active: sidebarCollapsed,
      onClick: () => setSidebarCollapsed(!sidebarCollapsed),
      activeColor: 'border-amber-500/40 bg-amber-500/10',
      iconColor: 'text-amber-400',
      checkColor: 'text-amber-400',
    },
    {
      id: 'hid',
      label: 'Proteção HID',
      description: 'Segurança de Hardware ID em tempo real',
      icon: FiShield,
      active: hardwareIDProtection,
      onClick: toggleHardwareIDProtection,
      activeColor: 'border-emerald-500/40 bg-emerald-500/10',
      iconColor: 'text-emerald-400',
      checkColor: 'text-emerald-400',
    },
  ];

  return (
    <AnimatePresence>
      {isOpen && (
        <div className="fixed inset-0 z-[200] flex items-center justify-center p-4 sm:p-6">
          {/* Backdrop */}
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="absolute inset-0 bg-black/70 backdrop-blur-md"
            onClick={onClose}
          />

          {/* Modal */}
          <motion.div
            initial={{ opacity: 0, scale: 0.95, y: 16 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.95, y: 16 }}
            transition={{ type: 'spring', damping: 24, stiffness: 200 }}
            className="relative w-full max-w-lg bg-slate-900 border border-slate-800 rounded-2xl shadow-2xl overflow-hidden"
          >
            {/* Subtle glow accent */}
            <div className="absolute top-0 right-0 w-48 h-48 bg-indigo-500/5 rounded-full blur-3xl pointer-events-none" />

            <div className="relative z-10 p-5 sm:p-6 space-y-5 sm:space-y-6">
              {/* Header */}
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <div className="w-9 h-9 rounded-xl bg-indigo-500/15 border border-indigo-500/30 flex items-center justify-center">
                    <FiSettings className="w-4 h-4 text-indigo-400" />
                  </div>
                  <div>
                    <h2 className="text-sm font-bold text-slate-100 tracking-wide">Configurações UI</h2>
                    <p className="text-[10px] text-slate-500 uppercase tracking-widest font-medium">Personalização do painel</p>
                  </div>
                </div>
                <button
                  onClick={onClose}
                  className="p-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-500 hover:text-slate-200 transition border border-slate-700"
                >
                  <FiX className="w-4 h-4" />
                </button>
              </div>

              {/* Options Grid */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {options.map((opt) => {
                  const Icon = opt.icon;
                  return (
                    <button
                      key={opt.id}
                      onClick={opt.onClick}
                      className={`group relative p-4 rounded-xl border transition-all duration-200 text-left flex flex-col gap-3
                        ${opt.active
                          ? `${opt.activeColor}`
                          : 'border-slate-800 bg-slate-800/40 hover:bg-slate-800 hover:border-slate-700'
                        }
                      `}
                    >
                      <div className="flex items-center justify-between">
                        <Icon className={`w-4 h-4 ${opt.active ? opt.iconColor : 'text-slate-600 group-hover:text-slate-400'} transition`} />
                        {opt.active && (
                          <motion.div
                            initial={{ scale: 0 }}
                            animate={{ scale: 1 }}
                            className={`w-4 h-4 rounded-full flex items-center justify-center ${opt.checkColor}`}
                          >
                            <FiCheck className="w-3.5 h-3.5" />
                          </motion.div>
                        )}
                      </div>
                      <div>
                        <p className={`text-xs font-semibold mb-0.5 ${opt.active ? 'text-slate-100' : 'text-slate-400 group-hover:text-slate-300'} transition`}>
                          {opt.label}
                        </p>
                        <p className={`text-[10px] leading-relaxed ${opt.active ? 'text-slate-400' : 'text-slate-600'} transition`}>
                          {opt.description}
                        </p>
                      </div>
                    </button>
                  );
                })}
              </div>

              {/* Footer */}
              <div className="pt-4 border-t border-slate-800">
                <p className="text-center text-[10px] text-slate-600 uppercase tracking-widest font-medium">
                  As alterações são salvas automaticamente
                </p>
              </div>
            </div>
          </motion.div>
        </div>
      )}
    </AnimatePresence>
  );
}
