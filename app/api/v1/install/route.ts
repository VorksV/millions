import { NextRequest, NextResponse } from 'next/server';
import { createClient } from '@supabase/supabase-js';
import {
    startOperation,
    getOrCreateCorrelationId,
    logRequest,
    logSupabaseError,
    logSuccess,
    jsonWithCorrelation,
    errorWithCorrelation,
    normalizeUuid,
} from '@/lib/voltris-log';

export const runtime = 'nodejs';

// Cache em memória para evitar rodar upserts pesados no banco a cada 15 segundos
const recentHeartbeatCache = new Map<string, { lastSaved: number; isLinked: boolean }>();

// Rate limiting em memoria. O heartbeat do app desktop chega a cada 15 s
// (4/min); 30/min nao encosta no polling normal e so corta flood/bot.
const rateLimitMap = new Map<string, { count: number; resetAt: number }>();
const RATE_LIMIT = 30;

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
 * POST /api/v1/install
 *
 * Registra / atualiza a instalacao (hardware + heartbeat). Chamado pelo app
 * desktop (sem sessao) e pelo painel (com sessao).
 *
 * Contrato: o app desktop envia snake_case. Por compatibilidade com builds
 * antigos, camelCase tambem e aceito — mas snake_case e o canonico.
 */
export async function POST(request: NextRequest) {
    const correlationId = getOrCreateCorrelationId(request);
    const ctx = startOperation('INSTALL_REGISTER', correlationId);

    const ip = request.headers.get('x-forwarded-for')?.split(',')[0]?.trim() || 'unknown';
    if (!checkRateLimit(ip)) {
        return errorWithCorrelation(ctx, 429, 'RATE_LIMITED', 'Too Many Requests');
    }

    let body: any;
    try {
        body = await request.json();
    } catch {
        logRequest(ctx, request);
        return errorWithCorrelation(ctx, 400, 'INVALID_JSON', 'JSON invalido no corpo da requisicao.');
    }
    logRequest(ctx, request, body);

    // Aceita snake_case (canonico) e camelCase (legado).
    const pick = (...keys: string[]) => {
        for (const k of keys) {
            const camel = k.replace(/_([a-z])/g, (_, c: string) => c.toUpperCase());
            const v = body?.[k] ?? body?.[camel];
            if (v !== undefined && v !== null) return v;
        }
        return undefined;
    };

    const rawInstallationId = pick('installation_id');
    const installationId = normalizeUuid(rawInstallationId);
    if (!installationId) {
        return errorWithCorrelation(
            ctx,
            400,
            'INVALID_INSTALLATION_ID',
            rawInstallationId
                ? 'Formato de installation_id invalido (UUID v1-v8 esperado).'
                : 'Missing installation_id'
        );
    }
    ctx.installationId = installationId;

    // Se a máquina já atualizou no banco há menos de 60s, confirma imediatamente em 1ms
    const nowMs = Date.now();
    const recent = recentHeartbeatCache.get(installationId);
    if (recent && nowMs - recent.lastSaved < 60_000) {
        return jsonWithCorrelation(ctx, {
            success: true,
            installation_id: installationId,
            is_linked: recent.isLinked,
            last_heartbeat: new Date(recent.lastSaved).toISOString(),
        });
    }

    const appVersion = pick('app_version');
    const hardware = body?.hardware ?? body?.Hardware ?? null;

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!supabaseUrl || !supabaseServiceKey) {
        return errorWithCorrelation(ctx, 500, 'SERVER_CONFIG', 'Configuracao do servidor incompleta.');
    }

    const supabase = createClient(supabaseUrl, supabaseServiceKey, {
        auth: { autoRefreshToken: false, persistSession: false },
    });

    // Mapeia nomes antigos e novos. Nao inventar coluna: o upsert abaixo envia
    // apenas colunas que existem em public.installations.
    const h = (snake: string, ...legacy: string[]) => {
        for (const k of [snake, ...legacy]) {
            const camel = k.replace(/_([a-z])/g, (_, c: string) => c.toUpperCase());
            const v = hardware?.[k] ?? hardware?.[camel];
            if (v !== undefined && v !== null) return v;
        }
        return null;
    };

    const now = new Date().toISOString();

    const upsertData: Record<string, unknown> = {
        id: installationId,
        app_version: appVersion ?? null,
        pc_name: h('pc_name', 'hostname', 'pc'),
        cpu_name: h('cpu_name', 'cpu', 'processor'),
        gpu_name: h('gpu_name', 'gpu', 'graphics'),
        disk_type: h('disk_type', 'disk'),
        disk_main_type: h('disk_main_type', 'disk_type', 'disk'),
        os_name: h('os_name', 'os'),
        os_build: h('os_build', 'build'),
        windows_edition: h('windows_edition', 'edition'),
        architecture: h('architecture'),
        last_heartbeat: now,
        updated_at: now,
    };

    const ram = h('ram_gb_total', 'ram', 'memory');
    if (ram !== null && ram !== undefined) {
        const ramNumber = Number(ram);
        if (Number.isFinite(ramNumber)) upsertData.ram_gb_total = Math.round(ramNumber);
    }

    // Colunas nulas nao sao enviadas: nao sobrescreve dado existente com vazio.
    for (const key of Object.keys(upsertData)) {
        if (key === 'id') continue;
        if (upsertData[key] === null) delete upsertData[key];
    }

    const { data, error } = await supabase
        .from('installations')
        .upsert(upsertData, { onConflict: 'id' })
        .select('id, user_id, last_heartbeat, updated_at')
        .single();

    if (error) {
        logSupabaseError(ctx, 'upsert installations', error);
        return errorWithCorrelation(
            ctx,
            500,
            'DB_UPSERT_FAILED',
            'Nao foi possivel registrar a instalacao.',
            { expose: true, details: { pg_code: error.code ?? null } }
        );
    }

    // Confirmacao pos-escrita: o vinculo nao pode ser alterado por este
    // endpoint (quem vincula e o /install/link, via sessao do dono).
    logSuccess(ctx, 'instalacao registrada', {
        linked: Boolean(data?.user_id),
        lastHeartbeat: data?.last_heartbeat ?? null,
        columnsSent: Object.keys(upsertData),
    });

    recentHeartbeatCache.set(installationId, {
        lastSaved: nowMs,
        isLinked: Boolean(data?.user_id),
    });

    return jsonWithCorrelation(ctx, {
        success: true,
        installation_id: data?.id ?? installationId,
        is_linked: Boolean(data?.user_id),
        last_heartbeat: data?.last_heartbeat ?? now,
    });
}

/** Health check do endpoint. */
export async function GET() {
    return NextResponse.json({ status: 'ok', endpoint: 'install' });
}
