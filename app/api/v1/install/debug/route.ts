import { NextRequest, NextResponse } from 'next/server';
import { createClient } from '@supabase/supabase-js';
import { requireAdmin } from '@/utils/supabase/requireAdmin';

export const runtime = 'nodejs';

// Rate limiting em memoria. Rota de diagnostico: uso eventual, nao polling.
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

export async function GET(request: NextRequest) {
    try {
        // SEGURANÇA: rota de diagnóstico — somente administradores
        const admin = await requireAdmin();
        if (admin.error) return admin.error;

        const ip = request.headers.get('x-forwarded-for')?.split(',')[0]?.trim() || 'unknown';
        if (!checkRateLimit(ip)) {
            return NextResponse.json({ error: 'Too Many Requests' }, { status: 429 });
        }

        const { searchParams } = new URL(request.url);
        const installation_id = searchParams.get('installation_id');

        if (!installation_id) {
            return NextResponse.json({ error: 'Missing installation_id' }, { status: 400 });
        }

        const supabaseUrl = process.env.NEXT_PUBLIC_SUPABASE_URL;
        const supabaseServiceKey = process.env.SUPABASE_SERVICE_ROLE_KEY;

        if (!supabaseUrl || !supabaseServiceKey) {
            return NextResponse.json({ error: 'Database configuration missing' }, { status: 500 });
        }

        const supabase = createClient(supabaseUrl, supabaseServiceKey);

        // Buscar a instalação usando service role (ignora RLS)
        const { data, error } = await supabase
            .from('installations')
            .select('*')
            .eq('id', installation_id)
            .single();

        if (error) {
            return NextResponse.json({
                error: error.message,
                details: error
            }, { status: 500 });
        }

        return NextResponse.json({
            installation: data,
            message: data ? 'Instalação encontrada' : 'Instalação não encontrada'
        });
    } catch (error: any) {
        return NextResponse.json({ error: error.message }, { status: 500 });
    }
}
