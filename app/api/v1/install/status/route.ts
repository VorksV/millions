import { NextRequest } from 'next/server';
import { createClient } from '@supabase/supabase-js';
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
    maskEmail,
} from '@/lib/voltris-log';
import {
    DEVICE_CREDENTIAL_FIELD,
    DEVICE_CREDENTIAL_HEADER,
    generateDeviceCredential,
    hashDeviceCredential,
    readPresentedCredential,
    verifyDeviceCredential,
} from '@/lib/device-credential';

export const runtime = 'nodejs';

export const dynamic = 'force-dynamic';

// In-memory caches para evitar queries desnecessárias ao Supabase
const userEmailCache = new Map<string, { email: string | null; expiresAt: number }>();
const lastLinkCheckWritten = new Map<string, number>();

// Rate limiting em memória.
// Cliente desktop chama a cada 5min (vinculado) ou nunca (sem vínculo).
// Tela de vinculação: pode chamar manualmente algumas vezes.
// 20/min por IP é muito acima do uso legítimo e corta flood/bot.
const rateLimitMap = new Map<string, { count: number; resetAt: number }>();
const RATE_LIMIT = 20;

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
 * GET /api/v1/install/status
 *
 * Estado de vínculo usado pelo app desktop.
 *
 * AUTENTICACAO DO DISPOSITIVO
 * O app não tem sessão. Ele se apresenta com a credencial de dispositivo
 * (header x-voltris-device-credential). A credencial é de REGRA do próprio
 * dispositivo: o app a gera e a registra em POST /api/v1/install/credential.
 * Aqui só verificamos. Quatro casos:
 *
 *   1. sem hash no banco + app apresenta token -> registra o hash do token
 *      APRESENTADO e devolve o email. Cobre a primeira execução e também
 *      desbloqueia máquinas presas por builds antigos (o /install/link rotaciona
 *      o hash, então o próximo poll se auto-registra).
 *   2. sem hash no banco + app sem token       -> bootstrap de transição, emite
 *      uma única credencial. Só para builds muito antigos; o app novo nunca
 *      chega aqui.
 *   3. hash correto                            -> devolve o email do dono.
 *   4. hash incorreto                          -> devolve apenas `linked`, SEM
 *      email e sem reemitir. O app orienta a revincular.
 *
 * Sem esse portão, este endpoint virava um oráculo de e-mail: bastava saber o
 * installation_id para descobrir a quem a máquina pertence.
 *
 * `is_linked === false` só é devolvido com HTTP 200 quando a resposta é
 * conclusiva — é isso que impede o app de tratar "rede caiu" como "desvinculado".
 */
export async function GET(request: NextRequest) {
    const correlationId = getOrCreateCorrelationId(request);
    const ctx = startOperation('INSTALL_STATUS', correlationId);

    const ip = request.headers.get('x-forwarded-for')?.split(',')[0]?.trim() || 'unknown';
    if (!checkRateLimit(ip)) {
        return errorWithCorrelation(ctx, 429, 'RATE_LIMITED', 'Too Many Requests', {
            details: { is_linked: false, linked: null, email: null },
        });
    }

    const { searchParams } = request.nextUrl;
    const installationId = normalizeUuid(searchParams.get('installation_id'));
    const since = searchParams.get('since');

    if (!installationId) {
        return errorWithCorrelation(ctx, 400, 'INVALID_INSTALLATION_ID', 'Missing or invalid installation_id.', {
            details: { is_linked: false },
        });
    }
    ctx.installationId = installationId;

    const ownershipError = await installationOwnershipErrorIfAuthenticated(installationId);
    if (ownershipError) return ownershipError;

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
    const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;
    if (!supabaseUrl || !supabaseServiceKey) {
        return errorWithCorrelation(ctx, 500, 'SERVER_CONFIG', 'Database configuration missing.', {
            details: { is_linked: false },
        });
    }

    const supabase = createClient(supabaseUrl, supabaseServiceKey, {
        auth: { autoRefreshToken: false, persistSession: false },
    });

    const { data: installation, error } = await supabase
        .from('installations')
        .select('id, user_id, linked_at, updated_at, last_heartbeat, device_credential_hash')
        .eq('id', installationId)
        .maybeSingle();

    if (error) {
        logSupabaseError(ctx, 'buscar instalacao', error);
        return errorWithCorrelation(ctx, 500, 'DB_READ_FAILED', 'Falha ao consultar o dispositivo.', {
            details: { is_linked: false, linked: null },
        });
    }

    if (!installation) {
        logWarn(ctx, 'instalacao nao encontrada', { since: since ?? null });
        return errorWithCorrelation(ctx, 404, 'INSTALLATION_NOT_FOUND', 'Installation not found.', {
            details: { linked: null, is_linked: false, email: null },
        });
    }

    let isLinked = Boolean(installation.user_id);

    if (isLinked && since) {
        const sinceDate = new Date(since);
        if (!Number.isNaN(sinceDate.getTime()) && installation.linked_at) {
            if (new Date(installation.linked_at) <= sinceDate) {
                logSuccess(ctx, 'vinculo anterior ao since, ignorado', { since });
                isLinked = false;
            }
        }
    }

    // Credencial apresentada pelo app.
    const presented = readPresentedCredential(
        request.headers.get(DEVICE_CREDENTIAL_HEADER),
        null
    );

    let issuedCredential: string | null = null;
    let credentialValid = false;
    let credentialInvalid = false;

    if (isLinked) {
        if (!installation.device_credential_hash) {
            if (presented) {
                // O app gerou o proprio token e ainda nao conseguiu registrar
                // (build antigo, ou registro anterior falhou). Gravamos o hash
                // do token QUE ELE APRESENTOU: nenhum segredo trafega, e o
                // device ja e o dono do token.
                const { error: lateError } = await supabase
                    .from('installations')
                    .update({
                        device_credential_hash: hashDeviceCredential(presented),
                        device_credential_issued: new Date().toISOString(),
                    })
                    .eq('id', installationId)
                    .is('device_credential_hash', null);

                if (lateError) {
                    logSupabaseError(ctx, 'registrar credencial (tardio)', lateError);
                } else {
                    credentialValid = true;
                    logSuccess(ctx, 'credencial registrada no primeiro poll');
                }
            } else {
                // Transicao: build muito antigo, sem token nenhum. Emite uma
                // vez. O app novo ja registra por POST /install/credential e
                // nunca chega aqui.
                issuedCredential = generateDeviceCredential();
                const { error: issueError } = await supabase
                    .from('installations')
                    .update({
                        device_credential_hash: hashDeviceCredential(issuedCredential),
                        device_credential_issued: new Date().toISOString(),
                    })
                    .eq('id', installationId);

                if (issueError) {
                    logSupabaseError(ctx, 'emitir credencial (bootstrap legado)', issueError);
                    issuedCredential = null;
                } else {
                    credentialValid = true;
                    logSuccess(ctx, 'credencial emitida no bootstrap legado');
                }
            }
        } else {
            // Hash ja existe: ou o app tem a credencial, ou nao tem.
            credentialValid = verifyDeviceCredential(presented, installation.device_credential_hash);
            credentialInvalid = !credentialValid;

            if (credentialInvalid) {
                logWarn(ctx, 'credencial ausente ou invalida; email nao sera devolvido', {
                    presented: Boolean(presented),
                });
            }
        }
    }

    let userEmail: string | null = null;
    if (isLinked && installation.user_id && credentialValid) {
        const cached = userEmailCache.get(installation.user_id);
        const nowMs = Date.now();
        if (cached && nowMs < cached.expiresAt) {
            userEmail = cached.email;
        } else {
            const { data: userData, error: userError } = await supabase.auth.admin.getUserById(installation.user_id);
            if (userError) {
                logSupabaseError(ctx, 'buscar email do usuario', userError);
            } else {
                userEmail = userData?.user?.email ?? null;
                userEmailCache.set(installation.user_id, {
                    email: userEmail,
                    expiresAt: nowMs + 15 * 60_000, // Cache de 15 minutos
                });
            }
        }
    }

    // Marca que o dispositivo consultou o estado (apenas a cada 5 minutos para nao floodar o banco).
    const lastCheck = lastLinkCheckWritten.get(installationId) ?? 0;
    const nowMs = Date.now();
    if (nowMs - lastCheck > 5 * 60_000) {
        lastLinkCheckWritten.set(installationId, nowMs);
        try {
            await supabase
                .from('installations')
                .update({ last_link_check_at: new Date(nowMs).toISOString() })
                .eq('id', installationId);
        } catch (e) {
            logWarn(ctx, 'nao foi possivel registrar last_link_check_at', { reason: (e as Error)?.message });
        }
    }

    logSuccess(ctx, 'status consultado', {
        isLinked,
        credentialValid,
        credentialInvalid,
        user: maskEmail(userEmail),
    });

    // Cache na CDN da Vercel:
    //   - Vinculado: 5min (cliente consulta a cada 5min — quase 100% de HIT na CDN).
    //     stale-while-revalidate de 1min garante que o HIT nunca bloqueia.
    //   - Não vinculado: 10s de cache na CDN (permite resposta rápida na tela de vinculação,
    //     mas impede que polling de 1s a 5s consuma milhares de invocações serverless).
    const cacheHeaders: Record<string, string> = isLinked
        ? {
            'Cache-Control': 'public, s-maxage=300, stale-while-revalidate=60',
            'CDN-Cache-Control': 'public, s-maxage=300, stale-while-revalidate=60',
            'Vercel-CDN-Cache-Control': 'public, s-maxage=300, stale-while-revalidate=60',
            'Vary': 'x-voltris-device-credential',
          }
        : {
            'Cache-Control': 'public, s-maxage=10, stale-while-revalidate=10',
            'CDN-Cache-Control': 'public, s-maxage=10, stale-while-revalidate=10',
            'Vercel-CDN-Cache-Control': 'public, s-maxage=10, stale-while-revalidate=10',
            'Vary': 'x-voltris-device-credential',
          };

    return jsonWithCorrelation(
        ctx,
        {
            linked: isLinked,
            is_linked: isLinked,
            email: userEmail,
            user_email: userEmail,
            user_id: installation.user_id,
            installation_id: installationId,
            linked_at: installation.linked_at,
            last_updated: installation.updated_at,
            last_heartbeat: installation.last_heartbeat,
            credential_valid: isLinked ? credentialValid : null,
            credential_invalid: isLinked ? credentialInvalid : null,
            ...(issuedCredential ? { [DEVICE_CREDENTIAL_FIELD]: issuedCredential } : {}),
        },
        200,
        cacheHeaders
    );
}
