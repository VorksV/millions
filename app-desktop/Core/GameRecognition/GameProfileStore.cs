using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace VoltrisOptimizer.Core.GameRecognition
{
    public class GameProfileStore
    {
        private readonly string _dir;
        private readonly string _file;
        private readonly Dictionary<string, GameProfile> _profiles = new();

        public GameProfileStore()
        {
            var appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _dir = Path.Combine(appdata, "VoltrisOptimizer", "GameProfiles");
            _file = Path.Combine(_dir, "profiles.json");
            Directory.CreateDirectory(_dir);
            Load();
        }

        public GameProfile? Get(string key) => _profiles.TryGetValue(key, out var p) ? p : null;
        public void Put(GameProfile p)
        {
            _profiles[p.GameName] = p;
            Save();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_file))
                {
                    var json = File.ReadAllText(_file);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, GameProfile>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new();
                    foreach (var kv in dict) _profiles[kv.Key] = kv.Value;
                }
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_file, json);
            }
            catch { }
        }
    }

    public class GameProfile
    {
        public string GameName { get; set; } = string.Empty;
        public bool CloseBackgroundApps { get; set; } = true;
        public bool OptimizeCPU { get; set; } = true;
        public bool OptimizeGPU { get; set; } = true;
        public bool ApplyFPSBoost { get; set; } = true;
        public bool ReduceLatency { get; set; } = true;
    }
}

