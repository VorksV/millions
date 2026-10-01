'use client';

import React, { useEffect, useState, useMemo } from 'react';
import { createClient } from '@/utils/supabase/client';
import { 
  FiMonitor, FiCpu, FiHardDrive, FiActivity, 
  FiSearch, FiRefreshCw, FiLock, FiTrash2, FiZap,
  FiTerminal, FiShield, FiTrendingUp, FiCheckCircle, FiX, FiArrowLeft
} from 'react-icons/fi';
import { motion, AnimatePresence } from 'framer-motion';
import toast from 'react-hot-toast';
import Link from 'next/link';
import { useDashboard } from '@/app/context/DashboardContext';

export default function DevicesClient() {
    const { transparencyMode } = useDashboard();
    const [loading, setLoading] = useState(true);
    const [devices, setDevices] = useState<any[]>([]);
    const [search, setSearch] = useState('');
    const supabase = useMemo(() => createClient(), []);

    const fetchDevices = async () => {
        setLoading(true);
        try {
            const { data: { user } } = await supabase.auth.getUser();
            if (user) {
                const { data: link } = await supabase.from('company_users').select('company_id').eq('user_id', user.id).single();
                if (link) {
                    const { data } = await supabase
                        .from('devices')
                        .select('*')
                        .eq('company_id', link.company_id)
                        .order('last_heartbeat', { ascending: false });
                    setDevices(data || []);
                }
            }
        } catch (error) {
            console.error('Error fetching devices:', error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchDevices();
    }, []);

    const sendCommand = async (deviceId: string, type: string) => {
        const { data: { user } } = await supabase.auth.getUser();
        if (!user) return;

        const { data: link } = await supabase.from('company_users').select('company_id').eq('user_id', user.id).single();
        if (!link) return;

        const { error } = await supabase.from('remote_commands').insert({
            device_id: deviceId,
            company_id: link.company_id,
            command_type: type,
            status: 'pending',
            create_user_id: user.id
        });

        if (error) {
            toast.error("Erro ao enviar comando para o nó");
        } else {
            toast.success(`Comando '${type}' enviado com sucesso!`, {
                icon: '⚡'
            });
        }
    };

    const handleLock = (device: any) => {
        if (confirm(`Bloquear licença de ${device.hostname || 'dispositivo'}?`)) {
            sendCommand(device.id, 'REMOTE_LOCK');
        }
    };

    const filteredDevices = devices.filter(d =>
        d.hostname?.toLowerCase().includes(search.toLowerCase()) ||
        d.machine_id?.toLowerCase().includes(search.toLowerCase())
    );

    return (
        <div className="flex flex-col gap-6 w-full max-w-full">
            {/* Header */}
            <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 pb-4 border-b border-slate-800/80">
                <div className="space-y-1">
                   <div className="flex items-center gap-3">
                     <Link href="/dashboard/companies" className="p-2 rounded-xl bg-slate-900 border border-slate-800 hover:bg-slate-800 text-slate-400 hover:text-white transition-colors" title="Voltar para Organização">
                        <FiArrowLeft className="w-4 h-4" />
                     </Link>
                     <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight">Nós da Frota</h2>
                   </div>
                   <p className="text-xs text-slate-400 pl-11">Telemetria de computadores e gerenciamento de comandos remotos corporativos</p>
                </div>

                <div className="flex items-center gap-2.5 w-full sm:w-auto">
                    <div className="relative flex-1 sm:w-64">
                        <FiSearch className="absolute left-3.5 top-1/2 -translate-y-1/2 text-slate-500 w-4 h-4" />
                        <input
                            type="text"
                            placeholder="Buscar por hostname ou ID..."
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            className="w-full pl-10 pr-4 py-2 rounded-xl bg-slate-900 border border-slate-800 text-white text-xs placeholder:text-slate-500 focus:outline-none focus:border-indigo-500 transition-colors shadow-sm"
                        />
                    </div>
                    <button 
                      onClick={fetchDevices} 
                      className={`p-2.5 rounded-xl bg-slate-900 border border-slate-800 text-slate-400 hover:text-white hover:bg-slate-800 transition-colors ${loading ? 'opacity-50' : ''}`}
                      title="Atualizar lista"
                    >
                        <FiRefreshCw className={`w-4 h-4 ${loading ? "animate-spin" : ""}`} />
                    </button>
                </div>
            </div>

            {/* Device Stream */}
            <div className="grid grid-cols-1 gap-4">
                <AnimatePresence mode="popLayout">
                    {loading ? (
                        [1, 2, 3].map(i => (
                          <div key={i} className="h-28 rounded-xl border border-slate-800 bg-slate-900/40 animate-pulse" />
                        ))
                    ) : filteredDevices.length === 0 ? (
                        <div className={`py-20 flex flex-col items-center justify-center text-center gap-3 rounded-2xl border border-slate-800 ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/40 shadow-xl'}`}>
                            <div className="w-12 h-12 rounded-xl bg-slate-800 border border-slate-700 flex items-center justify-center text-slate-400">
                              <FiMonitor className="w-6 h-6" />
                            </div>
                            <div className="space-y-1 max-w-sm">
                              <h3 className="text-base font-bold text-white tracking-tight">Nenhum nó localizado</h3>
                              <p className="text-xs text-slate-400">Nenhum dispositivo encontrado para os termos da busca ou não há computadores conectados nesta organização.</p>
                            </div>
                        </div>
                    ) : (
                        filteredDevices.map(device => (
                            <DeviceCard key={device.id} device={device} onCommand={sendCommand} onLock={handleLock} transparencyMode={transparencyMode} />
                        ))
                    )}
                </AnimatePresence>
            </div>
        </div>
    );
}

function DeviceCard({ device, onCommand, onLock, transparencyMode }: any) {
    const isOnline = device.last_heartbeat && (new Date().getTime() - new Date(device.last_heartbeat).getTime()) < 1000 * 60 * 15; 

    return (
        <div
            className={`p-5 rounded-2xl border transition-all duration-200 flex flex-col md:flex-row items-start md:items-center justify-between gap-5
              ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-sm'} 
              hover:border-slate-700
            `}
        >
            {/* Status & Name */}
            <div className="flex items-center gap-4 min-w-0">
                <div className={`w-12 h-12 rounded-xl flex items-center justify-center shrink-0 border text-lg
                  ${isOnline ? 'bg-indigo-500/10 text-indigo-400 border-indigo-500/20' : 'bg-slate-800/80 text-slate-500 border-slate-700'}`}>
                    <FiMonitor />
                </div>

                <div className="min-w-0 space-y-1">
                   <div className="flex items-center gap-2.5">
                     <h3 className="text-sm font-bold text-white tracking-tight truncate">{device.hostname || "Nó não identificado"}</h3>
                     <span className={`inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[10px] font-semibold border ${
                       isOnline 
                         ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' 
                         : 'bg-slate-800 text-slate-400 border-slate-700'
                     }`}>
                       <span className={`w-1.5 h-1.5 rounded-full ${isOnline ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'}`} />
                       {isOnline ? 'Online' : 'Offline'}
                     </span>
                   </div>
                   <div className="flex items-center gap-3 text-[11px] text-slate-400 font-mono">
                     <span>ID: <span className="text-slate-300">{device.machine_id?.slice(0, 16) || device.id.slice(0, 8)}</span></span>
                     <span>•</span>
                     <span>Sinal: {new Date(device.last_heartbeat || Date.now()).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                   </div>
                </div>
            </div>

            {/* Hardware specifications */}
            <div className="flex flex-wrap items-center gap-2">
                <div className="px-2.5 py-1 bg-slate-950/50 rounded-lg border border-slate-800 text-[11px] font-medium text-slate-300 flex items-center gap-1.5">
                   <FiCpu className="text-indigo-400 w-3.5 h-3.5 shrink-0" />
                   <span className="truncate max-w-[140px]">{device.cpu_model || 'CPU padrão'}</span>
                </div>
                <div className="px-2.5 py-1 bg-slate-950/50 rounded-lg border border-slate-800 text-[11px] font-medium text-slate-300 flex items-center gap-1.5">
                   <FiActivity className="text-emerald-400 w-3.5 h-3.5 shrink-0" />
                   <span>{device.ram_total_gb ? `${device.ram_total_gb}GB` : 'RAM'}</span>
                </div>
                <div className="px-2.5 py-1 bg-slate-950/50 rounded-lg border border-slate-800 text-[11px] font-medium text-slate-300 flex items-center gap-1.5">
                   <FiShield className="text-amber-400 w-3.5 h-3.5 shrink-0" />
                   <span>{device.os_version?.slice(0, 12) || 'Windows'}</span>
                </div>
            </div>

            {/* Actions */}
            <div className="flex items-center gap-2 w-full md:w-auto shrink-0">
                <button
                    onClick={() => onCommand(device.id, 'OPTIMIZE_RAM')}
                    className="flex-1 md:flex-none px-3.5 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs shadow-md shadow-indigo-600/20 transition-all flex items-center justify-center gap-1.5 active:scale-95"
                >
                    <FiZap className="w-3.5 h-3.5" />
                    <span>Otimizar</span>
                </button>
                <button
                    onClick={() => onLock(device)}
                    title="Bloquear Dispositivo"
                    className="p-2 rounded-xl bg-rose-500/10 text-rose-400 border border-rose-500/20 hover:bg-rose-500/20 hover:border-rose-500/40 transition-colors flex items-center justify-center"
                >
                    <FiLock className="w-3.5 h-3.5" />
                </button>
            </div>
        </div>
    );
}
