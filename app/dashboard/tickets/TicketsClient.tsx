'use client';

import React, { useState, useEffect, useMemo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useQuery } from '@tanstack/react-query';
import { createClient } from '@/utils/supabase/client';
import { toast } from 'react-hot-toast';
import { useAuth } from '@/app/hooks/useAuth';
import VoltrisIcon, { type VoltrisIconName } from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';
import { useDashboard } from '@/app/context/DashboardContext';

interface Ticket {
  id: string;
  title: string;
  description: string;
  status: 'Aberto' | 'Em Análise' | 'Resolvido' | 'Finalizado';
  priority: 'low' | 'medium' | 'high';
  created_at: string;
  updated_at: string;
}

export default function TicketsClient() {
  const { transparencyMode } = useDashboard();
  const { user } = useAuth();
  const supabase = useMemo(() => createClient(), []);
  const [filter, setFilter] = useState('all');
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [isCreating, setIsCreating] = useState(false);
  const [newTicket, setNewTicket] = useState({ title: '', description: '', priority: 'medium' as const });
  const [selectedTicket, setSelectedTicket] = useState<Ticket | null>(null);
  const [showTicketModal, setShowTicketModal] = useState(false);
  const [messages, setMessages] = useState<any[]>([]);
  const [replyText, setReplyText] = useState('');
  const [isSendingReply, setIsSendingReply] = useState(false);

  const { data: tickets = [], refetch, isLoading } = useQuery({
    queryKey: ['tickets', user?.id],
    queryFn: async () => {
      if (!user) return [];
      const { data, error } = await supabase.from('tickets').select('*').eq('user_id', user.id).order('created_at', { ascending: false });
      if (error) throw error;
      return data as Ticket[];
    },
    enabled: !!user
  });

  useEffect(() => {
    if (!user) return;
    const channel = supabase.channel(`tickets:${user.id}`)
      .on('postgres_changes', { event: '*', schema: 'public', table: 'tickets' }, () => refetch())
      .subscribe();
    return () => { 
      channel.unsubscribe();
      supabase.removeChannel(channel); 
    };
  }, [user, refetch, supabase]);

  useEffect(() => {
    const fetchMessages = async () => {
      if (!selectedTicket) return;
      const { data } = await supabase.from('ticket_messages').select('*').eq('ticket_id', selectedTicket.id).order('created_at', { ascending: true });
      setMessages(data || []);
    };
    if (selectedTicket && showTicketModal) fetchMessages();
  }, [selectedTicket, showTicketModal, supabase]);

  const handleCreateTicket = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsCreating(true);
    try {
      if (!user) throw new Error('Usuário não autenticado');
      const { data: ticket, error } = await supabase.from('tickets').insert([{
        title: newTicket.title, status: 'Aberto', user_id: user.id, priority: newTicket.priority
      }]).select().single();
      if (error) throw error;

      await supabase.from('ticket_messages').insert({ ticket_id: ticket.id, content: newTicket.description, user_id: user.id });

      setNewTicket({ title: '', description: '', priority: 'medium' });
      setShowCreateForm(false);
      refetch();
      toast.success('Chamado de suporte aberto com sucesso!');
    } catch (err: any) {
      toast.error(err.message || 'Erro ao abrir chamado');
    } finally {
      setIsCreating(false);
    }
  };

  const handleSendReply = async () => {
    if (!replyText.trim() || !selectedTicket || !user) return;
    setIsSendingReply(true);
    try {
      const { error } = await supabase.from('ticket_messages').insert({
        ticket_id: selectedTicket.id,
        content: replyText,
        user_id: user.id
      });
      if (error) throw error;
      setReplyText('');
      const { data } = await supabase.from('ticket_messages').select('*').eq('ticket_id', selectedTicket.id).order('created_at', { ascending: true });
      setMessages(data || []);
    } catch (error) {
       toast.error('Falha ao enviar mensagem');
    } finally {
       setIsSendingReply(false);
    }
  };

  const statusConfig = (status: string): { color: string; bg: string; border: string; icon: VoltrisIconName } => {
    switch (status) {
      case 'Aberto': return { color: 'text-[#F59E0B]', bg: 'bg-amber-500/10', border: 'border-amber-500/20', icon: 'clock' };
      case 'Em Análise': return { color: 'text-[#8B31FF]', bg: 'bg-indigo-500/10', border: 'border-indigo-500/20', icon: 'activity' };
      case 'Resolvido': return { color: 'text-[#00FF94]', bg: 'bg-emerald-500/10', border: 'border-emerald-500/20', icon: 'success' };
      case 'Finalizado': return { color: 'text-slate-400', bg: 'bg-slate-800', border: 'border-slate-700', icon: 'security' };
      default: return { color: 'text-slate-400', bg: 'bg-slate-800', border: 'border-slate-700', icon: 'inbox' };
    }
  };

  const filteredTickets = tickets.filter(t => filter === 'all' || t.status === filter);

  return (
    <div className="flex flex-col gap-6 w-full max-w-full">
      
      {/* Header Area */}
      <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 pb-4 border-b border-slate-800/80">
        <div>
           <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight">Central de Suporte</h2>
           <p className="text-xs text-slate-400 mt-0.5">Abra chamados para assistência técnica especializada, dúvidas de licença ou otimização</p>
        </div>

        <div className="flex items-center gap-2.5 w-full sm:w-auto">
           <div className="relative flex-1 sm:flex-none">
              <select 
                value={filter} 
                onChange={e => setFilter(e.target.value)}
                className="w-full sm:w-44 px-3.5 py-2 rounded-xl bg-slate-900 border border-slate-800 text-slate-300 text-xs font-medium focus:outline-none focus:border-indigo-500 transition-colors cursor-pointer shadow-sm"
              >
                 <option value="all">Todos os Status</option>
                 <option value="Aberto">Abertos</option>
                 <option value="Em Análise">Em Análise</option>
                 <option value="Resolvido">Resolvidos</option>
              </select>
           </div>
           <button 
             onClick={() => setShowCreateForm(true)}
             className="px-4 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all flex items-center justify-center gap-2 active:scale-95 shrink-0"
           >
              <VoltrisIcon name="plus" size={16} />
              <span>Novo Chamado</span>
           </button>
        </div>
      </div>

      {isLoading ? (
        <div className="flex flex-col items-center justify-center py-32 gap-3 text-slate-400">
          <div className="w-8 h-8 rounded-full border-2 border-slate-700 border-t-indigo-500 animate-spin"></div>
          <p className="text-xs font-medium text-slate-400">Carregando chamados...</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          <AnimatePresence mode="popLayout">
            {filteredTickets.length > 0 ? (
              filteredTickets.map((ticket) => {
                const config = statusConfig(ticket.status);
                const Icon = config.icon;
                return (
                  <div
                    key={ticket.id}
                    onClick={() => { setSelectedTicket(ticket); setShowTicketModal(true); }}
                    className={`p-5 rounded-2xl border transition-all duration-200 cursor-pointer flex flex-col justify-between gap-4 group
                      ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-sm'} 
                      hover:border-slate-700 hover:bg-slate-900/80
                    `}
                  >
                     <div className="space-y-3">
                        <div className="flex justify-between items-center">
                           <span className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${config.bg} ${config.color} ${config.border}`}>
                              <VoltrisIcon name={config.icon} size={12} />
                              <span>{ticket.status}</span>
                           </span>
                           <span className={`text-[10px] font-semibold px-2 py-0.5 rounded border ${
                             ticket.priority === 'high' 
                               ? 'text-rose-400 bg-rose-500/10 border-rose-500/20' 
                               : ticket.priority === 'medium' 
                                 ? 'text-amber-400 bg-amber-500/10 border-amber-500/20' 
                                 : 'text-slate-400 bg-slate-800 border-slate-700'
                           }`}>
                              {ticket.priority === 'high' ? 'Alta' : ticket.priority === 'medium' ? 'Média' : 'Normal'}
                           </span>
                        </div>

                        <div>
                          <h3 className="text-sm font-bold text-white tracking-tight group-hover:text-indigo-300 transition-colors line-clamp-1">{ticket.title}</h3>
                          <p className="text-xs text-slate-400 mt-1 line-clamp-2 leading-relaxed">{ticket.description}</p>
                        </div>
                     </div>

                     <div className="pt-3 border-t border-slate-800/80 flex items-center justify-between text-[11px] text-slate-500 font-mono">
                        <span>#{ticket.id.slice(0, 6)}</span>
                        <div className="flex items-center gap-1 text-slate-400 group-hover:text-white transition-colors">
                           <span>Ver mensagens</span>
                           <VoltrisIcon name="arrowRight" size={14} className="group-hover:translate-x-0.5 transition-transform" />
                        </div>
                     </div>
                  </div>
                );
              })
            ) : (
               <div className={`col-span-full p-16 rounded-2xl text-center border border-slate-800 flex flex-col items-center gap-3 ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/40 shadow-xl'}`}>
                 <VoltrisIconTile icon="inbox" accent={DASHBOARD_ACCENT.brand} size={12} />
                 <div className="space-y-1 max-w-sm">
                    <h3 className="text-base font-bold text-white tracking-tight">Nenhum chamado registrado</h3>
                    <p className="text-xs text-slate-400">Você não possui tickets de suporte ativos com os filtros selecionados.</p>
                 </div>
              </div>
            )}
          </AnimatePresence>
        </div>
      )}

      {/* Modern Ticket Creation Modal */}
      <AnimatePresence>
        {showCreateForm && (
          <div className="fixed inset-0 z-[300] flex items-center justify-center p-4">
            <motion.div 
              initial={{ opacity: 0 }} 
              animate={{ opacity: 1 }} 
              exit={{ opacity: 0 }} 
              className="absolute inset-0 bg-slate-950/80 backdrop-blur-sm" 
              onClick={() => setShowCreateForm(false)} 
            />
            <motion.div 
              initial={{ scale: 0.95, y: 15 }} 
              animate={{ scale: 1, y: 0 }} 
              exit={{ scale: 0.95, y: 15 }} 
              className={`relative w-full max-w-lg p-6 sm:p-8 rounded-2xl border border-slate-800 shadow-2xl ${transparencyMode ? 'voltris-glass' : 'bg-slate-900'}`}
            >
               <div className="flex items-center justify-between pb-4 mb-6 border-b border-slate-800">
                  <div className="flex items-center gap-3">
                     <VoltrisIconTile icon="message" accent={DASHBOARD_ACCENT.brand} size={10} />
                    <div>
                      <h3 className="text-lg font-bold text-white tracking-tight">Abrir Novo Chamado</h3>
                      <p className="text-xs text-slate-400">Nossa equipe técnica responderá o mais breve possível</p>
                    </div>
                  </div>
                  <button onClick={() => setShowCreateForm(false)} className="text-slate-500 hover:text-white transition-colors">
                    <VoltrisIcon name="close" size={20} />
                  </button>
               </div>

                <form onSubmit={handleCreateTicket} className="space-y-4">
                  <div className="space-y-1.5">
                    <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Assunto / Título</label>
                    <input 
                      type="text" required 
                      value={newTicket.title} onChange={e => setNewTicket({...newTicket, title: e.target.value})}
                      className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors placeholder:text-slate-500" 
                      placeholder="Ex: Dúvida sobre ativação do Optimizer"
                    />
                  </div>

                  <div className="space-y-1.5">
                    <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Prioridade</label>
                    <select 
                      value={newTicket.priority} onChange={e => setNewTicket({...newTicket, priority: e.target.value as any})}
                      className="w-full px-3.5 py-2.5 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors cursor-pointer"
                    >
                       <option value="low">Prioridade Normal</option>
                       <option value="medium">Prioridade Média</option>
                       <option value="high">Prioridade Urgente</option>
                    </select>
                  </div>

                  <div className="space-y-1.5">
                    <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Mensagem / Detalhes</label>
                    <textarea 
                      required rows={4}
                      value={newTicket.description} onChange={e => setNewTicket({...newTicket, description: e.target.value})}
                      className="w-full p-3.5 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors placeholder:text-slate-500 resize-none leading-relaxed" 
                      placeholder="Descreva o que está ocorrendo ou sua solicitação..."
                    />
                  </div>

                  <div className="pt-2 flex gap-3">
                    <button 
                      type="button"
                      onClick={() => setShowCreateForm(false)}
                      className="flex-1 py-2.5 rounded-xl bg-slate-800 text-slate-300 font-semibold text-xs hover:bg-slate-700 transition-colors"
                    >
                      Cancelar
                    </button>
                    <button 
                      type="submit" disabled={isCreating}
                      className="flex-1 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all disabled:opacity-50"
                    >
                      {isCreating ? 'Enviando...' : 'Enviar Chamado'}
                    </button>
                  </div>
               </form>
            </motion.div>
          </div>
        )}
      </AnimatePresence>

      {/* Details/Chat Modal */}
      <AnimatePresence>
        {showTicketModal && selectedTicket && (
          <div className="fixed inset-0 z-[350] flex items-center justify-center p-4">
            <motion.div initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className="absolute inset-0 bg-slate-950/80 backdrop-blur-sm" onClick={() => setShowTicketModal(false)} />
            <motion.div initial={{ scale: 0.95, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} exit={{ scale: 0.95, opacity: 0 }} onClick={e => e.stopPropagation()} className={`relative w-full max-w-2xl h-[85vh] flex flex-col overflow-hidden rounded-2xl border border-slate-800 shadow-2xl ${transparencyMode ? 'voltris-glass' : 'bg-slate-900'}`}>
               
               {/* Modal Header */}
               <div className="px-6 py-4 bg-slate-950/80 border-b border-slate-800 flex justify-between items-center shrink-0">
                  <div className="flex items-center gap-3 min-w-0">
                    <div className={`p-2 rounded-lg ${statusConfig(selectedTicket.status).bg} ${statusConfig(selectedTicket.status).color} border ${statusConfig(selectedTicket.status).border} shrink-0`}>
                       <VoltrisIcon name={statusConfig(selectedTicket.status).icon} size={16} />
                    </div>
                    <div className="min-w-0">
                      <h4 className="text-sm font-bold text-white tracking-tight truncate">{selectedTicket.title}</h4>
                      <p className="text-[10px] text-slate-500 font-mono">Chamado #{selectedTicket.id.slice(0, 8)}</p>
                    </div>
                 </div>
                  <button onClick={() => setShowTicketModal(false)} className="text-slate-400 hover:text-white transition-colors p-1">
                    <VoltrisIcon name="close" size={20} />
                  </button>
               </div>

               {/* Communications Stream */}
               <div className="flex-1 overflow-y-auto p-6 space-y-4 [scrollbar-width:none] [-ms-overflow-style:none] [&::-webkit-scrollbar]:hidden">
                  {messages.map((msg, i) => {
                    const isMe = msg.user_id === user?.id;
                    return (
                      <div 
                        key={i} 
                        className={`flex flex-col ${isMe ? 'items-end' : 'items-start'}`}
                      >
                         <div className={`px-4 py-3 rounded-2xl max-w-[85%] text-xs leading-relaxed ${isMe ? 'bg-indigo-600 text-white rounded-tr-none' : 'bg-slate-800 text-slate-200 border border-slate-700/80 rounded-tl-none'}`}>
                            <p className="whitespace-pre-wrap">{msg.content}</p>
                         </div>
                          <span className="mt-1 px-1 text-[9px] font-mono text-slate-500">
                            {isMe ? 'Você' : 'Suporte Técnico'} • {new Date(msg.created_at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                          </span>
                      </div>
                    );
                  })}
               </div>

               {/* Input Station */}
               <div className="p-4 bg-slate-950/80 border-t border-slate-800 shrink-0">
                   <div className="relative flex items-center">
                     <textarea 
                        rows={1}
                        value={replyText}
                        onChange={e => setReplyText(e.target.value)}
                        onKeyDown={e => {
                          if (e.key === 'Enter' && !e.shiftKey) {
                            e.preventDefault();
                            handleSendReply();
                          }
                        }}
                        placeholder="Escreva sua resposta..."
                        className="w-full bg-slate-900 border border-slate-700 rounded-xl pl-4 pr-24 py-3 text-white text-xs focus:border-indigo-500 outline-none transition-colors placeholder:text-slate-500 resize-none"
                     />
                     <button 
                       onClick={handleSendReply}
                       disabled={!replyText.trim() || isSendingReply}
                       className="absolute right-2 px-3 py-1.5 bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg text-xs font-semibold flex items-center gap-1.5 transition-all disabled:opacity-30"
                     >
                        <span>{isSendingReply ? 'Enviando...' : 'Enviar'}</span>
                        <VoltrisIcon name="send" size={12} />
                     </button>
                  </div>
                </div>

            </motion.div>
          </div>
        )}
      </AnimatePresence>
    </div>
  );
}
