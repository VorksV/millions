using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VoltrisOptimizer.Helpers
{
    /// <summary>
    /// Helper de segurança para validações e sanitizações
    /// Protege contra path traversal, injection e outras vulnerabilidades
    /// </summary>
    public static class SecurityHelper
    {
        #region Path Security
        
        /// <summary>
        /// Diretórios permitidos para operações de limpeza/otimização
        /// </summary>
        private static readonly HashSet<string> AllowedBasePaths = new(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows"};

        /// <summary>
        /// Diretórios que NUNCA devem ser modificados
        /// </summary>
        private static readonly HashSet<string> ProtectedPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows\System32",
            @"C:\Windows\SysWOW64",
            @"C:\Windows\Boot",
            @"C:\Windows\CSC",
            @"C:\Windows\INF",
            @"C:\Windows\PolicyDefinitions",
            @"C:\Windows\Resources",
            @"C:\Windows\Security",
            @"C:\Windows\servicing",
            @"C:\Windows\WinSxS",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData\Microsoft",
            @"C:\Users\Default",
            @"C:\Users\Public\Documents"};

        /// <summary>
        /// Extensões de arquivo que nunca devem ser deletadas
        /// </summary>
        private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".drv", ".msi", ".msp",
            ".bat", ".cmd", ".ps1", ".vbs", ".js",
            ".msc", ".cpl", ".inf", ".cat", ".mui",
            ".reg", ".pol", ".adm", ".admx", ".adml"
        };

        /// <summary>
        /// Nomes de arquivo que nunca devem ser deletados
        /// </summary>
        private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "ntuser.dat", "ntuser.dat.log", "ntuser.ini", "usrclass.dat",
            "desktop.ini", "thumbs.db", "iconcache.db",
            "bootmgr", "bootmgr.efi", "bcd", "bcd.log",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys"
        };

        /// <summary>
        /// Valida se um caminho é seguro para operações de escrita/deleção
        /// </summary>
        /// <param name="path">Caminho a validar</param>
        /// <param name="allowedBasePath">Caminho base obrigatório (null = usar lista padrão)</param>
        /// <returns>True se o caminho é seguro</returns>
        public static bool IsPathSafe(string path, string? allowedBasePath = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Normalizar caminho
                var fullPath = Path.GetFullPath(path);
                
                // Verificar path traversal
                if (ContainsPathTraversal(path))
                    return false;

                // Verificar se está em diretório protegido
                if (IsInProtectedDirectory(fullPath))
                    return false;

                // Se especificado, verificar se está dentro do diretório base permitido
                if (!string.IsNullOrEmpty(allowedBasePath))
                {
                    var normalizedBase = Path.GetFullPath(allowedBasePath);
                    if (!fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                else
                {
                    // Verificar se está em algum diretório permitido
                    bool inAllowedPath = AllowedBasePaths.Any(bp => 
                        fullPath.StartsWith(bp, StringComparison.OrdinalIgnoreCase));
                    
                    if (!inAllowedPath)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Valida se um arquivo pode ser deletado
        /// </summary>
        public static bool CanDeleteFile(string filePath)
        {
            if (!IsPathSafe(filePath))
                return false;

            try
            {
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);

                // Verificar nomes protegidos
                if (ProtectedFileNames.Contains(fileName))
                    return false;

                // Verificar extensões protegidas
                if (ProtectedExtensions.Contains(extension))
                    return false;

                // Verificar se arquivo existe e não é de sistema
                if (File.Exists(filePath))
                {
                    var attributes = File.GetAttributes(filePath);
                    if ((attributes & FileAttributes.System) == FileAttributes.System)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Verifica se o caminho contém tentativa de path traversal
        /// </summary>
        private static bool ContainsPathTraversal(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Verificar padrões comuns de path traversal
            var dangerousPatterns = new[]
            {
                "..", "..\\", "../", "%2e%2e", "%252e%252e",
                "..%5c", "..%2f", "%c0%ae", "%c1%9c"
            };

            var lowerPath = path.ToLowerInvariant();
            return dangerousPatterns.Any(p => lowerPath.Contains(p));
        }

        /// <summary>
        /// Verifica se o caminho está em diretório protegido
        /// </summary>
        private static bool IsInProtectedDirectory(string fullPath)
        {
            return ProtectedPaths.Any(pp => 
                fullPath.StartsWith(pp, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Sanitiza um nome de arquivo removendo caracteres perigosos
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "unnamed";

            // Remover caracteres inválidos para nomes de arquivo
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new StringBuilder(fileName.Length);

            foreach (char c in fileName)
            {
                if (!invalidChars.Contains(c))
                    sanitized.Append(c);
            }

            // Remover espaços múltiplos
            var result = Regex.Replace(sanitized.ToString(), @"\s+", " ").Trim();

            // Verificar se não ficou vazio
            if (string.IsNullOrWhiteSpace(result))
                return "unnamed";

            // Limitar tamanho
            if (result.Length > 200)
                result = result.Substring(0, 200);

            return result;
        }

        /// <summary>
        /// Sanitiza um caminho completo
        /// </summary>
        public static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                // Remover tentativas de path traversal
                var sanitized = path
                    .Replace("..", "")
                    .Replace("..\\", "")
                    .Replace("../", "");

                // Normalizar
                return Path.GetFullPath(sanitized);
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region Input Validation

        /// <summary>
        /// Valida se uma string contém apenas caracteres alfanuméricos
        /// </summary>
        public static bool IsAlphanumeric(string input)
        {
            return !string.IsNullOrEmpty(input) && input.All(char.IsLetterOrDigit);
        }

        /// <summary>
        /// Valida formato de chave de licença
        /// </summary>
        public static bool IsValidLicenseKeyFormat(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            // Formato: VOLTRIS-LIC-XXXX-YYYYMMDD-HASH
            var pattern = @"^VOLTRIS-LIC-[A-Z0-9]+-\d{8}-[A-F0-9]{16}$";
            return Regex.IsMatch(key.ToUpperInvariant(), pattern);
        }

        /// <summary>
        /// Valida URL
        /// </summary>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var result) &&
                   (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Sanitiza entrada de texto removendo caracteres de controle
        /// </summary>
        public static string SanitizeTextInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Remover caracteres de controle exceto newlines e tabs
            var sanitized = new StringBuilder(input.Length);
            
            foreach (char c in input)
            {
                if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
                    sanitized.Append(c);
            }

            return sanitized.ToString();
        }

        #endregion

        #region Command Validation

        /// <summary>
        /// Lista de comandos permitidos para execução
        /// </summary>
        private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
        {
            "powercfg.exe",
            "sc.exe",
            "netsh.exe",
            "ipconfig.exe",
            "rundll32.exe",
            "dism.exe",
            "cleanmgr.exe",
            "defrag.exe"
        };

        /// <summary>
        /// Argumentos proibidos em comandos
        /// </summary>
        private static readonly HashSet<string> ForbiddenArgPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            "format", "del /f", "rd /s", "rmdir /s",
            "shutdown", "restart", "logoff",
            "; rm ", "| rm ", "& rm ",
            "; del ", "| del ", "& del ",
            "$(", "`", "$("
        };

        /// <summary>
        /// Valida se um comando é seguro para execução
        /// </summary>
        public static bool IsCommandSafe(string command, string arguments)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            // Verificar se o comando está na lista permitida
            var commandName = Path.GetFileName(command);
            if (!AllowedCommands.Contains(commandName))
                return false;

            // Verificar argumentos
            if (!string.IsNullOrEmpty(arguments))
            {
                var lowerArgs = arguments.ToLowerInvariant();
                
                // Verificar padrões proibidos
                if (ForbiddenArgPatterns.Any(p => lowerArgs.Contains(p.ToLowerInvariant())))
                    return false;

                // Verificar injection básico
                if (lowerArgs.Contains(";") || lowerArgs.Contains("&&") || lowerArgs.Contains("||"))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Sanitiza argumentos de comando
        /// </summary>
        public static string SanitizeCommandArguments(string arguments)
        {
            if (string.IsNullOrEmpty(arguments))
                return string.Empty;

            // Remover caracteres de injection
            return arguments
                .Replace(";", "")
                .Replace("&&", "")
                .Replace("||", "")
                .Replace("|", "")
                .Replace("$(", "")
                .Replace("`", "")
                .Replace("\"", "\\\"");
        }

        #endregion

        #region Hashing & Encryption Helpers

        /// <summary>
        /// Calcula hash SHA256 de uma string
        /// </summary>
        public static string ComputeSHA256(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Calcula hash SHA256 de um arquivo
        /// </summary>
        public static string ComputeFileSHA256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var bytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Gera salt aleatório
        /// </summary>
        public static byte[] GenerateSalt(int length = 16)
        {
            var salt = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(salt);
            return salt;
        }

        /// <summary>
        /// Gera string aleatória segura
        /// </summary>
        public static string GenerateSecureRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(random);
            
            var result = new StringBuilder(length);
            foreach (var b in random)
            {
                result.Append(chars[b % chars.Length]);
            }
            return result.ToString();
        }

        #endregion

        #region Registry Security

        /// <summary>
        /// Chaves de registro protegidas que não devem ser modificadas
        /// </summary>
        private static readonly HashSet<string> ProtectedRegistryPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SafeBoot",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
            @"HKEY_LOCAL_MACHINE\SECURITY",
            @"HKEY_LOCAL_MACHINE\SAM"};

        /// <summary>
        /// Valida se uma chave de registro pode ser modificada
        /// </summary>
        public static bool IsRegistryKeySafe(string keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath))
                return false;

            return !ProtectedRegistryPaths.Any(p => 
                keyPath.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}

