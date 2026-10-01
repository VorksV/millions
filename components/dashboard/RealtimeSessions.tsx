'use client';

import { createClientComponentClient } from '@supabase/auth-helpers-nextjs';
import { useEffect, useState } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import VoltrisIcon from './VoltrisIcon';

interface Session {
    id: string;
    device_id: string;
    status: 'active' | 'idle' | 'completed' | 'timeout';
    started_at: string;
    last_heartbeat_at: string;
    device: {
        machine_id: string;
        hostname: string;
    };
}

export default function RealtimeSessions() {
    const [sessions, setSessions] = useState<Session[]>([]);
    const [stats, setStats] = useState({
        online: 0,
        idle: 0,
        todayTotal: 0
    });

    const supabase = createClientComponentClient();

    useEffect(() => {
        fetchSessions();

        // Subscribe to Realtime changes on sessions table
        const channel = supabase
            .channel('realtime-sessions')
            .on('postgres_changes', {
                event: '*',
                schema: 'public',
                table: 'sessions'
            }, (payload) => {
                // Ideally refresh or optimally update state
                // For simplicity, we refresh
                fetchSessions();
            })
            .subscribe();

        return () => {
            supabase.removeChannel(channel);
        };
    }, [supabase]);

    const fetchSessions = async () => {
        // Fetch active sessions
        const { data: activeData } = await supabase
            .from('sessions')
            .select(`
                id, device_id, status, started_at, last_heartbeat_at,
                device:devices(machine_id, hostname)
            `)
            .in('status', ['active', 'idle'])
            .order('started_at', { ascending: false });

        if (activeData) {
            // @ts-ignore
            setSessions(activeData);

            setStats(prev => ({
                ...prev,
                online: activeData.filter(s => s.status === 'active').length,
                idle: activeData.filter(s => s.status === 'idle').length
            }));
        }

        // Fetch daily total
        const todayStr = new Date().toISOString().split('T')[0];
        const { count } = await supabase
            .from('sessions')
            .select('id', { count: 'exact', head: true })
            .gte('started_at', todayStr);

        setStats(prev => ({ ...prev, todayTotal: count || 0 }));
    };

    const formatDuration = (startParams: string) => {
        const start = new Date(startParams);
        const now = new Date();
        const diff = Math.floor((now.getTime() - start.getTime()) / 1000); // seconds

        const h = Math.floor(diff / 3600);
        const m = Math.floor((diff % 3600) / 60);
        return `${h}h ${m}m`;
    };

    return (
        <div className="space-y-6">
            {/* Stats Cards */}
            <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
                <Card>
                    <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                        <CardTitle className="text-sm font-medium">Online Agora</CardTitle>
                        <VoltrisIcon name="users" size={16} className="text-green-500" />
                    </CardHeader>
                    <CardContent>
                        <div className="text-2xl font-bold">{stats.online}</div>
                        <p className="text-xs text-muted-foreground">
                            +{stats.online} usuários ativos
                        </p>
                    </CardContent>
                </Card>
                <Card>
                    <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                        <CardTitle className="text-sm font-medium">Ociosos (Idle)</CardTitle>
                        <VoltrisIcon name="clock" size={16} className="text-yellow-500" />
                    </CardHeader>
                    <CardContent>
                        <div className="text-2xl font-bold">{stats.idle}</div>
                        <p className="text-xs text-muted-foreground">
                            Sem input há +5min
                        </p>
                    </CardContent>
                </Card>
                <Card>
                    <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
                        <CardTitle className="text-sm font-medium">Sessões Hoje</CardTitle>
                        <VoltrisIcon name="activity" size={16} className="text-blue-500" />
                    </CardHeader>
                    <CardContent>
                        <div className="text-2xl font-bold">{stats.todayTotal}</div>
                        <p className="text-xs text-muted-foreground">
                            Aberturas do app
                        </p>
                    </CardContent>
                </Card>
            </div>

            {/* Live Table */}
            <Card className="col-span-4">
                <CardHeader>
                    <CardTitle>Sessões em Tempo Real</CardTitle>
                </CardHeader>
                <CardContent>
                    <Table>
                        <TableHeader>
                            <TableRow>
                                <TableHead>Hostname</TableHead>
                                <TableHead>Status</TableHead>
                                <TableHead>Duração</TableHead>
                                <TableHead>Último Sinal</TableHead>
                            </TableRow>
                        </TableHeader>
                        <TableBody>
                            {sessions.map((session) => (
                                <TableRow key={session.id}>
                                    <TableCell className="font-medium">
                                        {/* @ts-ignore */}
                                        {session.device?.hostname || session.device?.machine_id || 'Unknown'}
                                    </TableCell>
                                    <TableCell>
                                        <Badge variant={session.status === 'active' ? 'default' : 'secondary'}
                                            className={
                                                session.status === 'active' ? 'bg-green-500 hover:bg-green-600' :
                                                    'bg-yellow-500 hover:bg-yellow-600 text-black'
                                            }
                                        >
                                            {session.status.toUpperCase()}
                                        </Badge>
                                    </TableCell>
                                    <TableCell>{formatDuration(session.started_at)}</TableCell>
                                    <TableCell>{new Date(session.last_heartbeat_at).toLocaleTimeString()}</TableCell>
                                </TableRow>
                            ))}
                            {sessions.length === 0 && (
                                <TableRow>
                                    <TableCell colSpan={4} className="text-center">Nenhuma sessão ativa no momento.</TableCell>
                                </TableRow>
                            )}
                        </TableBody>
                    </Table>
                </CardContent>
            </Card>
        </div>
    );
}
