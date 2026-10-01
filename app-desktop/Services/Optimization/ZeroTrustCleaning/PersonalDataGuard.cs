using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Services.Optimization.ZeroTrustCleaning
{
    public class PersonalDataGuard : IPersonalDataGuard
    {
        private readonly ILoggingService _logger;
        private readonly string _userProfilePath;
        
        // Padrões de risco: Documentos, Imagens, Códigos, Bancos de Dados
        private readonly HashSet<string> _sensitiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".doc", ".docx", ".pdf", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv",
            ".jpg", ".jpeg", ".png", ".gif", ".psd", ".mp4", ".mov", ".avi", ".mkv",
            ".cs", ".js", ".py", ".cpp", ".html", ".css", ".json", ".xml", ".sln", ".csproj",
            ".db", ".sqlite", ".mdb", ".accdb", ".sql", ".bak", ".wallet", ".key", ".pem"
        };

        private readonly string[] _sacredDirectories = {
            "Desktop",
            "Documents",
            "Downloads",
            "Pictures",
            "Music",
            "Videos",
            "OneDrive",
            "Dropbox",
            "Google Drive"
        };

        public PersonalDataGuard(ILoggingService logger)
        {
            _logger = logger;
            _userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public bool ContainsUserGeneratedData(MutableNode node)
        {
            if (node.Type != NodeType.File && node.Type != NodeType.Directory) return false;

            // REGRA 1: Caminhos "Sagrados" do perfil do usuário
            foreach (var sacredDir in _sacredDirectories)
            {
                var sacredPath = Path.Combine(_userProfilePath, sacredDir);
                if (node.Path.StartsWith(sacredPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"[PersonalDataGuard] BLOQUEIO ABSOLUTO: Caminho pertence a diretório de usuário inviolável. ({node.Path})");
                    return true;
                }
            }

            // REGRA 2: Extensões sensíveis (Heurística de arquivo pessoal)
            if (node.Type == NodeType.File)
            {
                var extension = Path.GetExtension(node.Path);
                if (_sensitiveExtensions.Contains(extension))
                {
                    _logger.LogWarning($"[PersonalDataGuard] BLOQUEIO ABSOLUTO: Extensão '{extension}' classificada como dado pessoal/trabalho. ({node.Path})");
                    return true;
                }
            }

            return false;
        }

        public bool ContainsSensitiveMagicNumbers(MutableNode node)
        {
            if (node.Type != NodeType.File) return false;
            if (node.SizeBytes > 10 * 1024 * 1024) return false; // Evita carregar arquivos gigantes na memória para verificação de Magic Number

            try
            {
                if (!File.Exists(node.Path)) return false;

                // Leitura rápida do cabeçalho (Magic Numbers)
                using var fs = new FileStream(node.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[16];
                int bytesRead = fs.Read(buffer, 0, buffer.Length);

                if (bytesRead < 4) return false;

                // HEURÍSTICA: Detecção de SQLite disfarçado (ex: Chrome User Data / WhatsApp Temp)
                // SQLite format 3 = 53 51 4C 69 74 65 20 66 6F 72 6D 61 74 20 33 00
                string headerAscii = System.Text.Encoding.ASCII.GetString(buffer);
                if (headerAscii.StartsWith("SQLite format 3"))
                {
                    _logger.LogWarning($"[PersonalDataGuard] BLOQUEIO ABSOLUTO: Magic Number detectou SQLite DB oculto. Possível banco de senhas ou app data vital. ({node.Path})");
                    return true;
                }

                // Mais verificações de magic numbers (ex: PKZip/Office=50 4B 03 04, PDF=25 50 44 46)
                if (buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46) // %PDF
                {
                    _logger.LogWarning($"[PersonalDataGuard] BLOQUEIO ABSOLUTO: Magic Number detectou PDF disfarçado. ({node.Path})");
                    return true;
                }

                if (buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04) // PKZip (Pode ser Docx, Xlsx ou ZIP real)
                {
                    _logger.LogWarning($"[PersonalDataGuard] BLOQUEIO ABSOLUTO: Magic Number detectou Arquivo Compactado/Conteúdo Office (PKZip). Protegido contra purga automática. ({node.Path})");
                    return true;
                }

                return false;
            }
            catch
            {
                // Se não consegue ler para verificar, ASSUME COMO PROTEGIDO (Zero-Trust)
                return true; 
            }
        }
    }
}
