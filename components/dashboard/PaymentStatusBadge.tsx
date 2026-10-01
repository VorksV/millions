import React from 'react';
import VoltrisIcon, { type VoltrisIconName } from './VoltrisIcon';

interface PaymentStatusBadgeProps {
  status: string;
  className?: string;
}

export default function PaymentStatusBadge({ status, className = '' }: PaymentStatusBadgeProps) {
  // Glifos e acentos = sistema de ícones do app desktop
  // (D:\APLICATIVO VOLTRIS\UI\Themes\Icons.xaml + VoltrisDesignSystem.xaml:20-28).
  const getStatusConfig = (status: string) => {
    switch (status) {
      case 'approved':
        return {
          label: 'Aprovado',
          color: 'bg-[#00FF94]/10 text-[#00FF94] border-[#00FF94]/20',
          icon: 'success' as VoltrisIconName
        };
      case 'pending':
        return {
          label: 'Pendente',
          color: 'bg-[#F59E0B]/10 text-[#F59E0B] border-[#F59E0B]/20',
          icon: 'clock' as VoltrisIconName
        };
      case 'cancelled':
        return {
          label: 'Cancelado',
          color: 'bg-[#EF4444]/10 text-[#EF4444] border-[#EF4444]/20',
          icon: 'close' as VoltrisIconName
        };
      case 'processing':
        return {
          label: 'Processando',
          color: 'bg-[#00D4FF]/10 text-[#00D4FF] border-[#00D4FF]/20',
          icon: 'processing' as VoltrisIconName
        };
      case 'refunded':
        return {
          label: 'Estornado',
          color: 'bg-[#FF4B6B]/10 text-[#FF4B6B] border-[#FF4B6B]/20',
          icon: 'recovery' as VoltrisIconName
        };
      case 'disputed':
        return {
          label: 'Disputado',
          color: 'bg-[#8B31FF]/10 text-[#8B31FF] border-[#8B31FF]/20',
          icon: 'alertTriangle' as VoltrisIconName
        };
      case 'returned':
        return {
          label: 'Devolvido',
          color: 'bg-slate-800 text-slate-400 border-slate-700',
          icon: 'package' as VoltrisIconName
        };
      default:
        return {
          label: 'Desconhecido',
          color: 'bg-slate-800 text-slate-400 border-slate-700',
          icon: 'info' as VoltrisIconName
        };
    }
  };

  const config = getStatusConfig(status);

  return (
    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium border ${config.color} ${className}`}>
      <VoltrisIcon name={config.icon} size={12} className="mr-1" />
      {config.label}
    </span>
  );
}
