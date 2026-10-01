'use client';

import React, { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { useQuery } from '@tanstack/react-query';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';

interface Activity {
  type: 'order' | 'ticket' | 'other';
  title: string;
  time: string;
  status: 'success' | 'pending' | 'in_progress';
  icon: React.ReactNode;
}

interface Order {
  id: string;
  created_at: string;
  status: string;
  final_price: number;
}

interface Profile {
  id: string;
  full_name: string;
  phone?: string;
  city?: string;
  neighborhood?: string;
  state?: string;
  cep?: string;
}

export default function OverviewTab() {
  // Buscar perfil com React Query
  const { data: profile, isLoading: profileLoading } = useQuery<Profile | null>({
    queryKey: ['profile'],
    queryFn: async () => {
      const res = await fetch('/api/profile');
      const data = await res.json();
      return data.profile || null;
    },
    refetchOnWindowFocus: false,
  });

  // Buscar pedidos (mock ou ajuste conforme sua API)
  const { data: orders = [] } = useQuery<Order[]>({
    queryKey: ['orders'],
    queryFn: async () => {
      const res = await fetch('/api/orders');
      const data = await res.json();
      return data.orders || [];
    },
    refetchOnWindowFocus: false,
  });

  // Estatísticas calculadas
  const stats = [
    {
      title: 'Total de Pedidos',
      value: orders.length,
      color: 'from-[#FF4B6B] to-[#8B31FF]',
      icon: <VoltrisIcon name="orders" size={20} className="text-[#FF4B6B]" />
    },
    {
      title: 'Pedidos Pendentes',
      value: orders.filter(order => order.status === 'pending').length,
      color: 'from-[#8B31FF] to-[#31A8FF]',
      icon: <VoltrisIcon name="clock" size={20} className="text-[#8B31FF]" />
    },
    {
      title: 'Pedidos Concluídos',
      value: orders.filter(order => order.status === 'completed').length,
      color: 'from-[#31A8FF] to-[#FF4B6B]',
      icon: <VoltrisIcon name="success" size={20} className="text-[#31A8FF]" />
    }
  ];

  // Atividades recentes (mock)
  const recentActivity: Activity[] = [
    {
      type: 'order',
      title: 'Pedido #1234',
      time: '2 horas atrás',
      status: 'success',
      icon: <VoltrisIcon name="orders" size={16} className="text-[#8B31FF]" />
    },
    {
      type: 'ticket',
      title: 'Ticket de Suporte #5678',
      time: '5 horas atrás',
      status: 'in_progress',
      icon: <VoltrisIcon name="support" size={16} className="text-[#FF4B6B]" />
    },
    {
      type: 'other',
      title: 'Atualização de Perfil',
      time: '1 dia atrás',
      status: 'success',
      icon: <VoltrisIcon name="edit" size={16} className="text-[#31A8FF]" />
    }
  ];

  // Corrigir hydration: horário só no client
  const [lastUpdate, setLastUpdate] = useState<string | null>(null);
  useEffect(() => {
    setLastUpdate(new Date().toLocaleTimeString('pt-BR'));
  }, []);

  return (
    <div className="h-full flex flex-col gap-6">
      {/* Header */}
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="relative overflow-hidden bg-gradient-to-br from-[#1E1E1E]/90 via-[#8B31FF]/5 to-[#FF4B6B]/5 rounded-2xl p-6 border border-gray-800/30"
      >
        <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-6">
          <div>
            <div className="flex items-center gap-3 mb-2">
              <div className="bg-gradient-to-br from-[#FF4B6B] to-[#8B31FF] rounded-xl p-2 shadow-lg">
                <svg className="w-8 h-8 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M16 11V7a4 4 0 00-8 0v4M5 9h14l1 12H4L5 9z" />
                </svg>
              </div>
              <h1 className="text-2xl md:text-3xl font-bold bg-gradient-to-r from-[#FF4B6B] via-[#8B31FF] to-[#31A8FF] text-transparent bg-clip-text min-h-[2.5rem] flex items-center">
                Bem-Vindo(a), {profile?.full_name || 'Usuário'}
              </h1>
            </div>
            <p className="text-gray-300 text-base md:text-lg">Gerencie seus pedidos e acompanhe suas atividades</p>
          </div>
          <div className="flex items-center gap-4 text-sm text-gray-400">
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 bg-green-500 rounded-full animate-pulse"></div>
              <span>Sistema Online</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-2 h-2 bg-blue-500 rounded-full animate-pulse"></div>
              {lastUpdate && <span>Última atualização: {lastUpdate}</span>}
            </div>
          </div>
        </div>
      </motion.div>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {stats.map((stat, index) => (
          <motion.div
            key={stat.title}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5, delay: index * 0.1 }}
            className="group bg-gradient-to-br from-[#1E1E1E]/90 to-[#171313]/90 backdrop-blur-xl p-6 rounded-2xl border border-[#8B31FF]/20 hover:border-[#8B31FF]/40 shadow-lg hover:shadow-[#8B31FF]/20 transform hover:scale-[1.02] transition-all duration-300"
          >
            <div className="flex items-center justify-between">
              <div>
                <h3 className="text-lg font-medium text-gray-300">{stat.title}</h3>
                <p className={`text-3xl font-bold text-transparent bg-clip-text bg-gradient-to-r ${stat.color}`}>
                  {stat.value}
                </p>
              </div>
              <div className={`p-3 rounded-lg bg-gradient-to-r ${stat.color} bg-opacity-10 group-hover:bg-opacity-20 transition-all duration-300`}>
                {stat.icon}
              </div>
            </div>
            <div className="mt-4 bg-gray-700/30 rounded-full h-2 overflow-hidden">
              <motion.div 
                className="h-full bg-gradient-to-r from-[#8B31FF] to-[#31A8FF] rounded-full"
                initial={{ width: 0 }}
                animate={{ width: `${(stat.value / Math.max(...stats.map(s => s.value))) * 100}%` }}
                transition={{ delay: 0.7, duration: 1 }}
              />
            </div>
          </motion.div>
        ))}
      </div>

      {/* Recent Activity and Orders */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 flex-1">
        {/* Últimos Pedidos */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, delay: 0.3 }}
          className="bg-[#1E1E1E]/80 backdrop-blur-xl p-6 rounded-2xl border border-gray-800/30"
        >
          <h3 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
            <VoltrisIcon name="orders" size={18} className="text-[#8B31FF]" />
            Últimos Pedidos
          </h3>
          <div className="space-y-4">
            {orders.slice(0, 5).map((order) => (
              <div key={order.id} className="flex items-center justify-between p-4 bg-[#171313] rounded-xl hover:bg-[#171313]/80 transition-colors duration-300">
                <div>
                  <p className="font-medium text-white">Pedido #{order.id}</p>
                  <p className="text-sm text-gray-400">
                    {new Date(order.created_at).toLocaleDateString('pt-BR')}
                  </p>
                </div>
                <div className="flex items-center gap-4">
                  <span className={`px-2 py-1 rounded-full text-xs font-medium ${
                    order.status === 'completed' ? 'bg-green-500/10 text-green-400' :
                    order.status === 'pending' ? 'bg-yellow-500/10 text-yellow-400' :
                    order.status === 'processing' ? 'bg-blue-500/10 text-blue-400' :
                    'bg-red-500/10 text-red-400'
                  }`}>
                    {order.status === 'completed' ? 'Concluído' :
                     order.status === 'pending' ? 'Pendente' :
                     order.status === 'processing' ? 'Em Processamento' :
                     'Cancelado'}
                  </span>
                  <p className="text-lg font-bold text-[#8B31FF]">
                    R$ {order.final_price.toFixed(2)}
                  </p>
                </div>
              </div>
            ))}
          </div>
        </motion.div>

        {/* Atividade Recente */}
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.5, delay: 0.4 }}
          className="bg-[#1E1E1E]/80 backdrop-blur-xl p-6 rounded-2xl border border-gray-800/30"
        >
          <h3 className="text-lg font-semibold text-white mb-4 flex items-center gap-2">
            <svg className="w-5 h-5 text-[#8B31FF]" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            Atividade Recente
          </h3>
          <div className="space-y-4">
            {recentActivity.map((activity, index) => (
              <div key={index} className="flex items-center justify-between p-4 bg-[#171313] rounded-xl hover:bg-[#171313]/80 transition-colors duration-300">
                <div className="flex items-center gap-3">
                  <div className={`p-2 rounded-lg ${
                    activity.type === 'order' ? 'bg-[#8B31FF]/10' :
                    activity.type === 'ticket' ? 'bg-[#FF4B6B]/10' :
                    'bg-[#31A8FF]/10'
                  }`}>
                    {activity.icon}
                  </div>
                  <div>
                    <p className="font-medium text-white">{activity.title}</p>
                    <p className="text-sm text-gray-400">{activity.time}</p>
                  </div>
                </div>
                <span className={`px-2 py-1 rounded-full text-xs font-medium ${
                  activity.status === 'success' ? 'bg-green-500/10 text-green-400' :
                  activity.status === 'pending' ? 'bg-yellow-500/10 text-yellow-400' :
                  'bg-blue-500/10 text-blue-400'
                }`}>
                  {activity.status === 'success' ? 'Concluído' :
                   activity.status === 'pending' ? 'Pendente' :
                   'Em Andamento'}
                </span>
              </div>
            ))}
          </div>
        </motion.div>
      </div>
    </div>
  );
}
