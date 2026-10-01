'use client';

import { useState, useEffect, useMemo } from 'react';
import { useRouter } from 'next/navigation';
import { createClient } from '@/utils/supabase/client';
import { motion, AnimatePresence } from 'framer-motion';
import { toast } from 'react-hot-toast';
import { useDashboard } from '@/app/context/DashboardContext';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';

interface Service {
  id: string;
  name: string;
  description: string;
  price: number;
}

const MOCK_SERVICES: Service[] = [
  { id: 'formatacao_basica', name: 'Formatação Básica', description: 'Backup, formatação limpa, instalação de drivers e atualizações essenciais.', price: 99.90 },
  { id: 'formatacao_media', name: 'Formatação Média', description: 'Inclui Básica + pacote antivírus e otimização inicial de inicialização.', price: 149.90 },
  { id: 'formatacao_avancada', name: 'Formatação Avançada', description: 'Inclui Média + otimização profunda de performance e redução de latência.', price: 199.90 },
  { id: 'formatacao_corporativa', name: 'Formatação Corporativa', description: 'Para ambientes de trabalho, incluindo configuração de rede, backup e Office.', price: 349.90 },
  { id: 'formatacao_gamer', name: 'Formatação Gamer Extrema', description: 'Otimização máxima de FPS, input lag zero, drivers customizados e Windows stripped.', price: 449.90 },
  { id: 'otimizacao_basica', name: 'Otimização Básica', description: 'Atualização de drivers, limpeza de arquivos temporários e correção de erros comuns.', price: 79.90 },
  { id: 'otimizacao_media', name: 'Otimização Média', description: 'Inclui Básica + desativação de telemetrias inúteis e serviços em segundo plano.', price: 99.90 },
  { id: 'otimizacao_avancada', name: 'Otimização Avançada PRO', description: 'Tweaks de registro, ajustes de rede, TCP/IP, agendador de tarefas e latência.', price: 149.90 },
  { id: 'correcao_windows', name: 'Correção de Erros Windows', description: 'Diagnóstico e reparo de tela azul, DLLs corrompidas e falhas de inicialização.', price: 49.90 },
  { id: 'impressora_basica', name: 'Instalação de Impressora / Periféricos', description: 'Instalação remota de drivers, compartilhamento de rede e testes.', price: 49.90 },
  { id: 'virus_basica', name: 'Remoção de Vírus e Malware', description: 'Varredura profunda, expurgo de spywares, trojans e adware intrusivos.', price: 39.90 },
  { id: 'recuperacao_basica', name: 'Recuperação de Arquivos Básica', description: 'Recuperação de fotos e documentos deletados por engano recentemente.', price: 100.00 },
];

interface OrderItem {
  service_id: string;
  quantity: number;
  price: number;
}

export default function NewOrderClient() {
  const { transparencyMode } = useDashboard();
  const [services, setServices] = useState<Service[]>([]);
  const [selectedServices, setSelectedServices] = useState<OrderItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const router = useRouter();
  const supabase = useMemo(() => createClient(), []);

  useEffect(() => {
    fetchServices();
  }, []);

  const fetchServices = async () => {
    try {
      const { data, error } = await supabase
        .from('services')
        .select('*')
        .order('name');

      if (error || !data || data.length === 0) {
        setServices(MOCK_SERVICES);
      } else {
        setServices(data);
      }
    } catch {
      setServices(MOCK_SERVICES);
    } finally {
      setLoading(false);
    }
  };

  const addService = (service: Service) => {
    const existingItem = selectedServices.find(item => item.service_id === service.id);

    if (existingItem) {
      setSelectedServices(prev => prev.map(item =>
        item.service_id === service.id
          ? { ...item, quantity: item.quantity + 1 }
          : item
      ));
    } else {
      setSelectedServices(prev => [...prev, {
        service_id: service.id,
        quantity: 1,
        price: service.price
      }]);
    }
    toast.success(`${service.name} adicionado ao pedido`);
  };

  const removeService = (serviceId: string) => {
    setSelectedServices(prev => prev.filter(item => item.service_id !== serviceId));
  };

  const updateQuantity = (serviceId: string, quantity: number) => {
    if (quantity <= 0) {
      removeService(serviceId);
      return;
    }
    setSelectedServices(prev => prev.map(item =>
      item.service_id === serviceId
        ? { ...item, quantity }
        : item
    ));
  };

  const calculateTotal = () => {
    return selectedServices.reduce((total, item) => total + (item.price * item.quantity), 0);
  };

  const handleSubmit = async () => {
    if (selectedServices.length === 0) {
      toast.error('Selecione pelo menos um serviço');
      return;
    }

    setSubmitting(true);
    try {
      const response = await fetch('/api/orders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          items: selectedServices.map(item => ({
            service_id: item.service_id,
            quantity: item.quantity,
          })),
        }),
      });

      const data = await response.json();
      if (!response.ok) throw new Error(data.error || 'Erro ao criar pedido');

      toast.success('Pedido criado com sucesso!');
      router.push('/dashboard?tab=orders');
    } catch (error: any) {
      toast.error(error.message || 'Erro ao processar pedido');
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center py-32 gap-3 text-slate-400">
        <div className="w-8 h-8 rounded-full border-2 border-slate-700 border-t-indigo-500 animate-spin"></div>
        <p className="text-xs font-medium text-slate-400">Carregando catálogo de serviços...</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6 w-full max-w-full">
      
      {/* Header */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 pb-4 border-b border-slate-800/80">
        <div>
          <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight">Novos Pedidos & Serviços</h2>
          <p className="text-xs text-slate-400 mt-0.5">Selecione os módulos técnicos ou formatações especializadas para contratação</p>
        </div>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-12 gap-6 items-start">
        
        {/* Services Market */}
        <div className="xl:col-span-7 flex flex-col gap-4">
          <div className="flex items-center justify-between">
            <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Serviços Disponíveis</span>
            <span className="text-[11px] text-slate-500">{services.length} opções disponíveis</span>
          </div>
          
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            {services.map((service) => (
              <div
                key={service.id}
                className={`p-5 rounded-2xl border transition-all duration-200 flex flex-col justify-between gap-4 group
                  ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-sm'} 
                  hover:border-slate-700 hover:bg-slate-900/80
                `}
              >
                <div className="space-y-3">
                   <div className="flex justify-between items-start gap-2">
                      <VoltrisIconTile icon="package" accent={DASHBOARD_ACCENT.brand} size={9} />
                      <span className="text-base font-bold text-white tracking-tight">R$ {service.price.toFixed(2)}</span>
                   </div>
                   
                   <div>
                     <h4 className="text-sm font-bold text-white tracking-tight group-hover:text-indigo-300 transition-colors line-clamp-1">{service.name}</h4>
                     <p className="text-xs text-slate-400 mt-1 line-clamp-2 leading-relaxed">{service.description}</p>
                   </div>
                </div>

                <button
                  type="button"
                  onClick={() => addService(service)}
                  className="w-full py-2 px-3 rounded-xl bg-slate-950/70 hover:bg-indigo-600 border border-slate-800 hover:border-indigo-500 text-slate-200 hover:text-white font-semibold text-xs transition-all flex items-center justify-center gap-1.5 active:scale-98 shadow-sm"
                >
                  <VoltrisIcon name="plus" size={14} />
                  <span>Adicionar ao Pedido</span>
                </button>
              </div>
            ))}
          </div>
        </div>

        {/* Tactical Cart / Checkout */}
        <div className="xl:col-span-5 sticky top-6 flex flex-col gap-4">
          <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Resumo da Contratação</span>

          <div className={`p-6 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'}`}>
            <AnimatePresence mode="popLayout">
              {selectedServices.length === 0 ? (
                <div className="py-14 flex flex-col items-center text-center gap-3 text-slate-500">
                  <VoltrisIconTile icon="package" accent={DASHBOARD_ACCENT.brand} size={12} />
                  <div className="space-y-1">
                    <p className="text-sm font-bold text-white">Nenhum serviço selecionado</p>
                    <p className="text-xs text-slate-400">Escolha os módulos ao lado para montar seu pedido.</p>
                  </div>
                </div>
              ) : (
                <div className="space-y-4">
                  <div className="space-y-2.5 max-h-72 overflow-y-auto [scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden pr-1">
                    {selectedServices.map((item) => {
                      const service = services.find(s => s.id === item.service_id);
                      if (!service) return null;
                      return (
                        <div
                          key={item.service_id}
                          className="p-3 rounded-xl bg-slate-950/50 border border-slate-800 flex items-center justify-between gap-3"
                        >
                          <div className="min-w-0 flex-1">
                            <h5 className="text-xs font-semibold text-white truncate">{service.name}</h5>
                            <span className="text-[11px] font-semibold text-indigo-400">R$ {item.price.toFixed(2)}</span>
                          </div>

                          <div className="flex items-center gap-2">
                             <div className="flex items-center gap-1.5 p-1 bg-slate-900 rounded-lg border border-slate-800 text-xs">
                                <button type="button" onClick={() => updateQuantity(item.service_id, item.quantity - 1)} className="w-5 h-5 rounded flex items-center justify-center text-slate-400 hover:text-white hover:bg-slate-800 transition-colors">-</button>
                                <span className="w-4 text-center text-xs font-bold text-white">{item.quantity}</span>
                                <button type="button" onClick={() => updateQuantity(item.service_id, item.quantity + 1)} className="w-5 h-5 rounded flex items-center justify-center text-slate-400 hover:text-white hover:bg-slate-800 transition-colors">+</button>
                             </div>
                             <button type="button" onClick={() => removeService(item.service_id)} className="p-1.5 text-slate-500 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors">
                                <VoltrisIcon name="trash" size={14} />
                             </button>
                          </div>
                        </div>
                      );
                    })}
                  </div>

                  <div className="pt-4 border-t border-slate-800/80 space-y-4">
                    <div className="flex justify-between items-baseline">
                       <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">Total</span>
                       <span className="text-2xl font-bold text-white">R$ {calculateTotal().toFixed(2)}</span>
                    </div>

                    <div className="p-3 rounded-xl bg-emerald-500/5 border border-emerald-500/20 flex items-start gap-2.5">
                       <VoltrisIcon name="info" size={16} className="text-emerald-400 shrink-0 mt-0.5" />
                       <p className="text-[11px] text-emerald-400 leading-relaxed">Seu atendimento e credenciais serão disponibilizados imediatamente após a confirmação.</p>
                    </div>

                    <button
                      type="button"
                      onClick={handleSubmit}
                      disabled={submitting || selectedServices.length === 0}
                      className="w-full py-3 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all flex items-center justify-center gap-2 disabled:opacity-50 active:scale-98"
                    >
                      {submitting ? (
                        <VoltrisIcon name="processing" size={16} className="animate-spin" />
                      ) : (
                        <>
                          <span>Finalizar e Contratar</span>
                          <VoltrisIcon name="arrowRight" size={16} />
                        </>
                      )}
                    </button>
                  </div>
                </div>
              )}
            </AnimatePresence>
          </div>
        </div>

      </div>
    </div>
  );
}
