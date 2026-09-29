// ═══════════════════════════════════════════════════════════════
// ZERO imports de next/server ou qualquer módulo CJS do Node.js.
// Usa APENAS Web API nativas (Response, Request, URL, Headers)
// que funcionam identicamente em Edge Runtime, Node.js ESM e CJS.
// Headers de segurança estão definidos no next.config.mjs headers().
// ═══════════════════════════════════════════════════════════════
import { VALID_CATEGORIES, VALID_GUIDE_SLUGS } from './lib/valid-guide-slugs'

export function middleware(request: Request) {
    const url = new URL(request.url)
    const pathname = url.pathname.toLowerCase()

    // ============================================================
    // 1. TRATAMENTO DE IDIOMAS INVÁLIDOS (SEO REDIRECTS 301)
    // ============================================================
    const invalidLanguages = ['/fr', '/de', '/ja', '/es', '/pt-br']
    const matchedLang = invalidLanguages.find(lang =>
        pathname === lang || pathname.startsWith(lang + '/')
    )
    if (matchedLang) {
        let cleanPath = url.pathname.substring(matchedLang.length)
        if (!cleanPath || cleanPath === '/') cleanPath = '/'
        const target = new URL(request.url)
        target.pathname = cleanPath
        return Response.redirect(target, 301)
    }

    // ============================================================
    // 2. TRATAMENTO DE ROTAS SOB /guias/* (DYNAMIC 404 REMEDIATION)
    // ============================================================
    let decodedPath = pathname
    try { decodedPath = decodeURIComponent(pathname) } catch { /* fallback */ }

    let normalizedPath = decodedPath
    if (normalizedPath.endsWith('/') && normalizedPath.length > 1) {
        normalizedPath = normalizedPath.slice(0, -1)
    }

    if (normalizedPath.startsWith('/guias/')) {
        const slug = normalizedPath.substring(7)
        if (slug && slug !== '') {
            // URLs com caracteres especiais são lixo/legado → 410 GONE
            const hasSpecialChars = /[:()[\]áàãâéèêíìîóòõôúùûçñ,!?@#$%&=+]/.test(slug)
            if (hasSpecialChars) return new Response(null, { status: 410 })

            const isValid = VALID_CATEGORIES.has(slug) || VALID_GUIDE_SLUGS.has(slug)
            if (!isValid) {
                const target = new URL(request.url)
                target.pathname = '/guias'
                target.search = ''
                return Response.redirect(target, 301)
            }
        }
    }

    // ============================================================
    // 3. BLOQUEIO DE PÁGINAS DELETADAS PERMANENTEMENTE (HTTP 410)
    // ============================================================
    const GONE_URL_PATTERNS = ['indexnow-test', 'performance-test', '/teste-pagamento']
    const GONE_PATHS = ['/optimizer', '/gamers', '/about']

    const isBlogGone = pathname === '/blog' || pathname.startsWith('/blog/')
    const isGoneUrl =
        isBlogGone ||
        GONE_URL_PATTERNS.some(p => pathname.includes(p)) ||
        GONE_PATHS.some(p => pathname === p)

    if (isGoneUrl) return new Response(null, { status: 410 })

    // ============================================================
    // 4. CANONICALIZAÇÃO GLOBAL — http→https, non-www→www
    // ============================================================
    const protocol = url.protocol
    const hostname = url.hostname
    const isLocalhost = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1'

    if ((protocol === 'http:' && !isLocalhost) || hostname === 'voltris.com.br') {
        const target = new URL(request.url)
        if (!isLocalhost) target.protocol = 'https:'
        if (hostname === 'voltris.com.br') target.hostname = 'www.voltris.com.br'
        return Response.redirect(target, 301)
    }

    // ============================================================
    // 5. PROTEÇÃO DE ROTAS — verifica cookie de sessão Supabase
    // Validação real do JWT fica nos Server Components/Route Handlers.
    // ============================================================
    const protectedRoutes = ['/dashboard', '/restricted-area-admin']
    if (protectedRoutes.some(r => url.pathname.startsWith(r))) {
        const cookieHeader = request.headers.get('cookie') || ''
        const hasSession = cookieHeader.split(';').some(c => {
            const name = c.trim().split('=')[0] ?? ''
            return name.startsWith('sb-') && name.endsWith('-auth-token')
        })
        if (!hasSession) {
            const loginUrl = new URL('/login', request.url)
            loginUrl.searchParams.set('next', url.pathname)
            return Response.redirect(loginUrl, 307)
        }
    }

    // ============================================================
    // 6. PASS-THROUGH — continua para o handler original
    // Headers de segurança são injetados via next.config.mjs headers()
    // ============================================================
    return new Response(null, {
        headers: { 'x-middleware-next': '1' },
    })
}

export const config = {
    matcher: [
        '/((?!api|_next/static|_next/image|favicon.ico|assets|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|css|js|woff2?|ttf)$).*)',
    ],
}
