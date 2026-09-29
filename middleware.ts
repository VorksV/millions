import { NextResponse, type NextRequest } from 'next/server'
import { VALID_CATEGORIES, VALID_GUIDE_SLUGS } from './lib/valid-guide-slugs'

export function middleware(request: NextRequest) {
    const url = request.nextUrl
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
        const target = url.clone()
        target.pathname = cleanPath
        return NextResponse.redirect(target, 301)
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
            if (hasSpecialChars) return new NextResponse(null, { status: 410 })

            const isValid = VALID_CATEGORIES.has(slug) || VALID_GUIDE_SLUGS.has(slug)
            if (!isValid) {
                const target = url.clone()
                target.pathname = '/guias'
                target.search = ''
                return NextResponse.redirect(target, 301)
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

    if (isGoneUrl) return new NextResponse(null, { status: 410 })

    // ============================================================
    // 4. CANONICALIZAÇÃO GLOBAL — http→https, non-www→www
    // ============================================================
    const protocol = url.protocol
    const hostname = url.hostname
    const isLocalhost = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1'

    if ((protocol === 'http:' && !isLocalhost) || hostname === 'voltris.com.br') {
        const target = url.clone()
        if (!isLocalhost) target.protocol = 'https:'
        if (hostname === 'voltris.com.br') target.hostname = 'www.voltris.com.br'
        return NextResponse.redirect(target, 301)
    }

    // ============================================================
    // 5. PROTEÇÃO DE ROTAS — verifica cookie de sessão Supabase
    // Validação real do JWT fica nos Server Components/Route Handlers.
    // ============================================================
    const protectedRoutes = ['/dashboard', '/restricted-area-admin']
    if (protectedRoutes.some(r => url.pathname.startsWith(r))) {
        const hasSession = request.cookies.getAll().some(c =>
            c.name.startsWith('sb-') && c.name.endsWith('-auth-token')
        )
        if (!hasSession) {
            const loginUrl = new URL('/login', request.url)
            loginUrl.searchParams.set('next', url.pathname)
            return NextResponse.redirect(loginUrl, 307)
        }
    }

    // ============================================================
    // 6. PASS-THROUGH — continua para o handler original
    // ============================================================
    return NextResponse.next()
}

export const config = {
    matcher: [
        '/((?!api|_next/static|_next/image|favicon.ico|assets|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|css|js|woff2?|ttf)$).*)',
    ],
}
