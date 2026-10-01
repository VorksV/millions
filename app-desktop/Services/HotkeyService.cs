using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Models;

namespace VoltrisOptimizer.Services
{
    public sealed class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;

        private HwndSource? _hotkeyWindow;
        private readonly Dictionary<int, ShortcutItem> _registrations = new();
        private readonly ILoggingService? _logger;
        private bool _initialized;

        public event Action<int>? HotkeyPressed;

        public HotkeyService(ILoggingService? logger)
        {
            _logger = logger;
        }

        public IReadOnlyList<ShortcutItem> DefaultShortcuts { get; } = new List<ShortcutItem>
        {
            // ── NAVEGAÇÃO RÁPIDA (ALT + número) ──
            new() { Id = 1,  KeyCombo = "ALT + 1", Category = "Navigation",    IconGeometry = "IconDashboard" },
            new() { Id = 2,  KeyCombo = "ALT + 2", Category = "Navigation",    IconGeometry = "IconCleanup" },
            new() { Id = 3,  KeyCombo = "ALT + 3", Category = "Navigation",    IconGeometry = "IconBolt" },
            new() { Id = 4,  KeyCombo = "ALT + 4", Category = "Navigation",    IconGeometry = "IconGamepad" },
            new() { Id = 5,  KeyCombo = "ALT + 5", Category = "Navigation",    IconGeometry = "IconWrench" },
            new() { Id = 6,  KeyCombo = "ALT + 6", Category = "Navigation",    IconGeometry = "IconShield" },
            new() { Id = 7,  KeyCombo = "ALT + 7", Category = "Navigation",    IconGeometry = "IconGear" },

            // ── MODO GAMER ──
            new() { Id = 8,  KeyCombo = "CTRL + ALT + G", Category = "GamerMode",     IconGeometry = "IconGamepad" },
            new() { Id = 9,  KeyCombo = "CTRL + ALT + D", Category = "GamerMode",     IconGeometry = "IconGamepad" },
            new() { Id = 10, KeyCombo = "CTRL + ALT + F", Category = "GamerMode",     IconGeometry = "IconEye" },

            // ── WIDGET ──
            // CORREÇÃO: Usar CTRL+SHIFT+W em vez de CTRL+ALT+W para não conflitar com AltGr+W
            // Em teclados ABNT2, AltGr é traduzido como Ctrl+Alt pelo Windows.
            // RegisterHotKey intercepta a tecla no nível do OS, impedindo o caractere (vírgula) de ser gerado.
            new() { Id = 11, KeyCombo = "CTRL + SHIFT + W", Category = "WidgetOverlay", IconGeometry = "IconEye" },

            // ── SISTEMA E MANUTENÇÃO ──
            new() { Id = 12, KeyCombo = "CTRL + ALT + N", Category = "Maintenance",   IconGeometry = "IconBolt" },
            new() { Id = 13, KeyCombo = "CTRL + ALT + C", Category = "Maintenance",   IconGeometry = "IconCleanup" },
            new() { Id = 14, KeyCombo = "CTRL + ALT + B", Category = "Maintenance",   IconGeometry = "IconPerformance" },
            new() { Id = 15, KeyCombo = "CTRL + ALT + R", Category = "Maintenance",   IconGeometry = "IconWrench" },

            // ── REDE ──
            new() { Id = 16, KeyCombo = "CTRL + ALT + T", Category = "Network",       IconGeometry = "IconGlobe" },
            new() { Id = 17, KeyCombo = "CTRL + ALT + Y", Category = "Network",       IconGeometry = "IconRefresh" },

            // ── SEGURANÇA ──
            new() { Id = 18, KeyCombo = "CTRL + ALT + I", Category = "Security",      IconGeometry = "IconShield" },
            new() { Id = 19, KeyCombo = "CTRL + ALT + U", Category = "Security",      IconGeometry = "IconShieldPlus" },

            // ── UTILITÁRIOS ──
            new() { Id = 20, KeyCombo = "CTRL + SHIFT + Q", Category = "Utilities",     IconGeometry = "IconBolt" },
            new() { Id = 21, KeyCombo = "CTRL + ALT + E", Category = "Utilities",     IconGeometry = "IconRestore" },
        };

        public void Initialize()
        {
            if (_initialized) return;
            try
            {
                var p = new HwndSourceParameters("VoltrisOptimizer.HotkeyWindow")
                {
                    Width = 0,
                    Height = 0,
                    PositionX = -32000,
                    PositionY = -32000,
                    WindowStyle = unchecked((int)0x80000000),
                };
                _hotkeyWindow = new HwndSource(p);
                _hotkeyWindow.AddHook(WndProc);
                _initialized = true;
                _logger?.LogInfo("[HotkeyService] Janela oculta criada para hotkeys globais.");
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[HotkeyService] Falha ao criar janela oculta: {ex.Message}");
            }
        }

        public void RegisterAll()
        {
            if (!_initialized)
            {
                _logger?.LogWarning("[HotkeyService] Não inicializado — não é possível registrar hotkeys.");
                return;
            }

            foreach (var sc in DefaultShortcuts)
            {
                var (mod, vk) = ParseKeyCombo(sc.KeyCombo);
                if (vk == 0) continue;

                bool ok = NativeMethods.RegisterHotKey(_hotkeyWindow!.Handle, sc.Id, mod, vk);
                sc.IsRegistered = ok;
                if (ok)
                    _registrations[sc.Id] = sc;

                _logger?.LogInfo($"[HotkeyService] Hotkey {sc.KeyCombo} (ID:{sc.Id}) {(ok ? "registrada" : "FALHA ao registrar")}.");
            }
        }

        public void UnregisterAll()
        {
            foreach (var kv in _registrations)
            {
                NativeMethods.UnregisterHotKey(_hotkeyWindow?.Handle ?? IntPtr.Zero, kv.Key);
            }
            _registrations.Clear();
            _logger?.LogInfo("[HotkeyService] Todas as hotkeys foram desregistradas.");
        }

	public bool IsHotkeyRegistered(int id) => _registrations.ContainsKey(id);

	/// <summary>
	/// Devolve a combinação de teclas de um atalho pelo Id, para logging legível.
	/// Retorna <c>null</c> quando o Id não existe no catálogo.
	/// </summary>
	public string? GetShortcutDescription(int id)
	{
		var sc = DefaultShortcuts.FirstOrDefault(s => s.Id == id);
		return sc == null ? null : $"{sc.KeyCombo} (ID:{id})";
	}

	/// <summary>
	/// Atalhos cujo registro COM O WINDOWS FALHOU. Útil para a interface e para o log:
	/// o usuário precisa saber que o atalho que está vendo na tela não funciona porque
	/// outro programa já registrou aquela combinação.
	/// </summary>
	public IReadOnlyList<ShortcutItem> GetFailedRegistrations() =>
		DefaultShortcuts.Where(s => !_registrations.ContainsKey(s.Id)).ToList();

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                // PROTEÇÃO CONTRA O "AltGr": 
                // O Windows converte o AltGr fisicamente em (Ctrl Esquerdo + Alt Direito).
                // Se o Alt Direito (VK_RMENU = 0xA5) estiver pressionado, ignoramos a hotkey,
                // para não conflitar com caracteres especiais em notebooks (ex: AltGr + W).
                short rAltState = NativeMethods.GetAsyncKeyState(0xA5); // 0xA5 = VK_RMENU
                if ((rAltState & 0x8000) != 0)
                {
                    _logger?.LogInfo("[HotkeyService] Hotkey ignorada: Tecla AltGr pressionada fisicamente.");
                    return IntPtr.Zero;
                }

                int id = wParam.ToInt32();
                _logger?.LogInfo($"[HotkeyService] Hotkey pressionada — ID:{id}");
                HotkeyPressed?.Invoke(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private static (uint modifiers, uint vk) ParseKeyCombo(string combo)
        {
            uint mod = 0;
            uint vk = 0;
            var parts = combo.Split('+', StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":   mod |= 0x0002; break;
                    case "ALT":    mod |= 0x0001; break;
                    case "SHIFT":  mod |= 0x0004; break;
                    case "WIN":    mod |= 0x0008; break;
                    default:
                        vk = (uint)KeyInterop.VirtualKeyFromKey(ParseKey(part));
                        break;
                }
            }
            mod |= MOD_NOREPEAT;
            return (mod, vk);
        }

        private static Key ParseKey(string name)
        {
            return name.ToUpperInvariant() switch
            {
                "W" => Key.W,
                "G" => Key.G,
                "D" => Key.D,
                "O" => Key.O,
                "P" => Key.P,
                "L" => Key.L,
                "S" => Key.S,
                "Q" => Key.Q,
                "F" => Key.F,
                "N" => Key.N,
                "C" => Key.C,
                "B" => Key.B,
                "R" => Key.R,
                "T" => Key.T,
                "Y" => Key.Y,
                "I" => Key.I,
                "U" => Key.U,
                "E" => Key.E,
                "0" => Key.D0,
                "1" => Key.D1,
                "2" => Key.D2,
                "3" => Key.D3,
                "4" => Key.D4,
                "5" => Key.D5,
                "6" => Key.D6,
                "7" => Key.D7,
                "8" => Key.D8,
                "9" => Key.D9,
                _   => Key.None,
            };
        }

        public void Dispose()
        {
            UnregisterAll();
            if (_hotkeyWindow != null)
            {
                try { _hotkeyWindow.Dispose(); }
                catch { /* suppress */ }
                _hotkeyWindow = null;
            }
            _initialized = false;
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern short GetAsyncKeyState(int vKey);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        }
    }
}
