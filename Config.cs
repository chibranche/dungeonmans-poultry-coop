using DungeonmansModKit;

namespace DungeonmansPoultry;

// Per-feature on/off switches, shown in ModKit's Mod Settings menu and saved in Mods\Poultry\config.json.
// Every patch checks its switch each time it runs, so toggling takes effect immediately.
internal static class Config
{
	public sealed class Feature
	{
		public string Group;
		public string Key;
		public string Label;
		public string Description;
	}

	public const string BreedingFix = "breeding_fix";
	public const string Pantry = "pantry";

	// Everything else (the Coop, the bird window, Hatch shortcuts) is always on while the mod is on.
	public static readonly Feature[] Features =
	{
		F("Poultry", BreedingFix, "Bug fix: breeding genes bug", "Vanilla overwrites the first parent's genes with the egg's. Fixed: both parents keep theirs."),
		F("Poultry", Pantry, "Pantry (disables leaderboards)", "Academy storage for steaks, Plumage and poultices, and the Feathers shop. While on, no score is sent to the Steam leaderboards."),
	};

	private static Feature F(string group, string key, string label, string description) =>
		new Feature { Group = group, Key = key, Label = label, Description = description };

	private static ModSettings settings;

	// Called once by PoultryMod.Load: every feature becomes a switch in ModKit's Mod Settings menu.
	public static void Register(ModSettings s)
	{
		settings = s;
		foreach (Feature f in Features)
		{
			s.AddToggle(f.Key, f.Label, f.Description, group: f.Group);
		}
	}

	public static bool On(string key) => settings?.On(key) ?? true;

	public static void Toggle(string key) => settings?.Toggle(key);
}
