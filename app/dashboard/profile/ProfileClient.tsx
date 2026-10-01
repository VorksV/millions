'use client';

import { useState, useEffect, useMemo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { toast } from 'react-hot-toast';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';
import { createClient } from '@/utils/supabase/client';
import { useAuth } from '@/app/hooks/useAuth';
import AuthGuard from '@/components/AuthGuard';
import { useDashboard } from '@/app/context/DashboardContext';

interface Profile {
  id: string;
  email: string;
  full_name?: string;
  phone?: string;
  address?: string;
  city?: string;
  state?: string;
  cep?: string;
  created_at: string;
  updated_at: string;
}

export default function ProfileClient() {
  const { transparencyMode } = useDashboard();
  const { user } = useAuth();
  const [profile, setProfile] = useState<Profile | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState({
    full_name: '',
    phone: '',
    address: '',
    city: '',
    state: '',
    cep: ''
  });
  const supabase = useMemo(() => createClient(), []);

  useEffect(() => {
    const fetchProfile = async () => {
      if (!user) return;
      try {
        const { data, error } = await supabase
          .from('profiles')
          .select('*')
          .eq('id', user.id)
          .single();

        if (error) throw error;
        setProfile(data);
        setFormData({
          full_name: data.full_name || '',
          phone: data.phone || '',
          address: data.address || '',
          city: data.city || '',
          state: data.state || '',
          cep: data.cep || ''
        });
      } catch (error) {
        console.error('Error fetching profile:', error);
      } finally {
        setLoading(false);
      }
    };
    fetchProfile();
  }, [user, supabase]);

  const handleSave = async () => {
    if (!user) return;
    setSaving(true);
    try {
      const { error } = await supabase
        .from('profiles')
        .upsert({
          id: user.id,
          ...formData,
          updated_at: new Date().toISOString()
        });

      if (error) throw error;

      toast.success('Perfil atualizado com sucesso!');
      setIsEditing(false);
      
      const { data: updatedData } = await supabase
        .from('profiles')
        .select('*')
        .eq('id', user.id)
        .single();
      setProfile(updatedData);
    } catch (error) {
      toast.error('Erro ao salvar perfil');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center py-32 gap-3 text-slate-400">
        <div className="w-8 h-8 rounded-full border-2 border-slate-700 border-t-indigo-500 animate-spin"></div>
        <p className="text-xs font-medium text-slate-400">Carregando dados do usuário...</p>
      </div>
    );
  }

  return (
    <AuthGuard>
      <div className="flex flex-col gap-6 w-full max-w-full">
        
        {/* Header */}
        <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 pb-4 border-b border-slate-800/80">
          <div>
            <h2 className="text-xl sm:text-2xl font-bold text-white tracking-tight">Meu Perfil</h2>
            <p className="text-xs text-slate-400 mt-0.5">Gerenciamento de credenciais, informações de contato e endereço</p>
          </div>

          {!isEditing ? (
            <button
              onClick={() => setIsEditing(true)}
              className="px-4 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all flex items-center gap-2 active:scale-95"
            >
              <VoltrisIcon name="edit" size={14} />
              <span>Editar Informações</span>
            </button>
          ) : (
            <div className="flex items-center gap-2.5">
              <button
                onClick={() => setIsEditing(false)}
                className="px-3.5 py-2 rounded-xl bg-slate-800 text-slate-300 font-semibold text-xs hover:bg-slate-700 transition-colors"
              >
                Cancelar
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className="px-4 py-2 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all flex items-center gap-2 disabled:opacity-50"
              >
                <VoltrisIcon name="save" size={14} />
                <span>{saving ? 'Salvando...' : 'Salvar Alterações'}</span>
              </button>
            </div>
          )}
        </div>

        {/* Profile Card */}
        <div className={`p-6 sm:p-7 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'}`}>
          <div className="flex flex-col sm:flex-row items-center sm:items-start gap-6 text-center sm:text-left">
             <div className="relative shrink-0">
                <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-indigo-500 to-slate-700 flex items-center justify-center text-white text-2xl font-bold shadow-lg shadow-indigo-500/10">
                  {user?.email?.[0]?.toUpperCase() || 'U'}
                </div>
                <div className="absolute -bottom-1 -right-1 w-6 h-6 rounded-full bg-emerald-500 border-2 border-slate-900 flex items-center justify-center text-slate-950">
                  <VoltrisIcon name="check" size={14} />
                </div>
             </div>

             <div className="flex-1 space-y-2 min-w-0">
                <div className="space-y-0.5">
                   <h3 className="text-lg font-bold text-white tracking-tight truncate">{profile?.full_name || 'Nome não configurado'}</h3>
                   <p className="text-xs text-slate-400 font-mono truncate">{user?.email}</p>
                </div>

                <div className="flex flex-wrap items-center justify-center sm:justify-start gap-2 pt-1">
                   <div className="flex items-center gap-1.5 px-2.5 py-1 bg-slate-950/60 rounded-lg border border-slate-800 text-[11px] font-mono text-slate-300">
                      <VoltrisIcon name="system" size={12} className="text-indigo-400" />
                      <span>ID: {user?.id.slice(0, 8).toUpperCase()}</span>
                   </div>
                   <div className="flex items-center gap-1.5 px-2.5 py-1 bg-emerald-500/10 rounded-lg border border-emerald-500/20 text-[11px] font-medium text-emerald-400">
                      <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
                      <span>Conta Ativa</span>
                   </div>
                </div>
             </div>
          </div>
        </div>

        {/* Form Details Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          
          {/* Personal Info */}
          <div className={`p-6 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'} space-y-4`}>
            <div className="flex items-center gap-2.5 pb-3 border-b border-slate-800/80">
               <VoltrisIconTile icon="person" accent={DASHBOARD_ACCENT.brand} size={8} />
               <div>
                 <h3 className="text-sm font-bold text-white tracking-tight">Dados Pessoais</h3>
                 <p className="text-[11px] text-slate-500">Informações cadastrais e contato principal</p>
               </div>
            </div>

            <div className="space-y-4">
               <div className="space-y-1.5">
                 <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">E-mail Cadastrado</label>
                 <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/50 border border-slate-800 text-xs text-slate-400 font-mono">
                    {user?.email}
                 </div>
               </div>

               <div className="space-y-1.5">
                 <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Nome Completo</label>
                 {isEditing ? (
                    <input
                      type="text"
                      value={formData.full_name}
                      onChange={(e) => setFormData({ ...formData, full_name: e.target.value })}
                      className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors"
                      placeholder="Seu nome completo"
                    />
                 ) : (
                    <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/30 border border-slate-800/80 text-xs text-slate-200 font-medium">
                      {profile?.full_name || 'Não informado'}
                    </div>
                 )}
               </div>

               <div className="space-y-1.5">
                 <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Telefone / WhatsApp</label>
                 {isEditing ? (
                    <input
                      type="tel"
                      value={formData.phone}
                      onChange={(e) => setFormData({ ...formData, phone: e.target.value })}
                      className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors"
                      placeholder="(00) 00000-0000"
                    />
                 ) : (
                    <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/30 border border-slate-800/80 text-xs text-slate-200 font-medium">
                      {profile?.phone || 'Não informado'}
                    </div>
                 )}
               </div>
            </div>
          </div>

          {/* Location Info */}
          <div className={`p-6 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'} space-y-4`}>
            <div className="flex items-center gap-2.5 pb-3 border-b border-slate-800/80">
                 <VoltrisIconTile icon="mapPin" accent={DASHBOARD_ACCENT.success} size={8} />
               <div>
                 <h3 className="text-sm font-bold text-white tracking-tight">Endereço & Localização</h3>
                 <p className="text-[11px] text-slate-500">Dados para emissão de notas fiscais e suporte</p>
               </div>
            </div>

            <div className="space-y-4">
               <div className="space-y-1.5">
                 <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Endereço Residencial</label>
                 {isEditing ? (
                    <input
                      type="text"
                      value={formData.address}
                      onChange={(e) => setFormData({ ...formData, address: e.target.value })}
                      className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors"
                      placeholder="Rua, número, complemento, bairro"
                    />
                 ) : (
                    <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/30 border border-slate-800/80 text-xs text-slate-200 font-medium truncate">
                      {profile?.address || 'Não informado'}
                    </div>
                 )}
               </div>

               <div className="grid grid-cols-2 gap-3">
                 <div className="space-y-1.5">
                   <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Cidade</label>
                   {isEditing ? (
                      <input
                        type="text"
                        value={formData.city}
                        onChange={(e) => setFormData({ ...formData, city: e.target.value })}
                        className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors"
                        placeholder="Sua cidade"
                      />
                   ) : (
                      <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/30 border border-slate-800/80 text-xs text-slate-200 font-medium">
                        {profile?.city || 'Não informado'}
                      </div>
                   )}
                 </div>
                 <div className="space-y-1.5">
                   <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">Estado (UF)</label>
                   {isEditing ? (
                      <input
                        type="text"
                        maxLength={2}
                        value={formData.state}
                        onChange={(e) => setFormData({ ...formData, state: e.target.value.toUpperCase() })}
                        className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors uppercase"
                        placeholder="UF"
                      />
                   ) : (
                      <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/30 border border-slate-800/80 text-xs text-slate-200 font-medium">
                        {profile?.state || 'Não informado'}
                      </div>
                   )}
                 </div>
               </div>

               <div className="space-y-1.5">
                 <label className="text-[11px] font-semibold text-slate-400 uppercase tracking-wider block">CEP</label>
                 {isEditing ? (
                    <input
                      type="text"
                      value={formData.cep}
                      onChange={(e) => setFormData({ ...formData, cep: e.target.value })}
                      className="w-full px-3.5 py-2 rounded-xl bg-slate-950 border border-slate-700 text-white text-xs focus:border-indigo-500 outline-none transition-colors"
                      placeholder="00000-000"
                    />
                 ) : (
                    <div className="px-3.5 py-2.5 rounded-xl bg-slate-950/30 border border-slate-800/80 text-xs text-slate-200 font-medium">
                      {profile?.cep || 'Não informado'}
                    </div>
                 )}
               </div>
            </div>
          </div>
        </div>

      </div>
    </AuthGuard>
  );
}
