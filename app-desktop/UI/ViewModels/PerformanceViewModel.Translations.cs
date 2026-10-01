using System;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.ViewModels
{
    public partial class PerformanceViewModel
    {
        /// <summary>
        /// Traduz um texto de categoria/descricao do Perfil Inteligente.
        ///
        /// HISTORICO
        /// ---------
        /// Este arquivo mantinha um SEGUNDO dicionario de traducoes, paralelo ao
        /// LocalizationService, com 160 entradas. Duas tabelas com o mesmo
        /// conteudo significa que uma atualizacao feita em uma nao chega a
        /// outra — e e exatamente por isso que parte das descrições ficava
        /// presa em um idioma so.
        ///
        /// As 160 entradas foram consolidadas no LocalizationService.cs e este
        /// metodo agora delega para ele. Uma unica fonte de verdade.
        ///
        /// COMPATIBILIDADE
        /// ---------------
        /// A assinatura e o comportamento sao identicos aos anteriores:
        ///   - texto vazio devolve texto vazio;
        ///   - a chave e o texto em portugues com espacamentos removidos;
        ///   - texto sem traducao devolve o proprio texto original.
        /// Nenhum chamador precisou ser alterado.
        /// </summary>
        public static string TranslateText(string portugueseText, Language currentLanguage)
        {
            if (string.IsNullOrEmpty(portugueseText)) return portugueseText;

            var key = portugueseText.Trim();
            string localized = LocalizationService.Instance.GetString(key);
            return string.Equals(localized, key, StringComparison.Ordinal) ? portugueseText : localized;
        }
    }
}
