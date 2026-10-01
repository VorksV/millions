'use client';

import { useState } from 'react';
import { useNotificationContext } from '@/components/notifications/NotificationContext';
import { useAuth } from '@/app/hooks/useAuth';
import { motion, AnimatePresence } from 'framer-motion';
import { 
  FiBell, FiPackage, FiMessageSquare, FiInfo, 
  FiCheckCircle, FiAlertTriangle, FiClock, FiShield,
  FiTerminal, FiActivity, FiZap, FiCheck, FiFilter, FiRefreshCw
} from 'react-icons/fi';
import { useDashboard } from '@/app/context/DashboardContext';

export default function NotificationsClient() {
    const { transparencyMode } = useDashboard();
    const { notifications, markAsRead, markAllAsRead, refreshNotifications } = useNotificationContext();
    const { isAdmin } = useAuth();
    const [filter, setFilter] = useState<'all' | 'unread' | 'order' | 'ticket'>('all');
    const [isRefreshing, setIsRefreshing] = useState(false);

    // Filtragem conforme lógica original
    const baseNotifications = notifications.filter(n => isAdmin || (n.type !== 'newsletter' && n.type !== 'comment'));
    const unreadCount = baseNotifications.filter(n => !n.read).length;

    // Filtro ativo na aba
    const filteredNotifications = baseNotifications.filter(n => {
        if (filter === 'unread') return !n.read;
        if (filter === 'order') return n.type === 'order';
        if (filter === 'ticket') return n.type === 'ticket';
        return true;
    });

    const getIcon = (type: string) => {
        switch (type) {
            case 'order': return FiPackage;
            case 'ticket': return FiMessageSquare;
            case 'success': return FiCheckCircle;
            case 'warning': return FiAlertTriangle;
            default: return FiBell;
        }
    };

    // Cores dos ícones mantidas estritamente conforme configurado pelo usuário
    const getColor = (type: string) => {
        switch (type) {
            case 'order': return 'text-[#31A8FF] bg-[#31A8FF]/10 border-[#31A8FF]/20';
            case 'ticket': return 'text-[#8B31FF] bg-[#8B31FF]/10 border-[#8B31FF]/20';
            case 'success': return 'text-[#00FF88] bg-[#00FF88]/10 border-[#00FF88]/20';
            case 'warning': return 'text-amber-400 bg-amber-400/10 border-amber-400/20';
            default: return 'text-indigo-400 bg-indigo-500/10 border-indigo-500/20';
        }
    };

    const getTypeLabel = (type: string) => {
        switch (type) {
            case 'order': return 'Pedido';
            case 'ticket': return 'Ticket';
            case 'success': return 'Sucesso';
            case 'warning': return 'Alerta';
            default: return 'Sistema';
        }
    };

    const handleRefresh = async () => {
        setIsRefreshing(true);
        try {
            await refreshNotifications();
        } finally {
            setIsRefreshing(false);
        }
    };

    return (
        <div className="flex flex-col gap-6 w-full max-w-5xl mx-auto">
            
            {/* Page Header */}
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-4 border-b border-slate-800/80">
                <div>
                    <h2 className="text-xl sm:text-2xl font-bold tracking-tight text-white flex items-center gap-2.5">
                        <FiBell className="w-6 h-6 text-indigo-400" />
                        <span>Central de Notificações</span>
                    </h2>
                    <p className="text-xs sm:text-sm text-slate-400 mt-1">
                        Logs de eventos, atualizações de pedidos e avisos em tempo real.
                    </p>
                </div>

                <div className="flex items-center gap-2.5 flex-wrap">
                    {/* Unread Counter Badge */}
                    <div className="px-3 py-1.5 rounded-xl bg-slate-900/60 border border-slate-800 flex items-center gap-2">
                        <div className={`w-2 h-2 rounded-full ${unreadCount > 0 ? 'bg-indigo-400 animate-pulse' : 'bg-slate-600'}`}></div>
                        <span className="text-xs font-semibold text-slate-300">
                            {unreadCount} {unreadCount === 1 ? 'não lida' : 'não lidas'}
                        </span>
                    </div>

                    {/* Refresh Button */}
                    <button
                        onClick={handleRefresh}
                        disabled={isRefreshing}
                        className="p-2 rounded-xl bg-slate-900/60 border border-slate-800 text-slate-400 hover:text-white hover:bg-slate-800 transition-all shadow-sm active:scale-95 disabled:opacity-50"
                        title="Atualizar notificações"
                    >
                        <FiRefreshCw className={`w-4 h-4 ${isRefreshing ? 'animate-spin text-indigo-400' : ''}`} />
                    </button>

                    {/* Mark All as Read */}
                    {unreadCount > 0 && (
                        <button
                            onClick={() => markAllAsRead()}
                            className="px-3.5 py-1.5 rounded-xl bg-indigo-600/10 hover:bg-indigo-600/20 border border-indigo-500/20 text-indigo-300 hover:text-white text-xs font-semibold transition-all active:scale-95 flex items-center gap-1.5 shadow-sm"
                        >
                            <FiCheck className="w-3.5 h-3.5" />
                            <span>Marcar lidas</span>
                        </button>
                    )}
                </div>
            </div>

            {/* Filter Tabs */}
            <div className="flex items-center gap-2 overflow-x-auto pb-1 no-scrollbar">
                {[
                    { id: 'all', label: 'Todas', count: baseNotifications.length },
                    { id: 'unread', label: 'Não Lidas', count: unreadCount },
                    { id: 'order', label: 'Pedidos', count: baseNotifications.filter(n => n.type === 'order').length },
                    { id: 'ticket', label: 'Tickets', count: baseNotifications.filter(n => n.type === 'ticket').length },
                ].map((tab) => {
                    const isActive = filter === tab.id;
                    return (
                        <button
                            key={tab.id}
                            onClick={() => setFilter(tab.id as any)}
                            className={`px-3.5 py-2 rounded-xl text-xs font-medium transition-all whitespace-nowrap flex items-center gap-2
                                ${isActive
                                    ? 'bg-indigo-600 text-white shadow-md shadow-indigo-600/20 border border-indigo-500'
                                    : 'bg-slate-900/60 text-slate-400 hover:text-slate-200 border border-slate-800/80 hover:bg-slate-800/60'
                                }
                            `}
                        >
                            <span>{tab.label}</span>
                            {tab.count > 0 && (
                                <span className={`px-1.5 py-0.2 rounded-md text-[10px] font-bold ${
                                    isActive ? 'bg-white/20 text-white' : 'bg-slate-800 text-slate-400'
                                }`}>
                                    {tab.count}
                                </span>
                            )}
                        </button>
                    );
                })}
            </div>

            {/* Notifications Stream */}
            <div className="flex flex-col gap-3">
                <AnimatePresence mode="popLayout">
                    {filteredNotifications.length > 0 ? (
                        filteredNotifications.map((notif, i) => {
                            const Icon = getIcon(notif.type);
                            const colorClasses = getColor(notif.type);

                            return (
                                <motion.div
                                    key={notif.id}
                                    initial={{ opacity: 0, y: 12 }}
                                    animate={{ opacity: 1, y: 0 }}
                                    exit={{ opacity: 0, scale: 0.98 }}
                                    transition={{ duration: 0.2, delay: i < 8 ? i * 0.03 : 0 }}
                                    className={`relative p-4 sm:p-5 rounded-2xl border transition-all duration-200 group cursor-pointer
                                        ${notif.read
                                            ? 'bg-slate-900/30 border-slate-800/60 opacity-75 hover:opacity-100 hover:border-slate-700'
                                            : `${transparencyMode ? 'voltris-glass' : 'bg-slate-900/70 border-slate-800 shadow-md'} hover:border-indigo-500/40 hover:bg-slate-900/90`
                                        }
                                    `}
                                    onClick={() => markAsRead(notif.id)}
                                >
                                    <div className="flex items-start gap-3.5 sm:gap-4">
                                        {/* Icon Box with User's Exact Colors */}
                                        <div className={`p-3 sm:p-3.5 rounded-xl border flex-shrink-0 transition-transform duration-200 group-hover:scale-105 ${colorClasses}`}>
                                            <Icon className="w-5 h-5 sm:w-5 sm:h-5" />
                                        </div>

                                        {/* Content */}
                                        <div className="flex-1 min-w-0">
                                            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-1 sm:gap-2 mb-1">
                                                <div className="flex items-center gap-2 min-w-0">
                                                    {!notif.read && (
                                                        <span className="w-2 h-2 rounded-full bg-indigo-400 shrink-0" title="Não lida" />
                                                    )}
                                                    <h3 className={`text-sm sm:text-base font-semibold tracking-tight truncate ${notif.read ? 'text-slate-300' : 'text-white'}`}>
                                                        {notif.title}
                                                    </h3>
                                                </div>

                                                <div className="flex items-center gap-2 shrink-0">
                                                    <span className="px-2 py-0.5 rounded-md text-[10px] font-semibold bg-slate-800/80 text-slate-400 border border-slate-700/50 uppercase tracking-wider">
                                                        {getTypeLabel(notif.type)}
                                                    </span>
                                                    <span className="text-[11px] text-slate-500 font-mono flex items-center gap-1">
                                                        <FiClock className="w-3 h-3 text-slate-600" />
                                                        {new Date(notif.created_at).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}
                                                    </span>
                                                </div>
                                            </div>

                                            <p className={`text-xs sm:text-sm leading-relaxed ${notif.read ? 'text-slate-400' : 'text-slate-300'}`}>
                                                {notif.message}
                                            </p>
                                        </div>
                                    </div>
                                </motion.div>
                            );
                        })
                    ) : (
                        <div className={`py-20 sm:py-24 px-6 flex flex-col items-center justify-center text-center gap-4 rounded-2xl border border-slate-800 ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/40 shadow-sm'}`}>
                            <div className="w-14 h-14 rounded-2xl bg-slate-800/80 border border-slate-700 flex items-center justify-center text-slate-400">
                                <FiBell className="w-7 h-7 text-indigo-400" />
                            </div>
                            <div className="space-y-1.5 max-w-sm">
                                <h3 className="text-base font-bold text-white tracking-tight">Nenhuma notificação encontrada</h3>
                                <p className="text-xs text-slate-400 leading-relaxed">
                                    {filter === 'unread'
                                        ? 'Parabéns! Todas as notificações foram lidas.'
                                        : 'Você está em dia com todos os eventos e atualizações do sistema.'}
                                </p>
                            </div>
                        </div>
                    )}
                </AnimatePresence>
            </div>
        </div>
    );
}
