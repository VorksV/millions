import { NextResponse } from 'next/dist/server/web/spec-extension/response.js'
import { NextRequest } from 'next/dist/server/web/spec-extension/request.js'
import { VALID_CATEGORIES, VALID_GUIDE_SLUGS } from './lib/valid-guide-slugs'

const SECURITY_HEADERS = {
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
    'X-XSS-Protection': '1; mode=block',
    'Referrer-Policy': 'strict-origin-when-cross-origin',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), interest-cohort=()',
    'X-DNS-Prefetch-Control': 'on',
    'Server': 'Voltris Web Network',
}

export function middleware(request: NextRequest) {
    const pathname = request.nextUrl.pathname.toLowerCase();

    // ============================================================
    // 1. TRATAMENTO DE IDIOMAS INVÁLIDOS (SEO REDIRECTS 301)
    // ============================================================
    const invalidLanguages = ['/fr', '/de', '/ja', '/es', '/pt-br'];
    const matchedLang = invalidLanguages.find(lang =>
        pathname === lang || pathname.startsWith(lang + '/')
    );

    if (matchedLang) {
        let cleanPath = request.nextUrl.pathname.substring(matchedLang.length);
        if (!cleanPath || cleanPath === '/') {
            cleanPath = '/';
        }
        const url = request.nextUrl.clone();
        url.pathname = cleanPath;
        return NextResponse.redirect(url, 301);
    }

    // ============================================================
    // 2. TRATAMENTO DE ROTAS SOB /guias/* (DYNAMIC 404 REMEDIATION)
    // ============================================================
    let decodedPath = pathname;
    try {
        decodedPath = decodeURIComponent(pathname);
    } catch {
        // Fallback se houver algum caracter mal formado
    }

    // Normalizar: remover barra final
    let normalizedPath = decodedPath;
    if (normalizedPath.endsWith('/') && normalizedPath.length > 1) {
        normalizedPath = normalizedPath.slice(0, -1);
    }

    if (normalizedPath.startsWith('/guias/')) {
        const slug = normalizedPath.substring(7);

        if (slug && slug !== '') {
            // URLs com caracteres especiais são lixo/legado — retornar 410 GONE
            const hasSpecialChars = /[:()[\]áàãâéèêíìîóòõôúùûçñ,!?@#$%&=+]/.test(slug);
            if (hasSpecialChars) {
                return new NextResponse(null, { status: 410 });
            }

            const isValid = VALID_CATEGORIES.has(slug) || VALID_GUIDE_SLUGS.has(slug);

            if (!isValid) {
                const url = request.nextUrl.clone();
                url.pathname = '/guias';
                url.search = '';
                return NextResponse.redirect(url, 301);
            }
        }
    }

    // ============================================================
    // 3. BLOQUEIO DE PÁGINAS DELETADAS PERMANENTEMENTE (HTTP 410 GONE)
    // ============================================================
    const GONE_URL_PATTERNS = [
        'indexnow-test',
        'performance-test',
        '/teste-pagamento',
    ];
    const GONE_PATHS = ['/optimizer', '/gamers', '/about'];

    const isBlogGone = pathname === '/blog' || pathname.startsWith('/blog/');
    const isGoneUrl = isBlogGone ||
        GONE_URL_PATTERNS.some(pattern => pathname.includes(pattern)) ||
        GONE_PATHS.some(path => pathname === path);

    if (isGoneUrl) {
        return new NextResponse(null, { status: 410 });
    }

    // ============================================================
    // 4. CANONICALIZAÇÃO GLOBAL — http→https, non-www→www
    // ============================================================
    const protocol = request.nextUrl.protocol;
    const hostname = request.nextUrl.hostname;

    const isLocalhost = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1';
    const needsProtocolRedirect = protocol === 'http:' && !isLocalhost;
    const needsHostnameRedirect = hostname === 'voltris.com.br';

    if (needsProtocolRedirect || needsHostnameRedirect) {
        const url = request.nextUrl.clone();
        if (!isLocalhost) {
            url.protocol = 'https:';
        }
        if (hostname === 'voltris.com.br') {
            url.host = 'www.voltris.com.br';
        }
        return NextResponse.redirect(url, 301);
    }

    // ============================================================
    // 5. PROTEÇÃO DE ROTAS — verifica cookie de sessão Supabase
    // Nota: a validação real do token JWT acontece nos Server Components
    // e Route Handlers (Node.js runtime). Aqui fazemos apenas o redirect
    // para login quando não há cookie de sessão, de forma segura no Edge.
    // ============================================================
    const protectedRoutes = ['/dashboard', '/restricted-area-admin'];
    const isProtectedRoute = protectedRoutes.some(route =>
        request.nextUrl.pathname.startsWith(route)
    );

    if (isProtectedRoute) {
        const hasSessionCookie = request.cookies.getAll().some(
            c => c.name.startsWith('sb-') && c.name.includes('-auth-token')
        );

        if (!hasSessionCookie) {
            const loginUrl = new URL('/login', request.url);
            loginUrl.searchParams.set('next', request.nextUrl.pathname);
            return NextResponse.redirect(loginUrl);
        }
    }

    // ============================================================
    // 6. APLICAR HEADERS DE SEGURANÇA
    // ============================================================
    const response = NextResponse.next();
    Object.entries(SECURITY_HEADERS).forEach(([key, value]) => {
        response.headers.set(key, value);
    });
    response.headers.delete('x-powered-by');

    return response;
}

export const config = {
    runtime: 'nodejs',
    // nada em /api: protectedRoutes cobre apenas /dashboard e
    // /restricted-area-admin. As rotas de API validam o usuário dentro
    // do próprio handler. Os headers de segurança de /api vêm do next.config.mjs.
    matcher: [
        '/((?!api|_next/static|_next/image|favicon.ico|assets|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|css|js|woff2?|ttf)$).*)',
    ],
}
