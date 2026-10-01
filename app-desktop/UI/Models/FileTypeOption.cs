using System;
using System.Collections.Generic;
using System.Linq;
using VoltrisOptimizer.Services.Recovery;

namespace VoltrisOptimizer.UI.Models
{
    /// <summary>
    /// Uma opção do seletor de tipo da recuperação.
    ///
    /// O menu é montado a partir do que a varredura REALMENTE encontrou, e
    /// não de uma lista fixa. A diferença importa: uma lista fixa oferece
    /// "Áudio" numa varredura em que não há um único arquivo de áudio, e
    /// esconde ".json (3.120)" porque programador não é categoria de usuário.
    ///
    /// Cada opção carrega a contagem. O usuário descobre o que existe no
    /// volume antes de escolher, em vez de filtrar às cegas e ver zero
    /// resultado sem entender por quê.
    /// </summary>
    public class FileTypeOption
    {
        /// <summary>
        /// Como o filtro casa o item. Vazio = não filtra (todos).
        /// </summary>
        public string Key { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int Count { get; set; }

        /// <summary>Pastas não têm extensão; precisam de um caminho próprio.</summary>
        public bool IsFolder { get; set; }

        /// <summary>
        /// Quando true, casa pela CATEGORIA (Documentos, Imagens...) em vez
        /// de pela extensão exata.
        /// </summary>
        public FileCategory? Category { get; set; }

        /// <summary>
        /// Chave da opção que REVELA os itens de "só a estrutura" — registro
        /// MFT intacto com conteúdo sobrescrito. Vem antes de qualquer tipo,
        /// porque é o filtro que o usuário precisa quando um arquivo
        /// "some" da lista.
        /// </summary>
        public bool ShowContentGone { get; set; }

        public override string ToString() =>
            $"{Label}  ({Count:N0})";
    }

    public static class FileTypeOptionBuilder
    {
        /// <summary>
        /// Quantas extensões listar individualmente.
        ///
        /// 24 era pouco: numa varredura de 44 mil itens existem ~90
        /// extensões distintas, e o menu cortava no meio da lista. O
        /// usuário não conseguia escolher metade dos tipos.
        /// </summary>
        private const int MaxExtensions = 90;

        public static List<FileTypeOption> Build(IReadOnlyList<NativeRecoveredFile> items)
        {
            var options = new List<FileTypeOption>();
            if (items == null || items.Count == 0) return options;

            options.Add(new FileTypeOption { Label = "Todos os tipos", Count = items.Count });

            // ── Só a estrutura ─────────────────────────────────────────
            // Vem PRIMEIRO, antes de qualquer tipo. É o filtro que o usuário
            // precisa quando um arquivo que ele apagou "não aparece" — e é
            // a única forma de ver os 92 mil registros intactos cujo
            // conteúdo foi sobrescrito.
            //
            // Fora daqui esses itens estão escondidos, e está certo: eles
            // não são recuperáveis, e uma lista de 92 mil arquivos vazios
            // afoga os 44 mil que têm conteúdo de verdade.
            int goneCount = items.Count(i => i.ContentGone);
            if (goneCount > 0)
            {
                options.Add(new FileTypeOption
                {
                    Label = "Só a estrutura (conteúdo sobrescrito)",
                    Key = "__gone__",
                    ShowContentGone = true,
                    Count = goneCount
                });
            }

            // ── Pastas ────────────────────────────────────────────────
            // Vem primeiro e separado: pasta não tem extensão, e o usuário
            // pediu explicitamente para poder filtrar só por elas.
            int folders = items.Count(i => i.IsRecycleFolder);
            if (folders > 0)
            {
                options.Add(new FileTypeOption
                {
                    Label = "Pastas",
                    Key = "__folders__",
                    IsFolder = true,
                    Count = folders
                });
            }

            // ── Categorias ────────────────────────────────────────────
            // A ordem segue o que o usuário procura primeiro, não o
            // alphabetical do enum: documento e imagem antes de "Outros".
            var categoryOrder = new[]
            {
                FileCategory.Documents,
                FileCategory.Images,
                FileCategory.Videos,
                FileCategory.Music,
                FileCategory.Archives,
                FileCategory.System,
                FileCategory.Others
            };
            var categoryLabels = new Dictionary<FileCategory, string>
            {
                [FileCategory.Documents] = "Documentos",
                [FileCategory.Images] = "Imagens",
                [FileCategory.Videos] = "Vídeos",
                [FileCategory.Music] = "Áudio",
                [FileCategory.Archives] = "Compactados",
                [FileCategory.System] = "Programas e bibliotecas",
                [FileCategory.Others] = "Outros tipos"
            };

            foreach (var cat in categoryOrder)
            {
                int n = items.Count(i => !i.IsRecycleFolder && i.Category == cat);
                if (n <= 0) continue;
                options.Add(new FileTypeOption
                {
                    Label = categoryLabels[cat],
                    Key = "__cat__" + (int)cat,
                    Category = cat,
                    Count = n
                });
            }

            // ── Extensões individuais ─────────────────────────────────
            // É o que o usuário pediu de forma literal: "só PDF", "só TXT",
            // "só EXE".
            //
            // SEM filtro de occurrences mínimas.
            //
            // Havia aqui um `.Where(x => x.Count >= 2)` com a justificativa
            // de que "uma extensão com 1 ocorrência vira ruído". A conclusão
            // estava errada, e o custo foi concreto: o usuário apagou um
            // PDF, a varredura achou 1 PDF, e o menu NÃO tinha ".pdf". Ele
            // viu "não tem a opção de procurar por PDF" — e a opção estava
            // sendo omitida por causa de um item. Um menu de filtro existe
            // justamente para o usuário escolher um tipo específico; esconder
            // os tipos raros tira dele o controle sem evitar ruído nenhum,
            // porque o filtro já faz isso sozinho.
            //
            // A ordem por contagem mantém os mais comuns no topo, e o corte
            // em MaxExtensions só descarta o que é raríssimo de verdade.
            var byExt = items
                .Where(i => !i.IsRecycleFolder && !string.IsNullOrEmpty(i.Extension))
                .GroupBy(i => i.Extension, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Ext: g.Key.ToLowerInvariant(), Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Ext)
                .Take(MaxExtensions)
                .ToList();

            int withoutExt = items.Count(i => !i.IsRecycleFolder && string.IsNullOrEmpty(i.Extension));
            if (withoutExt > 0)
            {
                options.Add(new FileTypeOption
                {
                    Label = "Sem extensão",
                    Key = "__noext__",
                    Count = withoutExt
                });
            }

            foreach (var x in byExt)
            {
                options.Add(new FileTypeOption
                {
                    Label = "." + x.Ext,
                    Key = x.Ext,
                    Count = x.Count
                });
            }

            return options;
        }

        /// <summary>
        /// Aplica o tipo escolhido. Separado do filtro textual para que os
        /// dois se combinem: escolher "Documentos" E digitar "relatorio"
        /// tem que dar interseção, não um sobrescrever o outro.
        /// </summary>
        public static bool Matches(NativeRecoveredFile file, FileTypeOption? option)
        {
            // Sem seleção = todos os itens COM conteúdo.
            if (option == null || (option.Key.Length == 0 && !option.IsFolder))
                return !file.ContentGone;

            if (option.IsFolder) return file.IsRecycleFolder && !file.ContentGone;

            // "Só a estrutura": devolve os itens de conteúdo sobrescrito, e
            // também os que têm conteúdo — o usuário está procurando o
            // arquivo que sumiu, não uma lista de vazios.
            if (option.ShowContentGone) return true;

            // Item de conteúdo sobrescrito só aparece neste filtro. Fora
            // dele é invisível — 92 mil vazios afogariam a lista.
            if (file.ContentGone) return false;

            if (option.Category.HasValue)
                return file.Category == option.Category.Value;

            if (option.Key == "__noext__")
                return string.IsNullOrEmpty(file.Extension);

            if (option.Key.StartsWith("__cat__", StringComparison.Ordinal))
                return false;

            return string.Equals(file.Extension, option.Key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
