'use client';

import { useEffect, Suspense, useState, useCallback, useMemo } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { createClient } from '@/utils/supabase/client';
import { useAuth } from '@/app/hooks/useAuth';
import { Loader2, ShieldCheck, Cpu, Globe, Lock, ArrowRight } from 'lucide-react';
import { FiAlertCircle, FiCheckCircle } from 'react-icons/fi';
import Link from 'next/link';
import { motion, AnimatePresence } from 'framer-motion';

function LinkDeviceContent() {
    const { user, loading: authLoading } = useAuth();
    const supabase = useMemo(() => createClient(), []);
    const router = useRouter();
    const searchParams = useSearchParams();
    const installationId = searchParams.get('installation_id');
    
    const [status, setStatus] = useState<'checking' | 'linking' | 'success' | 'error'>('checking');
    const [localError, setLocalError] = useState<string | null>(null);
    const [progress, setProgress] = useState(0);

    const handleLinking = useCallback(async (userId: string) => {
        if (!installationId) {
            router.push('/dashboard?tab=pc');
            return;
        }

        try {
            setStatus('linking');
            // Simular progresso visual para parecer profissional e dar tempo de ver a animação
            const interval = setInterval(() => {
                setProgress(prev => {
                    if (prev >= 90) {
                        clearInterval(interval);
                        return 90;
                    }
                    return prev + 10;
                });
            }, 200);

            const { data: sessionData } = await supabase.auth.getSession();
            const token = sessionData?.session?.access_token;

            const response = await fetch('/api/v1/install/link', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify({
                    installation_id: installationId,
                    user_id: userId
                })
            });

            clearInterval(interval);
            setProgress(100);

            const responseData = await response.json();

            if (!response.ok) {
                setStatus('error');
                setLocalError(responseData.error || 'Erro na comunicação com o servidor de segurança.');
                return;
            }

            setStatus('success');
            
            // Aguardar um pouco no sucesso para o usuário apreciar a UI antes de redirecionar
            setTimeout(() => {
                router.push('/dashboard?tab=pc&linked=true');
            }, 2500);

        } catch (err: any) {
            console.error('[LINK_DEVICE] Erro:', err);
            setStatus('error');
            setLocalError(err.message || 'Falha na conexão segura com o endpoint Voltris.');
        }
    }, [installationId, router]);

    useEffect(() => {
        // 1. Se ainda está carregando auth, não faz nada
        if (authLoading) return;

        // 2. Se não tem usuário, redirecionar para login
        if (!user) {
            const redirectUrl = `/auth/link-device${installationId ? `?installation_id=${installationId}` : ''}`;
            router.push(`/login?redirect=${encodeURIComponent(redirectUrl)}&installation_id=${installationId || ''}`);
            return;
        }

        // 3. Se tem usuário e instalação, vincular
        if (status === 'checking' && user && installationId) {
            handleLinking(user.id);
        } else if (!installationId) {
            setStatus('error');
            setLocalError('ID de Instalação não fornecido. Por favor, abra o link através do aplicativo Voltris.');
        }
    }, [user, authLoading, installationId, status, handleLinking, router]);

    return (
        <div className="min-h-screen w-full bg-[#050510] relative overflow-hidden flex flex-col items-center justify-center p-4 selection:bg-[#31A8FF]/30">
            {/* Background Decorativo */}
            <div className="absolute inset-0 bg-[url('data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='noiseFilter'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.65' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23noiseFilter)'/%3E%3C/svg%3E')] opacity-20 pointer-events-none"></div>
            <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-[#8B31FF]/10 blur-[150px] rounded-full animate-pulse"></div>
            
            <motion.div 
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                className="relative z-10 w-full max-w-lg"
            >
                {/* Cabeçalho de Segurança */}
                <div className="flex items-center justify-center gap-3 mb-8">
                    <div className="h-px flex-1 bg-gradient-to-r from-transparent to-white/10"></div>
                    <div className="flex items-center gap-2 px-4 py-2 rounded-full border border-emerald-500/20 bg-emerald-500/5 backdrop-blur-md">
                        <Lock className="w-3 h-3 text-emerald-400" />
                        <span className="text-[10px] font-black uppercase tracking-[0.2em] text-emerald-400/80">Conexão Criptografada AES-256</span>
                    </div>
                    <div className="h-px flex-1 bg-gradient-to-l from-transparent to-white/10"></div>
                </div>

                {/* Card Principal */}
                <div className="p-10 rounded-[3rem] bg-[#0A0A0F]/80 border border-white/10 backdrop-blur-2xl shadow-[0_32px_64px_-12px_rgba(0,0,0,0.5)] overflow-hidden relative">
                    {/* Borda Gradiente Topo */}
                    <div className="absolute top-0 left-0 w-full h-[2px] bg-gradient-to-r from-transparent via-[#8B31FF] to-transparent"></div>

                    <AnimatePresence mode="wait">
                        {status === 'error' ? (
                            <motion.div 
                                key="error"
                                initial={{ opacity: 0, scale: 0.9 }}
                                animate={{ opacity: 1, scale: 1 }}
                                exit={{ opacity: 0, scale: 1.1 }}
                                className="space-y-8 text-center"
                            >
                                <div className="w-24 h-24 bg-red-500/10 text-red-500 rounded-[2rem] flex items-center justify-center mx-auto border border-red-500/20 shadow-[0_0_40px_rgba(239,68,68,0.15)]">
                                    <FiAlertCircle className="w-12 h-12" />
                                </div>
                                <div className="space-y-3">
                                    <h2 className="text-3xl font-black uppercase italic tracking-tighter text-white">Falha na Vinculação</h2>
                                    <p className="text-white/40 text-sm font-bold uppercase tracking-wide px-6 leading-relaxed">
                                        {localError}
                                    </p>
                                </div>
                                <div className="flex flex-col gap-3 pt-4">
                                    <button 
                                        onClick={() => window.location.reload()}
                                        className="w-full py-5 bg-white text-black font-black uppercase italic text-xs rounded-2xl hover:scale-[1.02] active:scale-[0.98] transition-all shadow-xl flex items-center justify-center gap-2"
                                    >
                                        Repetir Protocolo
                                    </button>
                                    <Link 
                                        href="/dashboard"
                                        className="w-full py-5 bg-white/5 text-white/40 font-black uppercase italic text-xs rounded-2xl hover:bg-white/10 border border-white/5 transition-all text-center"
                                    >
                                        Retornar ao Dashboard
                                    </Link>
                                </div>
                            </motion.div>
                        ) : status === 'success' ? (
                            <motion.div 
                                key="success"
                                initial={{ opacity: 0, scale: 0.9 }}
                                animate={{ opacity: 1, scale: 1 }}
                                className="space-y-8 text-center"
                            >
                                <div className="w-24 h-24 bg-emerald-500/10 text-emerald-500 rounded-[2rem] flex items-center justify-center mx-auto border border-emerald-500/20 shadow-[0_0_40px_rgba(16,185,129,0.2)]">
                                    <FiCheckCircle className="w-12 h-12" />
                                </div>
                                <div className="space-y-3">
                                    <h2 className="text-3xl font-black uppercase italic tracking-tighter text-white">Dispositivo Vinculado</h2>
                                    <p className="text-emerald-400 text-xs font-black uppercase tracking-[0.3em]">Protocolo Concluído com Sucesso</p>
                                </div>
                                <div className="p-6 rounded-2xl bg-white/5 border border-white/5 flex items-center gap-4 text-left">
                                    <div className="w-10 h-10 rounded-lg bg-[#8B31FF]/20 flex items-center justify-center">
                                        <ShieldCheck className="w-5 h-5 text-[#8B31FF]" />
                                    </div>
                                    <div>
                                        <p className="text-[10px] font-black text-white/40 uppercase tracking-widest">Conta Identificada</p>
                                        <p className="text-sm font-bold text-white">{user?.email}</p>
                                    </div>
                                </div>
                                <p className="text-white/20 text-[10px] font-medium animate-pulse">Redirecionando para o seu dashboard seguro...</p>
                            </motion.div>
                        ) : (
                            <motion.div 
                                key="loading"
                                initial={{ opacity: 0 }}
                                animate={{ opacity: 1 }}
                                exit={{ opacity: 0 }}
                                className="space-y-10 text-center py-4"
                            >
                                {/* Animação Central */}
                                <div className="relative mx-auto w-32 h-32">
                                    {/* Círculos Rotativos */}
                                    <div className="absolute inset-0 border-t-2 border-[#31A8FF] rounded-full animate-[spin_1.5s_linear_infinite]"></div>
                                    <div className="absolute inset-4 border-r-2 border-[#8B31FF] rounded-full animate-[spin_2s_linear_infinite_reverse] opacity-50"></div>
                                    <div className="absolute inset-8 border-b-2 border-[#FF4B6B] rounded-full animate-[spin_3s_linear_infinite] opacity-30"></div>
                                    
                                    <div className="absolute inset-0 flex items-center justify-center">
                                        <Cpu className="w-10 h-10 text-white animate-pulse" />
                                    </div>
                                </div>

                                <div className="space-y-4">
                                    <div className="space-y-2">
                                        <h2 className="text-3xl font-black uppercase italic tracking-tighter text-white animate-pulse">Vinculando sua conta...</h2>
                                        <p className="text-[#31A8FF] text-[10px] font-black uppercase tracking-[0.4em] opacity-70">Sincronizando Identidade Voltris</p>
                                    </div>

                                    {/* Barra de Progresso */}
                                    <div className="w-full h-1.5 bg-white/5 rounded-full overflow-hidden mt-8 border border-white/5">
                                        <motion.div 
                                            initial={{ width: 0 }}
                                            animate={{ width: `${progress}%` }}
                                            className="h-full bg-gradient-to-r from-[#31A8FF] via-[#8B31FF] to-[#FF4B6B]"
                                        />
                                    </div>
                                    
                                    {/* Logs de "Segurança" */}
                                    <div className="grid grid-cols-2 gap-4 mt-8 pt-8 border-t border-white/5">
                                        <div className="flex items-center gap-2 justify-center opacity-40">
                                            <Globe className="w-3 h-3" />
                                            <span className="text-[10px] font-bold uppercase tracking-widest">Global Link</span>
                                        </div>
                                        <div className="flex items-center gap-2 justify-center opacity-40">
                                            <ShieldCheck className="w-3 h-3 text-emerald-400" />
                                            <span className="text-[10px] font-bold uppercase tracking-widest text-emerald-400">Verificado</span>
                                        </div>
                                    </div>
                                </div>
                            </motion.div>
                        )}
                    </AnimatePresence>
                </div>

                {/* Rodapé de Informação */}
                <p className="mt-8 text-center text-white/20 text-[10px] font-bold uppercase tracking-[0.2em] px-10 leading-relaxed">
                    Este processo vincula permanentemente seu dispositivo à sua assinatura. Você poderá gerenciar este PC através do nosso Portal de Controle.
                </p>
            </motion.div>
        </div>
    );
}

export default function LinkDevicePage() {
    return (
        <Suspense fallback={
            <div className="h-screen w-full bg-[#050510] flex flex-col items-center justify-center text-white">
                <Loader2 className="w-10 h-10 animate-spin text-[#8B31FF] mb-4" />
                <p className="text-[10px] font-black uppercase tracking-[0.4em] text-white/20">Aguardando Segurança...</p>
            </div>
        }>
            <LinkDeviceContent />
        </Suspense>
    );
}
