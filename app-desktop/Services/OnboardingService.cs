using System;
using System.IO;
using System.Text.Json;
using VoltrisOptimizer.Helpers;

namespace VoltrisOptimizer.Services
{
    public class OnboardingService
    {
        private readonly string _onboardingPath;

        public OnboardingService()
        {
            _onboardingPath = AppDataPaths.GetPath("Onboarding/state.json");
            AppDataPaths.EnsureDirectory(Path.GetDirectoryName(_onboardingPath));
        }

        public bool HasCompletedOnboarding()
        {
            try
            {
                if (!File.Exists(_onboardingPath))
                {
                    return false;
                }

                var json = File.ReadAllText(_onboardingPath);
                var data = JsonSerializer.Deserialize<OnboardingData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                return data?.Completed ?? false;
            }
            catch
            {
                return false;
            }
        }

        public void MarkOnboardingComplete()
        {
            try
            {
                var data = new OnboardingData
                {
                    Completed = true,
                    CompletedDate = DateTime.Now
                };

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                File.WriteAllText(_onboardingPath, json);
            }
            catch
            {
                // Ignorar erros de salvamento
            }
        }

        public OnboardingData? GetOnboardingData()
        {
            try
            {
                if (File.Exists(_onboardingPath))
                {
                    var json = File.ReadAllText(_onboardingPath);
                    return JsonSerializer.Deserialize<OnboardingData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
            }
            catch { }
            return null;
        }

        public void ResetOnboarding()
        {
            try
            {
                if (File.Exists(_onboardingPath))
                {
                    File.Delete(_onboardingPath);
                }
            }
            catch { }
        }
    }

    public class OnboardingData
    {
        public bool Completed { get; set; }
        public DateTime? CompletedDate { get; set; }
    }
}

