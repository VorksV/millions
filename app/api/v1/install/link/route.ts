import { NextRequest } from 'next/server';
import { createClient } from '@supabase/supabase-js';
import { normalizeUuid, startOperation, getOrCreateCorrelationId, jsonWithCorrelation, errorWithCorrelation, logSuccess } from '@/lib/voltris-log';

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

/**
 * POST /api/v1/install/link
 *
 * Gera um link de vinculação temporário para o usuário conectar sua conta.
 *
 * Padrão "bate na porta > abre":
 *   1. App chama este endpoint com installation_id
 *   2. Backend gera um link_id único (JWT curto, 5 min TTL)
 *   3. Retorna link para app abrir no navegador
 *   4. Usuário faz login no site, confirma vinculação
 *   5. Site faz POST /api/v1/install/confirm {link_id, user_email}
 *   6. Backend atualiza installations.user_id
 *   7. Supabase trigger dispara evento
 *   8. SSE notifica app em <1s: "você foi vinculado"
 *   9. App fecha janela automaticamente
 *
 * IMPORTANTE: Não há polling aqui. App faz UMA requisição, depois aguarda notificação via SSE.
 */
export async function POST(request: NextRequest) {
    const correlationId = getOrCreateCorrelationId(request);
    const ctx = startOperation('INSTALL_LINK', correlationId);

    try {
        const body = await request.json();
        const { installation_id } = body;

        if (!installation_id) {
            return errorWithCorrelation(ctx, 400, 'INVALID_INPUT', 'Missing installation_id', {});
        }

        const normalizedId = normalizeUuid(installation_id);
        if (!normalizedId) {
            return errorWithCorrelation(ctx, 400, 'INVALID_INSTALLATION_ID', 'Invalid installation_id format', {});
        }

        ctx.installationId = normalizedId;

        const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
        const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;

        if (!supabaseUrl || !supabaseServiceKey) {
            return errorWithCorrelation(ctx, 500, 'SERVER_CONFIG', 'Database configuration missing', {});
        }

        const supabase = createClient(supabaseUrl, supabaseServiceKey, {
            auth: { autoRefreshToken: false, persistSession: false },
        });

        // Verificar que a instalação existe
        const { data: installation, error: checkError } = await supabase
            .from('installations')
            .select('id')
            .eq('id', normalizedId)
            .maybeSingle();

        if (checkError) {
            return errorWithCorrelation(ctx, 500, 'DB_ERROR', 'Failed to check installation', {});
        }

        if (!installation) {
            return errorWithCorrelation(ctx, 404, 'NOT_FOUND', 'Installation not found', {});
        }

        // Gerar um link_id temporário
        // Em produção, seria um JWT curto assinado com TTL de 5 minutos
        // Para MVP, pode ser um UUID com timestamp
        const link_id = generateLinkId();
        const now = new Date();
        const expiresAt = new Date(now.getTime() + 5 * 60_000); // 5 minutos

        // Armazenar em tabela 'link_sessions' para verificar depois
        // Se a tabela não existir, criar dinamicamente (ou usar 'installations' temp field)
        // Para MVP: apenas retornar o link_id
        // Em produção: armazenar no banco com TTL

        logSuccess(ctx, 'link gerado', {
            installation_id: normalizedId,
            link_id: maskId(link_id),
            expires_in_seconds: 300,
        });

        // Retornar URL de vinculação para o app abrir no navegador
        return jsonWithCorrelation(
            ctx,
            {
                link_id,
                browser_url: `https://www.voltris.com.br/auth/link?link_id=${link_id}`,
                expires_in_seconds: 300,
                instructions: 'Abra esta URL no seu navegador e faça login para confirmar a vinculação.',
            },
            200,
            {
                'Cache-Control': 'no-cache, no-store, must-revalidate',
            }
        );
    } catch (error) {
        return errorWithCorrelation(
            ctx,
            500,
            'SERVER_ERROR',
            `Unexpected error: ${error instanceof Error ? error.message : 'Unknown'}`,
            {}
        );
    }
}

/**
 * Gera um link_id único com timestamp
 * Em produção, seria um JWT assinado com chave privada
 */
function generateLinkId(): string {
    const timestamp = Date.now();
    const random = Math.random().toString(36).substring(2, 15);
    return `${timestamp}-${random}`;
}

/**
 * Mascarar ID para logs (ex: "1730...a7b2")
 */
function maskId(id: string): string {
    if (id.length <= 8) return id;
    return `${id.substring(0, 4)}...${id.substring(id.length - 4)}`;
}
