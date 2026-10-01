'use client';

import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { createClient } from '@/utils/supabase/client';
import { FiChevronUp, FiChevronDown, FiFilter, FiTool } from 'react-icons/fi';

interface ServiceRequest {
  id: string;
  created_at: string;
  status: string;
  requested_services: unknown[];
  scheduling_type: string;
  requested_datetime?: string;
  admin_notes?: string;
  final_price: number;
  order_id?: string;
  profiles?: {
    full_name: string;
    email: string;
  };
}

export default function AdminServiceRequestsTab() {
  const [serviceRequests, setServiceRequests] = useState<ServiceRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('all');
  const [sortBy, setSortBy] = useState('date');
  const [sortOrder, setSortOrder] = useState('desc');
  const supabase = createClient();

  useEffect(() => {
    const fetchServiceRequests = async () => {
      try {
        const { data, error } = await supabase
          .from('service_requests')
          .select(`*, profiles:user_id (full_name, email)`)
          .order('created_at', { ascending: false });
        if (error) throw error;
        setServiceRequests(data || []);
      } catch {}
      finally { setLoading(false); }
    };
    fetchServiceRequests();
  }, [supabase]);

  const filteredServiceRequests = serviceRequests.filter(r => filter === 'all' || r.status === filter);
  const sortedServiceRequests = [...filteredServiceRequests].sort((a, b) => {
    if (sortBy === 'date') return sortOrder === 'desc'
      ? new Date(b.created_at).getTime() - new Date(a.created_at).getTime()
      : new Date(a.created_at).getTime() - new Date(b.created_at).getTime();
    if (sortBy === 'total') return sortOrder === 'desc' ? b.final_price - a.final_price : a.final_price - b.final_price;
    return 0;
  });

  const updateServiceRequestStatus = async (requestId: string, newStatus: string) => {
    try {
      const { error } = await supabase.from('service_requests').update({ status: newStatus }).eq('id', requestId);
      if (error) throw error;
      setServiceRequests(serviceRequests.map(r => r.id === requestId ? { ...r, status: newStatus } : r));
    } catch {}
  };

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'completed':  return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20';
      case 'pending':    return 'bg-amber-500/10 text-amber-400 border-amber-500/20';
      case 'scheduled':  return 'bg-indigo-500/10 text-indigo-400 border-indigo-500/20';
      case 'cancelled':  return 'bg-rose-500/10 text-rose-400 border-rose-500/20';
      default:           return 'bg-slate-500/10 text-slate-400 border-slate-500/20';
    }
  };

  const getStatusText = (status: string) => {
    switch (status) {
      case 'completed': return 'Concluído';
      case 'pending':   return 'Pendente';
      case 'scheduled': return 'Agendado';
      case 'cancelled': return 'Cancelado';
      default:          return 'Desconhecido';
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="w-8 h-8 border-2 border-indigo-500 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-bold text-slate-100">Solicitações Técnicas</h1>
          <p className="text-xs text-slate-500 mt-0.5">{serviceRequests.length} solicitação(ões) no sistema</p>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <FiFilter className="text-slate-500 w-3.5 h-3.5" />
          <select
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            className="bg-slate-900 border border-slate-800 rounded-xl px-3 py-2 text-sm text-slate-300 focus:outline-none focus:border-indigo-500/50 transition"
          >
            <option value="all">Todos os Status</option>
            <option value="completed">Concluídos</option>
            <option value="pending">Pendentes</option>
            <option value="scheduled">Agendados</option>
            <option value="cancelled">Cancelados</option>
          </select>
        </div>
        <select
          value={sortBy}
          onChange={(e) => setSortBy(e.target.value)}
          className="bg-slate-900 border border-slate-800 rounded-xl px-3 py-2 text-sm text-slate-300 focus:outline-none focus:border-indigo-500/50 transition"
        >
          <option value="date">Ordenar por Data</option>
          <option value="total">Ordenar por Valor</option>
        </select>
        <button
          onClick={() => setSortOrder(sortOrder === 'desc' ? 'asc' : 'desc')}
          className="flex items-center gap-1.5 px-3 py-2 bg-slate-900 border border-slate-800 hover:bg-slate-800 rounded-xl text-slate-400 transition text-xs"
        >
          {sortOrder === 'desc' ? <FiChevronDown className="w-3.5 h-3.5" /> : <FiChevronUp className="w-3.5 h-3.5" />}
          {sortOrder === 'desc' ? 'Decrescente' : 'Crescente'}
        </button>
      </div>

      {/* Table */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden">
        <div className="overflow-x-auto">
          {loading ? (
            <div className="p-6 text-center text-slate-500 text-sm">Carregando...</div>
          ) : sortedServiceRequests.length === 0 ? (
            <div className="p-12 text-center">
              <div className="w-12 h-12 mx-auto mb-3 bg-slate-800 rounded-xl flex items-center justify-center">
                <FiTool className="w-5 h-5 text-slate-600" />
              </div>
              <p className="text-sm text-slate-500">Nenhuma solicitação encontrada</p>
            </div>
          ) : (
            <table className="min-w-full">
              <thead>
                <tr className="border-b border-slate-800 bg-slate-800/30">
                  {['ID', 'Cliente', 'Status', 'Agendamento', 'Data', 'Total', 'Serviços', 'Ações'].map(h => (
                    <th key={h} className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest whitespace-nowrap">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {sortedServiceRequests.map((request, index) => (
                  <motion.tr
                    key={request.id}
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    transition={{ delay: index * 0.04 }}
                    className="border-b border-slate-800/50 hover:bg-slate-800/20 transition"
                  >
                    <td className="px-4 py-3 text-xs font-mono text-slate-500">#{request.id.slice(0, 8)}</td>
                    <td className="px-4 py-3">
                      <p className="text-sm text-slate-200 font-medium">{request.profiles?.full_name || '—'}</p>
                      <p className="text-[10px] text-slate-600">{request.profiles?.email || '—'}</p>
                    </td>
                    <td className="px-4 py-3">
                      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${getStatusBadge(request.status)}`}>
                        {getStatusText(request.status)}
                      </span>
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-400">
                      {request.scheduling_type === 'now' ? 'Imediato' : 'Agendado'}
                      {request.requested_datetime && (
                        <div className="text-[10px] text-slate-600">{new Date(request.requested_datetime).toLocaleDateString('pt-BR')}</div>
                      )}
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-500 whitespace-nowrap">
                      {new Date(request.created_at).toLocaleDateString('pt-BR')}
                    </td>
                    <td className="px-4 py-3 text-sm font-semibold text-slate-200 whitespace-nowrap">
                      R$ {request.final_price.toFixed(2)}
                    </td>
                    <td className="px-4 py-3 text-xs text-slate-500">
                      <span className="text-slate-400">{request.requested_services?.length || 0} serviço(s)</span>
                      {request.requested_services?.slice(0, 2).map((service: unknown, i: number) => (
                        <div key={i} className="text-[10px] text-slate-600">• {(service as any).service_name || 'Serviço'}</div>
                      ))}
                      {(request.requested_services?.length || 0) > 2 && (
                        <div className="text-[10px] text-slate-700">+{request.requested_services!.length - 2} mais</div>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <select
                        value={request.status}
                        onChange={(e) => updateServiceRequestStatus(request.id, e.target.value)}
                        className="bg-slate-800 border border-slate-700 rounded-lg px-2 py-1.5 text-xs text-slate-300 focus:outline-none focus:border-indigo-500/50 transition"
                      >
                        <option value="pending">Pendente</option>
                        <option value="scheduled">Agendado</option>
                        <option value="completed">Concluído</option>
                        <option value="cancelled">Cancelado</option>
                      </select>
                    </td>
                  </motion.tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
}
