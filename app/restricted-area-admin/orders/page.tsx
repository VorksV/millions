'use client';

import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { createClient } from '@/utils/supabase/client';
import { FiSearch, FiFilter, FiEye, FiCheckCircle, FiClock, FiXCircle, FiPackage } from 'react-icons/fi';
import { toast } from 'react-hot-toast';
import type { Order } from '@/types/order';

interface OrderWithUser extends Order {
  profiles?: {
    full_name: string;
    email: string;
  };
}

export default function AdminOrdersPage() {
  const [orders, setOrders] = useState<OrderWithUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedOrder, setSelectedOrder] = useState<OrderWithUser | null>(null);
  const [showOrderModal, setShowOrderModal] = useState(false);
  const supabase = createClient();

  useEffect(() => { fetchOrders(); }, []);

  const fetchOrders = async () => {
    try {
      setLoading(true);
      const { data, error } = await supabase
        .from('orders')
        .select(`*, profiles:user_id (full_name, email)`)
        .order('created_at', { ascending: false });
      if (error) throw error;
      setOrders(data || []);
    } catch (error) {
      console.error('Erro ao buscar pedidos:', error);
      toast.error('Erro ao carregar pedidos');
    } finally {
      setLoading(false);
    }
  };

  const handleStatusChange = async (orderId: string, newStatus: Order['status']) => {
    try {
      let updateObj: any = { status: newStatus };
      if (newStatus === 'cancelled') updateObj.cancelled_by = 'admin';
      const { error } = await supabase.from('orders').update(updateObj).eq('id', orderId);
      if (error) throw error;
      toast.success('Status atualizado!');
      fetchOrders();
    } catch {
      toast.error('Erro ao atualizar status do pedido');
    }
  };

  const filteredOrders = orders.filter(order => {
    const matchesFilter = filter === 'all' || order.status === filter;
    const matchesSearch =
      order.id.toLowerCase().includes(searchTerm.toLowerCase()) ||
      order.profiles?.full_name?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      order.profiles?.email?.toLowerCase().includes(searchTerm.toLowerCase());
    return matchesFilter && matchesSearch;
  });

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'completed':  return { cls: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20', icon: <FiCheckCircle className="w-3 h-3" /> };
      case 'pending':    return { cls: 'bg-amber-500/10 text-amber-400 border-amber-500/20', icon: <FiClock className="w-3 h-3" /> };
      case 'processing': return { cls: 'bg-indigo-500/10 text-indigo-400 border-indigo-500/20', icon: <FiClock className="w-3 h-3" /> };
      case 'cancelled':  return { cls: 'bg-rose-500/10 text-rose-400 border-rose-500/20', icon: <FiXCircle className="w-3 h-3" /> };
      default:           return { cls: 'bg-slate-500/10 text-slate-400 border-slate-500/20', icon: <FiClock className="w-3 h-3" /> };
    }
  };

  const getStatusText = (status: string, cancelled_by?: string) => {
    if (status === 'cancelled') {
      if (cancelled_by === 'client') return 'Cancelado pelo Cliente';
      if (cancelled_by === 'admin') return 'Cancelado pelo Admin';
      return 'Cancelado';
    }
    switch (status) {
      case 'completed': return 'Concluído';
      case 'pending':   return 'Pendente';
      case 'processing': return 'Em Processamento';
      default: return 'Desconhecido';
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-slate-950">
        <div className="w-8 h-8 border-2 border-indigo-500 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-950 text-white p-6 space-y-5">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-bold text-slate-100">Gerenciar Pedidos</h1>
          <p className="text-xs text-slate-500 mt-0.5">{orders.length} pedido(s) no sistema</p>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-1.5 h-1.5 bg-emerald-500 rounded-full" />
          <span className="text-xs text-slate-500">Total: {orders.length}</span>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="flex-1 relative">
          <FiSearch className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-500 w-3.5 h-3.5" />
          <input
            type="text"
            placeholder="Buscar por ID, nome ou email do cliente..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-9 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 placeholder-slate-600 focus:outline-none focus:border-indigo-500/50 transition"
          />
        </div>
        <div className="flex items-center gap-2">
          <FiFilter className="text-slate-500 w-3.5 h-3.5 flex-shrink-0" />
          <select
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            className="bg-slate-900 border border-slate-800 rounded-xl px-3 py-2 text-sm text-slate-300 focus:outline-none focus:border-indigo-500/50 transition min-w-[150px]"
          >
            <option value="all">Todos os Status</option>
            <option value="pending">Pendentes</option>
            <option value="processing">Em Processamento</option>
            <option value="completed">Concluídos</option>
            <option value="cancelled">Cancelados</option>
          </select>
        </div>
      </div>

      {/* Orders */}
      <div className="space-y-3">
        {filteredOrders.length === 0 ? (
          <div className="text-center py-16 bg-slate-900 border border-slate-800 rounded-2xl">
            <div className="w-12 h-12 mx-auto mb-3 bg-slate-800 rounded-xl flex items-center justify-center">
              <FiPackage className="w-5 h-5 text-slate-600" />
            </div>
            <p className="text-sm text-slate-500">Nenhum pedido encontrado</p>
          </div>
        ) : (
          filteredOrders.map((order, index) => {
            const badge = getStatusBadge(order.status);
            return (
              <motion.div
                key={order.id}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.2, delay: index * 0.04 }}
                className="bg-slate-900 border border-slate-800 rounded-xl p-4 hover:border-slate-700 transition-all"
              >
                <div className="flex flex-col lg:flex-row justify-between items-start lg:items-center gap-4">
                  <div className="flex-1 min-w-0">
                    <div className="flex flex-wrap items-center gap-2 mb-2">
                      <span className="text-[10px] font-mono text-slate-600 bg-slate-800 px-2 py-0.5 rounded">#{order.id.slice(0, 8)}</span>
                      <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${badge.cls}`}>
                        {badge.icon}
                        {getStatusText(order.status, (order as any).cancelled_by)}
                      </span>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-1 text-xs">
                      <div className="flex gap-2"><span className="text-slate-600">Cliente:</span><span className="text-slate-300">{order.profiles?.full_name || '—'}</span></div>
                      <div className="flex gap-2"><span className="text-slate-600">Email:</span><span className="text-slate-300 truncate">{order.profiles?.email || '—'}</span></div>
                      <div className="flex gap-2"><span className="text-slate-600">Data:</span><span className="text-slate-300">{new Date(order.created_at).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })}</span></div>
                      <div className="flex gap-2"><span className="text-slate-600">Itens:</span><span className="text-slate-300">{order.items?.length || 0} item(s)</span></div>
                    </div>
                  </div>
                  <div className="flex items-center gap-3 flex-shrink-0">
                    <p className="text-base font-bold text-slate-100">R$ {order.total.toFixed(2)}</p>
                    <button
                      onClick={() => { setSelectedOrder(order); setShowOrderModal(true); }}
                      className="flex items-center gap-1.5 px-3 py-1.5 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-400 border border-indigo-500/20 rounded-lg transition text-xs font-medium"
                    >
                      <FiEye className="w-3.5 h-3.5" />
                      Detalhes
                    </button>
                    <select
                      value={order.status}
                      onChange={(e) => handleStatusChange(order.id, e.target.value as Order['status'])}
                      className="bg-slate-800 border border-slate-700 rounded-lg px-2 py-1.5 text-slate-300 focus:outline-none text-xs transition"
                    >
                      <option value="pending">Pendente</option>
                      <option value="processing">Em Processamento</option>
                      <option value="completed">Concluído</option>
                      <option value="cancelled">Cancelado</option>
                    </select>
                  </div>
                </div>
              </motion.div>
            );
          })
        )}
      </div>

      {/* Modal */}
      {showOrderModal && selectedOrder && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <motion.div
            initial={{ opacity: 0, scale: 0.96 }}
            animate={{ opacity: 1, scale: 1 }}
            className="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-2xl w-full max-h-[80vh] overflow-y-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
          >
            <div className="flex justify-between items-center mb-5">
              <h2 className="text-base font-bold text-slate-100">Detalhes do Pedido</h2>
              <button onClick={() => setShowOrderModal(false)} className="p-1.5 rounded-lg text-slate-500 hover:text-white hover:bg-slate-800 transition">
                <FiXCircle className="w-5 h-5" />
              </button>
            </div>
            <div className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {[
                  { label: 'ID', value: <span className="font-mono text-[11px]">{selectedOrder.id}</span> },
                  { label: 'Status', value: (() => { const b = getStatusBadge(selectedOrder.status); return <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${b.cls}`}>{b.icon}{getStatusText(selectedOrder.status)}</span>; })() },
                  { label: 'Cliente', value: selectedOrder.profiles?.full_name || '—' },
                  { label: 'Email', value: selectedOrder.profiles?.email || '—' },
                  { label: 'Data', value: new Date(selectedOrder.created_at).toLocaleDateString('pt-BR') },
                  { label: 'Total', value: <span className="font-bold text-slate-100">R$ {selectedOrder.total.toFixed(2)}</span> },
                ].map(({ label, value }) => (
                  <div key={label} className="bg-slate-800/50 rounded-xl p-3">
                    <p className="text-[10px] text-slate-600 uppercase tracking-wider mb-1">{label}</p>
                    <div className="text-sm text-slate-300">{value}</div>
                  </div>
                ))}
              </div>
              {selectedOrder.items && selectedOrder.items.length > 0 && (
                <div>
                  <p className="text-[10px] text-slate-600 uppercase tracking-wider mb-2">Itens</p>
                  <div className="space-y-2">
                    {selectedOrder.items.map((item: any, i: number) => (
                      <div key={i} className="bg-slate-800/50 rounded-xl p-3">
                        <p className="text-sm font-medium text-slate-200">{item.service_name}</p>
                        {item.service_description && <p className="text-xs text-slate-500 mt-0.5">{item.service_description}</p>}
                        <p className="text-sm font-semibold text-indigo-400 mt-1">R$ {item.price?.toFixed(2) || '0.00'}</p>
                      </div>
                    ))}
                  </div>
                </div>
              )}
              {selectedOrder.notes && (
                <div className="bg-slate-800/50 rounded-xl p-3">
                  <p className="text-[10px] text-slate-600 uppercase tracking-wider mb-1">Informações Adicionais</p>
                  <p className="text-sm text-slate-300 whitespace-pre-line">{selectedOrder.notes}</p>
                </div>
              )}
            </div>
          </motion.div>
        </div>
      )}
    </div>
  );
}
