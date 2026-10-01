import React, { useEffect, useState } from 'react';
import { createClient } from '@/utils/supabase/client';
import ConfirmModal from '@/components/ConfirmModal';
import { FiTrash2, FiUserX, FiUserCheck, FiUserPlus, FiUsers, FiChevronLeft, FiChevronRight } from 'react-icons/fi';
import { toast } from 'react-hot-toast';
import { motion } from 'framer-motion';

interface UserProfile {
  id: string;
  login: string | null;
  full_name: string | null;
  phone: string | null;
  city: string | null;
  neighborhood: string | null;
  state: string | null;
  cep: string | null;
  created_at: string;
  is_blocked: boolean;
  is_deleted: boolean;
  email?: string | null;
}

const AdminUsersTab: React.FC = () => {
  const [users, setUsers] = useState<UserProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [showConfirm, setShowConfirm] = useState(false);
  const [confirmMessage, setConfirmMessage] = useState('');
  const [confirmAction, setConfirmAction] = useState<(() => void) | null>(null);

  const [page, setPage] = useState(0);
  const [pageSize] = useState(10);
  const [totalCount, setTotalCount] = useState(0);

  const fetchUsers = async () => {
    setLoading(true);
    const supabase = createClient();
    const { data, error, count } = await supabase
      .from('profiles')
      .select('*', { count: 'exact' })
      .order('created_at', { ascending: false })
      .range(page * pageSize, (page + 1) * pageSize - 1);
    if (!error && data) {
      setUsers(data);
      if (count !== null) setTotalCount(count);
    }
    setLoading(false);
  };

  useEffect(() => { fetchUsers(); }, [page]);

  const handleBlockToggle = async (user: UserProfile) => {
    setActionLoading(user.id);
    const supabase = createClient();
    await supabase.from('profiles').update({ is_blocked: !user.is_blocked, updated_at: new Date().toISOString() }).eq('id', user.id);
    await fetchUsers();
    setActionLoading(null);
    toast.success(user.is_blocked ? 'Usuário desbloqueado!' : 'Usuário bloqueado!');
  };

  const handleDeleteToggle = (user: UserProfile) => {
    setConfirmMessage(user.is_deleted ? 'Restaurar este usuário?' : 'Excluir este usuário?');
    setConfirmAction(() => async () => {
      setActionLoading(user.id);
      const supabase = createClient();
      const { error } = await supabase.from('profiles').update({
        is_deleted: !user.is_deleted,
        updated_at: new Date().toISOString()
      }).eq('id', user.id);
      if (error) {
        toast.error('Erro ao atualizar usuário: ' + error.message);
        setActionLoading(null);
        return;
      }
      await fetchUsers();
      setActionLoading(null);
      setShowConfirm(false);
      toast.success(user.is_deleted ? 'Usuário restaurado!' : 'Usuário excluído!');
    });
    setShowConfirm(true);
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  if (loading && users.length === 0) {
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
          <h1 className="text-lg font-bold text-slate-100">Gerenciar Usuários</h1>
          <p className="text-xs text-slate-500 mt-0.5">{totalCount} usuário(s) cadastrado(s) • Pág. {page + 1}/{totalPages || 1}</p>
        </div>
        <div className="flex items-center gap-1.5">
          <FiUsers className="w-4 h-4 text-emerald-400" />
          <span className="text-xs text-slate-500">{totalCount} total</span>
        </div>
      </div>

      {/* Table */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl overflow-hidden">
        <div className="overflow-x-auto">
          <table className="min-w-full">
            <thead>
              <tr className="border-b border-slate-800 bg-slate-800/30">
                <th className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Login</th>
                <th className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Nome Completo</th>
                <th className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest hidden md:table-cell">E-mail</th>
                <th className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest hidden lg:table-cell">Cidade/UF</th>
                <th className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Status</th>
                <th className="px-4 py-3 text-left text-[10px] font-semibold text-slate-500 uppercase tracking-widest">Ações</th>
              </tr>
            </thead>
            <tbody>
              {users.map((user, index) => (
                <motion.tr
                  key={user.id}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ delay: index * 0.04 }}
                  className="border-b border-slate-800/50 hover:bg-slate-800/20 transition"
                >
                  <td className="px-4 py-3 text-sm text-slate-300 font-medium">{user.login || '—'}</td>
                  <td className="px-4 py-3 text-sm text-slate-200 font-medium">{user.full_name || '—'}</td>
                  <td className="px-4 py-3 text-sm text-slate-500 hidden md:table-cell">{user.email || '—'}</td>
                  <td className="px-4 py-3 text-sm text-slate-500 hidden lg:table-cell">
                    {user.city ? `${user.city}/${user.state}` : '—'}
                  </td>
                  <td className="px-4 py-3">
                    {user.is_deleted ? (
                      <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-slate-500/10 text-slate-500 border border-slate-500/20 uppercase">
                        Removido
                      </span>
                    ) : user.is_blocked ? (
                      <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-rose-500/10 text-rose-400 border border-rose-500/20 uppercase">
                        Bloqueado
                      </span>
                    ) : (
                      <span className="inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 uppercase">
                        Ativo
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1.5">
                      <button
                        title={user.is_blocked ? 'Desbloquear' : 'Bloquear'}
                        className={`p-1.5 rounded-lg transition-all ${
                          user.is_blocked
                            ? 'bg-emerald-500/10 text-emerald-400 hover:bg-emerald-500/20'
                            : 'bg-amber-500/10 text-amber-400 hover:bg-amber-500/20'
                        }`}
                        onClick={() => handleBlockToggle(user)}
                        disabled={actionLoading === user.id}
                      >
                        {user.is_blocked ? <FiUserCheck size={14} /> : <FiUserX size={14} />}
                      </button>
                      <button
                        title={user.is_deleted ? 'Restaurar' : 'Excluir'}
                        className={`p-1.5 rounded-lg transition-all ${
                          user.is_deleted
                            ? 'bg-indigo-500/10 text-indigo-400 hover:bg-indigo-500/20'
                            : 'bg-rose-500/10 text-rose-400 hover:bg-rose-500/20'
                        }`}
                        onClick={() => handleDeleteToggle(user)}
                        disabled={actionLoading === user.id}
                      >
                        {user.is_deleted ? <FiUserPlus size={14} /> : <FiTrash2 size={14} />}
                      </button>
                    </div>
                  </td>
                </motion.tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        <div className="flex items-center justify-between px-4 py-3 border-t border-slate-800 bg-slate-800/20">
          <button
            onClick={() => setPage(p => Math.max(0, p - 1))}
            disabled={page === 0 || loading}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-slate-400 bg-slate-800 hover:bg-slate-700 rounded-lg disabled:opacity-40 disabled:cursor-not-allowed transition"
          >
            <FiChevronLeft className="w-3.5 h-3.5" />
            Anterior
          </button>
          <div className="flex gap-1.5">
            {[...Array(totalPages)].map((_, i) => (
              <button
                key={i}
                onClick={() => setPage(i)}
                className={`w-7 h-7 rounded-lg text-xs font-bold transition-all ${
                  page === i
                    ? 'bg-indigo-600 text-white shadow-lg shadow-indigo-500/20'
                    : 'bg-slate-800 text-slate-500 hover:bg-slate-700 hover:text-slate-300'
                }`}
              >
                {i + 1}
              </button>
            )).slice(Math.max(0, page - 2), Math.min(totalPages, page + 3))}
          </div>
          <button
            onClick={() => setPage(p => p + 1)}
            disabled={(page + 1) * pageSize >= totalCount || loading}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-slate-400 bg-slate-800 hover:bg-slate-700 rounded-lg disabled:opacity-40 disabled:cursor-not-allowed transition"
          >
            Próxima
            <FiChevronRight className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      <ConfirmModal
        open={showConfirm}
        message={confirmMessage}
        confirmText="Confirmar"
        cancelText="Cancelar"
        onConfirm={() => { if (confirmAction) confirmAction(); }}
        onCancel={() => setShowConfirm(false)}
      />
    </div>
  );
};

export default AdminUsersTab;
