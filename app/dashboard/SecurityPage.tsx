'use client';

import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import VoltrisIcon from '@/components/dashboard/VoltrisIcon';
import VoltrisIconTile, { DASHBOARD_ACCENT } from '@/components/dashboard/VoltrisIconTile';
import { createClient } from '@/utils/supabase/client';
import { toast } from 'react-hot-toast';
import { useDashboard } from '@/app/context/DashboardContext';

export default function SecurityPage() {
  const supabase = createClient();
  const { transparencyMode } = useDashboard();
  
  const [factors, setFactors] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [isEnrolling, setIsEnrolling] = useState(false);
  const [enrollData, setEnrollData] = useState<any>(null);
  const [verifyCode, setVerifyCode] = useState('');
  const [isVerifying, setIsVerifying] = useState(false);

  const fetchFactors = async () => {
    try {
      const { data, error } = await supabase.auth.mfa.listFactors();
      if (error) throw error;
      setFactors(data.all || []);
    } catch (err) {
      console.error('Error fetching factors:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchFactors();
  }, []);

  const onEnroll = async () => {
    setIsEnrolling(true);
    try {
      // 1. Limpeza de fatores não verificados para evitar erro de "friendly name already exists"
      const { data: existingFactors } = await supabase.auth.mfa.listFactors();
      if (existingFactors?.all) {
        const unverified = existingFactors.all.filter(f => f.status === 'unverified');
        for (const f of unverified) {
          await supabase.auth.mfa.unenroll({ factorId: f.id });
        }
      }

      // 2. Iniciar novo enrollment
      const { data, error } = await supabase.auth.mfa.enroll({
        factorType: 'totp',
        issuer: 'Voltris',
        friendlyName: 'Dispositivo Principal'
      });
      if (error) throw error;
      setEnrollData(data);
    } catch (err: any) {
      toast.error('Erro ao iniciar ativação: ' + err.message);
      setIsEnrolling(false);
    }
  };

  const onVerify = async () => {
    if (verifyCode.length < 6) return;
    setIsVerifying(true);
    try {
      const { data, error } = await supabase.auth.mfa.challengeAndVerify({
        factorId: enrollData.id,
        code: verifyCode
      });
      if (error) throw error;
      
      toast.success('Google Authenticator ativado com sucesso!');
      setIsEnrolling(false);
      setEnrollData(null);
      setVerifyCode('');
      fetchFactors();
    } catch (err: any) {
      toast.error('Código inválido ou expirado.');
    } finally {
      setIsVerifying(false);
    }
  };

  const onUnenroll = async (factorId: string) => {
    if (!confirm('Tem certeza que deseja desativar a proteção 2FA? Sua conta ficará menos segura.')) return;
    try {
      const { error } = await supabase.auth.mfa.unenroll({ factorId });
      if (error) throw error;
      toast.success('Proteção 2FA desativada.');
      fetchFactors();
    } catch (err: any) {
      toast.error('Erro ao desativar: ' + err.message);
    }
  };

  if (loading) return null;

  const activeFactor = factors.find(f => f.status === 'verified');

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div className={`p-6 sm:p-8 rounded-2xl border ${transparencyMode ? 'voltris-glass' : 'bg-slate-900/60 border-slate-800 shadow-xl'}`}>
        <div className="space-y-6">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-6 border-b border-slate-800">
            <div className="flex items-center gap-3.5">
              <VoltrisIconTile
                icon="security"
                tone="gradient"
                size={12}
                accent={activeFactor ? DASHBOARD_ACCENT.success : DASHBOARD_ACCENT.brand}
              />
              <div>
                <h2 className="text-xl font-bold text-white tracking-tight">Central de Segurança</h2>
                <p className="text-xs text-slate-400 mt-0.5">Autenticação de Dois Fatores (MFA / 2FA)</p>
              </div>
            </div>
            
            {activeFactor && (
              <div className="px-3 py-1.5 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 flex items-center gap-2 self-start sm:self-center">
                <div className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse"></div>
                <span className="text-xs font-semibold">2FA Ativo</span>
              </div>
            )}
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div className="space-y-3">
              <h3 className="text-white font-semibold text-xs uppercase tracking-wider flex items-center gap-2">
                <VoltrisIcon name="lock" size={12} className="text-indigo-400" /> Por que ativar o 2FA?
              </h3>
              <ul className="space-y-2.5">
                {[
                  'Proteção contra roubo e vazamento de senhas',
                  'Acesso exclusivo via aplicativo no celular',
                  'Padrão internacional de segurança TOTP',
                  'Bloqueia invasões mesmo com a senha vazada'
                ].map((item, i) => (
                  <li key={i} className="flex items-center gap-2.5 text-slate-400 text-xs font-medium">
                    <VoltrisIcon name="success" size={16} className="text-emerald-400 shrink-0" />
                    <span>{item}</span>
                  </li>
                ))}
              </ul>
            </div>

            <div className={`p-6 rounded-xl border ${activeFactor ? 'bg-emerald-500/5 border-emerald-500/20' : 'bg-slate-950/40 border-slate-800'} flex flex-col justify-center items-center gap-4 text-center`}>
              {activeFactor ? (
                <>
                  <VoltrisIconTile icon="smartphone" accent={DASHBOARD_ACCENT.success} size={12} bordered={false} />
                  <div>
                    <h4 className="text-white font-semibold text-sm">Google Authenticator Ativo</h4>
                    <p className="text-slate-400 text-xs mt-1">Sua conta está protegida por verificação em duas etapas.</p>
                  </div>
                  <button 
                    onClick={() => onUnenroll(activeFactor.id)}
                    className="text-rose-400 hover:text-rose-300 text-xs font-medium flex items-center gap-1.5 transition-colors mt-2"
                  >
                    <VoltrisIcon name="trash" size={14} className="text-rose-400" /> Desativar Proteção
                  </button>
                </>
              ) : (
                <>
                  <VoltrisIconTile icon="alertTriangle" accent={DASHBOARD_ACCENT.warning} size={12} bordered={false} />
                  <div>
                    <h4 className="text-white font-semibold text-sm">2FA Desativado</h4>
                    <p className="text-slate-400 text-xs mt-1">Recomendamos ativar o Google Authenticator para blindar sua conta.</p>
                  </div>
                  {!isEnrolling && (
                    <button 
                      onClick={onEnroll}
                      className="w-full py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 transition-all active:scale-95"
                    >
                      Configurar 2FA Agora
                    </button>
                  )}
                </>
              )}
            </div>
          </div>

          <AnimatePresence>
            {isEnrolling && enrollData && (
              <motion.div 
                initial={{ opacity: 0, y: 15 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 15 }}
                className="bg-slate-950/60 rounded-xl p-6 border border-indigo-500/30 space-y-6"
              >
                <div className="flex flex-col lg:flex-row items-center gap-6">
                  <div className="bg-white p-3 rounded-2xl shadow-lg w-44 h-44 flex items-center justify-center shrink-0">
                    <img 
                      src={`https://api.qrserver.com/v1/create-qr-code/?size=250x250&data=${encodeURIComponent(enrollData.totp.uri)}`}
                      alt="MFA QR Code"
                      className="w-full h-full"
                    />
                  </div>
                  
                  <div className="space-y-4 flex-1 text-center lg:text-left">
                    <div>
                      <h4 className="text-base font-bold text-white tracking-tight">Escaneie o QR Code</h4>
                      <p className="text-slate-400 text-xs leading-relaxed mt-1">
                        1. Abra o app <strong className="text-white">Google Authenticator</strong> no seu celular.<br />
                        2. Toque em <strong className="text-white">+</strong> e selecione <strong className="text-white">Ler código QR</strong>.<br />
                        3. Digite abaixo o código de 6 dígitos gerado pelo aplicativo.
                      </p>
                    </div>
                    
                    <div className="flex flex-col sm:flex-row items-center gap-3">
                      <div className="bg-slate-900 border border-slate-700 rounded-xl px-3.5 py-2 flex items-center gap-2.5 w-full sm:w-48 focus-within:border-indigo-400 transition-all">
                        <VoltrisIcon name="key" size={16} className="text-slate-400 shrink-0" />
                        <input 
                          type="text" 
                          placeholder="000000" 
                          maxLength={6}
                          value={verifyCode}
                          onChange={e => setVerifyCode(e.target.value.replace(/\D/g, ''))}
                          className="bg-transparent w-full text-white text-base font-bold tracking-[0.3em] outline-none placeholder:tracking-normal placeholder:text-slate-600"
                        />
                      </div>
                      <button 
                        onClick={onVerify}
                        disabled={verifyCode.length < 6 || isVerifying}
                        className="w-full sm:w-auto px-6 py-2.5 bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-xs rounded-xl shadow-md shadow-indigo-600/20 disabled:opacity-50 transition-all"
                      >
                        {isVerifying ? 'Verificando...' : 'Confirmar e Ativar'}
                      </button>
                    </div>
                  </div>
                </div>

                <div className="pt-4 border-t border-slate-800">
                  <button 
                    onClick={() => setIsEnrolling(false)} 
                    className="text-slate-400 hover:text-white text-xs font-medium transition-colors"
                  >
                    Cancelar Ativação
                  </button>
                </div>
              </motion.div>
            )}
          </AnimatePresence>

        </div>
      </div>
    </div>
  );
}
