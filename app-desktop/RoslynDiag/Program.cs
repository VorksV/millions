using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("uso: RoslynDiag <caminho.cs>"); return 2; }
        string path = args[0];
        if (!File.Exists(path)) { Console.Error.WriteLine("arquivo nao existe: " + path); return 3; }

        byte[] bytes = File.ReadAllBytes(path);
        string text;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            text = new UTF8Encoding(true).GetString(bytes, 3, bytes.Length - 3);
        else
            text = Encoding.UTF8.GetString(bytesTakeUtf8(bytes));

        return Analyze(path, text);
    }

    static byte[] bytesTakeUtf8(byte[] b)
    {
        try { return Encoding.Convert(Encoding.UTF8, Encoding.UTF8, b); }
        catch { return b; }
    }

    static int Analyze(string path, string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetRoot();
        var diags = root.GetDiagnostics().OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line).ToArray();

        var lineMap = tree.GetText().Lines;
        int errores = diags.Count(d => d.Severity == DiagnosticSeverity.Error);
        Console.WriteLine("ARQUIVO=" + path);
        Console.WriteLine("LINHAS_TEXTO=" + lineMap.Count);
        Console.WriteLine("DIAGNOSTICO ERROS=" + errores);

        foreach (var d in diags.Where(d => d.Severity == DiagnosticSeverity.Error).Take(40))
        {
            var sp = d.Location.GetLineSpan().StartLinePosition;
            Console.WriteLine(string.Format("  ERRO linha {0} col {1}: {2}", sp.Line + 1, sp.Character + 1, d.Id + " " + d.GetMessage()));
            int from = Math.Max(0, sp.Line - 6);
            int to = Math.Min(lineMap.Count - 1, sp.Line + 8);
            for (int i = from; i <= to; i++)
            {
                string linha = lineMap[i].ToString();
                string seta = "";
                if (i == sp.Line) seta = "   <<<<<";
                Console.WriteLine(string.Format("      {0,4}: {1}{2}", i + 1, linha.Replace("\t", "    "), seta));
            }
            Console.WriteLine("      ---");
        }
        return errores == 0 ? 0 : 1;
    }
}
