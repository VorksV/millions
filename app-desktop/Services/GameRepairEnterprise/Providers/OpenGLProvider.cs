using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Services.GameRepairEnterprise.Interfaces;
using VoltrisOptimizer.Services.GameRepairEnterprise.Models;
using VoltrisOptimizer.Services.GameRepairEnterprise.Engine;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.GameRepairEnterprise.Providers
{
    public class OpenGLProvider : IGameDependencyProvider
    {
        public string ProviderName => "OpenGL Hardware Diagnostics";
        private readonly ISecureDownloadService _downloadService;
        private readonly ILoggingService _logger;

        private const int GL_RENDERER = 0x1B01;
        private const int GL_VENDOR = 0x1B00;
        private const int GL_VERSION = 0x1B02;

        [DllImport("opengl32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern IntPtr glGetString(uint name);

        [DllImport("opengl32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

        [DllImport("opengl32.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern bool wglDeleteContext(IntPtr hglrc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [StructLayout(LayoutKind.Sequential)]
        private struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize;
            public ushort nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
            public byte cAlphaBits, cAlphaShift;
            public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
            public byte cDepthBits, cStencilBits, cAuxBuffers;
            public byte iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }

        [DllImport("gdi32.dll")]
        private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR ppfd);

        [DllImport("gdi32.dll")]
        private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

        public OpenGLProvider(ISecureDownloadService downloadService, ILoggingService logger)
        {
            _downloadService = downloadService;
            _logger = logger;
        }

        public async Task<List<GameDependencyDiagnosis>> DiagnoseAsync(CancellationToken ct)
        {
            var results = new List<GameDependencyDiagnosis>();
            var diag = new GameDependencyDiagnosis { ComponentId = "OpenGL_Runtime", Title = "OpenGL ICD (Installable Client Driver)" };
            
            _logger.LogInfo("[OpenGLProvider] Iniciando verificação de renderizador WGL/OpenGL...");

            await Task.Run(() =>
            {
                IntPtr hdc = IntPtr.Zero;
                IntPtr hglrc = IntPtr.Zero;
                try
                {
                    IntPtr desktop = GetDesktopWindow();
                    hdc = GetDC(desktop);
                    
                    if (hdc == IntPtr.Zero)
                    {
                        throw new Exception("Falha ao obter Device Context do Desktop.");
                    }

                    var pfd = new PIXELFORMATDESCRIPTOR
                    {
                        nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR)),
                        nVersion = 1,
                        dwFlags = 0x00000020 | 0x00000004 | 0x00000001, // PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER
                        iPixelType = 0, // PFD_TYPE_RGBA
                        cColorBits = 32,
                        cDepthBits = 24,
                        iLayerType = 0 // PFD_MAIN_PLANE
                    };

                    int format = ChoosePixelFormat(hdc, ref pfd);
                    if (format == 0) throw new Exception("Falha no ChoosePixelFormat.");
                    
                    SetPixelFormat(hdc, format, ref pfd);

                    hglrc = wglCreateContext(hdc);
                    if (hglrc == IntPtr.Zero) throw new Exception("Falha no wglCreateContext. OpenGL ausente ou driver quebrado.");

                    wglMakeCurrent(hdc, hglrc);

                    IntPtr pRenderer = glGetString(GL_RENDERER);
                    IntPtr pVersion = glGetString(GL_VERSION);

                    if (pRenderer != IntPtr.Zero && pVersion != IntPtr.Zero)
                    {
                        string renderer = Marshal.PtrToStringAnsi(pRenderer);
                        string version = Marshal.PtrToStringAnsi(pVersion);

                        _logger.LogInfo($"[OpenGLProvider] Encontrado: {renderer} (Versão {version})");

                        if (renderer.Contains("Microsoft Basic Render Driver") || renderer.Contains("GDI Generic"))
                        {
                            _logger.LogWarning("[OpenGLProvider] O renderizador ativo é via SOFTWARE (CPU). Isso indica falha grave no driver de vídeo ou ambiente de Virtualização.");
                            diag.IsHealthy = false;
                diag.CanAutoFix = false;
                            diag.MissingEvidences.Add($"Software Rasterizer detectado: {renderer}");
                        }
                        else
                        {
                            diag.IsHealthy = true;
                            diag.Evidences.Add($"Hardware Renderer: {renderer}");
                            diag.Evidences.Add($"OpenGL Version: {version}");
                        }
                    }
                    else
                    {
                        diag.IsHealthy = false;
                        diag.CanAutoFix = false;
                        diag.MissingEvidences.Add(VoltrisOptimizer.Services.LocalizationService.Instance.GetString("GameRepairOpenGLMissing"));
                    }

                    wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[OpenGLProvider] Exceção crítica ao sondar OpenGL: {ex.Message}");
                    diag.IsHealthy = false;
                diag.CanAutoFix = false;
                    diag.MissingEvidences.Add($"Falha na API: {ex.Message}");
                }
                finally
                {
                    if (hglrc != IntPtr.Zero) wglDeleteContext(hglrc);
                    if (hdc != IntPtr.Zero) ReleaseDC(GetDesktopWindow(), hdc);
                }
            }, ct);

            results.Add(diag);
            return results;
        }

        public async Task<GameDependencyRepairResult> RepairAsync(GameDependencyDiagnosis issue, IProgress<(GameDependencyState state, int percentage, string message)> progress, CancellationToken ct)
        {
            _logger.LogWarning("[OpenGLProvider] O driver OpenGL ICD faz parte do driver da GPU. Não pode ser reparado isoladamente.");
            return new GameDependencyRepairResult 
            { 
                Success = false, 
                Message = "O Installable Client Driver do OpenGL está quebrado ou ausente. Por favor, reinstale o driver de vídeo oficial (NVIDIA/AMD/Intel)." 
            };
        }

        public async Task<bool> ValidateAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            var results = await DiagnoseAsync(ct);
            return results.Count > 0 && results[0].IsHealthy;
        }

        public async Task RollbackAsync(GameDependencyDiagnosis issue, CancellationToken ct)
        {
            await Task.CompletedTask;
        }
    }
}
