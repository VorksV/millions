import { createServerClient, type CookieOptions } from '@supabase/ssr'
import { NextResponse, type NextRequest } from 'next/server'
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

export async function middleware(request: NextRequest) {
    const pathname = request.nextUrl.pathname.toLowerCase();
    
    // ============================================================
    // 1. TRATAMENTO DE IDIOMAS INVÁLIDOS (SEO REDIRECTS 301)
    // Redireciona subpastas de idiomas inválidos (/fr, /de, /ja, etc.)
    // para a raiz ou equivalente limpo, retendo autoridade SEO.
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
    // Decodifica caracteres especiais (%C3%A1, acentos) e verifica
    // em O(1) se o guia ou categoria existe. Se não existir, 
    // redireciona (301) para a central de guias (/guias).
    // ============================================================
    let decodedPath = pathname;
    try {
        decodedPath = decodeURIComponent(pathname);
    } catch (e) {
        // Fallback se houver algum caracter mal formado
    }

    // Normalizar: remover barra final
    let normalizedPath = decodedPath;
    if (normalizedPath.endsWith('/') && normalizedPath.length > 1) {
        normalizedPath = normalizedPath.slice(0, -1);
    }

    if (normalizedPath.startsWith('/guias/')) {
        const slug = normalizedPath.substring(7); // extrai o slug
        
        if (slug && slug !== '') {
            // URLs com caracteres especiais são lixo/legado — retornar 410 GONE
            // Isso diz ao Google: "essa página nunca existiu e nunca vai existir"
            // Elimina centenas de erros "Página com redirecionamento" no GSC
            const hasSpecialChars = /[:()\[\]áàãâéèêíìîóòõôúùûçñ,!?@#$%&=+]/.test(slug);
            if (hasSpecialChars) {
                return new NextResponse(null, { status: 410 });
            }

            const isValid = VALID_CATEGORIES.has(slug) || VALID_GUIDE_SLUGS.has(slug);
            
            if (!isValid) {
                const url = request.nextUrl.clone();
                url.pathname = '/guias';
                url.search = ''; // Limpar params de busca lixo
                return NextResponse.redirect(url, 301);
            }
        }
    }

    // ============================================================
    // 3. BLOQUEIO DE PÁGINAS DELETADAS PERMANENTEMENTE (HTTP 410 GONE)
    // Indica explicitamente ao Googlebot que as páginas sob /blog
    // e outros caminhos legados foram removidas propositalmente e 
    // nunca mais voltarão, removendo-as do índice imediatamente.
    // ============================================================
    const GONE_URL_PATTERNS = [
        'indexnow-test',
        'performance-test',
        '/teste-pagamento',
    ];

    // Bloquear URLs de caminhos legados específicos que não existem mais
    const GONE_PATHS = ['/optimizer', '/gamers', '/about'];

    const isBlogGone = pathname === '/blog' || pathname.startsWith('/blog/');
    const isGoneUrl = isBlogGone || 
                      GONE_URL_PATTERNS.some(pattern => pathname.includes(pattern)) ||
                      GONE_PATHS.some(path => pathname === path);

    if (isGoneUrl) {
        return new NextResponse(null, { status: 410 });
    }

    // ============================================================
    // CANONICALIZAÇÃO GLOBAL ÚNICA - ENTERPRISE-GRADE
    // Responsabilidade única: middleware.ts (next.config.js não faz canonicalização)
    //
    // Regras:
    // 1. http:// → https:// (exceto localhost)
    // 2. voltris.com.br → www.voltris.com.br
    // 3. Apenas 1 hop até URL final (sem chains)
    // 4. Status 301 permanente para SEO
    // 5. Preserva porta customizada (usando url.host ao invés de url.hostname)
    // ============================================================

    const protocol = request.nextUrl.protocol;
    const hostname = request.nextUrl.hostname;

    // Verifica se é localhost - não forçar HTTPS (desenvolvimento)
    const isLocalhost = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '::1';

    // Verifica se precisa de redirect (única verificação)
    const needsProtocolRedirect = protocol === 'http:' && !isLocalhost;
    const needsHostnameRedirect = hostname === 'voltris.com.br';

    if (needsProtocolRedirect || needsHostnameRedirect) {
        const url = request.nextUrl.clone();

        // Força HTTPS (apenas se não for localhost)
        if (!isLocalhost) {
            url.protocol = 'https:';
        }

        // Força WWW (usando host para preservar porta customizada)
        if (hostname === 'voltris.com.br') {
            url.host = 'www.voltris.com.br';
        }

        // Status 301 permanente para SEO - apenas 1 hop
        return NextResponse.redirect(url, 301);
    }

    let response = NextResponse.next()

    // Aplicar headers de segurança em todas as respostas
    Object.entries(SECURITY_HEADERS).forEach(([key, value]) => {
        response.headers.set(key, value)
    })

    // Ocultar informações de infraestrutura
    response.headers.delete('x-powered-by')
    response.headers.delete('x-vercel-id')
    response.headers.delete('x-vercel-cache')

    const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL
    const supabaseAnonKey = process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY

    // CORREÇÃO: Early return se não tiver credenciais Supabase
    if (!supabaseUrl || !supabaseAnonKey) {
        return response
    }

    const protectedRoutes = ['/dashboard', '/restricted-area-admin']
    const isProtectedRoute = protectedRoutes.some(route =>
        request.nextUrl.pathname.startsWith(route)
    )

    // CORREÇÃO: Early return se não for rota protegida e não tiver cookies de sessão
    // Evita criação desnecessária de Supabase client para usuários anônimos em rotas públicas
    const hasAuthCookie = request.cookies.getAll().some(c => c.name.includes('supabase') || c.name.includes('sb-'));

    if (!isProtectedRoute && !hasAuthCookie) {
        return response
    }

    // Apenas cria Supabase client se necessário (rota protegida ou tem cookie de sessão)
    const supabase = createServerClient(
        supabaseUrl,
        supabaseAnonKey,
        {
            cookies: {
                get: (name: string) => request.cookies.get(name)?.value,
                set: (name: string, value: string, options: CookieOptions) => {
                    request.cookies.set({ name, value, ...options })
                    response = NextResponse.next({ request })
                    response.cookies.set({ name, value, ...options })
                    // Reaplicar headers após criar nova response
                    Object.entries(SECURITY_HEADERS).forEach(([k, v]) => {
                        response.headers.set(k, v)
                    })
                },
                remove: (name: string, options: CookieOptions) => {
                    request.cookies.set({ name, value: '', ...options })
                    response = NextResponse.next({ request })
                    response.cookies.set({ name, value: '', ...options })
                    Object.entries(SECURITY_HEADERS).forEach(([k, v]) => {
                        response.headers.set(k, v)
                    })
                },
            },
        }
    )

    // SEGURO: getUser() valida o token no servidor, getSession() não valida
    const { data: { user } } = await supabase.auth.getUser()

    if (isProtectedRoute) {
        if (!user) {
            const loginUrl = new URL('/login', request.url)
            loginUrl.searchParams.set('next', request.nextUrl.pathname)
            return NextResponse.redirect(loginUrl)
        }

        // SEGURANÇA EXTRA: Bloquear acesso admin no middleware se não for admin
        if (request.nextUrl.pathname.startsWith('/restricted-area-admin')) {
            const { data: profile } = await supabase
                .from('profiles')
                .select('is_admin')
                .eq('id', user.id)
                .single()

            if (!profile?.is_admin) {
                return NextResponse.redirect(new URL('/dashboard', request.url))
            }
        }
    }

    return response
}

export const config = {
    // `api` foi excluido do escopo de proposito. O app desktop faz polling
    // constante em /api/v1/* e o middleware roda ANTES das rewrites, entao cada
    // uma dessas requisicoes consumia uma invocacao de Edge Function mesmo quando
    // a resposta final vinha do arquivo estatico /api-mock.json.
    //
    // O middleware nunca protegeu nada em /api: `protectedRoutes` (linha acima)
    // cobre apenas /dashboard e /restricted-area-admin. As rotas de API que
    // exigem sessao validam o usuario dentro do proprio handler
    // (/api/v1/install/link, /api/v1/commands/create, requireAdmin, etc).
    // Os headers de seguranca de /api continuam vindos do next.config.js.
    matcher: [
        '/((?!api|_next/static|_next/image|favicon.ico|assets|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico|css|js|woff2?|ttf)$).*)',
    ],
}
