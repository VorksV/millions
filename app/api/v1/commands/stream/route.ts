import { createClient } from '@supabase/supabase-js';
import { NextRequest } from 'next/server';
import { normalizeUuid } from '@/lib/voltris-log';

/**
 * GET /api/v1/commands/stream
 *
 * Server-Sent Events (SSE) — "bate na porta > abre".
 *
 * O app desktop abre UMA conexão HTTP longa aqui e fica ouvindo.
 * Quando um comando é inserido em device_commands (pelo dashboard),
 * o Supabase Realtime notifica este servidor em <1 s, e o servidor
 * empurra o evento diretamente para o app — sem polling.
 *
 * Fluxo:
 *   1. Desktop conecta → servidor assina Realtime para aquele machine_id.
 *   2. Dashboard cria comando → Supabase faz INSERT → Realtime dispara.
 *   3. Servidor envia "data: {...}" via SSE → Desktop executa o comando.
 *   4. Após ~4m50s o servidor envia "event: reconnect" e fecha.
 *   5. Desktop reconecta imediatamente (sem janela de perda de evento).
 *
 * Custo Vercel: 1 invocação/5 min por máquina (vs. 1/min com polling antigo).
 * Latência de entrega: < 1 s (vs. até 5 min com polling).
 */

export const runtime = 'nodejs';

/**
 * maxDuration: Vercel Pro suporta até 300 s. Hobby até 60 s.
 * O servidor fecha aos 290 s e instrui o cliente a reconectar
 * para não ser cortado abruptamente pelo timeout da plataforma.
 */
export const maxDuration = 300;
export const dynamic = 'force-dynamic';

// SSE session registry: limita 1 conexão ativa por machine_id.
const activeSessions = new Map<string, AbortController>();

// Rate limit: máximo 10 novas conexões SSE por IP por minuto.
const sseRateLimit = new Map<string, { count: number; resetAt: number }>();

function checkSseRateLimit(ip: string): boolean {
    const now = Date.now();
    const entry = sseRateLimit.get(ip);
    if (!entry || now >= entry.resetAt) {
        sseRateLimit.set(ip, { count: 1, resetAt: now + 60_000 });
        return true;
    }
    if (entry.count >= 10) return false;
    entry.count++;
    return true;
}

/** Duração máxima de uma sessão SSE (folga de 10 s antes do maxDuration). */
const SESSION_DURATION_MS = 290_000; // 4 min 50 s

/** Keep-alive para evitar que proxies/balanceadores fechem conexões ociosas. */
const PING_INTERVAL_MS = 25_000; // 25 s

export async function GET(req: NextRequest) {
    const ip = req.headers.get('x-forwarded-for')?.split(',')[0]?.trim() ?? 'unknown';

    if (!checkSseRateLimit(ip)) {
        return new Response('Too Many Requests', { status: 429 });
    }

    const { searchParams } = req.nextUrl;
    const rawId = searchParams.get('machine_id') ?? searchParams.get('device_id');
    const machineId = normalizeUuid(rawId);

    if (!machineId) {
        return new Response('Missing or invalid machine_id', { status: 400 });
    }

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!supabaseUrl || !supabaseServiceKey) {
        return new Response('Server configuration error', { status: 500 });
    }

    // Se já existe uma sessão para este machine_id, aborta a antiga
    // (o app reconectou — não queremos dois listeners para o mesmo dispositivo).
    activeSessions.get(machineId)?.abort();

    const sessionAbort = new AbortController();
    activeSessions.set(machineId, sessionAbort);

    const encoder = new TextEncoder();

    const stream = new ReadableStream({
        async start(controller) {

            const send = (eventType: string, data: string) => {
                try {
                    const payload =
                        eventType === 'ping'
                            ? `: ping\n\n`
                            : `event: ${eventType}\ndata: ${data}\n\n`;
                    controller.enqueue(encoder.encode(payload));
                } catch {
                    // Conexão já fechada — ignora.
                }
            };

            // ── Supabase Realtime ──────────────────────────────────────────
            const supabase = createClient(supabaseUrl, supabaseServiceKey, {
                auth: { autoRefreshToken: false, persistSession: false },
                realtime: { params: { eventsPerSecond: 10 } },
            });

            const channelName = `commands:${machineId}`;
            const channel = supabase
                .channel(channelName)
                .on(
                    'postgres_changes',
                    {
                        event: 'INSERT',
                        schema: 'public',
                        table: 'device_commands',
                        filter: `installation_id=eq.${machineId}`,
                    },
                    (payload) => {
                        const cmd = payload.new as {
                            id: string;
                            command_type: string;
                            payload: unknown;
                            status: string;
                            created_at: string;
                        };

                        // Só entrega comandos pendentes e não expirados.
                        if (cmd.status !== 'pending') return;

                        const ttlMs = 30 * 60 * 1000; // 30 min (igual ao endpoint /pending)
                        const age = Date.now() - new Date(cmd.created_at).getTime();
                        if (age > ttlMs) return;

                        send('command', JSON.stringify(cmd));

                        if (cmd.command_type === 'device_linked') {
                            const email = (cmd.payload as any)?.user_email || null;
                            send('link_status_changed', JSON.stringify({
                                is_linked: true,
                                user_email: email,
                                timestamp: new Date().toISOString()
                            }));
                        } else if (cmd.command_type === 'device_unlinked' || cmd.command_type === 'device_unlink') {
                            send('link_status_changed', JSON.stringify({
                                is_linked: false,
                                user_email: null,
                                timestamp: new Date().toISOString()
                            }));
                        }
                    }
                )

                .subscribe();

            // ── Keep-alive pings ───────────────────────────────────────────
            const pingTimer = setInterval(() => {
                send('ping', '');
            }, PING_INTERVAL_MS);

            // ── Encerramento por timeout ───────────────────────────────────
            const sessionTimer = setTimeout(() => {
                send('reconnect', '{}');
                cleanup();
            }, SESSION_DURATION_MS);

            // ── Cleanup centralizado ───────────────────────────────────────
            let cleanedUp = false;
            function cleanup() {
                if (cleanedUp) return;
                cleanedUp = true;

                clearInterval(pingTimer);
                clearTimeout(sessionTimer);
                supabase.removeChannel(channel).catch(() => {});
                activeSessions.delete(machineId!);

                try { controller.close(); } catch { /* já fechado */ }
            }

            // Quando o cliente desconecta (fecha o app / perde rede):
            sessionAbort.signal.addEventListener('abort', cleanup);
            req.signal?.addEventListener('abort', cleanup);
        },

        cancel() {
            // ReadableStream cancelado (cliente desconectou durante leitura).
            activeSessions.delete(machineId!);
        },
    });

    return new Response(stream, {
        status: 200,
        headers: {
            'Content-Type': 'text/event-stream; charset=utf-8',
            'Cache-Control': 'no-cache, no-transform',
            'X-Accel-Buffering': 'no',      // Desabilita buffer do nginx/proxy
            'Connection': 'keep-alive',
            'Transfer-Encoding': 'chunked',
        },
    });
}
