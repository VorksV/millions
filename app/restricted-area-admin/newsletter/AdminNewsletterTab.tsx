import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import { createClient } from '@/utils/supabase/client';
import { FiSearch, FiMail, FiTrash2, FiDownload, FiCheckCircle, FiXCircle } from 'react-icons/fi';
import { toast } from 'react-hot-toast';
import ConfirmModal from '@/components/ConfirmModal';

interface NewsletterSubscriber {
  id: string;
  email: string;
  source: 'site' | 'blog';
  subscribed_at: string;
  status: 'active' | 'inactive';
  created_at: string;
}

export default function AdminNewsletterTab() {
  const [subscribers, setSubscribers] = useState<NewsletterSubscriber[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('all');
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedSubscribers, setSelectedSubscribers] = useState<string[]>([]);
  const [showConfirm, setShowConfirm] = useState(false);
  const [confirmMessage, setConfirmMessage] = useState('');
  const [confirmAction, setConfirmAction] = useState<(() => void) | null>(null);
  const supabase = createClient();

  useEffect(() => { fetchSubscribers(); }, []);

  const fetchSubscribers = async () => {
    try {
      setLoading(true);
      const { data, error } = await supabase
        .from('newsletter_subscribers')
        .select('*')
        .order('created_at', { ascending: false });
      if (error) throw error;
      setSubscribers(data || []);
    } catch (error) {
      console.error('Erro ao buscar inscritos:', error);
      toast.error('Erro ao carregar inscritos na newsletter');
    } finally {
      setLoading(false);
    }
  };

  const handleStatusChange = async (subscriberId: string, newStatus: 'active' | 'inactive') => {
    try {
      const { error } = await supabase
        .from('newsletter_subscribers')
        .update({ status: newStatus })
        .eq('id', subscriberId);
      if (error) throw error;
      toast.success('Status atualizado!');
      fetchSubscribers();
    } catch (error) {
      toast.error('Erro ao atualizar status');
    }
  };

  const handleDeleteSubscriber = async (subscriberId: string) => {
    setConfirmMessage('Tem certeza que deseja remover este inscrito?');
    setConfirmAction(() => async () => {
      try {
        const { error } = await supabase.from('newsletter_subscribers').delete().eq('id', subscriberId);
        if (error) throw error;
        toast.success('Inscrito removido!');
        fetchSubscribers();
      } catch (error) {
        toast.error('Erro ao remover inscrito');
      }
      setShowConfirm(false);
    });
    setShowConfirm(true);
  };

  const handleBulkDelete = async () => {
    if (selectedSubscribers.length === 0) { toast.error('Selecione pelo menos um inscrito'); return; }
    setConfirmMessage(`Remover ${selectedSubscribers.length} inscrito(s)?`);
    setConfirmAction(() => async () => {
      try {
        const { error } = await supabase.from('newsletter_subscribers').delete().in('id', selectedSubscribers);
        if (error) throw error;
        toast.success(`${selectedSubscribers.length} inscrito(s) removido(s)!`);
        setSelectedSubscribers([]);
        fetchSubscribers();
      } catch (error) {
        toast.error('Erro ao remover inscritos');
      }
      setShowConfirm(false);
    });
    setShowConfirm(true);
  };

  const handleSelectAll = () => {
    if (selectedSubscribers.length === filteredSubscribers.length) setSelectedSubscribers([]);
    else setSelectedSubscribers(filteredSubscribers.map(s => s.id));
  };

  const handleSelectSubscriber = (subscriberId: string) => {
    setSelectedSubscribers(prev =>
      prev.includes(subscriberId) ? prev.filter(id => id !== subscriberId) : [...prev, subscriberId]
    );
  };

  const exportToCSV = () => {
    const headers = ['Email', 'Fonte', 'Status', 'Data de Inscrição'];
    const csvContent = [
      headers.join(','),
      ...filteredSubscribers.map(s => [
        s.email,
        s.source === 'site' ? 'Site' : 'Blog',
        s.status === 'active' ? 'Ativo' : 'Inativo',
        new Date(s.subscribed_at).toLocaleDateString('pt-BR')
      ].join(','))
    ].join('\n');
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.setAttribute('href', url);
    link.setAttribute('download', `newsletter_subscribers_${new Date().toISOString().split('T')[0]}.csv`);
    link.style.visibility = 'hidden';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
  };

  const filteredSubscribers = subscribers.filter(subscriber => {
    const matchesFilter = filter === 'all' || subscriber.status === filter || subscriber.source === filter;
    const matchesSearch = subscriber.email.toLowerCase().includes(searchTerm.toLowerCase());
    return matchesFilter && matchesSearch;
  });

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
          <h1 className="text-lg font-bold text-slate-100">Gerenciar Newsletter</h1>
          <p className="text-xs text-slate-500 mt-0.5">{subscribers.length} inscrito(s) no total</p>
        </div>
        <button
          onClick={exportToCSV}
          className="flex items-center gap-2 px-4 py-2 bg-slate-800 hover:bg-slate-700 border border-slate-700 text-slate-300 rounded-xl transition text-xs font-medium"
        >
          <FiDownload className="w-3.5 h-3.5" />
          Exportar CSV
        </button>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="flex-1 relative">
          <FiSearch className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-500 w-3.5 h-3.5" />
          <input
            type="text"
            placeholder="Buscar por email..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            className="w-full pl-9 pr-4 py-2 bg-slate-900 border border-slate-800 rounded-xl text-sm text-slate-200 placeholder-slate-600 focus:outline-none focus:border-indigo-500/50 transition"
          />
        </div>
        <select
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          className="bg-slate-900 border border-slate-800 rounded-xl px-3 py-2 text-sm text-slate-300 focus:outline-none focus:border-indigo-500/50 transition"
        >
          <option value="all">Todos</option>
          <option value="active">Ativos</option>
          <option value="inactive">Inativos</option>
          <option value="site">Site</option>
          <option value="blog">Blog</option>
        </select>
      </div>

      {/* Bulk Action Bar */}
      {selectedSubscribers.length > 0 && (
        <motion.div
          initial={{ opacity: 0, y: -8 }}
          animate={{ opacity: 1, y: 0 }}
          className="bg-rose-500/10 border border-rose-500/20 rounded-xl p-3 flex items-center justify-between"
        >
          <span className="text-sm text-rose-300">{selectedSubscribers.length} selecionado(s)</span>
          <button
            onClick={handleBulkDelete}
            className="flex items-center gap-1.5 px-3 py-1.5 bg-rose-500/20 hover:bg-rose-500/30 text-rose-400 border border-rose-500/20 rounded-lg transition text-xs font-medium"
          >
            <FiTrash2 className="w-3.5 h-3.5" />
            Remover Selecionados
          </button>
        </motion.div>
      )}

      {/* Subscribers Table */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden">
        {/* Header row */}
        <div className="flex items-center gap-4 px-4 py-3 border-b border-slate-800 bg-slate-800/30">
          <input
            type="checkbox"
            checked={selectedSubscribers.length === filteredSubscribers.length && filteredSubscribers.length > 0}
            onChange={handleSelectAll}
            className="w-4 h-4 accent-indigo-500 rounded"
          />
          <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-widest">
            Selecionar todos ({filteredSubscribers.length})
          </span>
        </div>

        {filteredSubscribers.length === 0 ? (
          <div className="text-center py-16">
            <div className="w-12 h-12 mx-auto mb-3 bg-slate-800 rounded-xl flex items-center justify-center">
              <FiMail className="w-5 h-5 text-slate-600" />
            </div>
            <p className="text-sm text-slate-500 font-medium">Nenhum inscrito encontrado</p>
          </div>
        ) : (
          filteredSubscribers.map((subscriber, index) => (
            <motion.div
              key={subscriber.id}
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ delay: index * 0.03 }}
              className="flex items-center gap-4 px-4 py-3 border-b border-slate-800/50 last:border-0 hover:bg-slate-800/30 transition"
            >
              <input
                type="checkbox"
                checked={selectedSubscribers.includes(subscriber.id)}
                onChange={() => handleSelectSubscriber(subscriber.id)}
                className="w-4 h-4 accent-indigo-500 rounded flex-shrink-0"
              />
              <div className="flex-1 min-w-0">
                <div className="flex flex-wrap items-center gap-2 mb-1">
                  <p className="text-sm font-medium text-slate-200 truncate">{subscriber.email}</p>
                  <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold border ${subscriber.status === 'active' ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' : 'bg-rose-500/10 text-rose-400 border-rose-500/20'}`}>
                    {subscriber.status === 'active' ? <FiCheckCircle className="w-2.5 h-2.5" /> : <FiXCircle className="w-2.5 h-2.5" />}
                    {subscriber.status === 'active' ? 'Ativo' : 'Inativo'}
                  </span>
                  <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-semibold border ${subscriber.source === 'site' ? 'bg-indigo-500/10 text-indigo-400 border-indigo-500/20' : 'bg-violet-500/10 text-violet-400 border-violet-500/20'}`}>
                    {subscriber.source === 'site' ? 'Site' : 'Blog'}
                  </span>
                </div>
                <p className="text-[10px] text-slate-600">
                  Inscrito em: {new Date(subscriber.subscribed_at).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' })}
                </p>
              </div>
              <div className="flex items-center gap-2 flex-shrink-0">
                <select
                  value={subscriber.status}
                  onChange={(e) => handleStatusChange(subscriber.id, e.target.value as 'active' | 'inactive')}
                  className="bg-slate-800 border border-slate-700 rounded-lg px-2 py-1.5 text-slate-300 text-xs focus:outline-none focus:border-indigo-500/50 transition"
                >
                  <option value="active">Ativo</option>
                  <option value="inactive">Inativo</option>
                </select>
                <button
                  onClick={() => handleDeleteSubscriber(subscriber.id)}
                  className="p-1.5 rounded-lg text-slate-600 hover:text-rose-400 hover:bg-rose-500/10 transition"
                  title="Remover"
                >
                  <FiTrash2 className="w-3.5 h-3.5" />
                </button>
              </div>
            </motion.div>
          ))
        )}
      </div>

      <ConfirmModal
        open={showConfirm}
        message={confirmMessage}
        confirmText="Remover"
        cancelText="Cancelar"
        onConfirm={() => { if (confirmAction) confirmAction(); }}
        onCancel={() => setShowConfirm(false)}
      />
    </div>
  );
}
