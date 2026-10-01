'use client';

import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { toast } from 'react-hot-toast';
import Link from 'next/link';
import VoltrisIcon, { type VoltrisIconName } from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';

import type { Order } from '@/types/order';
import { createClient } from '@/utils/supabase/client';
import { useAuth } from '@/app/hooks/useAuth';
import AuthGuard from '@/components/AuthGuard';
import MyComputerPage from './MyComputerPage';
import SecurityPage from './SecurityPage';
import { useSearchParams } from 'next/navigation';
import { Suspense } from 'react';
import { useDashboard } from '@/app/context/DashboardContext';
import { notifyPageView } from '@/utils/notifications';


// Componente de Card de Estatística - Estilo Executivo
const StatCard = ({ title, value, icon, delay, description }: {
  title: string;
  value: number;
  icon: VoltrisIconName;
  delay: number;
  description?: string;
}) => {
  const { transparencyMode } = useDashboard();

  return (
    <motion.div
      initial={{ opacity: 0, y: 15 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.4, delay, ease: "easeOut" }}
      className={`relative group p-5 sm:p-6 rounded-2xl border transition-all duration-200
        ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/50 border-slate-800/80 shadow-sm'}
        hover:border-slate-700 hover:bg-slate-900/80
      `}
    >
      <div className="flex items-center justify-between gap-4 mb-4">
        <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">{title}</span>
        <VoltrisIconTile icon={icon} accent={DASHBOARD_ACCENT.brand} size={9} />
      </div>
      <div className="flex items-baseline justify-between">
        <h3 className="text-2xl sm:text-3xl font-bold text-white tracking-tight">{value}</h3>
        <div className="flex items-center gap-1.5">
          <div className="w-1.5 h-1.5 rounded-full bg-emerald-400"></div>
          <span className="text-[10px] font-medium text-slate-400 uppercase tracking-wider">Ativo</span>
        </div>
      </div>
      {description && (
        <p className="text-[11px] font-medium text-slate-500 mt-2">{description}</p>
      )}
    </motion.div>
  );
};

export default function DashboardClient() {
  return (
    <Suspense fallback={
      <div className="h-full w-full flex items-center justify-center">
        <div className="flex flex-col items-center gap-6">
          <div className="w-20 h-20 border-t-4 border-l-4 border-b-4 border-transparent border-r-4 border-r-[#31A8FF] rounded-full animate-spin"></div>
          <p className="text-gray-500 font-black uppercase tracking-[0.3em] text-xs animate-pulse">Iniciando Terminal...</p>
        </div>
      </div>
    }>
      <DashboardContent />
    </Suspense>
  );
}

function DashboardContent() {
  const { user, profile, loading } = useAuth();
  const { transparencyMode } = useDashboard();
  const searchParams = useSearchParams();
  const activeTab = searchParams.get('tab') || (searchParams.get('checkout_success') === 'true' ? 'licenses' : 'overview');

  const [orders, setOrders] = useState<Order[]>([]);
  const [payments, setPayments] = useState<any[]>([]);
  const [licenses, setLicenses] = useState<any[]>([]);
  const [installationsCount, setInstallationsCount] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isCancelModalOpen, setIsCancelModalOpen] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);
  const supabase = useMemo(() => createClient(), []);

  const fetchData = useCallback(async (showLoading = true) => {
    if (!user) return;
    try {
      if (showLoading) setIsLoading(true);

      const [ordersRes, licensesRes, installationsRes, paymentsRes] = await Promise.all([
        supabase.from('orders').select('*').eq('user_id', user.id).order('created_at', { ascending: false }),
        supabase.from('licenses').select('*').eq('email', user.email).order('created_at', { ascending: false }),
        supabase.from('installations').select('id', { count: 'exact', head: true }).eq('user_id', user.id),
        supabase.from('payments').select('*').eq('user_id', user.id).order('created_at', { ascending: false })
      ]);

      if (ordersRes.error) throw ordersRes.error;
      setOrders(ordersRes.data || []);
      setPayments(paymentsRes.data || []);

      if (!licensesRes.error) {
        setLicenses(licensesRes.data || []);
      }

      setInstallationsCount(installationsRes.count || 0);

    } catch (error) {
      console.error('Erro:', error);
      toast.error('Erro ao atualizar dashboard');
    } finally {
      if (showLoading) setIsLoading(false);
    }
  }, [user?.id, user?.email, supabase]); // Depende apenas do ID e email, não do objeto user inteiro

  const handleManageBilling = async () => {
    const toastId = toast.loading('Conectando ao Stripe...');
    try {
      const response = await fetch('/api/stripe/portal', { method: 'POST' });
      const data = await response.json();
      if (data.url) {
        window.location.href = data.url;
      } else {
        throw new Error(data.error || 'Erro ao abrir portal');
      }
    } catch (error: any) {
      toast.error(error.message, { id: toastId });
    } finally {
      toast.dismiss(toastId);
    }
  };

  const handleRequestRefund = async () => {
    if (!window.confirm("Aviso: O reembolso cancelará sua assinatura imediatamente e desativará seu acesso. O valor será estornado se a compra foi feita nos últimos 7 dias. Deseja prosseguir?")) {
      return;
    }

    const toastId = toast.loading('Processando reembolso com a Stripe...');
    try {
      const response = await fetch('/api/stripe/refund', { method: 'POST' });
      const data = await response.json();

      if (response.ok) {
        toast.success(data.message || 'Reembolso efetuado com sucesso!', { id: toastId });
        fetchData(false);
      } else {
        throw new Error(data.error || 'Erro ao processar reembolso');
      }
    } catch (error: any) {
      toast.error(error.message, { id: toastId });
    }
  };

  const handleConfirmCancel = async () => {
    setIsCancelling(true);
    const toastId = toast.loading('Processando cancelamento...');
    try {
      const response = await fetch('/api/stripe/cancel', { method: 'POST' });
      const data = await response.json();

      if (response.ok) {
        toast.success('Assinatura cancelada! Você ainda terá acesso até o fim do período.', { id: toastId });
        setIsCancelModalOpen(false);
        fetchData(false);
      } else {
        throw new Error(data.error || 'Erro ao cancelar');
      }
    } catch (error: any) {
      toast.error(error.message, { id: toastId });
    } finally {
      setIsCancelling(false);
    }
  };

  // Carregar dados apenas na montagem e quando o ID do usuário mudar de fato
  const userIdRef = useRef<string | undefined>(undefined);
  useEffect(() => {
    // Se o auth terminou de carregar e não temos usuário, paramos o loading local
    // para permitir que o AuthGuard execute o redirecionamento.
    if (!loading && !user) {
      setIsLoading(false);
      return;
    }

    if (!user?.id) return;

    // Só buscar se o ID mudou (evita refetch em token refresh que recria o objeto user)
    if (userIdRef.current === user.id) return;
    userIdRef.current = user.id;
    fetchData();
  }, [user?.id, loading, fetchData]);

  // Notificar visualização da aba de licenças no Dashboard
  useEffect(() => {
    if (activeTab === 'licenses') {
      notifyPageView("Aba de Licenças (Dashboard Interno)");
    }
  }, [activeTab]);

  useEffect(() => {
    const success = searchParams.get('checkout_success');
    if (success !== 'true' || loading) return;

    const type = searchParams.get('type');
    const successMsg = type === 'service' ? 'Serviço adquirido com sucesso!' : 'Pedido confirmado! Processando sua licença...';
    const successIcon = type === 'service' ? '🛠️' : '💎';

    toast.success(successMsg, {
      duration: 6000,
      position: 'top-center',
      icon: successIcon,
      style: {
        background: 'rgba(10, 10, 15, 0.9)',
        color: '#fff',
        border: '1px solid rgba(49, 168, 255, 0.3)',
        backdropFilter: 'blur(10px)',
        borderRadius: '1rem',
        fontWeight: 'bold'
      },
    });

    fetchData(false);
    const timer = setTimeout(() => fetchData(false), 5000);
    return () => clearTimeout(timer);
  }, [searchParams, loading, fetchData]);

  const stats = {
    totalOrders: orders.length + payments.length,
    activeLicenses: licenses.filter(l => l.is_active).length,
    computers: installationsCount
  };

  const hardwareIDProtection = stats.activeLicenses > 0;

  return (
    <AuthGuard>
      {isLoading ? (
        <div className="h-full w-full flex items-center justify-center min-h-[400px]">
          <div className="flex flex-col items-center gap-6">
            <div className="relative">
              <div className="w-20 h-20 border-r-4 border-[#31A8FF] rounded-full animate-spin"></div>
              <div className="absolute inset-0 w-20 h-20 border-t-4 border-[#8B31FF] rounded-full animate-spin-slow"></div>
            </div>
            <p className="text-gray-500 font-black uppercase tracking-[0.3em] text-[10px] animate-pulse">Sincronizando com Supabase Cloud...</p>
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-6 w-full max-w-full h-full min-h-0">
          {/* Dashboard Header - Executive Style */}
          <header className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-2 border-b border-slate-800/60">
            <div className="space-y-1">
              <div className="flex items-center gap-3 flex-wrap">
                <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight">
                  Painel de Controle
                </h2>

                {/* Hardware ID Protection Status Badge */}
                <div className={`inline-flex items-center gap-2 px-2.5 py-0.5 rounded-full text-[11px] font-medium border transition-colors
                 ${hardwareIDProtection
                    ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400'
                    : 'bg-rose-500/10 border-rose-500/20 text-rose-400'}
               `}>
                  <div className={`w-1.5 h-1.5 rounded-full ${hardwareIDProtection ? 'bg-emerald-400' : 'bg-rose-400'}`}></div>
                  <span>
                    Proteção HID: {hardwareIDProtection ? 'Ativa' : 'Offline'}
                  </span>
                </div>
              </div>
              <p className="text-slate-400 text-xs font-normal">
                Bem-vindo de volta, <span className="text-slate-200 font-medium">{profile?.full_name || user?.email?.split('@')[0] || 'Usuário'}</span>
              </p>
            </div>

            <div className="flex items-center gap-2.5">
              <Link 
                href="/" 
                className="px-3 py-2 rounded-xl border border-slate-800 bg-slate-900/60 hover:bg-slate-800 hover:border-slate-700 transition-all text-xs font-medium text-slate-300 hover:text-white flex items-center gap-1.5 shadow-sm"
              >
                <VoltrisIcon name="arrowLeft" size={14} className="text-slate-400" />
                <span>Voltar ao Site</span>
              </Link>
              <motion.button
                whileHover={{ scale: 1.02 }}
                whileTap={{ scale: 0.98 }}
                onClick={() => {
                  setIsRefreshing(true);
                  fetchData(false)
                    .then(() => toast.success('Dados atualizados!'))
                    .finally(() => setIsRefreshing(false));
                }}
                className={`p-2.5 rounded-xl bg-slate-900/60 border border-slate-800 text-slate-400 hover:text-white hover:bg-slate-800 transition-all ${isRefreshing ? 'opacity-50' : ''}`}
                title="Atualizar dados"
              >
                <VoltrisIcon name="processing" size={16} className={isRefreshing ? 'animate-spin' : ''} />
              </motion.button>
              <Link href="/servicos">
                <motion.button
                  whileHover={{ scale: 1.02 }}
                  whileTap={{ scale: 0.98 }}
                  className="flex items-center gap-2 px-4 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-colors"
                >
                  <VoltrisIcon name="plus" size={16} />
                  <span>Novo Pedido</span>
                </motion.button>
              </Link>
            </div>
          </header>

          {/* Tab Content Rendering */}
          <div className="flex-1 min-h-0 relative">
            <AnimatePresence mode="wait">
              {activeTab === 'overview' && (
                <motion.div
                  key="overview"
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -10 }}
                  transition={{ duration: 0.2 }}
                  className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5"
                >
                  <StatCard title="Serviços Adquiridos" value={stats.totalOrders} icon="package" delay={0.05} description="Total de pedidos e serviços" />
                  <StatCard title="Licenças Ativas" value={stats.activeLicenses} icon="success" delay={0.1} description="Disponíveis para uso imediato" />
                  <StatCard title="Computadores Vinculados" value={stats.computers} icon="display" delay={0.15} description="Máquinas com uplink ativo" />
                  
                  {/* Action Cards Grid */}
                  <div className="md:col-span-2 lg:col-span-3 grid grid-cols-1 lg:grid-cols-2 gap-5 mt-2">
                    
                    {/* App Download Card */}
                    <div className={`p-6 sm:p-7 rounded-2xl border transition-all ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/40 border-slate-800/80'} flex flex-col justify-between gap-6`}>
                      <div className="space-y-3">
                        <VoltrisIconTile icon="download" accent={DASHBOARD_ACCENT.brand} size={10} />
                        <h3 className="text-lg font-bold text-white tracking-tight">Voltris Optimizer Desktop</h3>
                        <p className="text-slate-400 text-xs leading-relaxed">
                          Baixe o aplicativo para aplicar otimizações de baixa latência, telemetria de hardware e gerenciamento de comandos remotos.
                        </p>
                      </div>
                      <div className="pt-2">
                        <Link href="/voltrisoptimizer" className="inline-flex items-center gap-2 px-4 py-2.5 bg-white hover:bg-slate-100 text-slate-950 font-semibold text-xs rounded-xl transition-all shadow-sm">
                          <VoltrisIcon name="download" size={16} />
                          <span>Baixar Voltris Optimizer</span>
                        </Link>
                      </div>
                    </div>

                    {/* Billing Hub Card */}
                    <div className={`p-6 sm:p-7 rounded-2xl border transition-all ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/40 border-slate-800/80'} flex flex-col justify-between gap-6`}>
                      <div className="space-y-3">
                        <VoltrisIconTile icon="creditCard" accent={DASHBOARD_ACCENT.brand} size={10} />
                        <h3 className="text-lg font-bold text-white tracking-tight">Faturamento & Assinatura</h3>
                        <p className="text-slate-400 text-xs leading-relaxed">
                          Gerencie seus métodos de pagamento, acesse faturas da Stripe, cancele renovações automáticas ou solicite reembolso de garantia.
                        </p>
                      </div>
                      <div className="flex flex-wrap items-center gap-3 pt-2">
                        <button
                          onClick={handleManageBilling}
                          className="px-4 py-2.5 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-200 font-medium text-xs rounded-xl transition-all flex items-center gap-2"
                        >
                          <VoltrisIcon name="externalLink" size={14} /> Portal Stripe
                        </button>
                        <button
                          onClick={() => setIsCancelModalOpen(true)}
                          className="px-4 py-2.5 bg-slate-900/60 hover:bg-slate-800 border border-slate-800 text-slate-300 font-medium text-xs rounded-xl transition-all"
                        >
                          Cancelar Renovação
                        </button>
                        <button
                          onClick={handleRequestRefund}
                          className="px-4 py-2.5 bg-rose-500/10 hover:bg-rose-500/15 border border-rose-500/20 text-rose-400 font-medium text-xs rounded-xl transition-all"
                        >
                          Reembolso (7 Dias)
                        </button>
                      </div>
                    </div>

                  </div>
                </motion.div>
              )}

              {activeTab === 'licenses' && (
                <motion.div
                  key="licenses"
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -10 }}
                  transition={{ duration: 0.2 }}
                  className="space-y-6"
                >
                  {/* Warning & Billing Actions Banner */}
                  <div className="flex flex-col gap-4 p-5 sm:p-6 rounded-2xl bg-amber-500/[0.06] border border-amber-500/20">
                    <div className="flex flex-col sm:flex-row items-center gap-4">
                       <VoltrisIconTile icon="alertTriangle" accent={DASHBOARD_ACCENT.warning} size={10} />
                      <div className="flex-1 text-center sm:text-left">
                        <h4 className="font-semibold text-slate-200 text-sm">Gestão de Licenças & Faturamento</h4>
                        <p className="text-slate-400 text-xs mt-0.5">Gerencie seu plano, cancele renovação ou solicite reembolso dentro de 7 dias.</p>
                      </div>
                      <button 
                        onClick={() => fetchData(true)} 
                        className="px-4 py-2 bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/30 text-amber-300 font-medium text-xs rounded-xl transition-all shadow-sm"
                      >
                        Sincronizar Agora
                      </button>
                    </div>

                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 pt-3 border-t border-amber-500/15">
                      <button
                        onClick={() => setIsCancelModalOpen(true)}
                        className="flex items-center justify-center gap-2 px-4 py-2.5 bg-slate-900 hover:bg-slate-800 border border-slate-800 text-slate-300 font-medium text-xs rounded-xl transition-all"
                      >
                        <VoltrisIcon name="close" size={14} /> Cancelar Renovação Automática
                      </button>
                      <button
                        onClick={handleRequestRefund}
                        className="flex items-center justify-center gap-2 px-4 py-2.5 bg-rose-500/10 hover:bg-rose-500/15 border border-rose-500/20 text-rose-400 font-medium text-xs rounded-xl transition-all"
                      >
                        <VoltrisIcon name="processing" size={14} /> Solicitar Reembolso (7 Dias)
                      </button>
                    </div>
                  </div>

                  <div className="grid grid-cols-1 xl:grid-cols-2 gap-5">
                    {licenses.length > 0 ? (
                      licenses.map((lic, i) => (
                        <motion.div
                          key={lic.id}
                          initial={{ opacity: 0, y: 15 }}
                          animate={{ opacity: 1, y: 0 }}
                          transition={{ delay: i * 0.05 }}
                          className={`group relative p-6 rounded-2xl border transition-all duration-200 ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/50 border-slate-800/80 shadow-sm'} hover:border-slate-700`}
                        >
                          <div className="relative z-10 flex flex-col gap-5">
                            {/* Card Top */}
                            <div className="flex items-center justify-between">
                              <div className="flex items-center gap-3">
                                <VoltrisIconTile
                                  icon="success"
                                  accent={lic.is_active ? DASHBOARD_ACCENT.brand : DASHBOARD_ACCENT.danger}
                                  size={10}
                                />
                                <div className="flex flex-col">
                                  <h4 className="text-base font-bold text-white tracking-tight">{lic.license_type}</h4>
                                  <span className={`text-[10px] font-medium px-2 py-0.5 rounded-full uppercase tracking-wider w-fit mt-0.5 border ${lic.is_active ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' : 'bg-rose-500/10 text-rose-400 border-rose-500/20'}`}>
                                    {lic.is_active ? 'Ativa' : 'Expirada'}
                                  </span>
                                </div>
                              </div>
                              <div className="flex flex-col items-end">
                                <span className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider">Validade</span>
                                <span className="text-xs font-semibold text-slate-200 mt-0.5">{new Date(lic.expires_at).toLocaleDateString('pt-BR')}</span>
                              </div>
                            </div>

                            {/* Key Section */}
                            <div className="bg-slate-950/70 rounded-xl p-3.5 border border-slate-800/80 flex flex-col gap-1.5">
                              <span className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider">Chave de Ativação</span>
                              <div className="flex items-center justify-between gap-3">
                                <code className="flex-1 font-mono text-xs font-semibold text-slate-200 tracking-wide truncate select-all">{lic.license_key}</code>
                                <div className="flex items-center gap-1.5">
                                  <button
                                    onClick={() => { navigator.clipboard.writeText(lic.license_key); toast.success('Chave copiada!'); }}
                                    className="p-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white transition-all border border-slate-700/80"
                                    title="Copiar Chave"
                                  >
                                    <VoltrisIcon name="copy" size={14} />
                                  </button>
                                  <Link href={`/dashboard?tab=pc`} className="p-2 rounded-lg bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-400 hover:text-indigo-300 transition-all border border-indigo-500/25" title="Ver em Meu Computador">
                                    <VoltrisIcon name="externalLink" size={14} />
                                  </Link>
                                </div>
                              </div>
                            </div>

                            {/* Footer Info */}
                            <div className="flex items-center justify-between border-t border-slate-800/60 pt-4 text-xs">
                              <div className="flex items-center gap-6">
                                <div className="flex flex-col">
                                  <span className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider">Dispositivos</span>
                                  <span className="text-xs font-bold text-white mt-0.5">{lic.devices_in_use}/{lic.max_devices}</span>
                                </div>
                                <div className="flex flex-col">
                                  <span className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider">Status HWID</span>
                                  <span className="text-xs font-semibold text-emerald-400 mt-0.5">Sincronizado</span>
                                </div>
                              </div>
                              <Link href="/voltrisoptimizer" className="text-xs font-semibold text-indigo-400 hover:text-indigo-300 flex items-center gap-1 transition-colors">
                                Baixar App <VoltrisIcon name="plus" size={12} />
                              </Link>
                            </div>
                          </div>
                        </motion.div>
                      ))
                    ) : (
                      <div className={`col-span-1 xl:col-span-2 p-12 sm:p-16 rounded-2xl text-center border border-slate-800/80 flex flex-col items-center gap-4 ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/30'}`}>
                        <VoltrisIconTile icon="security" accent={DASHBOARD_ACCENT.brand} size={14} />
                        <div className="space-y-1.5 max-w-md">
                          <h3 className="text-lg font-bold text-white tracking-tight">Nenhuma licença ativa</h3>
                          <p className="text-slate-400 text-xs leading-relaxed">Você ainda não possui licenças ativas vinculadas a este e-mail. Adquira uma licença para desbloquear o Optimizer PRO.</p>
                        </div>
                        <Link href="/adquirir-licenca" className="mt-2">
                          <button className="px-6 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl transition-all shadow-md shadow-indigo-600/20">
                            Explorar Planos PRO
                          </button>
                        </Link>
                      </div>
                    )}
                  </div>
                </motion.div>
              )}

              {activeTab === 'orders' && (
                <motion.div
                  key="orders"
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -10 }}
                  transition={{ duration: 0.2 }}
                  className="space-y-6"
                >
                  <div className={`p-6 sm:p-7 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/50 border-slate-800/80 shadow-sm'}`}>
                    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6 pb-4 border-b border-slate-800/80">
                      <div className="flex items-center gap-3">
                        <VoltrisIconTile icon="package" accent={DASHBOARD_ACCENT.brand} size={10} />
                        <div>
                          <h2 className="text-lg font-bold text-white tracking-tight">Histórico de Pedidos</h2>
                          <p className="text-slate-400 text-xs mt-0.5">Acompanhe todos os seus serviços contratados e licenças</p>
                        </div>
                      </div>
                    </div>

                    <div className="overflow-x-auto">
                      <table className="w-full text-left">
                        <thead>
                          <tr className="border-b border-slate-800/80">
                            <th className="pb-3 px-3 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Item / Serviço</th>
                            <th className="pb-3 px-3 text-[11px] font-semibold text-slate-400 uppercase tracking-wider hidden sm:table-cell">Data</th>
                            <th className="pb-3 px-3 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Valor</th>
                            <th className="pb-3 px-3 text-[11px] font-semibold text-slate-400 uppercase tracking-wider">Status</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-slate-800/60">
                          {/* Mesclagem de Pedidos e Pagamentos */}
                          {[
                            ...orders.map(o => ({ ...o, display_type: 'SERVICE_LEGACY', display_name: o.service_name, display_plan: o.plan_type, amount: o.total || o.final_price })),
                            ...payments.map(p => {
                              const isService = ['formatacao', 'otimizacao', 'correcao', 'impressora', 'virus', 'recuperacao'].some(key => p.plan_type?.includes(key));
                              return {
                                ...p,
                                display_type: isService ? 'SERVICE' : 'LICENSE',
                                display_name: isService ? (p.plan_type?.replace(/_/g, ' ').toUpperCase() || 'SERVIÇO') : `LICENÇA: ${p.plan_type?.toUpperCase()}`,
                                display_plan: isService ? 'SERVIÇO PROFISSIONAL' : 'SUPORTE PRIORITÁRIO',
                                amount: p.amount
                              };
                            })
                          ].sort((a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime()).length > 0 ? (
                            [
                              ...orders.map(o => ({ ...o, display_type: 'SERVICE_LEGACY', display_name: o.service_name, display_plan: o.plan_type, amount: o.total || o.final_price })),
                              ...payments.map(p => {
                                const isService = ['formatacao', 'otimizacao', 'correcao', 'impressora', 'virus', 'recuperacao'].some(key => p.plan_type?.includes(key));
                                return {
                                  ...p,
                                  display_type: isService ? 'SERVICE' : 'LICENSE',
                                  display_name: isService ? (p.plan_type?.replace(/_/g, ' ').toUpperCase() || 'SERVIÇO') : `LICENÇA: ${p.plan_type?.toUpperCase()}`,
                                  display_plan: isService ? 'SERVIÇO PROFISSIONAL' : 'SUPORTE PRIORITÁRIO',
                                  amount: p.amount
                                };
                              })
                            ].sort((a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime()).map((item, idx) => (
                              <tr key={item.id + idx} className="group hover:bg-slate-800/30 transition-colors">
                                <td className="py-4 px-3">
                                  <div className="flex flex-col">
                                    <span className="text-xs font-semibold text-white tracking-tight">{item.display_name}</span>
                                    <span className="text-[10px] font-medium text-slate-400 mt-0.5">
                                      {item.display_type === 'LICENSE' ? (
                                        <span className="text-indigo-400">Produto Digital</span>
                                      ) : item.display_type === 'SERVICE' ? (
                                        <span className="text-blue-400">Serviço Especializado</span>
                                      ) : (
                                        item.display_plan || 'Personalizado'
                                      )}
                                    </span>
                                  </div>
                                </td>
                                <td className="py-4 px-3 hidden sm:table-cell">
                                  <span className="text-xs font-medium text-slate-400">
                                    {new Date(item.created_at).toLocaleDateString('pt-BR')}
                                  </span>
                                </td>
                                <td className="py-4 px-3">
                                  <span className="text-xs font-semibold text-slate-200">
                                    R$ {(item.amount || 0).toLocaleString('pt-BR', { minimumFractionDigits: 2 })}
                                  </span>
                                </td>
                                <td className="py-4 px-3">
                                  <div className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full border text-[11px] font-medium
                                  ${(['completed', 'approved', 'paid', 'active'].includes(item.status?.toLowerCase())) ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400' :
                                      (['cancelled', 'rejected', 'declined', 'canceled', 'refunded'].includes(item.status?.toLowerCase())) ? 'bg-rose-500/10 border-rose-500/20 text-rose-400' :
                                        'bg-amber-500/10 border-amber-500/20 text-amber-400'}
                                `}>
                                    <div className={`w-1.5 h-1.5 rounded-full ${(['completed', 'approved', 'paid', 'active'].includes(item.status?.toLowerCase())) ? 'bg-emerald-400' : (['cancelled', 'rejected', 'declined', 'canceled', 'refunded'].includes(item.status?.toLowerCase())) ? 'bg-rose-400' : 'bg-amber-400'}`}></div>
                                    <span>
                                      {(['completed', 'approved', 'paid', 'active', 'succeeded', 'ativo'].includes(String(item.status).trim().toLowerCase())) ? 'Aprovado' :
                                        (['cancelled', 'rejected', 'declined', 'canceled', 'refunded', 'cancelado'].includes(String(item.status).trim().toLowerCase())) ? 'Cancelado' :
                                          'Pendente'}
                                    </span>
                                  </div>
                                </td>
                              </tr>
                            ))
                          ) : (
                            <tr>
                              <td colSpan={4} className="py-12 text-center">
                                <p className="text-slate-400 text-xs">Nenhum pedido ou pagamento registrado até o momento.</p>
                              </td>
                            </tr>
                          )}
                        </tbody>
                      </table>
                    </div>
                  </div>
                </motion.div>
              )}

              {activeTab === 'pc' && (
                <motion.div
                  key="pc"
                  initial={{ opacity: 0, scale: 0.98 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={{ opacity: 0, scale: 0.98 }}
                  className="h-full"
                >
                  <MyComputerPage userId={user?.id || ''} />
                </motion.div>
              )}

              {activeTab === 'security' && (
                <motion.div
                  key="security"
                  initial={{ opacity: 0, scale: 0.98 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={{ opacity: 0, scale: 0.98 }}
                  className="h-full"
                >
                  <SecurityPage />
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </div>
      )}

      {/* --- MODAL DE CANCELAMENTO PROFISSIONAL --- */}
      <AnimatePresence>
        {isCancelModalOpen && (
          <div className="fixed inset-0 z-[200] flex items-center justify-center p-4">
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setIsCancelModalOpen(false)}
              className="absolute inset-0 bg-black/60 backdrop-blur-md"
            />
            <motion.div
              initial={{ scale: 0.95, opacity: 0, y: 10 }}
              animate={{ scale: 1, opacity: 1, y: 0 }}
              exit={{ scale: 0.95, opacity: 0, y: 10 }}
              className="relative w-full max-w-md bg-slate-900 rounded-2xl p-7 shadow-2xl border border-slate-800 overflow-hidden"
            >
              <div className="relative z-10 flex flex-col items-center text-center">
                <VoltrisIconTile icon="alertTriangle" accent={DASHBOARD_ACCENT.danger} size={12} className="mb-5" />

                <h3 className="text-lg font-bold text-white tracking-tight mb-2">
                  Cancelar Assinatura?
                </h3>

                <p className="text-slate-400 text-xs leading-relaxed mb-6">
                  Ao confirmar, sua renovação automática será interrompida. Você continuará com acesso aos recursos PRO até o término do ciclo atual de faturamento.
                </p>

                <div className="flex flex-col gap-2.5 w-full">
                  <button
                    onClick={handleConfirmCancel}
                    disabled={isCancelling}
                    className="w-full py-2.5 bg-rose-500 hover:bg-rose-600 disabled:opacity-50 text-white font-semibold text-xs rounded-xl transition-all shadow-sm"
                  >
                    {isCancelling ? 'Processando...' : 'Confirmar Cancelamento'}
                  </button>
                  <button
                    onClick={() => setIsCancelModalOpen(false)}
                    className="w-full py-2.5 bg-slate-800 hover:bg-slate-700/80 border border-slate-700/70 text-slate-300 font-medium text-xs rounded-xl transition-all"
                  >
                    Manter minha assinatura
                  </button>
                </div>
              </div>
            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </AuthGuard>
  );
}

