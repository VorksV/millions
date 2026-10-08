'use client';

import { useState, useEffect, Suspense, useCallback, useMemo } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { createClient } from '@/utils/supabase/client';
// Header/Footer not imported to ensure clean full screen
import Link from 'next/link';
import { motion, AnimatePresence } from 'framer-motion';
import { useAuth } from '@/app/hooks/useAuth';
import GoogleLoginButton from '@/components/GoogleLoginButton';
import { notifyPageView } from '@/utils/notifications';

// Icons
import { FaWhatsapp } from 'react-icons/fa';
import { Mail, Lock, User, Phone as PhoneIcon, MapPin, ArrowLeft, Loader2, CheckCircle, ArrowRight, Smartphone as FiSmartphone, Key as FiKey } from 'lucide-react';
import { toast } from 'react-hot-toast';

export default function LoginPage() {
  return (
    <Suspense fallback={<div className="min-h-screen bg-gray-50" />}>
      <LoginContent />
    </Suspense>
  );
}

function LoginContent() {
  const router = useRouter();
  const searchParams = useSearchParams();

  // --- STATES ---
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [login, setLogin] = useState('');
  const [fullName, setFullName] = useState('');
  const [phone, setPhone] = useState('');
  const [city, setCity] = useState('');
  const [neighborhood, setNeighborhood] = useState('');
  const [state, setState] = useState('');
  const [cep, setCep] = useState('');
  const [street, setStreet] = useState('');
  const [number, setNumber] = useState('');
  const [isCepLoading, setIsCepLoading] = useState(false);
  const [isCepValid, setIsCepValid] = useState(false);

  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [isLoginView, setIsLoginView] = useState(true);
  const [isRecoveryView, setIsRecoveryView] = useState(false);
  const [signupStep, setSignupStep] = useState(1); // 1, 2, 3

  const [isMfaChallenge, setIsMfaChallenge] = useState(false);
  const [mfaCode, setMfaCode] = useState('');
  const [isVerifyingMfa, setIsVerifyingMfa] = useState(false);
  const [isAdmin, setIsAdmin] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [passwordStrength, setPasswordStrength] = useState(0);

  // --- PASSWORD STRENGTH LOGIC ---
  const getPasswordStrength = (pass: string) => {
    let score = 0;
    if (!pass) return 0;
    if (pass.length >= 8) score++;
    if (/[A-Z]/.test(pass)) score++;
    if (/[0-9]/.test(pass)) score++;
    if (/[^A-Za-z0-9]/.test(pass)) score++; // Special char
    if (pass.length >= 12) score++; // Bonus for length
    return score;
  };

  useEffect(() => {
    setPasswordStrength(getPasswordStrength(password));
  }, [password]);

  const strengthColor = () => {
    if (passwordStrength === 0) return 'bg-gray-200';
    if (passwordStrength <= 1) return 'bg-red-500';
    if (passwordStrength === 2) return 'bg-orange-500';
    if (passwordStrength === 3) return 'bg-yellow-500';
    return 'bg-emerald-500';
  };

  const strengthText = () => {
    if (!password) return '';
    if (passwordStrength <= 1) return 'Muito Fraca';
    if (passwordStrength === 2) return 'Regular';
    if (passwordStrength === 3) return 'Forte';
    return 'Muito Forte';
  };
  const [showWhatsAppBtn, setShowWhatsAppBtn] = useState(false);
  const [redirectText, setRedirectText] = useState('Redirecionando...');
  
  const redirectUrl = searchParams.get('redirect') || '';
  const pendingOrder = searchParams.get('pendingOrder') === 'true';
  const installationId = searchParams.get('installation_id');

  const { user, profile, loading: authLoading, isAdmin: authIsAdmin } = useAuth();
  const supabase = useMemo(() => createClient(), []);
  // --- EFFECTS ---
  useEffect(() => {
    if (searchParams.get('cadastro') === '1' || searchParams.get('signup') === 'true') {
      setIsLoginView(false);
      setIsRecoveryView(false);
    }

    const urlError = searchParams.get('error');
    if (urlError) {
      setError(decodeURIComponent(urlError));
    }
  }, [searchParams]);

  // Notificar intenção de cadastro
  useEffect(() => {
    if (!isLoginView) {
      notifyPageView("Página de Cadastro - Início (Intenção)");
    }
  }, [isLoginView]);

  /**
   * Vincula o dispositivo que originou o acesso.
   *
   * Só resolve se o backend confirmar (HTTP 2xx + verified). Antes esta função
   * logava "vinculado com sucesso" sem checar response.ok — o usuario era
   * redirecionado para o dashboard com um vinculo que nunca existiu.
   */
  const linkInstallation = async (userId: string): Promise<{ ok: boolean; message?: string }> => {
    if (!installationId) return { ok: true };
    try {
      const { data: sessionData } = await supabase.auth.getSession();
      const token = sessionData?.session?.access_token;

      const response = await fetch('/api/v1/install/link', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { 'Authorization': `Bearer ${token}` } : {})
        },
        body: JSON.stringify({ installation_id: installationId, user_id: userId })
      });

      const data = await response.json().catch(() => ({}));

      if (!response.ok) {
        console.error('[LINK] Falha ao vincular dispositivo:', response.status, data);
        return {
          ok: false,
          message:
            data?.error ||
            `Não foi possível vincular o dispositivo (HTTP ${response.status}).`
        };
      }

      if (data?.verified !== true) {
        console.error('[LINK] Backend não confirmou a persistência do vínculo:', data);
        return {
          ok: false,
          message: 'O servidor não confirmou o vínculo. Verifique e tente novamente.'
        };
      }

      console.log('[LINK] Dispositivo vinculado e confirmado:', data.installation_id);
      return { ok: true };
    } catch (err) {
      console.error('Erro ao vincular dispositivo:', err);
      return { ok: false, message: 'Falha de conexão ao vincular o dispositivo.' };
    }
  };

  const getFinalRedirect = useCallback(() => {
    // Se houver redirectUrl válido (ex: /adquirir-licenca?plan=pro), tem prioridade máxima
    if (redirectUrl && redirectUrl !== '/' && !redirectUrl.includes('/login')) {
      if (redirectUrl.includes('restricted') && !authIsAdmin) return '/dashboard';
      return redirectUrl;
    }
    if (pendingOrder) return '/dashboard?pendingOrder=true';
    return authIsAdmin ? '/restricted-area-admin' : '/dashboard';
  }, [redirectUrl, pendingOrder, authIsAdmin]);

  // Se o usuário já está logado ao chegar na página de login, redirecionar imediatamente
  useEffect(() => {
    if (authLoading || success) return;

    if (user) {
      const checkMfaAndRedirect = async () => {
        const { data: mfaData } = await supabase.auth.mfa.getAuthenticatorAssuranceLevel();
        if (mfaData?.nextLevel === 'aal2' && mfaData?.currentLevel !== 'aal2') {
          setIsMfaChallenge(true);
          return;
        }

        const dest = getFinalRedirect();
        if (installationId) {
          // Não redireciona como se estivesse tudo certo: se o vínculo falhou,
          // o usuário precisa saber e poder reenviar.
          const result = await linkInstallation(user.id);
          if (!result.ok) {
            setError(result.message || 'Não foi possível vincular o dispositivo.');
            window.location.href = `${dest}${dest.includes('?') ? '&' : '?'}link_failed=1`;
            return;
          }
        }
        window.location.href = dest;
      };
      checkMfaAndRedirect();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user, authLoading]);

  // --- HELPERS ---
  const translateError = (err: string) => {
    const msg = err.toLowerCase();
    if (msg.includes('unable to validate email address') || msg.includes('invalid format')) return 'Formato de e-mail inválido.';
    if (msg.includes('invalid login credentials')) return 'E-mail/Usuário ou senha incorretos.';
    if (msg.includes('email not confirmed')) return 'E-mail não confirmado. Verifique sua caixa de entrada.';
    if (msg.includes('user not found') || msg.includes('usuário não encontrado')) return 'Este usuário não foi encontrado.';
    if (msg.includes('password should be at least')) return 'A senha deve ter pelo menos 6 caracteres.';
    if (msg.includes('user already registered')) return 'Este usuário/e-mail já está cadastrado.';
    if (msg.includes('network error')) return 'Erro de conexão. Verifique sua internet.';
    return 'Ocorreu um erro técnico. Tente novamente.';
  };

  const validateEmail = (emailStr: string) => {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(emailStr);
  };

  // --- HANDLERS ---
  const handleMfaVerify = async (e: React.FormEvent) => {
    e.preventDefault();
    if (mfaCode.length < 6) return;
    setIsVerifyingMfa(true);
    setError(null);
    try {
      const { data: factorsData } = await supabase.auth.mfa.listFactors();
      const factor = factorsData?.all?.find(f => f.status === 'verified');
      if (!factor) throw new Error('Nenhum verificador ativo encontrado.');

      const { error } = await supabase.auth.mfa.challengeAndVerify({
        factorId: factor.id,
        code: mfaCode
      });
      if (error) throw error;

      toast.success('Autenticação confirmada!');
      setSuccess(true);
      
      const dest = getFinalRedirect();
      window.location.href = dest;
    } catch (err: any) {
      setError('Código de autenticação incorreto.');
    } finally {
      setIsVerifyingMfa(false);
    }
  };

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      let loginEmail = email.trim();
      const isEmail = validateEmail(loginEmail);

      // Se não for e-mail, tenta buscar por username
      if (!isEmail) {
        try {
          const res = await fetch('/api/v1/auth/get-email-by-username', {
            method: 'POST',
            body: JSON.stringify({ username: loginEmail }),
          });
          const data = await res.json();
          if (data.email) {
            loginEmail = data.email;
          } else {
            throw new Error('Usuário não encontrado');
          }
        } catch (err) {
          throw new Error('Usuário não encontrado ou erro de conexão.');
        }
      }

      console.log('🔄 Iniciando login para:', loginEmail);
      const { data: signInData, error } = await supabase.auth.signInWithPassword({
        email: loginEmail,
        password
      });

      if (error || !signInData.user) throw new Error(error?.message || 'Credenciais inválidas');
      console.log('✅ Login bem-sucedido:', signInData.user.id);

      // --- MFA CHECK ---
      const { data: mfaData } = await supabase.auth.mfa.getAuthenticatorAssuranceLevel();
      if (mfaData?.nextLevel === 'aal2' && mfaData?.currentLevel !== 'aal2') {
        setIsMfaChallenge(true);
        setLoading(false);
        return;
      }

      // Busca admin de forma fail-safe
      let admin = signInData.user.user_metadata?.is_admin === true;
      try {
        const profilePromise = supabase.from('profiles').select('is_admin').eq('id', signInData.user.id).single();
        const timeoutPromise = new Promise((_, r) => setTimeout(() => r(new Error('TIMEOUT')), 2500));
        const { data: profileData } = await Promise.race([profilePromise, timeoutPromise]) as any;
        if (profileData) admin = profileData.is_admin || admin;
      } catch (err) {
        console.warn('⚠️ [AUTH] Erro ou timeout ao buscar admin no login, prosseguindo com metadados.');
      }

      // Link de instalação sincronizado
      if (installationId) {
        try {
          await linkInstallation(signInData.user.id);
        } catch (err) {
          console.error('Erro linkInstallation:', err);
        }
      }

      setSuccess(true);
      if (typeof window !== 'undefined') window.dispatchEvent(new Event('auth-changed'));

      // Redirecionar diretamente após login bem-sucedido
      const dest = (redirectUrl && redirectUrl !== '/' && !redirectUrl.includes('/login'))
        ? (redirectUrl.includes('restricted') && !admin ? '/dashboard' : redirectUrl)
        : (pendingOrder ? '/dashboard?pendingOrder=true' : (admin ? '/restricted-area-admin' : '/dashboard'));

      console.log('🚀 Redirecionando para:', dest);

      // Reduzir delay do timeout para feedback mais rápido
      setTimeout(() => {
        window.location.href = dest;
      }, 400);
    } catch (err: any) {
      console.error('❌ Erro no handleLogin:', err);
      setError(translateError(err.message));
      setLoading(false); // Garantir que desativa o loading em caso de erro
    } finally {
      // O loading só deve ficar true se success for false e não houver erro
      // Mas se o redirect demorar, o botão pode mostrar o spinner ainda.
    }
  };

  const handleNextStep = async () => {
    if (signupStep === 1) {
      if (!login) return setError("Preencha o Usuário.");
      if (!email) return setError("Preencha o E-mail.");
      if (!validateEmail(email)) return setError("Formato de e-mail inválido.");
      
      // Validação de Senha Forte (Nivel Stripe/Enterprise)
      if (password.length < 8) return setError("A senha deve ter pelo menos 8 caracteres.");
      if (passwordStrength < 3) return setError("Sua senha é fraca. Use letras maiúsculas, números e símbolos.");
      
      setLoading(true);
      setError(null);
      try {
        // Validação de Existência (E-mail e Usuário) no Sistema
        const availabilityRes = await fetch('/api/v1/auth/check-availability', {
          method: 'POST',
          body: JSON.stringify({ email: email.trim(), login: login.trim() }),
          headers: { 'Content-Type': 'application/json' }
        });
        const availability = await availabilityRes.json();
        if (!availability.available) {
          setError(availability.error || "Este e-mail ou usuário já está em uso.");
          setLoading(false);
          return;
        }

        // Validação Profissional de DNS e Descartáveis
        const response = await fetch('/api/v1/auth/validate-email', {
          method: 'POST',
          body: JSON.stringify({ email: email.trim() }),
          headers: { 'Content-Type': 'application/json' }
        });
        const data = await response.json();
        if (!data.valid) {
          setError(data.error || "Este e-mail é inválido.");
          setLoading(false);
          return;
        }
      } catch (err) {
        setError("Erro crítico de validação. Tente novamente.");
        setLoading(false);
        return;
      } finally {
        setLoading(false);
      }
    }
    
    if (signupStep === 2) {
      if (!fullName) return setError("Preencha o Nome Completo.");
      if (!phone) return setError("Preencha o WhatsApp.");
      if (!validateBrazilianPhone(phone)) {
        return setError("Número de WhatsApp inválido ou DDD inexistente.");
      }

      setLoading(true);
      setError(null);
      try {
        // Validação de Telefone Duplicado no Sistema (Step 2)
        const availabilityRes = await fetch('/api/v1/auth/check-availability', {
          method: 'POST',
          body: JSON.stringify({ phone: phone.trim() }),
          headers: { 'Content-Type': 'application/json' }
        });
        const availability = await availabilityRes.json();
        if (!availability.available) {
          setError(availability.error || "Este WhatsApp já está sendo usado.");
          setLoading(false);
          return;
        }
      } catch (err) {
        setError("Erro de rede. Tente novamente.");
        setLoading(false);
        return;
      } finally {
        setLoading(false);
      }
    }
    setError(null);
    setSignupStep(prev => prev + 1);
  };

  const handleSignUp = async () => {
    if (!cep || !state || !city || !street || !number) {
      return setError("Por favor, preencha o endereço completo.");
    }
    if (!isCepValid) {
      return setError("O CEP informado é inválido. Por favor, utilize um CEP real.");
    }
    setError(null);
    setLoading(true);

    try {
      // 1. Bloqueio Profissional de WhatsApp Duplicado
      const cleanPhone = phone.replace(/\D/g, '');
      const { data: directCheck } = await supabase
        .from('profiles')
        .select('id')
        .or(`phone.eq.${phone},phone.ilike.%${cleanPhone}%`)
        .maybeSingle();

      if (directCheck) {
        setLoading(false);
        return setError("Este WhatsApp já está em uma conta. Use outro número.");
      }

      const { data: signUpData, error } = await supabase.auth.signUp({
        email, password,
        options: { 
          data: { 
            full_name: fullName, 
            phone, 
            city, 
            neighborhood, 
            state, 
            cep, 
            address: `${street}, ${number}`,
            login 
          } 
        },
      });
      if (error) throw error;

      if (signUpData.user) {
        // Verifica se sessão foi criada (login automático)
        let session = signUpData.session;

        // Se não houver sessão, tenta login forçado (caso conf. email esteja desligada mas não logo)
        if (!session) {
          const { data: signInData } = await supabase.auth.signInWithPassword({ email, password });
          session = signInData.session;
        }

        if (session) {
          setSuccess(true);
          if (typeof window !== 'undefined') window.dispatchEvent(new Event('auth-changed'));

          // Redirecionar diretamente após cadastro
          const dest = (redirectUrl && redirectUrl !== '/' && !redirectUrl.includes('/login'))
            ? redirectUrl
            : '/dashboard';

          // Notificar sucesso no Telegram (Formulário Normal)
          notifyPageView(`Novo Cadastro Realizado: ${email} (Via Formulário)`);

          setTimeout(() => {
            window.location.href = dest;
          }, 800);
        } else {
          // Requer confirmação de email
          setRedirectText("Cadastro realizado! Verifique seu e-mail para confirmar.");
          setSuccess(true);
          // Redireciona para tela de login após delay
          setTimeout(() => {
            setSuccess(false);
            setIsLoginView(true);
            setSignupStep(1);
            setRedirectText("Redirecionando...");
          }, 5000);
        }
      }
    } catch (err: any) {
      setError(translateError(err.message));
    } finally {
      setLoading(false);
    }
  };

  const handlePasswordRecovery = async () => {
    setError(null); setLoading(true);
    try {
      const { error } = await supabase.auth.resetPasswordForEmail(email, { redirectTo: `${window.location.origin}/reset-password` });
      if (error) throw error;
      alert("Email enviado!"); setIsRecoveryView(false); setIsLoginView(true);
    } catch (err: any) { setError(err.message); } finally { setLoading(false); }
  };

  // Formatters
  const formatPhone = (v: string) => { const n = v.replace(/\D/g, ''); return n.length <= 11 ? n.replace(/(\d{2})(\d{4,5})(\d{4})/, '($1) $2-$3') : v; };
  const formatCEP = (v: string) => { const n = v.replace(/\D/g, ''); return n.length <= 8 ? n.replace(/(\d{5})(\d{3})/, '$1-$2') : v; };

  const validateBrazilianPhone = (v: string) => {
    const n = v.replace(/\D/g, '');
    // Verifica tamanho 11 (DDD + 9 + 8 dígitos)
    if (n.length !== 11) return false;
    // O nono dígito (pos 2) deve ser obrigatoriamente 9 para celulares
    if (n[2] !== '9') return false;
    // Lista de DDDs válidos no Brasil
    const validDDDs = [
      '11','12','13','14','15','16','17','18','19',
      '21','22','24','27','28','31','32','33','34','35','37','38',
      '41','42','43','44','45','46','47','48','49',
      '51','53','54','55','61','62','63','64','65','66','67','68','69',
      '71','73','74','75','77','79','81','82','83','84','85','86','87','88','89',
      '91','92','93','94','95','96','97','98','99'
    ];
    if (!validDDDs.includes(n.substring(0, 2))) return false;
    return true;
  };

  const handleCepChange = async (v: string) => {
    const formattedCep = formatCEP(v);
    setCep(formattedCep);

    const cleanCep = formattedCep.replace(/\D/g, '');
    if (cleanCep.length === 8) {
      setIsCepLoading(true);
      setIsCepValid(false);
      setError(null);
      
      try {
        console.log('🔍 Validando CEP via Servidor:', cleanCep);
        const response = await fetch('/api/v1/auth/validate-cep', {
          method: 'POST',
          body: JSON.stringify({ cep: cleanCep }),
          headers: { 'Content-Type': 'application/json' }
        });
        
        const resData = await response.json();
        console.log('✅ Resposta do Servidor:', resData);
        
        if (!resData.valid) {
          setError(resData.error || "Este CEP não existe. Digite um CEP real.");
          setCity(''); setNeighborhood(''); setState(''); setStreet('');
          setIsCepValid(false);
        } else {
          // Extrair dados da resposta do servidor
          const { street, neighborhood, city, state } = resData.data;
          
          setCity(city || '');
          setNeighborhood(neighborhood || '');
          setState(state || '');
          setStreet(street || '');
          setError(null);
          setIsCepValid(true);
          console.log('🎯 Endereço preenchido com sucesso!');
        }
      } catch (err) {
        console.error('❌ Erro na validação de CEP:', err);
        setError("Erro de rede ao validar CEP. Tente novamente.");
        setIsCepValid(false);
      } finally {
        setIsCepLoading(false);
      }
    } else {
      setIsCepValid(false);
    }
  };

  return (
    <div className="h-[100dvh] w-full bg-gray-50 relative overflow-hidden flex items-center justify-center font-sans selection:bg-blue-200/30">

      {/* Backgrounds */}
      <div className="absolute inset-0 bg-[url('data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='noiseFilter'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.65' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23noiseFilter)'/%3E%3C/svg%3E')] opacity-5 pointer-events-none"></div>
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-blue-200/30 blur-[120px] rounded-full"></div>

      {/* Back Button */}
      <Link href="/" className="absolute top-8 left-8 z-50 flex items-center gap-2 text-gray-600 hover:text-gray-900 transition-colors group px-4 py-2 rounded-full hover:bg-gray-200">
        {/* Arrow Colored by Brand - Primary Pink */}
        <ArrowLeft className="w-4 h-4 text-pink-600 group-hover:-translate-x-1 transition-transform" />
        <span className="text-sm font-medium">Voltar</span>
      </Link>

      {/* Card */}
      <motion.div initial={{ scale: 0.9, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} className="w-full max-w-[400px] mx-4 relative z-10">
        <div className="bg-white/90 backdrop-blur-xl border border-gray-200 rounded-3xl shadow-lg overflow-hidden relative">
          {/* Gradient Border Top */}
          <div className="absolute top-0 left-0 w-full h-1 bg-gradient-to-r from-pink-600 via-purple-600 to-blue-600"></div>

          <div className="p-8">
            {/* Header Centered Flex */}
            <div className="flex flex-col items-center justify-center mb-6 w-full text-center">
              {/* Titulo Reduzido - text-sm */}
              <h1 className="text-sm font-bold text-gray-900 tracking-tight mb-1 whitespace-nowrap">
                {isRecoveryView ? 'Recuperação' : (isLoginView ? 'Bem-vindo' : 'Criar Conta')}
              </h1>
              {/* Subtitulo Micro - text-[10px] */}
              <p className="text-[10px] font-bold text-gray-500 uppercase tracking-widest mt-1">
                {isRecoveryView ? 'Redefinir Senha' : (isLoginView ? 'Acesse o Painel' : `Etapa ${signupStep} de 3`)}
              </p>
            </div>

            <AnimatePresence mode="wait">
              {success ? (
                <motion.div initial={{ scale: 0.8, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} className="text-center py-10 flex flex-col items-center relative">
                  <div className="absolute inset-0 bg-emerald-500/20 blur-[50px] rounded-full pointer-events-none"></div>
                  <div className="relative z-10 w-20 h-20 rounded-full bg-emerald-100 border border-emerald-200 flex items-center justify-center mb-6 shadow-md">
                    <CheckCircle className="w-10 h-10 text-emerald-600" />
                  </div>
                  <h2 className="text-2xl font-bold text-gray-900 mb-2">Sucesso!</h2>
                  <p className="text-emerald-600 text-sm max-w-[250px] font-medium animate-pulse">{redirectText}</p>
                </motion.div>
              ) : isMfaChallenge ? (
                <motion.div initial={{ scale: 0.9, opacity: 0 }} animate={{ scale: 1, opacity: 1 }} className="space-y-6 py-6 text-center">
                  <div className="w-16 h-16 bg-blue-100 rounded-2xl flex items-center justify-center mx-auto mb-4 shadow-md">
                    <FiSmartphone className="w-8 h-8 text-blue-600" />
                  </div>
                  <div className="space-y-2">
                    <h2 className="text-xl font-black text-gray-900 italic uppercase tracking-tighter">Autenticação 2FA</h2>
                    <p className="text-gray-500 text-[10px] font-bold uppercase tracking-widest leading-relaxed px-4">Proteção Voltris: Digite o código de 6 dígitos do seu Google Authenticator.</p>
                  </div>

                  <form onSubmit={handleMfaVerify} className="space-y-6">
                     <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-4 flex items-center gap-3 focus-within:border-blue-300 transition-all">
                        <FiKey className="w-5 h-5 text-gray-500" />
                        <input 
                          type="text" 
                          placeholder="000 000" 
                          maxLength={6}
                          value={mfaCode}
                          onChange={e => setMfaCode(e.target.value.replace(/\D/g, ''))}
                          className="bg-transparent w-full text-gray-900 text-xl font-black tracking-[0.4em] outline-none placeholder:tracking-normal placeholder:text-gray-400 text-center"
                          autoFocus
                        />
                     </div>

                     {error && (
                        <div className="p-2 rounded-lg bg-red-100 border border-red-200">
                          <p className="text-red-600 text-[10px] font-bold uppercase tracking-widest text-center">{error}</p>
                        </div>
                     )}

                     <button 
                      type="submit" 
                      disabled={isVerifyingMfa || mfaCode.length < 6}
                      className="w-full py-4 bg-gray-900 text-white font-black uppercase italic text-xs rounded-xl shadow-md hover:bg-gray-800 active:scale-[0.98] transition-all disabled:opacity-50"
                     >
                       {isVerifyingMfa ? 'Verificando...' : 'Confirmar e Acessar'}
                     </button>

                     <button 
                      type="button"
                      onClick={() => { 
                        setIsMfaChallenge(false); 
                        setMfaCode(''); 
                        setError(null); 
                        supabase.auth.signOut();
                        window.location.reload(); 
                      }}
                      className="w-full text-gray-400 hover:text-gray-600 text-[9px] font-black uppercase tracking-widest transition-all"
                     >
                       Sair da Sessão
                     </button>
                  </form>
                </motion.div>
              ) : (
                <motion.div key={isLoginView ? 'login' : 'signup'} initial={{ opacity: 0, x: 20 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: -20 }} className="space-y-4">

                  {/* --- LOGIN FORM --- */}
                  {isLoginView && !isRecoveryView && (
                    <form 
                      onSubmit={handleLogin} 
                      className="space-y-4 p-0 m-0 w-full"
                    >
                      <div className="space-y-4">
                        <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3 focus-within:border-blue-300 transition-all group/input">
                          <Mail className="w-4 h-4 text-gray-500 group-focus-within/input:text-blue-600 transition-colors" />
                          <input
                            type="text"
                            placeholder="E-mail ou Usuário"
                            value={email}
                            onChange={e => setEmail(e.target.value)}
                            className="bg-transparent w-full text-gray-900 text-sm outline-none placeholder:text-gray-400 appearance-none"
                            autoComplete="username"
                            required
                          />
                        </div>
                        <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3 focus-within:border-blue-300 transition-all group/input">
                          <Lock className="w-4 h-4 text-gray-500 group-focus-within/input:text-blue-600 transition-colors" />
                          <input 
                            type={showPassword ? "text" : "password"} 
                            placeholder="Sua Senha" 
                            value={password} 
                            onChange={e => setPassword(e.target.value)} 
                            className="bg-transparent w-full text-gray-900 text-sm outline-none placeholder:text-gray-400 appearance-none" 
                            autoComplete="current-password"
                            required
                          />
                          <button 
                            type="button" 
                            onClick={() => setShowPassword(!showPassword)}
                            className="text-gray-500 hover:text-gray-900 transition-colors"
                          >
                            <User className="w-4 h-4" />
                          </button>
                        </div>
                      </div>

                      <div className="flex justify-end">
                        <button type="button" onClick={() => setIsRecoveryView(true)} className="text-xs text-gray-500 hover:text-blue-600 transition-colors font-medium">Esqueci a senha</button>
                      </div>

                      {error && (
                        <motion.div initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }} className="p-3 rounded-lg bg-red-100 border border-red-200">
                          <p className="text-red-600 text-xs text-center font-bold tracking-tight">{error}</p>
                        </motion.div>
                      )}

                      <button 
                        type="submit" 
                        disabled={loading} 
                        className="w-full py-3 bg-gradient-to-r from-pink-600 via-purple-600 to-blue-600 hover:brightness-110 active:scale-[0.98] text-white font-bold rounded-xl text-sm transition-all shadow-md flex items-center justify-center gap-2"
                      >
                        {loading ? (
                          <>
                            <Loader2 className="w-4 h-4 animate-spin" />
                            <span>Entrando...</span>
                          </>
                        ) : (
                          <>
                            <span>Entrar no Painel</span>
                            <ArrowRight className="w-4 h-4" />
                          </>
                        )}
                      </button>

                      <div className="py-2 flex items-center gap-4">
                        <div className="h-px flex-1 bg-gray-200"></div>
                        <span className="text-[10px] font-bold text-gray-300 uppercase tracking-widest">OU</span>
                        <div className="h-px flex-1 bg-gray-200"></div>
                      </div>

                      <div className="w-full flex justify-center">
                        <GoogleLoginButton 
                          onSuccess={() => {}} 
                          onError={(err) => setError(translateError(err))} 
                          disabled={loading} 
                          redirect={redirectUrl} 
                          label={isLoginView ? "Continuar com Google" : "Cadastrar com o Google"}
                          isSignup={!isLoginView}
                        />
                      </div>
                    </form>
                  )}

                  {/* --- SIGNUP WIZARD --- */}
                  {!isLoginView && !isRecoveryView && (
                    <div className="space-y-4">
                      {signupStep === 1 && (
                        <div className="space-y-3">
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3"><User className="w-4 h-4 text-gray-500" /><input type="text" placeholder="Usuário (Login)" value={login} onChange={e => setLogin(e.target.value)} className="bg-transparent w-full text-gray-900 text-sm outline-none" /></div>
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3"><Mail className="w-4 h-4 text-gray-500" /><input type="email" placeholder="E-mail" value={email} onChange={e => setEmail(e.target.value)} className="bg-transparent w-full text-gray-900 text-sm outline-none" /></div>
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3">
                            <Lock className="w-4 h-4 text-gray-500" />
                            <input 
                              type={showPassword ? "text" : "password"} 
                              placeholder="Senha" 
                              value={password} 
                              onChange={e => setPassword(e.target.value)} 
                              className="bg-transparent w-full text-gray-900 text-sm outline-none" 
                            />
                            <button type="button" onClick={() => setShowPassword(!showPassword)} className="text-gray-500">
                                <Lock className="w-4 h-4" />
                            </button>
                          </div>

                          {/* Password Strength Meter */}
                          <div className="space-y-2 mt-1 px-1">
                            <div className="flex justify-between items-center">
                              <span className="text-[10px] font-bold text-gray-500 uppercase tracking-widest">Segurança</span>
                              <span className={`text-[10px] font-bold uppercase tracking-widest transition-colors ${passwordStrength === 0 ? 'text-gray-400' : (passwordStrength <= 1 ? 'text-red-500' : (passwordStrength === 2 ? 'text-orange-500' : 'text-emerald-500'))}`}>
                                {strengthText()}
                              </span>
                            </div>
                            <div className="h-1 w-full bg-gray-200 rounded-full overflow-hidden">
                              <motion.div 
                                className={`h-full ${strengthColor()} transition-all duration-500`}
                                initial={{ width: 0 }}
                                animate={{ width: `${(passwordStrength / 5) * 100}%` }}
                              />
                            </div>
                          </div>

                          <div className="py-2 flex items-center gap-4">
                            <div className="h-px flex-1 bg-gray-200"></div>
                            <span className="text-[10px] font-bold text-gray-300 uppercase tracking-widest">OU</span>
                            <div className="h-px flex-1 bg-gray-200"></div>
                          </div>

                          <div className="w-full flex justify-center">
                            <GoogleLoginButton 
                              onSuccess={() => {}} 
                              onError={(err) => setError(translateError(err))} 
                              disabled={loading} 
                              redirect={redirectUrl} 
                              label="Cadastrar com o Google"
                              isSignup={true}
                            />
                          </div>
                        </div>
                      )}
                      {signupStep === 2 && (
                        <div className="space-y-3">
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3"><User className="w-4 h-4 text-gray-500" /><input type="text" placeholder="Nome Completo" value={fullName} onChange={e => setFullName(e.target.value)} className="bg-transparent w-full text-gray-900 text-sm outline-none" /></div>
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3"><PhoneIcon className="w-4 h-4 text-gray-500" /><input type="tel" placeholder="WhatsApp" value={phone} onChange={e => setPhone(formatPhone(e.target.value))} className="bg-transparent w-full text-gray-900 text-sm outline-none" /></div>
                        </div>
                      )}
                      {signupStep === 3 && (
                        <div className="space-y-3">
                          <div className="grid grid-cols-2 gap-3">
                            <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-2 focus-within:border-blue-300 transition-all">
                              <input 
                                type="text" 
                                placeholder="CEP" 
                                value={cep} 
                                onChange={e => handleCepChange(e.target.value)} 
                                className="bg-transparent w-full text-gray-900 text-sm outline-none" 
                              />
                              {isCepLoading && <Loader2 className="w-4 h-4 text-blue-600 animate-spin" />}
                            </div>
                            <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3">
                              <input 
                                type="text" 
                                placeholder="UF" 
                                maxLength={2} 
                                readOnly={isCepValid}
                                value={state} 
                                onChange={e => !isCepValid && setState(e.target.value.toUpperCase())} 
                                className={`bg-transparent w-full text-gray-900 text-sm outline-none transition-all ${isCepValid ? 'opacity-30 cursor-not-allowed select-none' : ''}`} 
                              />
                            </div>
                          </div>
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3">
                            <input 
                              type="text" 
                              placeholder="Rua / Logradouro" 
                              readOnly={isCepValid}
                              value={street} 
                              onChange={e => !isCepValid && setStreet(e.target.value)} 
                              className={`bg-transparent w-full text-gray-900 text-sm outline-none transition-all ${isCepValid ? 'opacity-30 cursor-not-allowed select-none' : ''}`} 
                            />
                          </div>
                          <div className="grid grid-cols-3 gap-3">
                            <div className="col-span-2 bg-gray-100 border border-gray-200 rounded-xl px-4 py-3">
                              <input 
                                type="text" 
                                placeholder="Bairro" 
                                readOnly={isCepValid}
                                value={neighborhood} 
                                onChange={e => !isCepValid && setNeighborhood(e.target.value)} 
                                className={`bg-transparent w-full text-gray-900 text-sm outline-none transition-all ${isCepValid ? 'opacity-30 cursor-not-allowed select-none' : ''}`} 
                              />
                            </div>
                            <div className="bg-gray-100 border border-blue-300 rounded-xl px-4 py-3 focus-within:border-blue-300 transition-all bg-gradient-to-br from-blue-100/50 to-transparent">
                              <input 
                                type="text" 
                                placeholder="Nº" 
                                value={number} 
                                onChange={e => setNumber(e.target.value)} 
                                className="bg-transparent w-full text-gray-900 text-sm outline-none placeholder:text-blue-600/40 font-bold" 
                              />
                            </div>
                          </div>
                          <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3">
                            <input 
                              type="text" 
                              placeholder="Cidade" 
                              readOnly={isCepValid}
                              value={city} 
                              onChange={e => !isCepValid && setCity(e.target.value)} 
                              className={`bg-transparent w-full text-gray-900 text-sm outline-none transition-all ${isCepValid ? 'opacity-30 cursor-not-allowed select-none' : ''}`} 
                            />
                          </div>
                          {isCepValid && (
                            <button 
                              type="button"
                              onClick={() => { setCep(''); setIsCepValid(false); setStreet(''); setNeighborhood(''); setCity(''); setState(''); }}
                              className="text-[10px] text-pink-600 font-bold uppercase tracking-widest hover:underline text-center w-full mt-1"
                            >
                              Corrigir CEP / Digitar Manualmente
                            </button>
                          )}
                        </div>
                      )}

                      {error && <p className="text-red-600 text-xs text-center">{error}</p>}

                      <div className="flex gap-3 pt-2">
                        {signupStep > 1 && <button onClick={() => setSignupStep(prev => prev - 1)} className="px-4 py-3 rounded-xl border border-gray-200 text-gray-500 hover:text-gray-900 text-sm">Voltar</button>}
                        <button onClick={signupStep < 3 ? handleNextStep : handleSignUp} disabled={loading} className="flex-1 py-3 bg-gradient-to-r from-pink-600 to-purple-600 text-white font-bold rounded-xl text-sm shadow-md hover:brightness-110">
                          {loading ? <Loader2 className="w-4 h-4 animate-spin mx-auto" /> : (signupStep < 3 ? 'Próximo' : 'Concluir')}
                        </button>
                      </div>
                    </div>
                  )}

                  {/* --- RECOVERY --- */}
                  {isRecoveryView && (
                    <div className="space-y-4">
                      <p className="text-gray-600 text-sm text-center">Digite seu email para receber o link.</p>
                      <div className="bg-gray-100 border border-gray-200 rounded-xl px-4 py-3 flex items-center gap-3"><Mail className="w-4 h-4 text-gray-500" /><input type="email" placeholder="E-mail cadastrado" value={email} onChange={e => setEmail(e.target.value)} className="bg-transparent w-full text-gray-900 text-sm outline-none" /></div>
                      <button onClick={handlePasswordRecovery} disabled={loading} className="w-full py-3 bg-blue-600 text-white font-bold rounded-xl text-sm">Enviar Link</button>
                      <button onClick={() => setIsRecoveryView(false)} className="w-full text-center text-xs text-gray-500">Voltar</button>
                    </div>
                  )}

                  <div className="pt-4 border-t border-gray-200 text-center">
                    {!isRecoveryView && (
                      <div className="space-y-3">
                        <button onClick={() => { setIsLoginView(!isLoginView); setSignupStep(1); setError(null); }} className="text-sm text-gray-600">
                          {isLoginView ? 'Não tem conta? ' : 'Já tem conta? '}

                          <span className="font-bold text-transparent bg-clip-text bg-gradient-to-r from-pink-600 via-purple-600 to-blue-600 hover:opacity-80 transition-opacity">
                            {isLoginView ? 'Cadastre-se' : 'Faça Login'}
                          </span>

                        </button>
                      </div>
                    )}
                    {isLoginView && !isRecoveryView && (
                      <div className="mt-4 flex justify-center w-full relative">
                      </div>
                    )}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </div>
      </motion.div>
    </div>
  );
}
