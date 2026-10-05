import { NextRequest } from 'next/server';
import { normalizeUuid, startOperation, getOrCreateCorrelationId, jsonWithCorrelation, errorWithCorrelation, logSuccess } from '@/lib/voltris-log';

export const runtime = 'nodejs';
export const dynamic = 'force-dynamic';

/**
 * GET /api/v1/install/version-check
 *
 * Verifica se a versão do app é compatível com o backend.
 * Se for antiga demais, força atualização.
 *
 * Fluxo:
 * 1. App envia sua versão: GET /api/v1/install/version-check?app_version=1.0.0&installation_id={uuid}
 * 2. Servidor compara com versão mínima obrigatória
 * 3. Se app_version < min_version:
 *    - Retorna HTTP 410 GONE (ou 426 UPGRADE_REQUIRED)
 *    - Força atualização
 * 4. Se app_version >= min_version:
 *    - Retorna HTTP 200 OK
 *    - App continua normalmente
 *
 * IMPORTANTE: Versões antigas (que fazem polling infinito) são bloqueadas aqui.
 */

// Versão mínima obrigatória
// Atualize este número quando lançar versão com event-driven
const MIN_APP_VERSION = '1.0.3.0'; // Event-driven com SSE
const POLLING_VERSION_THRESHOLD = '1.0.2.9'; // Versões <= 1.0.2.9 fazem polling infinito

// Mapa de versões e features
const VERSION_FEATURES: Record<string, { polling: boolean; sse: boolean; description: string }> = {
  '1.0.0': { polling: true, sse: false, description: 'Original - polling 5min' },
  '1.0.2.0': { polling: true, sse: false, description: 'Versão intermediária - polling 5min' },
  '1.0.2.9': { polling: true, sse: false, description: 'Última antes do event-driven' },
  '1.0.3.0': { polling: false, sse: true, description: 'Event-driven com SSE' },
};

export async function GET(request: NextRequest) {
  const correlationId = getOrCreateCorrelationId(request);
  const ctx = startOperation('VERSION_CHECK', correlationId);

  try {
    const { searchParams } = request.nextUrl;
    const appVersion = searchParams.get('app_version');
    const installationId = normalizeUuid(searchParams.get('installation_id'));

    if (!appVersion) {
      return errorWithCorrelation(ctx, 400, 'MISSING_APP_VERSION', 'Missing app_version parameter', {
        required_params: ['app_version', 'installation_id'],
      });
    }

    if (!installationId) {
      return errorWithCorrelation(ctx, 400, 'INVALID_INSTALLATION_ID', 'Invalid or missing installation_id', {});
    }

    ctx.installationId = installationId;

    const versionCompare = compareVersions(appVersion, MIN_APP_VERSION);
    const isOutdated = versionCompare < 0;

    logSuccess(ctx, 'version check', {
      app_version: appVersion,
      min_version: MIN_APP_VERSION,
      is_outdated: isOutdated,
      comparison: versionCompare < 0 ? 'outdated' : versionCompare > 0 ? 'newer' : 'current',
    });

    if (isOutdated) {
      // App é muito antigo — FORÇAR ATUALIZAÇÃO
      return jsonWithCorrelation(
        ctx,
        {
          status: 'OUTDATED',
          min_version: MIN_APP_VERSION,
          current_version: appVersion,
          action: 'UPDATE_REQUIRED',
          reason: 'Your app version is too old and uses inefficient polling. Please update to enable event-driven communication.',
          download_url: 'https://www.voltris.com.br/download',
          force_update: true,
          block_polling: true,
        },
        410, // 410 Gone (HTTP standard para versão obsoleta)
        {
          'Cache-Control': 'no-cache, no-store, must-revalidate',
          'Upgrade-Required': MIN_APP_VERSION,
        }
      );
    }

    // App é compatível
    const features = VERSION_FEATURES[appVersion] || {
      polling: versionCompare > 0, // Assume features novas se version > min
      sse: true,
      description: 'Unknown version - assuming latest features',
    };

    return jsonWithCorrelation(
      ctx,
      {
        status: 'OK',
        current_version: appVersion,
        min_version: MIN_APP_VERSION,
        features: {
          polling_enabled: features.polling,
          sse_enabled: features.sse,
          description: features.description,
        },
        action: 'CONTINUE',
        force_update: false,
        block_polling: false,
      },
      200,
      {
        'Cache-Control': 'public, s-maxage=3600, stale-while-revalidate=86400',
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
 * Compara duas versões semantic versioning (major.minor.patch)
 * Retorna: -1 (v1 < v2), 0 (v1 == v2), 1 (v1 > v2)
 */
function compareVersions(v1: string, v2: string): number {
  const parts1 = v1.split('.').map(Number);
  const parts2 = v2.split('.').map(Number);

  for (let i = 0; i < Math.max(parts1.length, parts2.length); i++) {
    const p1 = parts1[i] || 0;
    const p2 = parts2[i] || 0;

    if (p1 < p2) return -1;
    if (p1 > p2) return 1;
  }

  return 0;
}
