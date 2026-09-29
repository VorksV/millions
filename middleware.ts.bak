import { NextResponse, type NextRequest } from 'next/server'

// ============================================================
// DADOS INLINE — Evitar import externo no Edge Runtime do Vercel
// (imports de arquivos locais no middleware causam falha em produção)
// ============================================================
const VALID_CATEGORIES = new Set<string>([
    'inteligencia-artificial',
    'otimizacao',
    'games-fix',
    'windows-erros',
    'hardware',
    'perifericos',
    'software',
    'rede-seguranca',
    'windows-geral',
    'emulacao',
    'linux'
])

const VALID_GUIDE_SLUGS = new Set<string>([
    'abrir-portas-roteador-nat-aberto',
    'aceleracao-hardware-gpu-agendamento',
    'age-of-empires-6-pc-configuracoes',
    'ajustar-configuracoes-audio-windows',
    'amd-adrenalin-configuracao-competitiva',
    'antivirus-para-jogos-windows-defender-exclusao',
    'apex-legends-config-autoexec-fps',
    'api-ms-win-crt-runtime-missing',
    'assetto-corsa-content-manager-csp-sol',
    'atalhos-navegador-produtividade',
    'atalhos-produtividade-windows',
    'atualizacao-drivers-video',
    'atualizar-bios-seguro',
    'aumentar-volume-microfone-windows',
    'autenticacao-dois-fatores',
    'automacao-tarefas',
    'backup-automatico-nuvem',
    'backup-dados',
    'baldurs-gate-3-otimizacao-ato-3-fps',
    'beamng-drive-otimizacao-cpu-traffic',
    'bios-otimizacao-xmp-tpm',
    'bitlocker-desempenho-jogos-ssd',
    'bloquear-internet-firewall-windows',
    'bluestacks-ldplayer-otimizacao-free-fire-120fps',
    'bluestacks-otimizacao-free-fire-pubg',
    'bluestacks-vs-ldplayer-qual-mais-leve',
    'bufferbloat-qos-sqm-roteador-ping',
    'cable-management-organizacao-cabos-pc',
    'cadeira-gamer-ergonomia-postura-aim',
    'cadeira-gamer-vs-escritorio-ergonomia',
    'calibrar-bateria-notebook',
    'calibrar-cores-monitor',
    'cemu-emulador-wii-u-zelda-botw-4k-60fps',
    'cheat-engine-speedhack-jogos-offline',
    'cities-skylines-2-otimizacao-fps-simulation',
    'citra-lime3ds-emulador-3ds-pokemon-hd',
    'clash-royale-clash-of-clans-pc-oficial',
    'cod-warzone-melhores-configuracoes-graficas',
    'como-analisar-tela-azul-bsod-dmp-guia',
    'como-escolher-fonte-pc-gamer',
    'como-escolher-placa-de-video',
    'como-escolher-processador',
    'como-gravar-tela-pc',
    'como-limpar-cache-dns-ip-flushdns',
    'como-programar-com-ia-vibe-coding',
    'como-resolver-tela-azul',
    'como-trocar-pasta-termica-cpu-gpu-guia',
    'como-usar-ddu-driver-uninstaller',
    'como-usar-obs-studio-gravar-tela',
    'compartilhamento-impressoras',
    'conectar-impressora-sem-fio-windows',
    'configuracao-roteador-wifi',
    'controle-ps4-ps5-overclock-ds4windows',
    'corrigir-dll-faltando-vcredist-directx',
    'corrigir-problemas-som-pc',
    'criar-pendrive-bootavel',
    'criar-ponto-restauracao-windows',
    'criptografia-dados',
    'cs2-melhores-comandos-console-fps',
    'cs2-otimizacao-fps-competitivo',
    'cyberpunk-2077-hdd-mode-otimizacao',
    'cyberpunk-2077-otimizacao-fps-raytracing',
    'cyberpunk-2077-phantom-liberty-pc-otimizacao',
    'ddu-limpeza-drivers-video-guia',
    'debloat-windows-11-otimizacao-powershell',
    'debloating-windows-11',
    'diagnostico-hardware',
    'discord-nitro-qualidade-voz-krisp',
    'discord-otimizacao-gamer',
    'discord-otimizacao-overlay-lag',
    'discord-otimizar-para-jogos',
    'dns-mais-rapido-para-jogos-benchmark',
    'dolphin-emulador-wii-gamecube-ubershaders-guia',
    'dota-2-melhores-configuracoes-fps',
    'dpc-watchdog-violation-como-resolver',
    'dual-monitor-lag-fix-hz-diferentes',
    'duckstation-emulador-ps1-graficos-melhorados-pgxp',
    'dx11-feature-level-10.0-error-valorant',
    'ea-sports-fc-2026-pc-configuracoes-fps',
    'eld-ring-stuttering-fix-dx12',
    'elden-ring-fps-unlock-stutter-fix',
    'elden-ring-fps-unlock-widescreen-fix-stutter',
    'elder-scrolls-6-pc-requisitos-configuracoes',
    'epic-games-launcher-lento-cpu-fix',
    'equalizer-apo-peace-aumentar-passos-fps',
    'erro-0xc00007b-aplicativo-nao-inicializou',
    'erro-disco-100-porcento-gerenciador-tarefas',
    'escape-from-tarkov-otimizacao-fps-ram',
    'euro-truck-simulator-2-otimizacao',
    'euro-truck-simulator-2-otimizacao-aa-promods',
    'excluir-conta-instagram-definitivamente',
    'exitlag-vale-a-pena-ou-enganacao',
    'extensoes-produtividade-chrome',
    'fans-fluxo-de-ar-pc-gamer',
    'firewall-configuracao',
    'formatacao-limpa-windows-11-rufus-gpt',
    'formatacao-windows',
    'formatfactory-vs-handbrake-converter-video',
    'fortnite-modo-performance-pc-fraco',
    'fortnite-texturas-nao-carregam-streaming',
    'forza-horizon-5-vram-fix-input-lag',
    'forza-motorsport-2026-pc-configuracoes',
    'free-fire-pc-fraco-smartgaga',
    'g-sync-freesync-configuracao-correta',
    'gabinete-gamer-airflow-importancia',
    'genshin-impact-fps-unlocker-pc',
    'genshin-impact-stuttering-fix-pc',
    'geometry-dash-4gb-patch-lag',
    'gestao-pacotes',
    'gestao-servicos',
    'god-mode-windows-11-ativar',
    'god-of-war-pc-memory-leak-fix',
    'google-chrome-consumo-ram-fix',
    'google-play-games-pc-beta-vale-a-pena',
    'gravacao-tela-windows-nativa-dicas',
    'gta-6-pc-configuracoes-requisitos',
    'gta-iv-complete-edition-lag-fix',
    'gta-iv-fix-windows-10-11',
    'gta-san-andreas-correcao-grafica',
    'gta-v-err-gfx-d3d-init-crash',
    'gta-v-fix-texturas-sumindo',
    'gta-v-otimizar-fps-pc-fraco',
    'guia-compra-monitores',
    'guia-montagem-pc',
    'hard-reset-celular-formatar',
    'hard-reset-fones-bluetooth-fix',
    'hdmi-2.1-vs-displayport-1.4-diferencas',
    'hdr-windows-11-calibracao-jogos',
    'hdr-windows-vale-a-pena-jogos',
    'hds-vs-ssd-qual-a-diferenca',
    'headset-7.1-real-vs-virtual-vale-a-pena',
    'hibernacao-vs-suspensao-qual-o-melhor',
    'hogwarts-legacy-stutter-fix-ram',
    'hollow-knight-stuttering-fix-mod',
    'hyper-v-desempenho-jogos',
    'identificacao-phishing',
    'importancia-pasta-termica-pc',
    'instalacao-drivers',
    'instalacao-limpa-drivers-nvidia-amd',
    'instalacao-windows-11',
    'instalar-apps-android-windows-11',
    'instalar-impressora-wifi',
    'is-valorant-dying-state-of-game',
    'jogos-android-no-pc-melhores-emuladores',
    'ldplayer-emulador-leve-pc-fraco',
    'league-of-legends-fps-drop-fix',
    'league-of-legends-tela-preta-carregamento',
    'lethal-company-fps-boost-mods',
    'limitar-fps-rivatuner-nvidia',
    'limpar-cache-navegador-chrome-edge',
    'limpar-memoria-ram-windows',
    'limpeza-computador',
    'limpeza-disco-profunda-arquivos-temporarios',
    'limpeza-fisica-pc-gamer',
    'limpeza-navegadores',
    'limpeza-perifericos-mousepad-teclado',
    'lineage-2-otimizar-pvp-fps',
    'linux-gaming-bazzite-nobara-steam-deck-pc',
    'lossless-scaling-frame-generation-fsr-guia',
    'manutencao-preventiva',
    'manutencao-preventiva-computador',
    'melhor-dns-jogos',
    'melhor-dns-para-jogos-google-vs-cloudflare',
    'melhores-drivers-nvidia-antigos',
    'melhores-jogos-2026-configuracoes-pc',
    'melhores-navegadores-custo-beneficio',
    'memoria-virtual-pagefile-ssd-otimizacao',
    'micro-stuttering-em-jogos-causas',
    'microsoft-flight-simulator-otimizacao-cache-lod',
    'minecraft-alocar-mais-ram',
    'minecraft-aumentar-fps-fabric-sodium',
    'minecraft-lag-fix-optifine-fabric',
    'minecraft-lento-como-ganhar-fps',
    'minecraft-optifine-vs-sodium-fabric',
    'minecraft-shaders-iris-sodium-otimizacao-fps',
    'modo-de-jogo-windows-atikvar-ou-nao',
    'monitor-240hz-360hz-vale-a-pena-ghosting',
    'monitor-hz-configuracao-correta',
    'monitor-ips-vs-va-vs-tn-jogos',
    'monitor-ultrawide-jogos-competitivos',
    'monitoramento-sistema',
    'monitorar-temperatura-pc',
    'montagem-pc-gamer-erros-comuns',
    'mouse-acceleration-raw-accel-guia',
    'mouse-clique-duplo-falhando-fix',
    'mouse-dpi-polling-rate-ideal',
    'mouse-otimizacao-windows-precisao',
    'mousepad-speed-vs-control',
    'msi-afterburner-overclock-undervolt-guia',
    'mu-online-reduzir-lag-muvoltris',
    'notebook-gamer-bateria-otimizacao',
    'notion-vs-obsidian-produtividade',
    'nvidia-painel-controle-melhores-configuracoes',
    'nvidia-refelx-on-vs-boost-diferenca',
    'nvme-vs-sata-vale-a-pena-upgrade',
    'o-que-sao-ai-agents-guia-completo',
    'obs-studio-gravacao-replay-buffer-av1',
    'obs-studio-melhores-configuracoes-stream',
    'obs-studio-streaming-twitch-youtube-guia-completo',
    'onde-baixar-planilhas-excel-gratis',
    'otimizacao-jogos-pc',
    'otimizacao-performance',
    'otimizacao-registro',
    'otimizacao-ssd-windows-11',
    'otimizacoes-para-notebook-gamer',
    'overclock-gpu-msi-afterburner',
    'overclock-processador',
    'overwatch-2-melhores-configuracoes-fps',
    'overwatch-2-otimizacao-fps-input-lag-reduce-buffering',
    'painel-de-controle-nvidia',
    'palworld-otimizacao-server-dlss',
    'pasta-windows-winsxs-gigante-como-limpar',
    'pc-gamer-barato-custo-beneficio',
    'pc-lento-formatar-vs-limpar',
    'pc-liga-sem-video-diagnostico',
    'pcsx2-otimizacao-4k-widescreen-texturas-guia',
    'perda-de-pacote-packet-loss-fix',
    'performance-monitor-v2',
    'perifericos-gamer-vale-a-pena',
    'perifericos-gamer-vale-a-pena-marcas',
    'pesquisar-arquivos-windows-mais-rapido',
    'phasmophobia-reconhecimento-voz-vr',
    'playnite-launchbox-frontend-organizacao-biblioteca',
    'pobreza-digital-pc-fraco-produtividade',
    'pos-instalacao-windows-11',
    'privacidade-windows-telemetria',
    'problemas-conexao-wifi-causa-solucao',
    'problemas-mouse-gamer-sensor',
    'processadores-2026-analise',
    'programas-essenciais-windows',
    'project-zomboid-fps-boost',
    'protecao-dados-privacidade',
    'protecao-ransomware',
    'pubg-steam-fix-stuttering-travadas',
    'qual-melhor-windows-para-jogos',
    'rainbow-six-siege-vulkan-fps-configuracao-competitiva',
    're-size-bar-ativar-pc-gamer',
    'recuperacao-dados',
    'recuperacao-dados-hd-corrompido',
    'recuperacao-sistema',
    'red-dead-redemption-2-melhores-configuracoes',
    'red-dead-redemption-2-melhores-configuracoes-rdr2',
    'rede-corporativa',
    'rede-domestica',
    'reduzir-ping-exitlag-noping-dns',
    'reduzir-ping-jogos-online',
    'reduzir-ping-regedit-cmd-jogos',
    'remocao-virus-malware',
    'remover-bloatware-windows-powershell',
    'reshade-guia-instalacao-ray-tracing-rtgi-filtros',
    'reshade-instalacao-configuracao',
    'resident-evil-9-pc-configuracoes',
    'resolver-erros-windows',
    'retroarch-guia-completo-cores-shaders-crt',
    'roblox-fix-erro-conexao',
    'roblox-fps-unlocker-bloat-fix-bloxstrap',
    'roblox-fps-unlocker-guia',
    'roblox-fps-unlocker-tutorial',
    'roblox-tela-branca-travada-fix',
    'rocket-league-camera-settings-bakkesmod-air-roll',
    'rocket-league-melhores-configuracoes-camera',
    'rodar-llm-local-pc-ollama',
    'rog-ally-legion-go-otimizacao-windows-tdp-guia',
    'rpcs3-otimizacao-configuracao-60fps-patches-guia',
    'rtx-4060-vale-a-pena',
    'rust-otimizacao-fps-pvp-visibility',
    'saude-bateria-notebook',
    'segundo-monitor-vertical-configurar',
    'seguranca-digital',
    'seguranca-senhas-gerenciadores',
    'seguranca-wifi-avancada',
    'smart-delivery-xbox-pc-como-funciona',
    'solucao-problemas-audio',
    'solucao-problemas-bluetooth',
    'som-espacial-windows-configurar',
    'ssd-nvme-vs-sata-jogos',
    'ssd-vs-hd-qual-melhor',
    'ssd-vs-hdd-guia',
    'stalker-2-pc-configuracoes-otimizacao',
    'stardew-valley-mods-lag-fix',
    'starfield-2-pc-configuracoes-otimizacao',
    'starfield-otimizacao-dlss-mods',
    'steam-deck-otimizacao-cryoutilities-protonge-guia',
    'steam-launch-options-comandos-fps-boost',
    'steam-slow-download-fix',
    'steam-tarda-baixar-lento-fix',
    'streamlabs-vs-obs-qual-usar',
    'street-fighter-6-pc-configuracoes',
    'substituicao-ssd',
    'sync-vertical-g-sync-free-sync-explicacao',
    'taxa-amostragem-audio-44khz-192khz-bug',
    'team-fortress-2-mastercomfig-fps-competitivo',
    'teclado-desconfigurado-abnt2-ansi',
    'teclado-mecanico-rapid-trigger-snap-tap',
    'teclado-mecanico-vs-membrana-qual-o-melhor',
    'teclado-notebook-parou-fix',
    'teclados-mecanicos-guia',
    'teclados-mecanicos-switches-guia',
    'tela-azul-memory-management-fix',
    'termperatura-pc-fan-control-curva',
    'terraria-tmodloader-64bit-fix',
    'terraria-tmodloader-calamity-fps-fix',
    'testar-fonte-pc-multimetro',
    'teste-velocidade-internet',
    'the-witcher-3-next-gen-otimizacao-ray-tracing',
    'the-witcher-3-next-gen-performance',
    'tlauncher-viring-falso-positivo',
    'troubleshooting-internet',
    'tutorial-discord-instalar-configurar',
    'undervolt-cpu-notebook',
    'unpark-cpu-cores-performance-jogos',
    'upgrade-memoria-ram',
    'upgrade-pc-antigo-vale-a-pena',
    'usb-nao-reconhecido-reset-drivers',
    'valorant-fix-van-9003-secure-boot',
    'valorant-reduzir-input-lag',
    'valorant-reduzir-input-lag-fps-boost-config',
    'valorant-van-9003-secure-boot-tpm-fix',
    'vbs-memory-integrity-performance',
    'vcruntime140-dll-nao-encontrado',
    'verificar-saude-hd-ssd-crystaldiskinfo',
    'virtualizacao-vmware',
    'vita3k-emulador-ps-vita-configuracao-android-pc',
    'vlc-media-player-vs-potplayer',
    'vpn-configuracao',
    'vpn-jogos-exitlag-noping-vale-a-pena',
    'vpn-vale-a-pena-jogos',
    'water-cooler-vs-air-cooler',
    'water-cooler-vs-air-cooler-qual-escolher',
    'webcam-piscando-tela-preta-fix',
    'wifi-desconectando-sozinho-windows',
    'windows-defender-otimizacao-jogos',
    'windows-sandbox-testar-virus',
    'windows-update-corrigir-erros',
    'winrar-vs-7zip-qual-melhor',
    'xbox-app-nao-baixa-jogos-gamepass',
    'xbox-game-bar-desativar-fps-drop',
    'xbox-game-pass-pc-vale-a-pena',
    'xenia-emulador-xbox-360-red-dead-redemption-60fps',
    'yuzu-ryujinx-otimizacao-zelda-mario-60fps-guia',
    'z-index-css-explicacao',
    'zonas-mortas-analogico-controle-fix'
])

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
