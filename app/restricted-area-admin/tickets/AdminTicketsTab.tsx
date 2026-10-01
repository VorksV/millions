import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { createClient } from '@/utils/supabase/client';
import { FiSearch, FiFilter, FiEye, FiCheckCircle, FiClock, FiXCircle, FiMessageSquare, FiSend } from 'react-icons/fi';
import { toast } from 'react-hot-toast';
import type { Ticket, TicketMessage } from '@/types/ticket';
import { useAuth } from '@/app/hooks/useAuth';

interface UserInfo {
  full_name: string;
  login: string;
}

interface TicketWithUser extends Ticket {
  user_info?: UserInfo;
  messages?: TicketMessage[];
}

export default function AdminTicketsTab() {
  const [tickets, setTickets] = useState<TicketWithUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedTicket, setSelectedTicket] = useState<TicketWithUser | null>(null);
  const [showTicketModal, setShowTicketModal] = useState(false);
  const supabase = createClient();
  const { user } = useAuth();
  const [reply, setReply] = useState('');
  const [sendingReply, setSendingReply] = useState(false);
  const [messages, setMessages] = useState<TicketMessage[]>([]);

  useEffect(() => {
    fetchTickets();
    const supabase = createClient();
    let channel: any;
    async function setupRealtime() {
      channel = supabase.channel('tickets-admin-realtime')
        .on('postgres_changes', { event: '*', schema: 'public', table: 'tickets' }, (payload: any) => {
          const changedTicket = payload.new;
          if (!changedTicket) return;
          setTickets(prevTickets => {
            if (payload.eventType === 'UPDATE') {
              return prevTickets.map(t => t.id === changedTicket.id ? { ...t, ...changedTicket } : t);
            } else if (payload.eventType === 'INSERT') {
              if (prevTickets.some(t => t.id === changedTicket.id)) return prevTickets;
              return [changedTicket, ...prevTickets];
            } else if (payload.eventType === 'DELETE') {
              return prevTickets.filter(t => t.id !== payload.old.id);
            }
            return prevTickets;
          });
        })
        .subscribe();
    }
    setupRealtime();
    return () => { if (channel) supabase.removeChannel(channel); };
  }, []);

  useEffect(() => {
    const fetchMessages = async () => {
      if (!selectedTicket) return;
      const { data, error } = await supabase
        .from('ticket_messages')
        .select('*')
        .eq('ticket_id', selectedTicket.id)
        .order('created_at', { ascending: true });
      if (!error && data) setMessages(data);
    };
    if (selectedTicket) fetchMessages();
  }, [selectedTicket, showTicketModal]);

  const handleSendReply = async () => {
    if (!reply.trim() || !selectedTicket || !user) return;
    setSendingReply(true);
    const { error } = await supabase.from('ticket_messages').insert({
      ticket_id: selectedTicket.id,
      content: reply,
      user_id: user.id
    });
    setSendingReply(false);
    if (!error) {
      setReply('');
      const { data } = await supabase
        .from('ticket_messages')
        .select('*')
        .eq('ticket_id', selectedTicket.id)
        .order('created_at', { ascending: true });
      if (data) setMessages(data);
      toast.success('Resposta enviada!');
    } else {
      toast.error('Erro ao enviar resposta');
    }
  };

  const fetchTickets = async () => {
    try {
      setLoading(true);
      const { data: ticketsData, error: ticketsError } = await supabase
        .from('tickets')
        .select('*')
        .order('created_at', { ascending: false });
      if (ticketsError) throw ticketsError;
      if (ticketsData) {
        const ticketsWithUserInfo = await Promise.all(
          ticketsData.map(async (ticket: any) => {
            const { data: profileData } = await supabase
              .from('profiles')
              .select('full_name, login')
              .eq('id', ticket.user_id)
              .single();
            return { ...ticket, user_info: profileData || { full_name: 'Nome não informado', login: 'Login não informado' } };
          })
        );
        setTickets(ticketsWithUserInfo);
      }
    } catch (error) {
      console.error('Erro ao buscar tickets:', error);
      toast.error('Erro ao carregar tickets');
    } finally {
      setLoading(false);
    }
  };

  const handleStatusChange = async (ticketId: string, newStatus: Ticket['status']) => {
    try {
      const { data: updated, error } = await supabase
        .from('tickets')
        .update({ status: newStatus })
        .eq('id', ticketId)
        .select()
        .single();
      let msg = '';
      if (error) {
        if (typeof error === 'string') msg = error;
        else if (error && typeof error === 'object') msg = [error.message, error.details, error.code, JSON.stringify(error)].filter(Boolean).join(' | ');
        toast.error('Erro: ' + (msg || 'Desconhecido'));
        throw error;
      }
      if (updated) {
        setTickets(prev => prev.map(t => t.id === ticketId ? { ...t, ...updated } : t));
        toast.success('Ticket atualizado!');
      }
    } catch (error) {
      console.error('Erro ao atualizar status:', error);
    }
  };

  const filteredTickets = tickets.filter(ticket => {
    const matchesFilter = filter === 'all' || ticket.status === filter;
    const matchesSearch =
      ticket.id.toLowerCase().includes(searchTerm.toLowerCase()) ||
      ticket.user_info?.full_name?.toLowerCase().includes(searchTerm.toLowerCase()) ||
      ticket.user_info?.login?.toLowerCase().includes(searchTerm.toLowerCase());
    return matchesFilter && matchesSearch;
  });

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Resolvido':   return { cls: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20', icon: <FiCheckCircle className="w-3 h-3" /> };
      case 'Aberto':      return { cls: 'bg-amber-500/10 text-amber-400 border-amber-500/20', icon: <FiClock className="w-3 h-3" /> };
      case 'Em Análise':  return { cls: 'bg-indigo-500/10 text-indigo-400 border-indigo-500/20', icon: <FiClock className="w-3 h-3" /> };
      case 'Finalizado':  return { cls: 'bg-slate-500/10 text-slate-400 border-slate-500/20', icon: <FiXCircle className="w-3 h-3" /> };
      default:            return { cls: 'bg-slate-500/10 text-slate-400 border-slate-500/20', icon: <FiClock className="w-3 h-3" /> };
    }
  };

  const getPriorityBadge = (priority: string) => {
    switch (priority) {
      case 'high':   return { cls: 'bg-rose-500/10 text-rose-400 border-rose-500/20', label: 'Alta' };
      case 'medium': return { cls: 'bg-amber-500/10 text-amber-400 border-amber-500/20', label: 'Média' };
      case 'low':    return { cls: 'bg-slate-500/10 text-slate-400 border-slate-500/20', label: 'Baixa' };
      default:       return { cls: 'bg-slate-500/10 text-slate-400 border-slate-500/20', label: priority };
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
      {/* Page Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-bold text-slate-100">Gerenciar Tickets</h1>
          <p className="text-xs text-slate-500 mt-0.5">{tickets.length} ticket(s) no sistema</p>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-1.5 h-1.5 bg-emerald-500 rounded-full animate-pulse" />
          <span className="text-xs text-slate-500">Tempo real</span>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="flex-1 relative">
          <FiSearch className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-500 w-3.5 h-3.5" />
          <input
            type="text"
            placeholder="Buscar por ID, nome ou login..."
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
            <option value="Aberto">Abertos</option>
            <option value="Em Análise">Em Análise</option>
            <option value="Resolvido">Resolvidos</option>
            <option value="Finalizado">Finalizados</option>
          </select>
        </div>
      </div>

      {/* Tickets List */}
      <div className="space-y-3">
        {filteredTickets.length === 0 ? (
          <div className="text-center py-16 bg-slate-900 border border-slate-800 rounded-2xl">
            <div className="w-12 h-12 mx-auto mb-3 bg-slate-800 rounded-xl flex items-center justify-center">
              <FiMessageSquare className="w-5 h-5 text-slate-600" />
            </div>
            <p className="text-sm text-slate-500 font-medium">Nenhum ticket encontrado</p>
          </div>
        ) : (
          filteredTickets.map((ticket, index) => {
            const badge = getStatusBadge(ticket.status);
            const priority = getPriorityBadge(ticket.priority);
            return (
              <motion.div
                key={ticket.id}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.2, delay: index < 10 ? index * 0.04 : 0 }}
                className="bg-slate-900 border border-slate-800 rounded-xl p-4 hover:border-slate-700 transition-all"
              >
                <div className="flex flex-col lg:flex-row justify-between items-start lg:items-center gap-4">
                  <div className="flex-1 min-w-0">
                    <div className="flex flex-wrap items-center gap-2 mb-2">
                      <span className="text-[10px] font-mono text-slate-600 bg-slate-800 px-2 py-0.5 rounded">
                        #{ticket.id.slice(0, 8)}
                      </span>
                      <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${badge.cls}`}>
                        {badge.icon}
                        {ticket.status}
                      </span>
                      <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${priority.cls}`}>
                        {priority.label}
                      </span>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6 gap-y-1 text-xs">
                      <div className="flex gap-2">
                        <span className="text-slate-600">Cliente:</span>
                        <span className="text-slate-300 truncate">{ticket.user_info?.full_name || '—'}</span>
                      </div>
                      <div className="flex gap-2">
                        <span className="text-slate-600">Login:</span>
                        <span className="text-slate-300 truncate">{ticket.user_info?.login || '—'}</span>
                      </div>
                      <div className="flex gap-2 col-span-2">
                        <span className="text-slate-600">Título:</span>
                        <span className="text-slate-300 truncate">{ticket.title}</span>
                      </div>
                      <div className="flex gap-2">
                        <span className="text-slate-600">Data:</span>
                        <span className="text-slate-300">{new Date(ticket.created_at).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })}</span>
                      </div>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 flex-shrink-0">
                    <button
                      onClick={() => { setSelectedTicket(ticket); setShowTicketModal(true); }}
                      className="flex items-center gap-1.5 px-3 py-1.5 bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-400 border border-indigo-500/20 rounded-lg transition text-xs font-medium"
                    >
                      <FiEye className="w-3.5 h-3.5" />
                      Abrir
                    </button>
                    <select
                      value={ticket.status}
                      onChange={(e) => handleStatusChange(ticket.id, e.target.value as Ticket['status'])}
                      className="bg-slate-800 border border-slate-700 rounded-lg px-2 py-1.5 text-slate-300 focus:outline-none focus:border-indigo-500/50 transition text-xs"
                    >
                      <option value="Aberto">Aberto</option>
                      <option value="Em Análise">Em Análise</option>
                      <option value="Resolvido">Resolvido</option>
                      <option value="Finalizado">Finalizado</option>
                    </select>
                  </div>
                </div>
              </motion.div>
            );
          })
        )}
      </div>

      {/* Ticket Detail Modal */}
      {showTicketModal && selectedTicket && (
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4 z-50">
          <motion.div
            initial={{ opacity: 0, scale: 0.96 }}
            animate={{ opacity: 1, scale: 1 }}
            className="bg-slate-900 border border-slate-800 rounded-2xl p-6 max-w-2xl w-full max-h-[85vh] flex flex-col overflow-hidden"
          >
            <div className="flex justify-between items-center mb-5 flex-shrink-0">
              <h2 className="text-base font-bold text-slate-100">Detalhes do Ticket</h2>
              <button onClick={() => setShowTicketModal(false)} className="p-1.5 rounded-lg text-slate-500 hover:text-white hover:bg-slate-800 transition">
                <FiXCircle className="w-5 h-5" />
              </button>
            </div>

            <div className="flex-1 overflow-y-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {[
                  { label: 'ID', value: <span className="font-mono text-[11px]">{selectedTicket.id}</span> },
                  { label: 'Status', value: (() => { const b = getStatusBadge(selectedTicket.status); return <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${b.cls}`}>{b.icon}{selectedTicket.status}</span>; })() },
                  { label: 'Cliente', value: selectedTicket.user_info?.full_name || '—' },
                  { label: 'Login', value: selectedTicket.user_info?.login || '—' },
                  { label: 'Título', value: selectedTicket.title },
                  { label: 'Prioridade', value: (() => { const p = getPriorityBadge(selectedTicket.priority); return <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-[10px] font-semibold border ${p.cls}`}>{p.label}</span>; })() },
                  { label: 'Criado em', value: new Date(selectedTicket.created_at).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) },
                ].map(({ label, value }) => (
                  <div key={label} className="bg-slate-800/50 rounded-xl p-3">
                    <p className="text-[10px] text-slate-600 uppercase tracking-wider mb-1">{label}</p>
                    <div className="text-sm text-slate-300">{value}</div>
                  </div>
                ))}
              </div>

              {/* Messages */}
              <div>
                <p className="text-[10px] text-slate-600 uppercase tracking-wider mb-2">Mensagens</p>
                <div className="bg-slate-800/50 rounded-xl p-3 max-h-52 overflow-y-auto [scrollbar-width:none] [&::-webkit-scrollbar]:hidden space-y-3">
                  {messages.length === 0 ? (
                    <p className="text-xs text-slate-600">Nenhuma mensagem ainda.</p>
                  ) : (
                    messages.map(msg => (
                      <div key={msg.id} className="border-b border-slate-700/50 pb-2 last:border-0">
                        <p className="text-[10px] text-slate-600">{msg.created_at ? new Date(msg.created_at).toLocaleString('pt-BR') : ''}</p>
                        <p className="text-sm text-slate-300 mt-0.5">{msg.content}</p>
                      </div>
                    ))
                  )}
                </div>
              </div>

              {/* Reply */}
              <div>
                <p className="text-[10px] text-slate-600 uppercase tracking-wider mb-2">Responder</p>
                <textarea
                  value={reply}
                  onChange={e => setReply(e.target.value)}
                  rows={3}
                  className="w-full rounded-xl border border-slate-700 bg-slate-800 text-sm text-slate-200 p-3 focus:outline-none focus:border-indigo-500/50 transition placeholder-slate-600 resize-none"
                  placeholder="Digite sua resposta..."
                  disabled={sendingReply}
                />
                <button
                  onClick={handleSendReply}
                  disabled={sendingReply || !reply.trim()}
                  className="mt-2 flex items-center gap-2 px-4 py-2 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-semibold transition disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <FiSend className="w-3.5 h-3.5" />
                  {sendingReply ? 'Enviando…' : 'Responder'}
                </button>
              </div>
            </div>
          </motion.div>
        </div>
      )}
    </div>
  );
}
