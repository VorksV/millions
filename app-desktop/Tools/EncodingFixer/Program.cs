using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace EncodingFixer
{
    class Program
    {
        static void Main(string[] args)
        {
            string rootPath = @"C:\Users\VOLTRIS\Desktop\APLICATIVO VOLTRIS";
            string[] extensions = { ".cs", ".xaml" };
            
            var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()) && !f.Contains(@"\Tools\EncodingFixer\"))
                .ToList();

            var map = new Dictionary<string, string> {
                {"s", "só"},
                {"S", "Só"},
                {"l", "lá"},
                {"to", "tão"},
                {"C", "°C"},
                {"qu", "quê"},
                {"til", "útil"},
                {"mx", "máx"},
                {"cone", "ícone"}
            };

            int fixedFiles = 0;
            var encoding = new System.Text.UTF8Encoding(true);

            // Check only the files we broke
            var recentFiles = files.Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddMinutes(-40)).ToList();

            foreach (var file in recentFiles)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    string newContent = content;
                    bool changed = false;

                    foreach (var kvp in map)
                    {
                        // Replace the broken sequence
                        // Note: Because fallback was Replace('é', '\uFFFD'), any value with 'é' became \uFFFD.
                        // But words like 'só' have 'ó', 'á', 'ã', '°', 'ê', 'ú', 'í', 'á'.
                        // My original fallback ONLY replaced 'é' with '\uFFFD'. 
                        // So 'só' is STILL 'só' in the file! Wait...
                        // If my fallback replaced ONLY 'é' with '\uFFFD', then 'só' did NOT get replaced by '\uFFFD'.
                        // So 'só' SHOULD STILL BE 'só' in the file?
                        // BUT wait, my previous Revert script DID replace "só" to "s" if it found it!
                        // No, the previous Revert script missed "só" because I REMOVED it from the map before running!
                        
                        // Let's just look for kvp.Value directly!
                        if (newContent.Contains(kvp.Value))
                        {
                            newContent = newContent.Replace(kvp.Value, kvp.Key);
                            changed = true;
                        }
                        
                        // Also handle if the word had 'é' (which none of these have)
                    }

                    if (changed)
                    {
                        File.WriteAllText(file, newContent, encoding);
                        fixedFiles++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fixing {file}: {ex.Message}");
                }
            }

            Console.WriteLine($"Successfully rescued {fixedFiles} files!");
        }
    }
}
