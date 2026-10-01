'use client';

import React, { useEffect, useState, useMemo } from 'react';
import { createClient } from '@/utils/supabase/client';
import { motion, AnimatePresence } from 'framer-motion';
import Link from 'next/link';
import toast from 'react-hot-toast';
import { useDashboard } from '@/app/context/DashboardContext';
import VoltrisIcon, { type VoltrisIconName } from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';

export default function CompaniesClient() {
    const { transparencyMode } = useDashboard();
    const [loading, setLoading] = useState(true);
    const [company, setCompany] = useState<any>(null);
    const [stats, setStats] = useState({ devices: 0, alerts: 0, avgHealth: 100 });
    const [recentAlerts, setRecentAlerts] = useState<any[]>([]);
    const [isBuyModalOpen, setIsBuyModalOpen] = useState(false);
    const [buyQuantity, setBuyQuantity] = useState(5);
    const supabase = useMemo(() => createClient(), []);

    useEffect(() => {
        async function loadData() {
            try {
                const { data: { user } } = await supabase.auth.getUser();
                if (!user) return;

                const { data: link } = await supabase
                    .from('company_users')
                    .select('*, companies(*)')
                    .eq('user_id', user.id)
                    .single();

                if (link && link.companies) {
                    setCompany(link.companies);

                    const { count: devCount } = await supabase
                        .from('devices')
                        .select('*', { count: 'exact', head: true })
                        .eq('company_id', link.companies.id);

                    const { count: alertCount } = await supabase
                        .from('device_alerts')
                        .select('*', { count: 'exact', head: true })
                        .eq('company_id', link.companies.id)
                        .eq('is_resolved', false);

                    setStats({
                        devices: devCount || 0,
                        alerts: alertCount || 0,
                        avgHealth: 98, 
                    });

                    const { data: alerts } = await supabase
                        .from('device_alerts')
                        .select('*, devices(hostname)')
                        .eq('company_id', link.companies.id)
                        .eq('is_resolved', false)
                        .order('created_at', { ascending: false })
                        .limit(5);

                    setRecentAlerts(alerts || []);
                }
            } catch (error) {
                console.error('Error:', error);
            } finally {
                setLoading(false);
            }
        }
        loadData();
    }, [supabase]);

    const handleCreateCompany = async () => {
        const name = window.prompt("Qual o nome da sua empresa/organização?");
        if (!name) return;

        try {
            setLoading(true);
            const { data: { user } } = await supabase.auth.getUser();
            if (!user) {
                toast.error("Você precisa estar logado.");
                return;
            }

            const { error } = await supabase.rpc('create_new_organization', {
                org_name: name
            });

            if (error) throw error;

            toast.success("Organização criada com sucesso!");
            window.location.reload();

        } catch (err: any) {
            toast.error("Erro ao criar organização: " + err.message);
        } finally {
            setLoading(false);
        }
    };

    const handleBuyLicenses = async () => {
        try {
            toast.loading("Processando checkout de expansão...");
            const res = await fetch('/api/checkout/company-slots', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    company_id: company.id,
                    quantity: buyQuantity
                })
            });
            const data = await res.json();
            if (data.url) {
                window.location.href = data.url;
            } else {
                throw new Error(data.error || 'Erro ao gerar checkout');
            }
        } catch (err: any) {
            toast.dismiss();
            toast.error(err.message);
        }
    };

    const handleOptimizeAll = async () => {
        if (!confirm("Isso enviará uma sequência de OTIMIZAÇÃO para TODOS os nós ativos. Continuar?")) return;

        const toastId = toast.loading("Transmitindo diretriz para a frota...");

        try {
            const { data: devices } = await supabase
                .from('devices')
                .select('id')
                .eq('company_id', company.id);

            if (!devices || devices.length === 0) {
                toast.dismiss(toastId);
                toast.error("Nenhum nó ativo encontrado.");
                return;
            }

            const { data: { user } } = await supabase.auth.getUser();
            const commands = devices.map(d => ({
                device_id: d.id,
                company_id: company.id,
                command_type: 'OPTIMIZE_RAM',
                status: 'pending',
                create_user_id: user?.id
            }));

            const { error } = await supabase.from('remote_commands').insert(commands);
            if (error) throw error;

            toast.dismiss(toastId);
            toast.success(`Comando enviado para ${devices.length} dispositivos.`);

        } catch (err: any) {
            toast.dismiss(toastId);
            toast.error("Falha no envio: " + err.message);
        }
    };

    if (loading) {
        return (
          <div className="flex flex-col items-center justify-center py-32 gap-3 text-slate-400">
            <div className="w-8 h-8 rounded-full border-2 border-slate-700 border-t-indigo-500 animate-spin"></div>
            <p className="text-xs font-medium text-slate-400">Sincronizando telemetria da organização...</p>
          </div>
        );
    }

    if (!company) {
        return (
            <div className="flex flex-col items-center justify-center py-20 text-center">
                <div className={`p-8 sm:p-12 rounded-2xl border border-slate-800 flex flex-col items-center gap-6 max-w-md w-full ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 shadow-xl'}`}>
                    <VoltrisIconTile icon="streamHub" accent={DASHBOARD_ACCENT.brand} size={16} />
                    <div className="space-y-2">
                        <h2 className="text-xl font-bold text-white tracking-tight">Nenhuma Organização Vinculada</h2>
                        <p className="text-xs text-slate-400 leading-relaxed">
                            Sua conta ainda não está associada a uma empresa. Crie uma organização corporativa para gerenciar frotas de computadores centralizadamente.
                        </p>
                    </div>
                    <button
                        onClick={handleCreateCompany}
                        className="w-full py-3 px-6 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all active:scale-95"
                    >
                        Criar Nova Organização
                    </button>
                </div>
            </div>
        );
    }

    return (
        <div className="flex flex-col gap-6 w-full max-w-full">
            
            {/* Header */}
            <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 pb-4 border-b border-slate-800/80">
                <div className="space-y-1">
                   <div className="flex items-center gap-3">
                     <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight">{company.name}</h2>
                     <span className="px-2.5 py-0.5 rounded-full text-[10px] font-semibold bg-indigo-500/10 text-indigo-400 border border-indigo-500/20 uppercase tracking-wider">
                       Plano {company.plan_type}
                     </span>
                   </div>
                   <p className="text-xs text-slate-400">
                     Capacidade alocada: <span className="text-emerald-400 font-semibold">{stats.devices}</span> de <span className="text-white font-semibold">{company.max_devices} slots</span> em uso
                   </p>
                </div>

                <div className="flex items-center gap-2.5 w-full sm:w-auto">
                    <Link 
                      href="/dashboard/companies/devices" 
                      className="flex-1 sm:flex-none px-4 py-2.5 bg-slate-900 border border-slate-800 hover:bg-slate-800 hover:border-slate-700 text-slate-200 text-xs font-semibold rounded-xl transition-all flex items-center justify-center gap-2 shadow-sm"
                    >
                        <VoltrisIcon name="display" size={16} className="text-indigo-400" />
                        <span>Gerenciar Nós</span>
                    </Link>
                    <button
                        onClick={() => setIsBuyModalOpen(true)}
                        className="flex-1 sm:flex-none px-4 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-semibold rounded-xl shadow-md shadow-indigo-600/20 transition-all flex items-center justify-center gap-2"
                    >
                        <VoltrisIcon name="plus" size={16} />
                        <span>Expandir Slots</span>
                    </button>
                </div>
            </div>

            {/* Metrics Grid */}
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <StatCard
                    title="Nós Conectados"
                    value={stats.devices.toString()}
                    icon="display"
                    color="indigo"
                    subtext={`${company.max_devices - stats.devices} slots livres`}
                    transparencyMode={transparencyMode}
                />
                <StatCard
                    title="Alertas Críticos"
                    value={stats.alerts.toString()}
                    icon="alertTriangle"
                    color={stats.alerts > 0 ? "rose" : "slate"}
                    subtext={stats.alerts > 0 ? "Atenção necessária" : "Tudo normal"}
                    alert={stats.alerts > 0}
                    transparencyMode={transparencyMode}
                />
                <StatCard
                    title="Saúde da Frota"
                    value={`${stats.avgHealth}%`}
                    icon="trendingUp"
                    color="emerald"
                    subtext="Estabilidade média"
                    transparencyMode={transparencyMode}
                />
                <StatCard
                    title="Carga de Sistema"
                    value="12%"
                    icon="system"
                    color="indigo"
                    subtext="Uso nominal estável"
                    transparencyMode={transparencyMode}
                />
            </div>

            {/* Content Section */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
                
                {/* Recent Alerts */}
                <div className={`lg:col-span-2 p-6 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'}`}>
                    <div className="flex items-center justify-between pb-4 mb-4 border-b border-slate-800/80">
                       <div className="flex items-center gap-2.5">
                          <VoltrisIconTile icon="alertTriangle" accent={DASHBOARD_ACCENT.danger} size={8} />
                          <div>
                            <h3 className="text-sm font-bold text-white tracking-tight">Registro de Ocorrências</h3>
                            <p className="text-[11px] text-slate-500">Monitoramento e anomalias de hardware na frota</p>
                          </div>
                       </div>
                       <span className="text-[10px] font-semibold text-slate-400 px-2 py-0.5 rounded bg-slate-800 border border-slate-700">Tempo Real</span>
                    </div>

                    {recentAlerts.length === 0 ? (
                        <div className="py-16 flex flex-col items-center justify-center text-center gap-2 text-slate-500">
                            <VoltrisIcon name="success" size={40} className="text-emerald-400 mb-1" />
                            <p className="text-xs font-semibold text-slate-300">Nenhum alerta pendente</p>
                            <p className="text-[11px] text-slate-500">Todos os computadores da organização operam normalmente.</p>
                        </div>
                    ) : (
                        <div className="space-y-3">
                            {recentAlerts.map(alert => (
                                <AlertItem
                                    key={alert.id}
                                    device={alert.devices?.hostname || "Dispositivo não identificado"}
                                    msg={alert.message}
                                    time={new Date(alert.created_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                                    level={alert.level.toLowerCase()}
                                />
                            ))}
                        </div>
                    )}
                </div>

                {/* Remote Directives */}
                <div className={`p-6 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'} flex flex-col justify-between`}>
                    <div>
                      <div className="flex items-center gap-2.5 pb-4 mb-4 border-b border-slate-800/80">
                         <VoltrisIconTile icon="bolt" accent={DASHBOARD_ACCENT.brand} size={8} />
                         <div>
                            <h3 className="text-sm font-bold text-white tracking-tight">Comandos Rápidos</h3>
                            <p className="text-[11px] text-slate-500">Rotinas automatizadas para múltiplos nós</p>
                         </div>
                      </div>

                      <div className="space-y-2.5">
                          <button
                              onClick={handleOptimizeAll}
                              className="w-full text-left p-3.5 rounded-xl bg-slate-950/60 border border-slate-800 hover:border-indigo-500/40 hover:bg-slate-800/50 transition-all flex items-center justify-between group"
                          >
                              <div className="flex items-center gap-3 min-w-0">
                                  <VoltrisIconTile icon="bolt" accent={DASHBOARD_ACCENT.brand} size={9} />
                                 <div className="min-w-0">
                                    <span className="block text-xs font-semibold text-slate-200 group-hover:text-white truncate">Otimização em Massa</span>
                                    <span className="block text-[10px] text-slate-500 truncate">Limpeza e RAM em todos os nós</span>
                                 </div>
                              </div>
                              <VoltrisIcon name="arrowRight" size={16} className="text-slate-500 group-hover:text-indigo-400 group-hover:translate-x-0.5 transition-all shrink-0 ml-2" />
                          </button>

                          <Link href="/dashboard/companies/devices" className="w-full text-left p-3.5 rounded-xl bg-slate-950/60 border border-slate-800 hover:border-emerald-500/40 hover:bg-slate-800/50 transition-all flex items-center justify-between group">
                              <div className="flex items-center gap-3 min-w-0">
                                  <VoltrisIconTile icon="security" accent={DASHBOARD_ACCENT.success} size={9} />
                                 <div className="min-w-0">
                                    <span className="block text-xs font-semibold text-slate-200 group-hover:text-white truncate">Inventário & Dispositivos</span>
                                    <span className="block text-[10px] text-slate-500 truncate">Listar e gerenciar acessos</span>
                                 </div>
                              </div>
                              <VoltrisIcon name="arrowRight" size={16} className="text-slate-500 group-hover:text-emerald-400 group-hover:translate-x-0.5 transition-all shrink-0 ml-2" />
                          </Link>
                      </div>
                    </div>
                </div>
            </div>

            {/* Expansion Modal */}
            <AnimatePresence>
                {isBuyModalOpen && (
                    <div className="fixed inset-0 z-[300] flex items-center justify-center p-4">
                        <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className="absolute inset-0 bg-slate-950/80 backdrop-blur-sm" onClick={() => setIsBuyModalOpen(false)} />
                        <motion.div
                            initial={{ scale: 0.95, opacity: 0 }}
                            animate={{ scale: 1, opacity: 1 }}
                            exit={{ scale: 0.95, opacity: 0 }}
                            className={`border border-slate-800 rounded-2xl p-6 sm:p-8 max-w-lg w-full shadow-2xl relative overflow-hidden ${transparencyMode ? 'voltris-glass' : 'bg-slate-900'}`}
                        >
                            <div className="flex items-center justify-between mb-6 pb-4 border-b border-slate-800">
                                <div className="flex items-center gap-3">
                                  <VoltrisIconTile icon="pieChart" accent={DASHBOARD_ACCENT.brand} size={10} />
                                  <div>
                                    <h2 className="text-lg font-bold text-white tracking-tight">Expandir Capacidade da Frota</h2>
                                    <p className="text-xs text-slate-400">Adicione novos nós simultâneos ao plano corporativo</p>
                                  </div>
                               </div>
                               <button onClick={() => setIsBuyModalOpen(false)} className="text-slate-500 hover:text-white transition-colors">
                                 <VoltrisIcon name="close" size={20} />
                               </button>
                            </div>

                            <div className="mb-6 space-y-2">
                                <label className="text-xs font-semibold text-slate-300 block">Quantidade de novos computadores:</label>
                                <div className="grid grid-cols-4 gap-2.5">
                                    {[5, 10, 25, 50].map(qty => (
                                        <button
                                            key={qty}
                                            type="button"
                                            onClick={() => setBuyQuantity(qty)}
                                            className={`py-2.5 rounded-xl border text-xs font-semibold transition-all
                                              ${buyQuantity === qty 
                                                ? 'bg-indigo-600 border-indigo-500 text-white shadow-md shadow-indigo-600/30' 
                                                : 'bg-slate-950/50 border-slate-800 text-slate-400 hover:bg-slate-800 hover:text-white'}`}
                                        >
                                            +{qty} nós
                                        </button>
                                    ))}
                                </div>
                            </div>

                            <div className="flex justify-between items-center bg-slate-950/60 border border-slate-800 p-4 rounded-xl mb-6">
                                <div className="space-y-0.5">
                                  <span className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Valor Estimado</span>
                                  <div className="flex items-baseline gap-1.5">
                                     <span className="text-2xl font-bold text-white">R$ {(buyQuantity * 29.90).toFixed(2)}</span>
                                     <span className="text-[11px] text-slate-500">/ mês</span>
                                  </div>
                                </div>
                                <div className="text-right">
                                   <span className="text-[11px] font-semibold text-indigo-400">Ativação Imediata</span>
                                   <span className="text-[10px] text-slate-500 block">Cobrança proporcional</span>
                                </div>
                            </div>

                            <div className="flex gap-3">
                                <button
                                    onClick={() => setIsBuyModalOpen(false)}
                                    className="flex-1 py-2.5 rounded-xl bg-slate-800 text-slate-300 font-semibold text-xs hover:bg-slate-700 transition-colors"
                                >
                                    Cancelar
                                </button>
                                <button
                                    onClick={handleBuyLicenses}
                                    className="flex-1 py-2.5 px-4 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs shadow-md shadow-indigo-600/20 transition-all active:scale-95"
                                >
                                    Prosseguir para Checkout
                                </button>
                            </div>
                        </motion.div>
                    </div>
                )}
            </AnimatePresence>
        </div>
    );
}

function StatCard({ title, value, icon, color = 'indigo', subtext, alert = false, transparencyMode }: {
    title: string;
    value: string;
    icon: VoltrisIconName;
    color?: string;
    subtext?: string;
    alert?: boolean;
    transparencyMode?: boolean;
}) {
    // Acentos = paleta do app desktop (VoltrisDesignSystem.xaml:20-28).
    const colorStyles: Record<string, string> = {
      indigo: DASHBOARD_ACCENT.brand,
      emerald: DASHBOARD_ACCENT.success,
      rose: DASHBOARD_ACCENT.danger,
      slate: DASHBOARD_ACCENT.brand,
    };

    const currentAccent = colorStyles[color] || colorStyles.indigo;

    return (
        <div
            className={`border rounded-xl p-5 relative overflow-hidden transition-all duration-200
              ${alert ? 'border-rose-500/40 bg-rose-500/5' : 'border-slate-800/80 bg-slate-900/50 shadow-sm'} 
              hover:border-slate-700
            `}
        >
            <div className="relative z-10 flex flex-col justify-between h-full gap-3">
                <div className="flex justify-between items-center">
                    <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">{title}</span>
                    <VoltrisIconTile icon={icon} accent={currentAccent} size={8} />
                </div>
                <div>
                   <h3 className="text-2xl font-bold text-white tracking-tight leading-none mb-1">{value}</h3>
                   <span className="text-[11px] font-medium text-slate-500">{subtext}</span>
                </div>
            </div>
        </div>
    );
}

function AlertItem({ device, msg, time, level = 'warning' }: any) {
    const badgeAccent = level === 'critical' ? DASHBOARD_ACCENT.danger : DASHBOARD_ACCENT.warning;

    return (
        <div className="flex items-center gap-3.5 p-3.5 rounded-xl bg-slate-950/40 border border-slate-800/80 hover:border-slate-700 transition-colors">
            <VoltrisIconTile icon="alertTriangle" accent={badgeAccent} size={9} />
            <div className="flex-1 min-w-0">
                <h4 className="text-xs font-semibold text-slate-200 truncate">{msg}</h4>
                <div className="flex items-center gap-2 mt-0.5">
                   <span className="text-[11px] text-slate-400 font-mono">{device}</span>
                </div>
            </div>
            <span className="text-[10px] text-slate-500 font-mono shrink-0">{time}</span>
        </div>
    );
}
