/** @type {import('next').Config} */
const nextConfig = {
  poweredByHeader: false,
  experimental: {
    optimizeCss: true,
    optimizePackageImports: [
      'lucide-react',
      'react-icons',
      'framer-motion',
      '@heroicons/react',
    ],
  },
  eslint: {
    ignoreDuringBuilds: true, // TEMPORÁRIO: Corrigir erros ESLint gradualmente
  },
  typescript: {
    ignoreBuildErrors: true, // TEMPORÁRIO: Corrigir erros TypeScript gradualmente
  },
  serverExternalPackages: ['sharp'],
  images: {
    formats: ['image/avif', 'image/webp'],
    deviceSizes: [320, 480, 640, 750, 828, 1080, 1200, 1920, 2048, 3840],
    imageSizes: [16, 32, 48, 64, 96, 128, 256, 384],
    minimumCacheTTL: 2592000,
    // SEGURANÇA: desativado — nenhuma imagem SVG é servida via next/image
    // no projeto (SVGs são usados apenas como CSS background).
    dangerouslyAllowSVG: false,
    loader: 'default',
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'img.clerk.com',
      },
      {
        protocol: 'https',
        hostname: 'vjscxpxvdfhryixksrno.supabase.co',
      },
      {
        protocol: 'https',
        hostname: 'images.unsplash.com',
      },
      {
        protocol: 'https',
        hostname: 'www.gravatar.com',
      },
    ],
  },
  async redirects() {
    return [
      // ============================================================
      // REDIRECTS DE CONTEÚDO (apenas regras de conteúdo, não canonicalização)
      // Canonicalização (http→https, non-www→www) é feita pelo middleware.ts
      // ============================================================
      // Correcção de links quebrados (404) reportados por Screaming Frog - SEO Técnico
      { source: '/suporte-ao-windows', destination: '/todos-os-servicos/suporte-ao-windows', permanent: true },
      { source: '/todos-os-servicos/otimizacao-pc', destination: '/otimizacao-pc', permanent: true },

      { source: '/todos-os-servicos/criacao-de-sites/plano-profissional', destination: '/criar-site', permanent: true },
      { source: '/todos-os-servicos/criacao-de-sites/plano-empresarial', destination: '/criar-site', permanent: true },
      { source: '/todos-os-servicos/otimizacao-de-windows/otimizacao-para-jogos', destination: '/otimizacao-pc', permanent: true },
      { source: '/todos-os-servicos/otimizacao-de-windows/otimizacao-para-trabalho', destination: '/otimizacao-pc', permanent: true },
      { source: '/todos-os-servicos/manutencao-preventiva/check-up-completo', destination: '/tecnico-informatica', permanent: true },
      { source: '/servicos/otimizacao-pc', destination: '/otimizacao-pc', permanent: true },
      { source: '/servicos/tecnico-informatica', destination: '/tecnico-informatica', permanent: true },
      { source: '/servicos/criar-site', destination: '/criar-site', permanent: true },
      { source: '/vendas', destination: '/restricted-area-admin', permanent: true },
      { source: '/profile', destination: '/perfil', permanent: true },
      { source: '/admin/:path*', destination: '/restricted-area-admin/:path*', permanent: true },
      { source: '/todos-os-servicos/criacao-de-sites', destination: '/criar-site', permanent: true },
      { source: '/todos-os-servicos/criacao-de-sites/plano-basico', destination: '/criar-site', permanent: true },
      { source: '/exterior/exterior/orcamento', destination: '/tecnico-informatica', permanent: true },

      // Redirecionamentos de bloqueios foram removidos porque o 308 Redirect causava um loop infinito de Retry no C# HttpClient.

      // ============================================================
      // CONSOLIDAÇÃO DE GUIAS DUPLICADOS — SEO / AdSense Fix
      // Redireciona duplicatas para a URL canônica mais completa.
      // ============================================================

      // Grupo: SSD vs HDD (5 duplicatas → 1 canônico)
      { source: '/guias/ssd-vs-hd-qual-melhor', destination: '/guias/ssd-vs-hdd-guia', permanent: true },
      { source: '/guias/hds-vs-ssd-qual-a-diferenca', destination: '/guias/ssd-vs-hdd-guia', permanent: true },
      // FLATTENED: antes apontava para ssd-nvme-vs-sata-jogos (chain intermediário)
      { source: '/guias/nvme-vs-sata-vale-a-pena-upgrade', destination: '/guias/ssd-vs-hdd-guia', permanent: true },
      { source: '/guias/ssd-nvme-vs-sata-jogos', destination: '/guias/ssd-vs-hdd-guia', permanent: true },

      // Grupo: DNS para Jogos (3 duplicatas → 1 canônico)
      // FLATTENED: antes apontava para melhor-dns-jogos-2026 (chain intermediário)
      { source: '/guias/melhor-dns-para-jogos-google-vs-cloudflare', destination: '/guias/melhor-dns-jogos', permanent: true },
      { source: '/guias/dns-mais-rapido-para-jogos-benchmark', destination: '/guias/melhor-dns-jogos', permanent: true },

      // Grupo: Debloat Windows 11 (2 duplicatas → 1 canônico)
      { source: '/guias/debloating-windows-11', destination: '/guias/debloat-windows-11-otimizacao-powershell', permanent: true },

      // Grupo: Overwatch 2 (2 duplicatas → 1 canônico)
      { source: '/guias/overwatch-2-melhores-configuracoes-fps', destination: '/guias/overwatch-2-otimizacao-fps-input-lag-reduce-buffering', permanent: true },

      // Grupo: RDR2 (2 duplicatas → 1 canônico)
      { source: '/guias/red-dead-redemption-2-melhores-configuracoes', destination: '/guias/red-dead-redemption-2-melhores-configuracoes-rdr2', permanent: true },

      // Grupo: Euro Truck Simulator 2 (2 duplicatas → 1 canônico)
      { source: '/guias/euro-truck-simulator-2-otimizacao', destination: '/guias/euro-truck-simulator-2-otimizacao-aa-promods', permanent: true },

      // Grupo: Rocket League (2 duplicatas → 1 canônico)
      { source: '/guias/rocket-league-melhores-configuracoes-camera', destination: '/guias/rocket-league-camera-settings-bakkesmod-air-roll', permanent: true },

      // Grupo: Manutencao preventiva (2 duplicatas → 1 canônico)
      { source: '/guias/manutencao-preventiva', destination: '/guias/manutencao-preventiva-computador', permanent: true },

      // Grupo: Roblox FPS Unlocker (3 duplicatas → 1 canônico)
      { source: '/guias/roblox-fps-unlocker-guia', destination: '/guias/roblox-fps-unlocker-bloat-fix-bloxstrap', permanent: true },
      { source: '/guias/roblox-fps-unlocker-tutorial', destination: '/guias/roblox-fps-unlocker-bloat-fix-bloxstrap', permanent: true },

      // Grupo: Elden Ring (3 duplicatas → 1 canônico)
      { source: '/guias/eld-ring-stuttering-fix-dx12', destination: '/guias/elden-ring-fps-unlock-stutter-fix', permanent: true },
      { source: '/guias/elden-ring-fps-unlock-widescreen-fix-stutter', destination: '/guias/elden-ring-fps-unlock-stutter-fix', permanent: true },

      // Grupo: OBS Studio streaming (3 duplicatas → 1 canônico)
      { source: '/guias/obs-studio-melhores-configuracoes-stream-2026', destination: '/guias/obs-studio-melhores-configuracoes-stream', permanent: true },
      { source: '/guias/obs-studio-streaming-twitch-youtube-guia-completo', destination: '/guias/obs-studio-melhores-configuracoes-stream', permanent: true },

      // Grupo: Cadeiras (2 duplicatas → 1 canônico)
      { source: '/guias/cadeira-gamer-ergonomia-postura-aim', destination: '/guias/cadeira-gamer-vs-escritorio-ergonomia', permanent: true },

      // Grupo: Teclados mecânicos (2 duplicatas → 1 canônico)
      { source: '/guias/teclados-mecanicos-switches-guia', destination: '/guias/teclados-mecanicos-guia', permanent: true },

      // Grupo: Water cooler vs air cooler (2 duplicatas → 1 canônico)
      { source: '/guias/water-cooler-vs-air-cooler', destination: '/guias/water-cooler-vs-air-cooler-qual-escolher', permanent: true },

      // Grupo: Periféricos gamer (2 duplicatas → 1 canônico)
      { source: '/guias/perifericos-gamer-vale-a-pena', destination: '/guias/perifericos-gamer-vale-a-pena-marcas', permanent: true },

      // Grupo: Bluestacks (3 duplicatas → 1 canônico)
      { source: '/guias/bluestacks-ldplayer-otimizacao-free-fire-120fps', destination: '/guias/bluestacks-otimizacao-free-fire-pubg', permanent: true },
      { source: '/guias/bluestacks-vs-ldplayer-qual-mais-leve', destination: '/guias/bluestacks-otimizacao-free-fire-pubg', permanent: true },

      // Grupo: VPN para jogos (2 duplicatas → 1 canônico)
      { source: '/guias/vpn-vale-a-pena-jogos', destination: '/guias/vpn-jogos-exitlag-noping-vale-a-pena', permanent: true },

      // Grupo: HDR (2 duplicatas → 1 canônico)
      { source: '/guias/hdr-windows-vale-a-pena-jogos', destination: '/guias/hdr-windows-11-calibracao-jogos', permanent: true },

      // Grupo: Monitor sync (2 duplicatas → 1 canônico)
      { source: '/guias/sync-vertical-g-sync-free-sync-explicacao', destination: '/guias/g-sync-freesync-configuracao-correta', permanent: true },

      // Grupo: Reduzir ping (3 duplicatas → 1 canônico)
      { source: '/guias/reduzir-ping-jogos-online', destination: '/guias/reduzir-ping-regedit-cmd-jogos', permanent: true },
      { source: '/guias/reduzir-ping-exitlag-noping-dns', destination: '/guias/reduzir-ping-regedit-cmd-jogos', permanent: true },

      // Grupo: Backup (2 duplicatas → 1 canônico)
      { source: '/guias/backup-dados', destination: '/guias/backup-automatico-nuvem', permanent: true },

      // Grupo: The Witcher 3 (2 duplicatas → 1 canônico)
      { source: '/guias/the-witcher-3-next-gen-performance', destination: '/guias/the-witcher-3-next-gen-otimizacao-ray-tracing', permanent: true },

      // Grupo: Valorant VAN (2 duplicatas → 1 canônico)
      { source: '/guias/valorant-fix-van-9003-secure-boot', destination: '/guias/valorant-van-9003-secure-boot-tpm-fix', permanent: true },

      // Grupo: GTA IV (2 duplicatas → 1 canônico)
      { source: '/guias/gta-iv-complete-edition-lag-fix', destination: '/guias/gta-iv-fix-windows-10-11', permanent: true },

      // Grupo: Discord otimizar (2 duplicatas → 1 canônico)
      { source: '/guias/discord-otimizar-para-jogos', destination: '/guias/discord-otimizacao-overlay-lag', permanent: true },

      // Grupo: formatação windows (2 duplicatas → 1 canônico)
      { source: '/guias/formatacao-windows', destination: '/guias/formatacao-limpa-windows-11-rufus-gpt', permanent: true },
      { source: '/guias/gta-v-como-resolver-texturas-sumindo-ou-demorando-para-carregar', destination: '/guias/gta-v-fix-texturas-sumindo', permanent: true },

      // ============================================================
      // CONVERSÃO DE URLS DATADAS PARA EVERGREEN
      // ============================================================
      { source: '/guias/como-escolher-processador-2026', destination: '/guias/como-escolher-processador', permanent: true },
      { source: '/guias/melhor-dns-jogos-2026', destination: '/guias/melhor-dns-jogos', permanent: true },
      { source: '/guias/pc-gamer-barato-custo-beneficio-2026', destination: '/guias/pc-gamer-barato-custo-beneficio', permanent: true },
      { source: '/guias/rtx-4060-vale-a-pena-2026', destination: '/guias/rtx-4060-vale-a-pena', permanent: true },

      { source: '/como-aumentar-fps-cs2-2026', destination: '/como-aumentar-fps-cs2', permanent: true },
      { source: '/como-aumentar-fps-valorant-2026', destination: '/como-aumentar-fps-valorant', permanent: true },
      { source: '/como-aumentar-fps-warzone-2026', destination: '/como-aumentar-fps-warzone', permanent: true },
      { source: '/guia-definitivo-privacidade-windows-2026', destination: '/guia-definitivo-privacidade-windows', permanent: true },
      { source: '/otimizar-windows-11-para-warzone-2026', destination: '/otimizar-windows-11-para-warzone', permanent: true },
      { source: '/otimizar-windows-para-fortnite-2026', destination: '/otimizar-windows-para-fortnite', permanent: true },
      { source: '/otimizar-windows-para-league-of-legends-2026', destination: '/otimizar-windows-para-league-of-legends', permanent: true },
    ];
  },
  async rewrites() {
    return {
      beforeFiles: [
        // ============================================================
        // STATIC MOCK ROUTES FOR DESKTOP APP
        // Ao invés de redirecionar (o que causa HTTP Exception no C# e force Retry Infinito),
        // nós devolvemos um JSON estático (Edge Cache, custo zero de CPU) com 200 OK.
        // O App em C# entende que deu certo e volta a dormir pelo intervalo programado.
        // O beforeFiles garante que o rewrite atue antes do route.ts original ser chamado,
        // economizando milhares de execuções de Vercel Functions por segundo.
        // ============================================================
        { source: '/api/telemetry/:path*', destination: '/api-mock.json' },
        { source: '/api/v1/telemetry/:path*', destination: '/api-mock.json' },
        { source: '/api/v1/sessions/heartbeat', destination: '/api-mock.json' },
        { source: '/api/v1/sessions/start', destination: '/api-mock.json' },
        { source: '/api/admin/sessions/live', destination: '/api-mock.json' },
        { source: '/api/admin/telemetry/:path*', destination: '/api-mock.json' },
        { source: '/api/v1/license/sync', destination: '/api-mock.json' },
        // REMOVIDO: /api/v1/commands/pending — agora é real (comandos remotos do dashboard)
        // REMOVIDO: /api/v1/install/status — agora é real (vinculação de conta)
      ]
    };
  },
  headers: async () => [
    {
      source: '/(.*)',
      headers: [
        { key: 'Server', value: 'Voltris Web Network' },
        { key: 'X-DNS-Prefetch-Control', value: 'on' },
        { key: 'X-Content-Type-Options', value: 'nosniff' },
        { key: 'X-Frame-Options', value: 'DENY' },
        { key: 'X-XSS-Protection', value: '1; mode=block' },
        { key: 'Referrer-Policy', value: 'strict-origin-when-cross-origin' },
        { key: 'Permissions-Policy', value: 'camera=(), microphone=(), geolocation=()' },
        {
          key: 'Content-Security-Policy',
          value: [
            "default-src 'self'",
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://www.googletagmanager.com https://www.google-analytics.com https://js.stripe.com https://pagead2.googlesyndication.com https://adservice.google.com.br https://adservice.google.com https://static.cloudflareinsights.com https://*.adtrafficquality.google",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
            "font-src 'self' https://fonts.gstatic.com",
            "img-src 'self' data: blob: https: http:",
            "connect-src 'self' https://*.supabase.co wss://*.supabase.co https://www.google-analytics.com https://api.stripe.com https://checkout.stripe.com https://pagead2.googlesyndication.com https://*.adtrafficquality.google https://static.cloudflareinsights.com https://accounts.google.com",
            "frame-src 'self' https://js.stripe.com https://hooks.stripe.com https://td.doubleclick.net https://googleads.g.doubleclick.net https://*.adtrafficquality.google https://www.google.com https://accounts.google.com",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self' https://*.supabase.co https://accounts.google.com",
          ].join('; '),
        },
      ],
    },
    // Não aplicar CSP em rotas de API (pode quebrar webhooks)
    {
      source: '/api/(.*)',
      headers: [
        { key: 'X-Content-Type-Options', value: 'nosniff' },
        { key: 'X-Frame-Options', value: 'DENY' },
      ],
    },
    // Cache agressivo para imagens estáticas
    {
      source: '/logo.png',
      headers: [
        {
          key: 'Cache-Control',
          value: 'public, max-age=31536000, immutable',
        },
      ],
    },
    {
      source: '/(.*).(png|jpg|jpeg|webp|svg|ico)',
      headers: [
        {
          key: 'Cache-Control',
          value: 'public, max-age=31536000, immutable',
        },
      ],
    },
  ],
};

export default nextConfig;
