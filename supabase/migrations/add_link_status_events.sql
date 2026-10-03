-- Migração: Adicionar suporte para eventos de vínculo em tempo real
-- 
-- Este arquivo configura o Supabase Realtime para disparar eventos
-- quando installations.user_id muda (vinculação/desvinculação).
--
-- Fluxo:
-- 1. Site faz UPDATE installations SET user_id = ... (via POST /api/v1/install/confirm)
-- 2. Supabase Realtime detecta UPDATE
-- 3. SSE notifica app em <1s: "link_status_changed"
-- 4. App fecha janela automaticamente

-- Habilitar Realtime para a tabela installations se ainda não estiver
-- NOTA: Esta parte pode já estar feita. Se der erro, ignorar.
BEGIN;

-- ⚠️ IMPORTANTE: NÃO USE RLS COM DEVICE_CREDENTIAL
-- RLS trabalha com auth.uid() (sessão de usuário), não com device_credential (header)
-- Segurança vem da validação de credencial no backend (NextJS), não no BD

-- Se RLS já estiver habilitado, DESABILITAR:
ALTER TABLE installations DISABLE ROW LEVEL SECURITY;

-- Realtime já está ativo para installations (verificar no Supabase)
-- Se precisar ativar manualmente:
-- ALTER PUBLICATION supabase_realtime ADD TABLE installations;

-- Criar uma view "link_sessions" para armazenar links temporários de vinculação
-- (opcional, para rastreamento; não é crítico para o SSE)
CREATE TABLE IF NOT EXISTS link_sessions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    installation_id uuid NOT NULL REFERENCES installations(id) ON DELETE CASCADE,
    link_id TEXT NOT NULL UNIQUE,
    status TEXT NOT NULL DEFAULT 'pending', -- pending, confirmed, expired, failed
    created_at TIMESTAMP WITH TIME ZONE DEFAULT now(),
    expires_at TIMESTAMP WITH TIME ZONE NOT NULL,
    confirmed_at TIMESTAMP WITH TIME ZONE,
    confirmed_by_user_id uuid,
    
    CONSTRAINT link_sessions_valid_status CHECK (status IN ('pending', 'confirmed', 'expired', 'failed'))
);

-- Índices para performance
CREATE INDEX IF NOT EXISTS link_sessions_installation_id_idx ON link_sessions(installation_id);
CREATE INDEX IF NOT EXISTS link_sessions_link_id_idx ON link_sessions(link_id);
CREATE INDEX IF NOT EXISTS link_sessions_expires_at_idx ON link_sessions(expires_at);

-- Habilitar Realtime para link_sessions também
ALTER PUBLICATION supabase_realtime ADD TABLE link_sessions;

-- Função para limpar links expirados (opcional, pode ser cronjob)
CREATE OR REPLACE FUNCTION cleanup_expired_links()
RETURNS void AS $$
BEGIN
    UPDATE link_sessions
    SET status = 'expired'
    WHERE status = 'pending' AND expires_at < now();
END;
$$ LANGUAGE plpgsql;

-- IMPORTANTE: Adicionar campos à tabela installations se não existirem
-- (Para rastreamento avançado — opcional)
ALTER TABLE installations 
ADD COLUMN IF NOT EXISTS linked_at TIMESTAMP WITH TIME ZONE,
ADD COLUMN IF NOT EXISTS link_confirmed_via_ip TEXT,
ADD COLUMN IF NOT EXISTS last_link_check_at TIMESTAMP WITH TIME ZONE;

COMMIT;

-- ============================================================================
-- IMPORTANTE: Após aplicar esta migração no Supabase:
-- 
-- 1. Verificar que RLS está habilitado:
--    SELECT * FROM pg_tables WHERE tablename = 'installations';
--
-- 2. Testar que Realtime funciona:
--    - Fazer UPDATE na tabela installations
--    - Verificar que supabase_realtime subscription recebe o evento
--
-- 3. Verificar permissões de device_credential:
--    - Devices devem conseguir ler suas próprias linhas
--    - Podem usar credencial de dispositivo em vez de sessão
-- ============================================================================
