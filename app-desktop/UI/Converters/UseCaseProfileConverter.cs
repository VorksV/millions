using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.UI.Helpers;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte o token estável de "uso" do questionário (que é um texto em
    /// português, por desenho) no <see cref="IntelligentProfileType"/> real.
    ///
    /// Existe porque a página de Perfil Inteligente precisa dos MESMOS ícones e
    /// cores do modal aberto pelo Dashboard, e o modal trabalha com o enum. Sem
    /// esta ponte seria preciso duplicar os dez path data — que é exatamente o
    /// tipo de duplicação que faz os dois pontos de entrada divergirem.
    ///
    /// Os tokens são os mesmos que o <c>StringToBoolConverter</c> dos
    /// RadioButtons grava em <c>UseCase</c>, então a seleção continua estável
    /// em qualquer idioma (o texto visível é traduzido, o token não).
    /// </summary>
    public static class UseCaseProfileMap
    {
        public const string GamerCompetitive = "Gamer Competitivo";

        /// <summary>
        /// [FIX:GAMER-VARIANTS] As quatro variantes gamer.
        ///
        /// BUG ORIGINAL: estes quatro perfis existem no enum
        /// <see cref="IntelligentProfileType"/>, TÊM ícone
        /// (<c>ProfileIconConverter</c>), TÊM cor, descrição e tradução
        /// (<c>IntelligentProfileCatalog</c>) — e eram inalcançáveis pelo
        /// questionário. O token "Gamer Competitivo" era o único mapeado, de
        /// modo que <see cref="GameClassificationService"/> conseguia SELECIONAR
        /// esses perfis automaticamente, mas o usuário não podia ESCOLHER nenhum
        /// deles: 4 dos 10 perfis do sistema eram inalcançáveis na mão.
        ///
        /// Os tokens são estáveis em português por desenho — é o mesmo contrato
        /// dos outros seis, e é o que o <c>StringToBoolConverter</c> grava em
        /// <c>UseCase</c>.
        /// </summary>
        public const string GamerSinglePlayer = "Gamer Single Player";
        public const string GamerSimulation = "Gamer Simulação";
        public const string GamerMMO = "Gamer MMO / RPG";
        public const string GamerStrategy = "Gamer Estratégia";

        /// <summary>
        /// [FIX:GAMER-VARIANTS] As cinco variantes gamer, na ordem em que a
        /// sub-escolha as exibe. Vem para uso direto do XAML (via
        /// <c>ItemsControl</c>), e é a MESMA lista que o ViewModel usa para
        /// decidir se a sub-escolha deve aparecer — uma lista só, para as duas
        /// decisões não poderem divergir.
        /// </summary>
        public static IReadOnlyList<string> GamerVariantTokens { get; } = new[]
        {
            GamerCompetitive,
            GamerSinglePlayer,
            GamerSimulation,
            GamerMMO,
            GamerStrategy
        };

        public const string WorkOffice = "Trabalho";
        public const string CreativeVideoEditing = "Criador de Conteúdo";
        public const string DeveloperProgramming = "Desenvolvimento / Code";
        public const string EnterpriseSecure = "Corporativo / Segurança";
        public const string GeneralBalanced = "Uso Familiar Casual";

        public static IntelligentProfileType ToProfile(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return IntelligentProfileType.GeneralBalanced;
            }

            // A ordem importa: as variantes gamer são testadas ANTES do
            // genérico "Gamer Competitivo", porque o teste é por igualdade
            // exata e não haveria colisão — mas deixar o genérico por último
            // torna a intenção inequívoca e evita que uma variante nova caia no
            // perfil errado por semelhança de nome.
            if (token == GamerSinglePlayer) return IntelligentProfileType.GamerSinglePlayer;
            if (token == GamerSimulation) return IntelligentProfileType.GamerSimulation;
            if (token == GamerMMO) return IntelligentProfileType.GamerMMO;
            if (token == GamerStrategy) return IntelligentProfileType.GamerStrategy;
            if (token == GamerCompetitive) return IntelligentProfileType.GamerCompetitive;
            if (token == WorkOffice) return IntelligentProfileType.WorkOffice;
            if (token == CreativeVideoEditing) return IntelligentProfileType.CreativeVideoEditing;
            if (token == DeveloperProgramming) return IntelligentProfileType.DeveloperProgramming;
            if (token == EnterpriseSecure) return IntelligentProfileType.EnterpriseSecure;
            return IntelligentProfileType.GeneralBalanced;
        }
    }

    /// <summary>
    /// [FIX:GAMER-VARIANTS] Estado marcado/desmarcado do card "Gamer Competitivo"
    /// para QUALQUER uma das cinco variantes gamer.
    ///
    /// BUG ORIGINAL
    /// ===========
    /// O card principal era marcado por igualdade exata de token
    /// (<c>StringToBoolConverter</c> com <c>ConverterParameter='Gamer Competitivo'</c>).
    /// Isso funcionava porque havia UM token gamer. Ao adicionar a sub-escolha,
    /// o binding continuou sendo reavaliado a cada mudança de <c>UseCase</c> — e
    /// escolher "Gamer Simulação" fazia o card principal virar para desmarcado,
    /// enquanto a sub-escolha mostrava a variante ligada.
    ///
    /// O resultado na tela era incoerente: o usuário tinha escolhido gamer, o
    /// card gamer estava apagado, e a única coisa marcada na página era um item
    /// da linha de sub-escolha. A sub-escolha depende do card estar marcado
    /// (<c>DataTrigger</c> em <c>IsGamerFamilySelected</c>), então o arranjo
    /// também era frágil por dentro: a linha some quando o grupo desmarca.
    ///
    /// POR QUE UM CONVERTER, E NÃO MAIS UM TOKEN SOLTO
    /// ==============================================
    /// A pergunta do card principal não é "o token é este?", e sim "o token
    /// pertence à família gamer?". A resposta é SEMÂNTICA: ela é decidida pelo
    /// mapeamento, não por uma lista escrita à mão no XAML. Por isso o conversor
    /// pergunta ao <see cref="UseCaseProfileMap"/> se o token mapeia para um
    /// perfil gamer — a mesma autoridade que decide qual perfil será salvo, e
    /// que a sub-escolha usa para montar a lista.
    ///
    /// Consequência prática: uma variante gamer nova que seja acrescentada ao
    /// enum e ao mapa passa a marcar o card principal automaticamente, sem tocar
    /// no XAML. As três decisões — card marcado, linha visível e perfil salvo —
    /// não podem divergir, porque leem a mesma fonte.
    ///
    /// O <c>ConvertBack</c> devolve o token GENÉRICO: clicar no card principal
    /// escolhe "Gamer Competitivo", que é o comportamento de sempre e o estado
    /// mais seguro (não sobrescreve uma variante que o usuário já tinha
    /// escolhido ao voltar para o card).
    /// </summary>
    public sealed class GamerFamilyBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
            {
                return false;
            }

            var profile = UseCaseProfileMap.ToProfile(value.ToString());

            foreach (var gamerToken in UseCaseProfileMap.GamerVariantTokens)
            {
                if (UseCaseProfileMap.ToProfile(gamerToken) == profile)
                {
                    return true;
                }
            }

            return false;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is true
                ? UseCaseProfileMap.GamerCompetitive
                : Binding.DoNothing;
        }
    }

    /// <summary>
    /// Token de uso -> nome exibido, nos 3 idiomas.
    ///
    /// [FIX:GAMER-VARIANTS] POR QUE UM CONVERTER, E NÃO LÓGICA NO VIEWMODEL
    /// ==========================================================
    /// A sub-escolha gamer mostra o nome do perfil dentro de um
    /// <c>DataTemplate</c>, cujo <c>DataContext</c> é o TOKEN (uma string), e
    /// não o ViewModel. Reaching de volta ao ViewModel por
    /// <c>AncestorType=UserControl</c> funciona para a escrita de
    /// <c>UseCase</c>, mas o texto precisaria do mesmo caminho — e aí o XAML
    /// carregaria uma dependência do catálogo dentro de um template.
    ///
    /// O conversor resolve no ponto certo: o item da sub-escolha sabe seu
    /// próprio token, e o token é o mesmo contrato do resto da página. Sem
    /// duplicar nada, e sem que o template dependa do ViewModel.
    ///
    /// A chave vem do <c>IntelligentProfileCatalog</c> — a MESMA fonte que
    /// alimenta o modal do Dashboard. Antes havia um switch de 6 casos aqui no
    /// ViewModel: a quarta cópia do mesmo mapeamento, e a razão pela qual as
    /// quatro variantes gamer não apareciam.
    /// </summary>
    public class ProfileDisplayNameConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var token = value as string;
            if (string.IsNullOrEmpty(token))
            {
                return string.Empty;
            }

            try
            {
                var profile = UseCaseProfileMap.ToProfile(token);
                var meta = VoltrisOptimizer.UI.Helpers.IntelligentProfileCatalog.GetProfileMeta(profile);
                return VoltrisOptimizer.Services.LocalizationService.Instance.GetString(meta.NameKey);
            }
            catch
            {
                // Sem nome é melhor do que a sub-escolha inteira quebrar.
                return token;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Token de uso -> ícone vetorial (mesmo do modal).</summary>
    public class UseCaseIconConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var profile = UseCaseProfileMap.ToProfile(value as string);
            return ProfileIconConverter.GetGeometryFor(profile);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Token de uso -> brush de destaque do perfil.</summary>
    public class UseCaseAccentConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var profile = UseCaseProfileMap.ToProfile(value as string);
            return IntelligentProfileCatalog.BrushFromHex(IntelligentProfileCatalog.GetProfileMeta(profile).Accent);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
