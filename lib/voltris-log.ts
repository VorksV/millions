/**
 * Correlation ID + log estruturado para o canal SITE <-> SUPABASE <-> APP.
 *
 * Objetivo: uma operacao de vinculo/desvinculo precisa ser rastreada de ponta a
 * ponta. O mesmo identificador aparece no log do app desktop, no log do servidor
 * (Vercel) e na resposta HTTP, entao qualquer falha pode ser localizada
 * exatamente em qual camada ocorreu.
 *
 * Exemplo: VOLTRIS-LINK-20260926-7F3A9C21
 *
 * NUNCA registrar: senha, access token, refresh token, service role key,
 * payload de assinatura, chave privada. `redact()` centraliza essa garantia.
 */

import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';

const CORRELATION_HEADER = 'x-correlation-id';

export type VoltrisOperation =
    | 'INSTALL_REGISTER'
    | 'INSTALL_LINK'
    | 'INSTALL_UNLINK'
    | 'INSTALL_STATUS'
    | 'INSTALL_HARDWARE'
    | 'INSTALL_FORCE_LINK'
    | 'INSTALL_CREDENTIAL'
    | 'COMMAND_CREATE'
    | 'COMMAND_PENDING'
    | 'COMMAND_UPDATE'
    | 'LICENSE_ACTIVATE'
    | 'LICENSE_DEACTIVATE'
    | 'LICENSE_INFO'
    | 'LICENSE_ME'
    | 'LICENSE_SYNC'
    | 'LICENSE_VALIDATE';

type LogLevel = 'info' | 'warn' | 'error';

export interface OperationContext {
    /** Correlation ID propagado do app desktop (se enviado) ou gerado aqui. */
    correlationId: string;
    operation: VoltrisOperation;
    startedAt: number;
    /** Preenchido quando a operacao exige usuario autenticado. */
    userId?: string;
    installationId?: string;
    /** Metadados seguros ja sanitizados. */
    meta: Record<string, unknown>;
}

/**
 * Gera um correlation ID no formato do padrao VOLTRIS.
 * Prefixo: VOLTRIS-LINK para o fluxo de vinculo (padrao do projeto).
 */
export function generateCorrelationId(prefix = 'VOLTRIS-LINK'): string {
    const now = new Date();
    const y = now.getFullYear();
    const m = String(now.getMonth() + 1).padStart(2, '0');
    const d = String(now.getDate()).padStart(2, '0');
    const rand = Array.from({ length: 4 }, () => Math.floor(Math.random() * 0x10000))
        .map((n) => n.toString(16).toUpperCase().padStart(4, '0'))
        .join('');
    return `${prefix}-${y}${m}${d}-${rand}`;
}

/**
 * Remove qualquer segredo de um objeto antes de logar.
 * A lista negada e sempre aplicada, independentemente do que o chamador passe.
 */
const FORBIDDEN_KEYS = [
    'password',
    'senha',
    'password_hash',
    'access_token',
    'refresh_token',
    'token',
    'authorization',
    'apikey',
    'api_key',
    'supabase_service_role_key',
    'service_role',
    'secret',
    'signature',
    'x-voltris-signature',
    'private_key',
    'session',
];

const MAX_STRING = 300;

export function redact(value: unknown, depth = 0): unknown {
    if (value === null || value === undefined) return value;
    if (depth > 4) return '[deep]';

    if (typeof value === 'string') {
        return value.length > MAX_STRING ? `${value.slice(0, MAX_STRING)}…[trunc]` : value;
    }
    if (typeof value === 'number' || typeof value === 'boolean') return value;
    if (Array.isArray(value)) return value.slice(0, 20).map((v) => redact(v, depth + 1));

    if (typeof value === 'object') {
        const out: Record<string, unknown> = {};
        for (const [k, v] of Object.entries(value as Record<string, unknown>)) {
            const lower = k.toLowerCase();
            if (FORBIDDEN_KEYS.some((f) => lower === f.toLowerCase() || lower.includes(f.toLowerCase()))) {
                out[k] = '[redacted]';
                continue;
            }
            out[k] = redact(v, depth + 1);
        }
        return out;
    }
    return '[unserializable]';
}

/**
 * Extrai o correlation ID do pedido (header `x-correlation-id`) ou gera um novo.
 */
export function getOrCreateCorrelationId(req: NextRequest): string {
    const incoming = req.headers.get(CORRELATION_HEADER);
    if (incoming && /^[A-Za-z0-9-]{8,64}$/.test(incoming)) return incoming;
    return generateCorrelationId();
}

export function startOperation(
    operation: VoltrisOperation,
    correlationId: string
): OperationContext {
    return { correlationId, operation, startedAt: Date.now(), meta: {} };
}

function emit(level: LogLevel, ctx: OperationContext, message: string, extra?: Record<string, unknown>) {
    const durationMs = Date.now() - ctx.startedAt;
    const payload = {
        correlationId: ctx.correlationId,
        operation: ctx.operation,
        userId: ctx.userId ?? null,
        installationId: ctx.installationId ?? null,
        durationMs,
        ...(extra ? (redact(extra) as Record<string, unknown>) : {}),
    };

    const line = `[VOLTRIS][${ctx.correlationId}][${ctx.operation}] ${message}`;

    if (level === 'error') console.error(line, payload);
    else if (level === 'warn') console.warn(line, payload);
    else console.log(line, payload);
}

export function logInfo(ctx: OperationContext, message: string, extra?: Record<string, unknown>) {
    emit('info', ctx, message, extra);
}

export function logWarn(ctx: OperationContext, message: string, extra?: Record<string, unknown>) {
    emit('warn', ctx, message, extra);
}

export function logSuccess(ctx: OperationContext, message: string, extra?: Record<string, unknown>) {
    emit('info', ctx, message, extra);
}

export function logError(ctx: OperationContext, message: string, extra?: Record<string, unknown>) {
    emit('error', ctx, message, extra);
}

/** Log de acesso: request recebido, com payload seguro. */
export function logRequest(ctx: OperationContext, req: NextRequest, body?: unknown) {
    emit('info', ctx, 'request recebido', {
        method: req.method,
        path: req.nextUrl.pathname,
        ip: req.headers.get('x-forwarded-for')?.split(',')[0]?.trim() ?? 'unknown',
        userAgent: req.headers.get('user-agent') ?? 'unknown',
        body: body === undefined ? undefined : redact(body),
    });
}

/** Log de saida: status HTTP, duracao total e corpo de resposta seguro. */
export function logResponse(
    ctx: OperationContext,
    status: number,
    body?: unknown,
    extra?: Record<string, unknown>
) {
    const level: LogLevel = status >= 500 ? 'error' : status >= 400 ? 'warn' : 'info';
    emit(level, ctx, `resposta HTTP ${status}`, {
        status,
        totalMs: Date.now() - ctx.startedAt,
        body: body === undefined ? undefined : redact(body),
        ...(extra ? (redact(extra) as Record<string, unknown>) : {}),
    });
}

/**
 * Converte um erro do supabase-js em log estruturado com o codigo Postgres.
 */
export function logSupabaseError(
    ctx: OperationContext,
    step: string,
    error: { message?: string; code?: string; details?: string | null; hint?: string | null } | null
) {
    if (!error) return;
    emit('error', ctx, `falha no Supabase em ${step}`, {
        step,
        pgCode: error.code ?? null,
        message: error.message ?? null,
        details: error.details ?? null,
        hint: error.hint ?? null,
    });
}

/**
 * Resposta JSON padronizada: sempre inclui o correlation ID para o cliente
 * poder reportar o problema, e nunca expoe stack trace em producao.
 */
export function jsonWithCorrelation(
    ctx: OperationContext,
    body: Record<string, unknown>,
    status = 200,
    customHeaders?: Record<string, string>
): NextResponse {
    const payload = { ...body, correlation_id: ctx.correlationId };
    logResponse(ctx, status, payload);
    return NextResponse.json(payload, {
        status,
        headers: {
            [CORRELATION_HEADER]: ctx.correlationId,
            ...(customHeaders ?? {}),
        },
    });
}

/**
 * Resposta de erro padronizada.
 * `expose` controla se a mensagem tecnica vai para o cliente. Em producao,
 * so mensagens de dominio (valores invalidos, nao autorizado) sao expostas.
 */
export function errorWithCorrelation(
    ctx: OperationContext,
    status: number,
    code: string,
    message: string,
    options?: { expose?: boolean; details?: Record<string, unknown> }
): NextResponse {
    const expose = options?.expose ?? status < 500;
    const payload: Record<string, unknown> = {
        error: message,
        code,
        correlation_id: ctx.correlationId,
    };
    if (expose && options?.details) payload.details = redact(options.details);

    logResponse(ctx, status, payload, { code });
    return NextResponse.json(payload, {
        status,
        headers: { [CORRELATION_HEADER]: ctx.correlationId },
    });
}

/** Mascara e-mail para log: v****@dominio.com */
export function maskEmail(email?: string | null): string | null {
    if (!email) return null;
    const [local, domain] = email.split('@');
    if (!domain) return '***';
    const head = local.slice(0, 1);
    return `${head}${'*'.repeat(Math.max(2, local.length - 1))}@${domain}`;
}

/** UUID estritamente RFC 4122 (versao 1-8, variante 8/9/A/B). */
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export function isValidUuid(value: unknown): value is string {
    return typeof value === 'string' && UUID_RE.test(value.trim());
}

export function normalizeUuid(value: unknown): string | null {
    if (!isValidUuid(value)) return null;
    return value.trim().toLowerCase();
}
