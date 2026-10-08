import { NextRequest } from 'next/server';
import { createClient } from '@supabase/supabase-js';
import { createServerClient, type CookieOptions } from '@supabase/ssr';
import {
    startOperation,
    getOrCreateCorrelationId,
    logRequest,
    logSuccess,
    logSupabaseError,
    jsonWithCorrelation,
    errorWithCorrelation,
    normalizeUuid,
    maskEmail,
} from '@/lib/voltris-log';

export const runtime = 'nodejs';

/**
 * POST /api/v1/install/link
 *
 * Vincula uma instalacao a conta do usuario AUTENTICADO.
 *
 * Regra de seguranca: o `user_id` do body e apenas uma dica de UI. O dono real
 * vem SEMPRE da sessao (cookie). Divergencia entre os dois e 403.
 *
 * Sucesso so e declarado depois de releitura no banco (regra 19): o app desktop
 * so exibe "vinculado" quando este endpoint responde success.
 */
export async function POST(request: NextRequest) {
    const correlationId = getOrCreateCorrelationId(request);
    const ctx = startOperation('INSTALL_LINK', correlationId);

    let body: any;
    try {
        body = await request.json();
    } catch {
        logRequest(ctx, request);
        return errorWithCorrelation(ctx, 400, 'INVALID_JSON', 'JSON invalido no corpo da requisicao.');
    }
    logRequest(ctx, request, body);

    const pick = (...keys: string[]) => {
        for (const k of keys) {
            const camel = k.replace(/_([a-z])/g, (_, c: string) => c.toUpperCase());
            const v = body?.[k] ?? body?.[camel];
            if (v !== undefined && v !== null) return v;
        }
        return undefined;
    };

    const installationId = normalizeUuid(pick('installation_id'));
    if (!installationId) {
        return errorWithCorrelation(ctx, 400, 'INVALID_INSTALLATION_ID', 'installation_id invalido ou ausente.');
    }
    ctx.installationId = installationId;

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const supabaseAnonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY;
    const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!supabaseUrl || !supabaseAnonKey || !supabaseServiceKey) {
        return errorWithCorrelation(ctx, 500, 'SERVER_CONFIG', 'Configuracao do servidor incompleta.');
    }

    const admin = createClient(supabaseUrl, supabaseServiceKey, {
        auth: { autoRefreshToken: false, persistSession: false },
    });

    let user: any = null;
    let authErrorMsg: string | null = null;

    // 1. Tentar autenticacao direta via header Authorization: Bearer <token>
    const authHeader = request.headers.get('authorization') || request.headers.get('Authorization');
    if (authHeader && authHeader.toLowerCase().startsWith('bearer ')) {
        const token = authHeader.slice(7).trim();
        if (token) {
            const { data: tokenUserData, error: tokenError } = await admin.auth.getUser(token);
            if (!tokenError && tokenUserData?.user) {
                user = tokenUserData.user;
            } else if (tokenError) {
                authErrorMsg = tokenError.message;
            }
        }
    }

    // 2. Se nao autenticou por Bearer, tentar via cookies da sessao (com suporte a getAll)
    if (!user) {
        const serverSupabase = createServerClient(supabaseUrl, supabaseAnonKey, {
            cookies: {
                getAll() {
                    return request.cookies.getAll();
                },
                setAll() {},
            },
        });

        const { data: sessionData, error: authError } = await serverSupabase.auth.getUser();
        if (sessionData?.user) {
            user = sessionData.user;
        } else if (authError) {
            authErrorMsg = authError.message;
        }
    }

    if (!user) {
        return errorWithCorrelation(ctx, 401, 'AUTH_SESSION_ERROR', 'Sessao expirada ou nao autenticada. Faca login novamente.', {
            expose: true,
            details: { reason: authErrorMsg || 'Auth session missing!' },
        });
    }
    ctx.userId = user.id;

    const claimedUserId = pick('user_id');
    if (claimedUserId && String(claimedUserId).toLowerCase() !== user.id.toLowerCase()) {
        return errorWithCorrelation(
            ctx,
            403,
            'INTEGRITY_VIOLATION',
            'Violacao de integridade: o usuario do corpo difere da sessao.'
        );
    }

    // O vinculo aponta para profiles.id (FK). Garante que o perfil existe antes
    // de gravar, para nao estourar 23503 e devolver 409 opaco.
    const { data: profile, error: profileError } = await admin
        .from('profiles')
        .select('id, email')
        .eq('id', user.id)
        .maybeSingle();

    if (profileError) {
        logSupabaseError(ctx, 'buscar perfil do usuario', profileError);
        return errorWithCorrelation(ctx, 500, 'PROFILE_LOOKUP_FAILED', 'Nao foi possivel validar o perfil.');
    }
    if (!profile) {
        return errorWithCorrelation(
            ctx,
            409,
            'PROFILE_MISSING',
            'Perfil do usuario ainda nao existe. Recarregue a pagina e tente novamente.'
        );
    }

    const now = new Date().toISOString();

    const { error: upsertError } = await admin.from('installations').upsert(
        {
            id: installationId,
            user_id: user.id,
            linked_at: now,
            last_link_check_at: now,
            updated_at: now,
            unlinked_at: null,
        },
        { onConflict: 'id' }
    );

    if (upsertError) {
        logSupabaseError(ctx, 'upsert link installations', upsertError);
        return errorWithCorrelation(ctx, 500, 'DB_LINK_FAILED', 'Erro ao vincular dispositivo no banco de dados.', {
            expose: true,
            details: { pg_code: upsertError.code ?? null },
        });
    }

    // REGRA 19: so declara sucesso depois de confirmar no banco.
    const { data: confirmed, error: confirmError } = await admin
        .from('installations')
        .select('id, user_id, linked_at')
        .eq('id', installationId)
        .single();

    if (confirmError || !confirmed) {
        logSupabaseError(ctx, 'confirmar vinculo', confirmError);
        return errorWithCorrelation(
            ctx,
            500,
            'LINK_NOT_CONFIRMED',
            'O vinculo nao pode ser confirmado no banco. Nada foi vinculado.'
        );
    }
    if (confirmed.user_id !== user.id) {
        return errorWithCorrelation(
            ctx,
            500,
            'LINK_NOT_CONFIRMED',
            'O banco nao retornou o vinculo esperado. Nada foi vinculado.'
        );
    }

    logSuccess(ctx, 'vinculacao confirmada', { email: maskEmail(user.email) });

    // Limpa quaisquer comandos anteriores do dispositivo (ex: unlinks antigos pendentes)
    try {
        await admin
            .from('device_commands')
            .delete()
            .eq('installation_id', installationId);
    } catch (cleanErr: any) {
        logSupabaseError(ctx, 'limpar comandos antigos ao vincular', cleanErr ?? null);
    }

    // Emite comando em tempo real para o app desktop via Realtime / SSE
    try {
        await admin.from('device_commands').insert({
            installation_id: installationId,
            command_type: 'device_linked',
            payload: {
                is_linked: true,
                user_email: user.email,
                timestamp: now,
            },
            status: 'pending',
        });
        logSuccess(ctx, 'comando device_linked emitido em tempo real');
    } catch (cmdInsertErr: any) {
        logSupabaseError(ctx, 'falha ao inserir comando device_linked', cmdInsertErr ?? null);
    }

    // A credencial de dispositivo NAO e emitida aqui.
    //
    // O navegador autentica por sessão e não precisa dela; o token era
    // devolvido no corpo da resposta, mas o JavaScript da página nunca o lia —
    // então o hash ficava gravado sem o app nunca receber o token. Resultado:
    // o poll do app caia em "credencial invalida", nao devolvia email e o
    // modal de sucesso ao vincular nunca aparecia.
    //
    // A credencial é de REGRA do dispositivo: o app gera o próprio token e o
    // registra em POST /api/v1/install/credential antes de abrir o navegador.
    // Assim nenhum segredo trafega do servidor para o app.

    return jsonWithCorrelation(ctx, {
        success: true,
        message: 'Dispositivo vinculado com sucesso.',
        installation_id: confirmed.id,
        user_id: confirmed.user_id,
        email: user.email,
        linked_at: confirmed.linked_at ?? now,
        verified: true,
    });
}

/** Health check do endpoint. */
export async function GET() {
    return Response.json({ status: 'ok', endpoint: 'install/link' });
}
