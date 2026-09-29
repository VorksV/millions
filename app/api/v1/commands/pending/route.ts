import { createClient } from '@supabase/supabase-js';
import { NextRequest } from 'next/server';
import { installationOwnershipErrorIfAuthenticated } from '@/utils/supabase/ownership';
import {
    startOperation,
    getOrCreateCorrelationId,
    logSuccess,
    logWarn,
    logSupabaseError,
    jsonWithCorrelation,
    errorWithCorrelation,
    normalizeUuid,
} from '@/lib/voltris-log';

export const dynamic = 'force-dynamic';
export const revalidate = 0;

/** Comandos expirados nao sao entregues: evita replay de comando antigo. */
const COMMAND_TTL_MINUTES = 30;

// Rate limiting em memoria. O app desktop consulta a cada 15 s (4/min), entao
// 30/min nao encosta no polling normal e so corta flood/bot.
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
 * GET /api/v1/commands/pending
 *
 * Entrega os comandos pendentes de UMA maquina. O dispositivo authentica pelo
 * proprio installation_id (que ele gera e persiste localmente).
 */
export async function GET(req: NextRequest) {
    const correlationId = getOrCreateCorrelationId(req);
    const ctx = startOperation('COMMAND_PENDING', correlationId);

    const ip = req.headers.get('x-forwarded-for')?.split(',')[0]?.trim() || 'unknown';
    if (!checkRateLimit(ip)) {
        return errorWithCorrelation(ctx, 429, 'RATE_LIMITED', 'Too Many Requests', {
            details: { commands: [] },
        });
    }

    const { searchParams } = req.nextUrl;
    const rawId = searchParams.get('machine_id') ?? searchParams.get('device_id');
    const machineId = normalizeUuid(rawId);

    if (!machineId) {
        return errorWithCorrelation(ctx, 400, 'INVALID_MACHINE_ID', 'Missing or invalid machine_id', {
            details: { commands: [] },
        });
    }
    ctx.installationId = machineId;

    const ownershipError = await installationOwnershipErrorIfAuthenticated(machineId);
    if (ownershipError) return ownershipError;

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!supabaseUrl || !supabaseServiceKey) {
        return errorWithCorrelation(ctx, 500, 'SERVER_CONFIG', 'Database configuration missing', {
            details: { commands: [] },
        });
    }

    const supabase = createClient(supabaseUrl, supabaseServiceKey, {
        auth: { autoRefreshToken: false, persistSession: false },
    });

    const { data: installation, error: installError } = await supabase
        .from('installations')
        .select('id')
        .eq('id', machineId)
        .maybeSingle();

    if (installError) {
        logSupabaseError(ctx, 'buscar instalacao', installError);
        return errorWithCorrelation(ctx, 500, 'DB_READ_FAILED', 'Erro ao consultar o dispositivo.', {
            details: { commands: [] },
        });
    }

    if (!installation) {
        // Dispositivo ainda nao registrado: responde lista vazia (nao erro) para
        // o app nao entrar em loop de retry.
        logWarn(ctx, 'instalacao nao encontrada; retornando lista vazia');
        return jsonWithCorrelation(ctx, { commands: [], registered: false });
    }

    const notExpired = new Date(Date.now() - COMMAND_TTL_MINUTES * 60_000).toISOString();

    const { data: commands, error } = await supabase
        .from('device_commands')
        .select('id, command_type, payload, status, created_at')
        .eq('installation_id', installation.id)
        .eq('status', 'pending')
        .gte('created_at', notExpired)
        .order('created_at', { ascending: true });

    if (error) {
        logSupabaseError(ctx, 'buscar device_commands pendentes', error);
        return errorWithCorrelation(ctx, 500, 'DB_COMMAND_READ_FAILED', 'Erro ao buscar comandos.', {
            details: { commands: [] },
        });
    }

    logSuccess(ctx, 'comandos pendentes entregues', { count: commands?.length ?? 0 });

    const hasCommands = Boolean(commands && commands.length > 0);
    const cacheHeaders = hasCommands
        ? { 'Cache-Control': 'no-store, no-cache, must-revalidate' }
        : {
            'Cache-Control': 'public, s-maxage=10, stale-while-revalidate=20',
            'CDN-Cache-Control': 'public, s-maxage=10, stale-while-revalidate=20',
            'Vercel-CDN-Cache-Control': 'public, s-maxage=10, stale-while-revalidate=20',
        };

    return jsonWithCorrelation(
        ctx,
        { commands: commands ?? [], registered: true },
        200,
        cacheHeaders
    );
}
