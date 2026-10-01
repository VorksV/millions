'use client';

import { useState, useEffect, useMemo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import { createClient } from '@/utils/supabase/client';
import { toast } from 'react-hot-toast';
import { useDashboard } from '@/app/context/DashboardContext';

export default function GamerClient() {
    const { transparencyMode } = useDashboard();
    const [loading, setLoading] = useState(false);
    const [gamerModeActive, setGamerModeActive] = useState(false);
    const [machineId, setMachineId] = useState<string | null>(null);
    const [lastHeartbeat, setLastHeartbeat] = useState<string | null>(null);
    const supabase = useMemo(() => createClient(), []);

    useEffect(() => {
        loadStatus();
    }, []);

    const loadStatus = async () => {
        try {
            const { data: { user } } = await supabase.auth.getUser();
            if (!user) return;
            const { data: installation } = await supabase
                .from('installations')
                .select('machine_id, gamer_mode_active, last_heartbeat')
                .eq('user_id', user.id)
                .single();

            if (installation) {
                setMachineId(installation.machine_id);
                setGamerModeActive(installation.gamer_mode_active || false);
                setLastHeartbeat(installation.last_heartbeat);
            }
        } catch (error) {
            console.error('Error:', error);
        }
    };

    const toggleGamerMode = async () => {
        if (!machineId) {
            toast.error('Nenhum nó detectado');
            return;
        }

        setLoading(true);
        const newStatus = !gamerModeActive;
        try {
            const { error } = await supabase
                .from('installations')
                .update({ gamer_mode_active: newStatus })
                .eq('machine_id', machineId);

            if (error) throw error;
            setGamerModeActive(newStatus);
            toast.success(newStatus ? 'Protocolo Gamer: ATIVO' : 'Protocolo Gamer: DESATIVADO', {
              icon: newStatus ? '🔥' : '❄️',
              style: { background: 'rgba(10, 10, 15, 0.9)', color: '#fff', border: '1px solid rgba(49, 168, 255, 0.2)' }
            });
        } catch (error) {
            toast.error('Falha de Protocolo');
        } finally {
            setLoading(false);
        }
    };

    return (
        <div className="flex flex-col gap-10">
            
            {/* Header */}
            <div className="flex flex-col gap-2">
                <div className="flex items-center gap-3">
                    <div className="w-2 h-8 bg-gradient-to-b from-[#FF4B6B] to-[#FF9B31] rounded-full"></div>
                    <h2 className="text-4xl font-black text-gray-900 italic uppercase tracking-tighter">Protocolo <span className="text-[#FF4B6B] not-italic">Gamer</span></h2>
                </div>
                <p className="text-gray-500 font-bold text-xs uppercase tracking-[0.2em] pl-5 font-mono">Overclock neural e supressão de latência</p>
            </div>

            {/* Neural Switch Card */}
            <motion.div
              initial={{ opacity: 0, scale: 0.98 }}
              animate={{ opacity: 1, scale: 1 }}
              className={`group relative p-12 rounded-[4rem] border transition-all duration-700 overflow-hidden ${transparencyMode ? 'voltris-glass' : 'bg-white border-gray-200 shadow-xl'}`}
            >
               {/* Background Effects */}
               <div className={`absolute -right-40 -top-40 w-[600px] h-[600px] ${gamerModeActive ? 'bg-[#FF4B6B]/10' : 'bg-[#31A8FF]/5'} blur-[150px] rounded-full transition-all duration-1000`}></div>
               
               <div className="relative z-10 flex flex-col xl:flex-row items-center justify-between gap-12">
                  <div className="flex items-center gap-10">
                     <div className="relative">
                        <div className={`w-40 h-40 rounded-[3rem] p-[2px] transition-all duration-700 shadow-2xl ${gamerModeActive ? 'bg-gradient-to-br from-[#FF4B6B] to-[#FF9B31] rotate-3' : 'bg-white/5 grayscale'}`}>
                           <div className="w-full h-full rounded-[2.85rem] bg-gray-100 flex items-center justify-center">
                              {gamerModeActive ? <VoltrisIcon name="bolt" size={64} className="text-[#FF4B6B] animate-pulse" /> : <VoltrisIcon name="boltOff" size={64} className="text-gray-300" />}
                           </div>
                        </div>
                        {gamerModeActive && (
                           <div className="absolute inset-0 rounded-[3rem] blur-3xl opacity-40 bg-[#FF4B6B] animate-pulse"></div>
                        )}
                     </div>

                     <div className="space-y-3">
                        <div className="flex items-center gap-4">
                           <span className={`text-4xl font-black italic uppercase tracking-tighter ${gamerModeActive ? 'text-gray-900' : 'text-gray-400'}`}>
                              {gamerModeActive ? 'Ultra Ativo' : 'Protocolo Inativo'}
                           </span>
                           <div className={`px-4 py-1 rounded-full border ${gamerModeActive ? 'bg-emerald-100 border-emerald-200 text-emerald-700' : 'bg-gray-100 border-gray-200 text-gray-400'}`}>
                              <span className="text-[10px] font-black uppercase tracking-widest">{gamerModeActive ? 'Performance Máxima' : 'Ocioso'}</span>
                           </div>
                        </div>
                        <p className="text-gray-500 font-bold text-xs uppercase tracking-widest leading-relaxed max-w-md">
                           {gamerModeActive 
                             ? 'O sistema está priorizando o pool de threads e os shaders da GPU. Entropia de fundo minimizada.' 
                             : 'Ative para redirecionar os recursos do sistema para o núcleo de processamento visual primário.'}
                        </p>
                     </div>
                  </div>

                  <button 
                    onClick={toggleGamerMode} 
                    disabled={loading || !machineId}
                    className={`relative group px-12 py-6 rounded-3xl font-black uppercase italic text-xs tracking-[0.3em] shadow-2xl transition-all hover:scale-[1.05] active:scale-95 disabled:opacity-30
                      ${gamerModeActive 
                        ? 'bg-gradient-to-r from-red-600 to-red-400 text-white' 
                        : 'bg-white text-black'
                      }
                    `}
                  >
                    {loading ? 'Transmitindo...' : gamerModeActive ? 'DESATIVAR PROTOCOLO' : 'INICIALIZAR OVERCLOCK'}
                  </button>
               </div>
            </motion.div>

            {/* Sub-system Telemetry */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
               {([
                 { label: 'Prioridade de CPU', icon: 'system', status: gamerModeActive ? 'Thread Alta' : 'Equilibrado', color: 'text-[#31A8FF]' },
                 { label: 'Mapa de Latência', icon: 'trendingUp', status: gamerModeActive ? 'Sub-Atômica' : 'Padrão', color: 'text-[#FF4B6B]' },
                 { label: 'Controle de Entropia', icon: 'activity', status: gamerModeActive ? 'Nível Zero' : 'Nominal', color: 'text-[#00FF88]' },
               ] as const).map((mod, i) => (
                 <motion.div
                   key={i}
                   initial={{ opacity: 0, y: 20 }}
                   animate={{ opacity: 1, y: 0 }}
                   transition={{ delay: i * 0.1 }}
                   className={`p-10 rounded-[3.5rem] border transition-all duration-500 overflow-hidden relative group ${transparencyMode ? 'voltris-glass' : 'bg-white border-gray-200 shadow-xl'}`}
                 >
                    <div className="relative z-10 flex flex-col gap-6 items-center text-center">
                       <div className={`p-5 rounded-2xl bg-gray-100 border border-gray-200 group-hover:bg-gray-200 group-hover:scale-110 transition-all ${mod.color}`}>
                          <VoltrisIcon name={mod.icon} size={32} />
                       </div>
                       <div className="space-y-1">
                          <h4 className="text-[10px] font-black text-gray-400 uppercase tracking-[0.3em] font-mono">{mod.label}</h4>
                          <span className="text-2xl font-black text-gray-900 uppercase italic tracking-tighter">{mod.status}</span>
                       </div>
                    </div>
                    {/* Interior Progress Bar Decor */}
                    <div className={`absolute bottom-0 left-0 h-1 bg-current opacity-20 transition-all duration-1000 ${mod.color}`} style={{ width: gamerModeActive ? '100%' : '30%' }}></div>
                 </motion.div>
               ))}
            </div>

            {/* Tactical Briefing */}
            {!machineId && (
               <div className="p-8 rounded-3xl bg-amber-500/5 border border-amber-500/10 flex items-center gap-6">
                  <div className="p-4 rounded-2xl bg-amber-500/10 text-amber-500 shadow-lg shadow-amber-500/10">
                    <VoltrisIcon name="alertCircle" size={32} className="text-amber-500" />
                  </div>
                  <div className="space-y-1">
                    <p className="text-sm font-black text-gray-900 uppercase italic tracking-tighter">Nenhum Nó Vinculado Detectado</p>
                    <p className="text-gray-500 text-[10px] font-bold uppercase tracking-widest">Conecte seu PC à Rede Neural Voltris para habilitar a troca remota de protocolos.</p>
                  </div>
               </div>
            )}

        </div>
    );
}
