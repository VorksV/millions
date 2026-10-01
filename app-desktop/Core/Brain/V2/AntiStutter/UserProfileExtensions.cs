namespace VoltrisOptimizer.Core.Brain.V2.AntiStutter;

public static class UserProfileExtensions
{
	public static ProfileConfiguration Config(this UserProfile profile)
	{
		return AntiStutterProfileManager.Get(profile);
	}

	public static string DisplayName(this UserProfile profile)
	{
		return profile.Config().DisplayName;
	}

	public static string Description(this UserProfile profile)
	{
		return profile.Config().Description;
	}
}
