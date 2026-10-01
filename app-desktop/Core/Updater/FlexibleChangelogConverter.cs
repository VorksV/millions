using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Core.Updater
{
    /// <summary>
    /// [FIX:C-3] Aceita <c>changelog</c> como string OU como array de strings.
    ///
    /// BUG ORIGINAL: <see cref="UpdateInfo.Changelog"/> era declarado como
    /// <c>string</c>, e o servidor devolve um array:
    ///
    ///   JsonException: The JSON value could not be converted to System.String.
    ///   Path: $.changelog | LineNumber: 4
    ///   InvalidOperationException: Cannot get the value of a token type
    ///   'StartArray' as a string.
    ///
    /// Ou seja: o VOLTRIS não conseguia ler a página de notas de versão que ele
    /// mesmo publica. Todo consumidor do changelog (UpdateWindow, que exibe as
    /// notas na tela de atualização) ficava sem nada para mostrar — e o
    /// desserializador inteiro da resposta falhava junto, o que arrastava
    /// LatestVersion e DownloadUrl junto: o check de atualização BREAKAVA
    /// inteiro por causa de um campo de texto.
    ///
    /// Isso só era invisível porque, até o <c>[FIX:C-3]</c>, a exceção ficava
    /// presa no log do VoltrisDiag. Agora aparece no log principal, que é
    /// exatamente como o problema deve ser encontrado.
    ///
    /// O conversor normaliza os dois formatos para uma única string, com
    /// quebra de linha entre itens, porque UpdateWindow.xaml.cs já faz
    /// <c>Changelog.Split('\n')</c> para formatar.
    /// </summary>
    public sealed class FlexibleChangelogConverter : JsonConverter<string>
    {
        /// <summary>
        /// [FIX:C-3] Necessário para que o conversor seja chamado também quando o
        /// valor é <c>null</c>. Sem isto, o System.Text.Json ignora o conversor
        /// para null, e o teste real acusou que <c>changelog: null</c> não
        /// retornava "". Isso importa porque o servidor pode omitir ou anular o
        /// campo, e aí o consumidor que espera string não-nula receberia null.
        /// </summary>
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString() ?? "";

                case JsonTokenType.StartArray:
                    var itens = new List<string>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            var s = reader.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) itens.Add(s.Trim());
                        }
                        else if (reader.TokenType == JsonTokenType.Number)
                        {
                            itens.Add(reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                    return string.Join("\n", itens);

                case JsonTokenType.Null:
                    return "";

                default:
                    // Token inesperado: consome e devolve vazio em vez de
                    // derrubar a desserializacao inteira da resposta.
                    reader.Skip();
                    return "";
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            // Serializa sempre como string — o formato de leitura e o de escrita
            // nao precisam ser simetricos, e string e o formato canonico.
            writer.WriteStringValue(value ?? "");
        }
    }
}
