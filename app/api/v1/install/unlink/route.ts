import { NextRequest } from 'next/server';
import { createClient } from '@supabase/supabase-js';
import { createClient as createServerClient } from '@/utils/supabase/server';
import {
    startOperation,
    getOrCreateCorrelationId,
    logRequest,
    logSuccess,
    logWarn,
    logSupabaseError,
    jsonWithCorrelation,
    errorWithCorrelation,
    normalizeUuid,
} from '@/lib/voltris-log';
import {
    DEVICE_CREDENTIAL_HEADER,
    readPresentedCredential,
    verifyDeviceCredential,
} from '@/lib/device-credential';

export const runtime = 'nodejs';

// Rate limiting em memoria. Desvinculacao e acionada por um clique (1 chamada
// por acao), tanto no dashboard quanto no app. 10/min nao atrapalha ninguem e
// impede que alguem use esta rota para varrer installation_ids.
const rateLimitMap = new Map<string, { count: number; resetAt: number }>();
const RATE_LIMIT = 10;

function checkRateLimit(ip: string): boolean {
    const now = Date.now();
    const entry = rateLimitMap.get(ip);
    if (!entry || now >= entry.resetAt) {
        rateLimitMap.set(ip, { count: 1, resetAt: now + 60000 });
        return true;
    }
    if (entry.count >= RATE_LIMIT) return false;
    entry.count++;
    return true;
}

/**
 * POST /api/v1/install/unlink
 *
 * Desvincula a maquina. Duas autorizacoes, conforme quem chama:
 *
 *   1. SESSAO (dashboard do site) — o dono desvinca pela aba MyComputer.
 *      Exigida, como antes.
 *
 *   2. CREDENCIAL DE DISPOSITIVO (app desktop) — o proprio dispositivo se
 *      desvincula. E o que permite o botao "Desvincular deste Computador"
 *      funcionar: o app nao tem cookie de sessao, tem a credencial emitida no
 *      momento do vinculo.
 *
 * Sem sessao E sem credencial valida => 401. Nunca desvincula "por saber o
 * installation_id", que e o que causava o IDOR.
 *
 * O `installation_id` e aceito no body OU na query string.
 */
export async function POST(request: NextRequest) {
    const correlationId = getOrCreateCorrelationId(request);
    const ctx = startOperation('INSTALL_UNLINK', correlationId);

    const ip = request.headers.get('x-forwarded-for')?.split(',')[0]?.trim() || 'unknown';
    if (!checkRateLimit(ip)) {
        return errorWithCorrelation(ctx, 429, 'RATE_LIMITED', 'Too Many Requests');
    }

    let body: any = {};
    try {
        body = await request.json();
    } catch {
        body = {};
    }
    logRequest(ctx, request, body);

    const rawId =
        body?.installation_id ??
        body?.installationId ??
        request.nextUrl.searchParams.get('installation_id');

    const installationId = normalizeUuid(rawId);
    if (!installationId) {
        return errorWithCorrelation(ctx, 400, 'INVALID_INSTALLATION_ID', 'Missing or invalid installation_id.');
    }
    ctx.installationId = installationId;

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!supabaseUrl || !supabaseServiceKey) {
        return errorWithCorrelation(ctx, 500, 'SERVER_CONFIG', 'Configuracao do servidor incompleta.');
    }

    const admin = createClient(supabaseUrl, supabaseServiceKey, {
        auth: { autoRefreshToken: false, persistSession: false },
    });

    const { data: installation, error: fetchError } = await admin
        .from('installations')
        .select('id, user_id, device_credential_hash')
        .eq('id', installationId)
        .maybeSingle();

    if (fetchError) {
        logSupabaseError(ctx, 'buscar instalacao', fetchError);
        return errorWithCorrelation(ctx, 500, 'DB_READ_FAILED', 'Erro ao verificar instalacao.', {
            expose: true,
            details: { pg_code: fetchError.code ?? null },
        });
    }

    if (!installation) {
        return errorWithCorrelation(ctx, 404, 'INSTALLATION_NOT_FOUND', 'Instalacao nao encontrada.');
    }

    // --- Autorizacao: sessao OU credencial de dispositivo ---
    const supabase = await createServerClient();
    const { data: sessionData } = await supabase.auth.getUser();
    const user = sessionData?.user ?? null;

    const presentedCredential = readPresentedCredential(
        request.headers.get(DEVICE_CREDENTIAL_HEADER),
        body?.device_credential
    );
    const credentialOk = verifyDeviceCredential(presentedCredential, installation.device_credential_hash);

    if (user) {
        ctx.userId = user.id;
        if (installation.user_id !== user.id) {
            return errorWithCorrelation(ctx, 403, 'NOT_OWNER', 'Voce nao e dono desta instalacao.');
        }
    } else if (!credentialOk) {
        logWarn(ctx, 'desvinculo sem sessao e sem credencial valida', {
            presented: Boolean(presentedCredential),
            hasStoredCredential: Boolean(installation.device_credential_hash),
        });
        return errorWithCorrelation(
            ctx,
            401,
            'UNAUTHORIZED',
            'Sessao ausente e credencial de dispositivo invalida. Revincule o dispositivo pela conta.'
        );
    } else {
        logSuccess(ctx, 'desvinculo autorizado por credencial de dispositivo');
    }

    if (installation.user_id === null) {
        // Ja desvinculado: idempotente, nao e erro.
        logSuccess(ctx, 'instalacao ja estava desvinculada');
        return jsonWithCorrelation(ctx, {
            success: true,
            already_unlinked: true,
            installation_id: installationId,
        });
    }

    // Descarte a credencial: ela vale para UM vinculo. Revincular gera outra.
    const { error: updateError } = await admin
        .from('installations')
        .update({
            user_id: null,
            linked_at: null,
            device_credential_hash: null,
            device_credential_issued: null,
            unlinked_at: new Date().toISOString(),
            updated_at: new Date().toISOString(),
        })
        .eq('id', installationId)
        .not('user_id', 'is', null);

    if (updateError) {
        logSupabaseError(ctx, 'desvincular instalacao', updateError);
        return errorWithCorrelation(ctx, 500, 'DB_UNLINK_FAILED', 'Erro ao desvincular dispositivo.', {
            expose: true,
            details: { pg_code: updateError.code ?? null },
        });
    }

    // Confirmacao pos-escrita (regra 19).
    const { data: confirmed, error: confirmError } = await admin
        .from('installations')
        .select('id, user_id, device_credential_hash')
        .eq('id', installationId)
        .single();

    if (confirmError || !confirmed || confirmed.user_id !== null) {
        logSupabaseError(ctx, 'confirmar desvinculo', confirmError);
        return errorWithCorrelation(
            ctx,
            500,
            'UNLINK_NOT_CONFIRMED',
            'A desvinculacao nao pode ser confirmada no banco.'
        );
    }

    // Limpeza de comandos pendentes. Nao e mais engolida em silencio: se
    // falhar, o vinculo continua desfeito e o cliente recebe o aviso.
    let pendingRemoved = 0;
    let cleanupWarning: string | null = null;
    try {
        const { count, error: cmdError } = await admin
            .from('device_commands')
            .delete({ count: 'exact' })
            .eq('installation_id', installationId)
            .eq('status', 'pending');
        if (cmdError) throw cmdError;
        pendingRemoved = count ?? 0;
    } catch (cmdError: any) {
        cleanupWarning = 'Comandos pendentes nao foram limpos.';
        logSupabaseError(ctx, 'limpar device_commands pendentes', cmdError ?? null);
    }

    logSuccess(ctx, 'desvinculacao confirmada', {
        authorizedBy: user ? 'session' : 'device_credential',
        pendingRemoved,
        cleanupWarning,
    });

    return jsonWithCorrelation(ctx, {
        success: true,
        verified: true,
        installation_id: confirmed.id,
        pending_commands_removed: pendingRemoved,
        ...(cleanupWarning ? { warning: cleanupWarning } : {}),
    });
}

/** Health check do endpoint. */
export async function GET() {
    return Response.json({ status: 'ok', endpoint: 'install/unlink' });
}
