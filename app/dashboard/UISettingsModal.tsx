'use client';

import { motion, AnimatePresence } from 'framer-motion';
import { useDashboard } from '@/app/context/DashboardContext';
import VoltrisIcon, { type VoltrisIconName } from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';

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

  const options: Array<{
    id: string;
    label: string;
    description: string;
    icon: VoltrisIconName;
    active: boolean;
    onClick: () => void;
    activeColor: string;
    iconColor: string;
    checkColor: string;
  }> = [
    {
      id: 'transparent',
      label: 'Modo Transparente',
      description: 'Fundo translúcido (Glassmorphism)',
      icon: 'eye',
      active: transparencyMode,
      onClick: toggleTransparency,
      activeColor: 'border-[#8B31FF]/40 bg-[#8B31FF]/10',
      iconColor: 'text-[#8B31FF]',
      checkColor: 'text-[#8B31FF]',
    },
    {
      id: 'solid',
      label: 'Modo Sólido',
      description: 'Fundo opaco de alto contraste',
      icon: 'display',
      active: !transparencyMode,
      onClick: toggleTransparency,
      activeColor: 'border-[#8B31FF]/40 bg-[#8B31FF]/10',
      iconColor: 'text-[#8B31FF]',
      checkColor: 'text-[#8B31FF]',
    },
    {
      id: 'compact',
      label: 'Sidebar Compacto',
      description: 'Reduz o menu lateral apenas a ícones',
      icon: 'layout',
      active: sidebarCollapsed,
      onClick: () => setSidebarCollapsed(!sidebarCollapsed),
      activeColor: 'border-[#F59E0B]/40 bg-[#F59E0B]/10',
      iconColor: 'text-[#F59E0B]',
      checkColor: 'text-[#F59E0B]',
    },
    {
      id: 'hid',
      label: 'Proteção HID',
      description: 'Segurança de Hardware ID em tempo real',
      icon: 'security',
      active: hardwareIDProtection,
      onClick: toggleHardwareIDProtection,
      activeColor: 'border-[#00FF94]/40 bg-[#00FF94]/10',
      iconColor: 'text-[#00FF94]',
      checkColor: 'text-[#00FF94]',
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
                  <VoltrisIconTile icon="settings" accent={DASHBOARD_ACCENT.brand} size={9} />
                  <div>
                    <h2 className="text-sm font-bold text-slate-100 tracking-wide">Configurações UI</h2>
                    <p className="text-[10px] text-slate-500 uppercase tracking-widest font-medium">Personalização do painel</p>
                  </div>
                </div>
                <button
                  onClick={onClose}
                  className="p-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-500 hover:text-slate-200 transition border border-slate-700"
                >
                  <VoltrisIcon name="close" size={16} />
                </button>
              </div>

              {/* Options Grid */}
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {options.map((opt) => {
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
                        <VoltrisIcon name={opt.icon} size={16} className={`transition ${opt.active ? opt.iconColor : 'text-slate-600 group-hover:text-slate-400'}`} />
                        {opt.active && (
                          <motion.div
                            initial={{ scale: 0 }}
                            animate={{ scale: 1 }}
                            className={`w-4 h-4 rounded-full flex items-center justify-center ${opt.checkColor}`}
                          >
                            <VoltrisIcon name="check" size={14} className={`rounded-full ${opt.checkColor}`} />
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
