'use client';

import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import Link from 'next/link';
import type { Order } from '@/app/types/order';
import { supabase } from '@/app/lib/supabase/client';

interface OrderDetails {
  order: Order;
  service?: any;
}

export default function RecentOrders() {
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedOrder, setSelectedOrder] = useState<OrderDetails | null>(null);
  const [showOrderModal, setShowOrderModal] = useState(false);

  useEffect(() => {
    const fetchRecentOrders = async () => {
      try {
        const { data: { user } } = await supabase.auth.getUser();
        if (!user) return;

        const { data, error } = await supabase
          .from('orders')
          .select('*')
          .eq('user_id', user.id)
          .order('created_at', { ascending: false })
          .limit(5);

        if (error) throw error;
        setOrders(data || []);
      } catch (err) {
        console.error('Erro ao carregar pedidos recentes:', err);
      } finally {
        setLoading(false);
      }
    };

    fetchRecentOrders();
  }, []);

  const handleViewOrderDetails = (order: Order) => {
    setSelectedOrder({ order });
    setShowOrderModal(true);
  };

  const getStatusIcon = (status: string) => {
    switch (status) {
      case 'completed':
        return <VoltrisIcon name="success" size={16} className="text-[#00FF94]" />;
      case 'pending':
        return <VoltrisIcon name="clock" size={16} className="text-[#F59E0B]" />;
      case 'processing':
        return <VoltrisIcon name="clock" size={16} className="text-[#00D4FF]" />;
      case 'cancelled':
        return <VoltrisIcon name="alertCircle" size={16} className="text-[#EF4444]" />;
      default:
        return <VoltrisIcon name="clock" size={16} className="text-gray-500" />;
    }
  };

  const getStatusText = (status: string) => {
    switch (status) {
      case 'completed':
        return 'Concluído';
      case 'pending':
        return 'Pendente';
      case 'processing':
        return 'Em Processamento';
      case 'cancelled':
        return 'Cancelado';
      default:
        return 'Desconhecido';
    }
  };

  const getStatusColor = (status: string) => {
    switch (status) {
      case 'completed':
        return 'bg-green-500/10 text-green-400 border-green-500/30';
      case 'pending':
        return 'bg-yellow-500/10 text-yellow-400 border-yellow-500/30';
      case 'processing':
        return 'bg-blue-500/10 text-blue-400 border-blue-500/30';
      case 'cancelled':
        return 'bg-red-500/10 text-red-400 border-red-500/30';
      default:
        return 'bg-gray-500/10 text-gray-400 border-gray-500/30';
    }
  };

  if (loading) {
    return (
      <div className="bg-[#1E1E1E]/40 backdrop-blur-xl p-6 rounded-2xl border border-gray-800/30">
        <div className="flex items-center justify-between mb-6">
          <div>
            <h3 className="text-xl font-bold text-white">Pedidos Recentes</h3>
            <p className="text-gray-400 text-sm">Carregando...</p>
          </div>
        </div>
        <div className="space-y-4">
          {[...Array(3)].map((_, i) => (
            <div key={i} className="animate-pulse">
              <div className="h-16 bg-gray-700/30 rounded-lg"></div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="bg-[#1E1E1E]/40 backdrop-blur-xl p-6 rounded-2xl border border-gray-800/30">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h3 className="text-xl font-bold text-white">Pedidos Recentes</h3>
          <p className="text-gray-400 text-sm">Seus últimos pedidos e serviços</p>
        </div>
        <Link
          href="/dashboard/orders"
          className="flex items-center gap-2 px-4 py-2 bg-[#8B31FF]/10 text-[#8B31FF] rounded-lg hover:bg-[#8B31FF]/20 transition-colors duration-300"
        >
          Ver Todos
          <VoltrisIcon name="arrowRight" size={16} />
        </Link>
      </div>

      {orders.length === 0 ? (
        <div className="text-center py-8">
          <div className="w-12 h-12 mx-auto mb-3 rounded-full bg-gray-500/10 flex items-center justify-center">
            <svg className="w-6 h-6 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
            </svg>
          </div>
          <p className="text-gray-400 text-sm">Nenhum pedido encontrado</p>
          <p className="text-gray-500 text-xs mt-1">Faça seu primeiro pedido para começar</p>
        </div>
      ) : (
        <div className="space-y-4">
          {orders.map((order) => (
            <motion.div
              key={order.id}
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="flex items-center justify-between p-4 bg-[#1E1E1E]/40 rounded-xl border border-gray-800/30 hover:border-[#8B31FF]/30 transition-all duration-300"
            >
              <div className="flex-1">
                <div className="flex items-center gap-3 mb-2">
                  <h4 className="text-white font-medium">Pedido #{order.id.slice(0, 8)}</h4>
                  <span className={`px-2 py-1 rounded-full text-xs font-medium border ${getStatusColor(order.status)}`}>
                    {getStatusIcon(order.status)}
                    <span className="ml-1">{getStatusText(order.status)}</span>
                  </span>
                </div>
                <p className="text-sm text-gray-400 mb-1">
                  {new Date(order.created_at).toLocaleDateString('pt-BR')}
                </p>
                {order.items && order.items[0]?.service_name && (
                  <p className="text-white text-sm">{order.items[0].service_name}</p>
                )}
              </div>
              <div className="flex items-center gap-4">
                <div className="text-right">
                  <p className="text-[#8B31FF] font-bold">
                    R$ {order.total?.toFixed(2) || '0,00'}
                  </p>
                </div>
                <button
                  onClick={() => handleViewOrderDetails(order)}
                  className="flex items-center gap-2 px-3 py-2 bg-[#8B31FF]/10 text-[#8B31FF] rounded-lg hover:bg-[#8B31FF]/20 transition-colors duration-300"
                >
                  <VoltrisIcon name="eye" size={16} />
                  Ver
                </button>
              </div>
            </motion.div>
          ))}
        </div>
      )}

      {/* Order Details Modal */}
      <AnimatePresence>
        {showOrderModal && selectedOrder && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 bg-black/50 backdrop-blur-sm z-50 flex items-center justify-center p-4"
            onClick={() => setShowOrderModal(false)}
          >
            <motion.div
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.9 }}
              className="bg-[#1E1E1E] rounded-2xl p-6 max-w-2xl w-full max-h-[90vh] overflow-y-auto border border-gray-800/30"
              onClick={(e) => e.stopPropagation()}
            >
              <div className="flex justify-between items-start mb-6">
                <h2 className="text-2xl font-bold text-white">Detalhes do Pedido</h2>
                <button
                  onClick={() => setShowOrderModal(false)}
                  className="text-gray-400 hover:text-white transition-colors duration-300"
                >
                  <VoltrisIcon name="close" size={24} />
                </button>
              </div>

              <div className="space-y-6">
                {/* Order Info */}
                <div className="bg-[#171313] rounded-xl p-4">
                  <h3 className="text-lg font-semibold text-white mb-3">Informações do Pedido</h3>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div>
                      <p className="text-gray-400 text-sm">ID do Pedido</p>
                      <p className="text-white font-medium">#{selectedOrder.order.id}</p>
                    </div>
                    <div>
                      <p className="text-gray-400 text-sm">Data de Criação</p>
                      <p className="text-white font-medium">
                        {new Date(selectedOrder.order.created_at).toLocaleDateString('pt-BR')} às{' '}
                        {new Date(selectedOrder.order.created_at).toLocaleTimeString('pt-BR')}
                      </p>
                    </div>
                    <div>
                      <p className="text-gray-400 text-sm">Status</p>
                      <div className="flex items-center gap-2">
                        {getStatusIcon(selectedOrder.order.status)}
                        <span className="text-white font-medium">
                          {getStatusText(selectedOrder.order.status)}
                        </span>
                      </div>
                    </div>
                    <div>
                      <p className="text-gray-400 text-sm">Valor Total</p>
                      <p className="text-[#8B31FF] font-bold text-lg">
                        R$ {selectedOrder.order.total?.toFixed(2) || '0,00'}
                      </p>
                    </div>
                  </div>
                </div>

                {/* Service Info */}
                {selectedOrder.order.items && selectedOrder.order.items[0] && (
                  <div className="bg-[#171313] rounded-xl p-4">
                    <h3 className="text-lg font-semibold text-white mb-3">Detalhes do Serviço</h3>
                    <div className="space-y-3">
                      <div>
                        <p className="text-gray-400 text-sm">Nome do Serviço</p>
                        <p className="text-white font-medium">{selectedOrder.order.items[0].service_name}</p>
                      </div>
                      {selectedOrder.order.items[0].service_description && (
                        <div>
                          <p className="text-gray-400 text-sm">Descrição</p>
                          <p className="text-gray-300">{selectedOrder.order.items[0].service_description}</p>
                        </div>
                      )}
                      {selectedOrder.order.items[0].plan_type && (
                        <div>
                          <p className="text-gray-400 text-sm">Tipo de Plano</p>
                          <p className="text-white font-medium capitalize">{selectedOrder.order.items[0].plan_type}</p>
                        </div>
                      )}
                      {selectedOrder.order.items[0].notes && (
                        <div>
                          <p className="text-gray-400 text-sm">Observações</p>
                          <p className="text-gray-300">{selectedOrder.order.items[0].notes}</p>
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
