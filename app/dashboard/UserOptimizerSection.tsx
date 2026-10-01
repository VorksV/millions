'use client';

import { useState, useEffect } from 'react';
import { createClient } from '@/utils/supabase/client';
import { toast } from 'react-hot-toast';
import { motion, AnimatePresence } from 'framer-motion';
import { FiMonitor, FiCpu, FiZap, FiActivity, FiClock, FiShield, FiX } from 'react-icons/fi';

export default function UserOptimizerSection({ userId }: { userId: string }) {
    const [installations, setInstallations] = useState<any[]>([]);
    const [loading, setLoading] = useState(true);
    const [unlinkModalOpen, setUnlinkModalOpen] = useState(false);
    const [selectedInstallation, setSelectedInstallation] = useState<any>(null);
    const supabase = createClient();

    useEffect(() => {
        console.log('[UserOptimizerSection] userId:', userId);
        if (userId) {
            fetchData();

            const channel = supabase
                .channel(`user-installs-${userId}`)
                .on('postgres_changes' as any, {
                    event: '*',
                    table: 'installations',
                    filter: `user_id=eq.${userId}`
                }, () => {
                    console.log('[UserOptimizerSection] Realtime update received');
                    fetchData();
                })
                .subscribe();

            return () => {
                supabase.removeChannel(channel);
            };
        }
    }, [userId]);

    const fetchData = async () => {
        try {
            console.log('[UserOptimizerSection] Buscando instalações para userId:', userId);
            const { data, error } = await supabase
                .from('installations')
                .select('*')
                .eq('user_id', userId)
                .order('last_heartbeat', { ascending: false });

            console.log('[UserOptimizerSection] Resultado da query:', { data, error });
            if (error) throw error;
            console.log('[UserOptimizerSection] Instalações encontradas:', data?.length || 0);
            setInstallations(data || []);
        } catch (err) {
            console.error('[UserOptimizerSection] Erro ao buscar instalações:', err);
        } finally {
            setLoading(false);
        }
    };

    const handleUnlinkClick = (installation: any) => {
        setSelectedInstallation(installation);
        setUnlinkModalOpen(true);
    };

  const handleConfirmUnlink = async () => {
    if (!selectedInstallation) return;
    setUnlinkModalOpen(false);
    const loadingId = toast.loading('Processando...');
    try {
      const unlinkRes = await fetch('/api/v1/install/unlink', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ installation_id: selectedInstallation.id })
      });
      const unlinkData = await unlinkRes.json().catch(() => ({}));

      if (!unlinkRes.ok) {
        const reason = unlinkData?.error || `HTTP ${unlinkRes.status}`;
        const corr = unlinkData?.correlation_id ? ` (id: ${unlinkData.correlation_id})` : '';
        console.error('[UNLINK] Falha ao desvincular:', unlinkRes.status, unlinkData);
        throw new Error(`${reason}${corr}`);
      }

      if (unlinkData?.verified !== true && unlinkData?.already_unlinked !== true) {
        throw new Error('O servidor não confirmou a desvinculação.');
      }

      toast.success('Dispositivo removido.', { id: loadingId, icon: '🗑️' });
      fetchData();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'erro desconhecido';
      console.error('[UNLINK] Erro:', err);
      toast.error(`Falha ao desvincular: ${message}`, { id: loadingId, duration: 8000 });
    }
    setSelectedInstallation(null);
  };


    if (loading) return null;

    if (installations.length === 0) {
        return (
            <div className="space-y-4 pt-6 mt-6 border-t border-slate-800">
                <div className="flex items-center justify-between px-1">
                    <h2 className="text-sm font-semibold text-white flex items-center gap-2">
                        <FiMonitor className="text-indigo-400 w-4 h-4" /> Meu Computador
                    </h2>
                </div>

                <motion.div
                    initial={{ opacity: 0, y: 15 }}
                    animate={{ opacity: 1, y: 0 }}
                    className="p-8 bg-slate-900/60 border border-slate-800 rounded-2xl flex flex-col items-center text-center mx-auto shadow-xl"
                >
                    <div className="p-3.5 bg-indigo-500/10 rounded-xl border border-indigo-500/20 text-indigo-400 mb-5">
                        <FiZap className="w-7 h-7" />
                    </div>

                    <h3 className="text-base font-bold text-white mb-1.5">Vincule seu computador</h3>
                    <p className="text-xs text-slate-400 mb-6 max-w-xs leading-relaxed">
                        Acesse informações em tempo real da sua máquina, status de otimização e gerencie sua licença diretamente do site.
                    </p>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 w-full mb-6">
                        <div className="bg-slate-800/60 border border-slate-700 p-3.5 rounded-xl flex items-center gap-3 text-left">
                            <div className="w-8 h-8 bg-slate-700 rounded-lg flex items-center justify-center text-white shrink-0 font-bold text-xs">1</div>
                            <p className="text-xs text-slate-400">Abra o <span className="text-white font-semibold">Voltris Optimizer</span> no seu PC</p>
                        </div>
                        <div className="bg-slate-800/60 border border-slate-700 p-3.5 rounded-xl flex items-center gap-3 text-left">
                            <div className="w-8 h-8 bg-slate-700 rounded-lg flex items-center justify-center text-white shrink-0 font-bold text-xs">2</div>
                            <p className="text-xs text-slate-400">Clique em <span className="text-white font-semibold">Vincular Conta</span> no topo do app</p>
                        </div>
                    </div>

                    <div className="pt-5 border-t border-slate-800 w-full flex flex-col items-center gap-3">
                        <p className="text-[10px] text-slate-500 uppercase tracking-wider font-medium">Não tem o programa?</p>
                        <a
                            href="https://www.voltris.com.br/voltrisoptimizer"
                            target="_blank"
                            className="inline-flex items-center gap-2 px-5 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all active:scale-95"
                        >
                            Baixar Voltris Optimizer
                        </a>
                    </div>
                </motion.div>
            </div>
        );
    }

    return (
        <>
            <div className="space-y-4 pt-6 mt-6 border-t border-slate-800">
                <div className="flex items-center justify-between px-1">
                    <h2 className="text-sm font-semibold text-white flex items-center gap-2">
                        <FiMonitor className="text-indigo-400 w-4 h-4" /> Meus Computadores
                    </h2>
                    <span className="text-[10px] text-slate-500 font-medium">Sincronizado via Telemetria</span>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {installations.map((inst) => {
                        const isOnline = new Date().getTime() - new Date(inst.last_heartbeat).getTime() < 900000;
                        return (
                            <motion.div
                                key={inst.id}
                                initial={{ opacity: 0, scale: 0.97 }}
                                animate={{ opacity: 1, scale: 1 }}
                                className="bg-slate-900/60 border border-slate-800 p-4 rounded-xl hover:border-slate-700 transition-all overflow-hidden relative"
                            >
                                <div className="flex justify-between items-start">
                                    <div className="space-y-2.5 flex-1 min-w-0">
                                        <div className="flex items-center gap-2">
                                            <div className={`w-2 h-2 rounded-full shrink-0 ${isOnline ? 'bg-emerald-400 animate-pulse' : 'bg-slate-600'}`}></div>
                                            <span className="text-white font-semibold text-sm truncate">{inst.os_name}</span>
                                        </div>

                                        <div className="flex flex-col gap-1">
                                            <div className="flex items-center gap-1.5 text-xs text-slate-400">
                                                <FiCpu className="text-indigo-400 shrink-0 w-3.5 h-3.5" />
                                                <span className="truncate max-w-[160px]">{inst.cpu_name}</span>
                                            </div>
                                            <div className="flex items-center gap-1.5 text-xs text-slate-400">
                                                <FiShield className="text-indigo-400 shrink-0 w-3.5 h-3.5" />
                                                <span>v{inst.app_version} • {inst.ram_gb_total}GB RAM</span>
                                            </div>
                                        </div>
                                    </div>

                                    <div className="flex flex-col items-end gap-1.5 text-right shrink-0 ml-3">
                                        <div className={`px-2 py-0.5 rounded-lg text-[10px] font-semibold border ${
                                            inst.is_optimized
                                                ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
                                                : 'bg-slate-800 text-slate-400 border-slate-700'
                                        }`}>
                                            {inst.is_optimized ? 'Otimizado' : 'Padrão'}
                                        </div>

                                        <div className={`text-[9px] font-semibold px-1.5 py-0.5 rounded border ${
                                            inst.license_status === 'active'
                                                ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/20'
                                                : 'text-indigo-400 bg-indigo-500/10 border-indigo-500/20'
                                        }`}>
                                            {inst.license_status?.toUpperCase() || 'TRIAL'}
                                        </div>

                                        <div className="text-[10px] text-slate-500 font-mono flex items-center gap-1">
                                            {new Date(inst.last_heartbeat).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} <FiClock className="w-3 h-3" />
                                        </div>
                                    </div>
                                </div>

                                {/* Action Row */}
                                <div className="mt-3.5 pt-3.5 border-t border-slate-800/80 flex gap-2 items-center">
                                    <div className="flex-1">
                                        <div className="text-[10px] text-slate-500 font-medium mb-1.5">Performance</div>
                                        <div className="h-1 bg-slate-800 rounded-full overflow-hidden">
                                            <motion.div
                                                initial={{ width: 0 }}
                                                animate={{ width: inst.is_optimized ? '100%' : '60%' }}
                                                className={`h-full rounded-full ${inst.is_optimized ? 'bg-emerald-500' : 'bg-indigo-500'}`}
                                            />
                                        </div>
                                    </div>
                                    <div className="flex gap-2 items-center">
                                        <button
                                            onClick={async () => {
                                                const toastId = toast.loading('Enviando comando...');
                                                try {
                                                    await fetch('/api/v1/commands/create', {
                                                        method: 'POST',
                                                        headers: { 'Content-Type': 'application/json' },
                                                        body: JSON.stringify({ installation_id: inst.id, command_type: 'OPTIMIZE_RAM' })
                                                    });
                                                    toast.success('Comando enviado!', { id: toastId });
                                                } catch { toast.error('Falha no envio', { id: toastId }); }
                                            }}
                                            className="px-2.5 py-1 bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 hover:bg-indigo-500/20 text-[10px] font-semibold rounded-lg transition-colors"
                                        >
                                            ⚡ RAM
                                        </button>
                                        <button
                                            onClick={async () => {
                                                const toastId = toast.loading('Solicitando limpeza...');
                                                try {
                                                    await fetch('/api/v1/commands/create', {
                                                        method: 'POST',
                                                        headers: { 'Content-Type': 'application/json' },
                                                        body: JSON.stringify({ installation_id: inst.id, command_type: 'CLEAN_TEMP' })
                                                    });
                                                    toast.success('Limpeza agendada!', { id: toastId });
                                                } catch { toast.error('Falha no envio', { id: toastId }); }
                                            }}
                                            className="px-2.5 py-1 bg-slate-800 border border-slate-700 text-slate-300 hover:bg-slate-700 text-[10px] font-semibold rounded-lg transition-colors"
                                        >
                                            🧹 Cache
                                        </button>

                                        <div className="h-4 w-px bg-slate-700 mx-0.5"></div>

                                        <button
                                            onClick={() => handleUnlinkClick(inst)}
                                            className="w-7 h-7 flex items-center justify-center bg-rose-500/10 text-rose-400 border border-rose-500/20 rounded-lg hover:bg-rose-500/20 transition-colors shrink-0"
                                            title="Desvincular Computador"
                                        >
                                            <FiX className="w-3.5 h-3.5" />
                                        </button>
                                    </div>
                                </div>
                            </motion.div>
                        );
                    })}
                </div>
            </div>

            {/* Confirmation Modal */}
            <AnimatePresence>
                {unlinkModalOpen && (
                    <motion.div
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/80 backdrop-blur-sm"
                        onClick={() => setUnlinkModalOpen(false)}
                    >
                        <motion.div
                            initial={{ scale: 0.95, opacity: 0 }}
                            animate={{ scale: 1, opacity: 1 }}
                            exit={{ scale: 0.95, opacity: 0 }}
                            onClick={(e) => e.stopPropagation()}
                            className="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-sm w-full shadow-2xl"
                        >
                            <div className="flex items-start gap-4 mb-5">
                                <div className="w-10 h-10 rounded-xl bg-rose-500/10 border border-rose-500/20 flex items-center justify-center text-rose-400 shrink-0">
                                    <FiX className="w-5 h-5" />
                                </div>
                                <div className="flex-1">
                                    <h2 className="text-sm font-bold text-white mb-1">Desvincular Computador?</h2>
                                    <p className="text-xs text-slate-400 leading-relaxed">
                                        Você perderá o acesso remoto e a telemetria deste dispositivo.
                                    </p>
                                </div>
                                <button
                                    onClick={() => setUnlinkModalOpen(false)}
                                    className="text-slate-500 hover:text-white transition-colors shrink-0"
                                >
                                    <FiX className="w-4 h-4" />
                                </button>
                            </div>
                            <div className="flex gap-2.5 justify-end">
                                <button
                                    onClick={() => setUnlinkModalOpen(false)}
                                    className="px-4 py-2 text-xs font-medium text-slate-400 hover:text-white transition-colors"
                                >
                                    Cancelar
                                </button>
                                <button
                                    onClick={handleConfirmUnlink}
                                    className="px-5 py-2 text-xs bg-rose-600 hover:bg-rose-500 text-white rounded-xl font-semibold transition-all shadow-lg shadow-rose-600/20 active:scale-95"
                                >
                                    Confirmar
                                </button>
                            </div>
                        </motion.div>
                    </motion.div>
                )}
            </AnimatePresence>
        </>
    );
}
