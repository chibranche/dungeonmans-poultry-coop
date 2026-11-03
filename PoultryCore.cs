using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DungeonMans;
using DungeonmansModKit;
using HarmonyLib;

namespace DungeonmansPoultry;

// Game-side logic for the Coop: breeding bug fix, Pantry storage, Feathers, and bird operations
// (feed / ascend / breed / prune / release / store / take / equip / heal). The UI lives in CoopPanel.
// Persistent data (Pantry, Feathers, Pantry size) is stored in the game's own save, as "overworld" map
// script vars, like vanilla's pet flags; they survive hero deaths and "new overworld, keep Academy".
internal static class PoultryCore
{
	public const string Passionate = "passionate_poultry_poultice";
	public const string Pruning = "power_pruning_poultice";
	private static readonly string[] PantryArchetypes = { Passionate, Pruning, "poultry_protection_potion", "bird_rename_card" };

	// ---- tunables ----
	public const int StartUnits = 30;             // Pantry space counts single items, not stacks
	public const int UnitsPerPurchase = 10;
	public const int SpacePriceStep = 25;         // upgrade k costs 25 * k Feathers (25, 50, 75...)
	public const int PoulticeFeatherPrice = 50; // Passionate Poultry Poultice only
	public const int PoulticeSellFeathers = 5;
	// Flat: generation/Pluck-based rewards would let one strong bird mint Feathers through endless breeding.
	public const int ReleaseFeatherReward = 5;
	public static int SpacePrice => SpacePriceStep * ((PantryCapacity - StartUnits) / UnitsPerPurchase + 1);
	public static int ReleaseFeathers(dmBirdEgg egg) => ReleaseFeatherReward;
	public static int RenderFeathers(Food f) => f.IsPlumage ? 10 : (int)Math.Pow(5, f.Tier);

	private static dmGame Game => dmUtilities.theGame;
	private static dmHero Hero => Game.dungeonMans;

	// =====================================================================
	// Breeding bug fix: CombineTwoBirds works directly on the first parent's gene table.
	// =====================================================================
	public static void Register(Harmony h)
	{
		h.Patch(Mod.Target(typeof(dmPetBird), nameof(dmPetBird.CombineTwoBirds)),
			prefix: Mod.Hook(typeof(PoultryCore), nameof(CombinePrefix)),
			postfix: Mod.Hook(typeof(PoultryCore), nameof(CombinePostfix)));
	}

	private static void CombinePrefix(dmBirdEgg birdA, out Dictionary<string, int> __state)
	{
		__state = Config.On(Config.BreedingFix) ? new Dictionary<string, int>(birdA.GetAllGenes()) : null;
	}

	private static void CombinePostfix(dmBirdEgg birdA, Dictionary<string, int> __state)
	{
		if (__state == null)
		{
			return;
		}
		Dictionary<string, int> genes = birdA.GetAllGenes();
		genes.Clear();
		foreach (var kv in __state)
		{
			genes[kv.Key] = kv.Value;
		}
	}

	// =====================================================================
	// Feathers + Pantry (persistent)
	// =====================================================================
	private const string MapName = "overworld";
	// The save variables keep their "qol_" names from when this was part of the QoL mod, so existing Pantries,
	// Feathers and favorites carry over.

	public static int Feathers
	{
		get => Math.Max(0, dmGazetteer.GetMapScriptVar_Int("qol_feathers", MapName));
		set => dmGazetteer.SetMapScriptVar_Int("qol_feathers", Math.Max(0, value), MapName);
	}

	// How many items the Pantry holds. Older versions counted stacks (10, +3 per purchase): each purchase made
	// then becomes a +10 purchase here.
	public static int PantryCapacity
	{
		get
		{
			int v = dmGazetteer.GetMapScriptVar_Int("qol_pantry_units", MapName);
			if (v > 0)
			{
				return v;
			}
			int oldStacks = dmGazetteer.GetMapScriptVar_Int("qol_pantry_slots", MapName);
			return StartUnits + Math.Max(0, (oldStacks - 10) / 3) * UnitsPerPurchase;
		}
		set => dmGazetteer.SetMapScriptVar_Int("qol_pantry_units", value, MapName);
	}

	// Pantry: key -> count. Keys:  s|<monster>|<tier>|<gene>   p|<power>   i|<archetype>
	// Parsed once per change of the saved string (the Coop asks for counts many times a frame). Callers get a copy.
	private static string pantryRaw;
	private static List<KeyValuePair<string, int>> pantryParsed = new List<KeyValuePair<string, int>>();

	public static List<KeyValuePair<string, int>> Pantry
	{
		get
		{
			string raw = dmGazetteer.GetMapScriptVar_String("qol_pantry", MapName) ?? "";
			if (raw != pantryRaw)
			{
				var list = new List<KeyValuePair<string, int>>();
				foreach (string part in raw.Split(';'))
				{
					int eq = part.LastIndexOf('=');
					if (eq > 0 && int.TryParse(part.Substring(eq + 1), out int n) && n > 0)
					{
						list.Add(new KeyValuePair<string, int>(part.Substring(0, eq), n));
					}
				}
				pantryParsed = list;
				pantryRaw = raw;
			}
			return new List<KeyValuePair<string, int>>(pantryParsed);
		}
	}

	private static void SavePantry(List<KeyValuePair<string, int>> list)
	{
		dmGazetteer.SetMapScriptVar_String("qol_pantry",
			string.Join(";", list.Where(kv => kv.Value > 0).Select(kv => kv.Key + "=" + kv.Value)), MapName);
	}

	public static int PantryCount(string key) => Pantry.Where(kv => kv.Key == key).Sum(kv => kv.Value);
	public static int UsedUnits => Pantry.Sum(kv => kv.Value);
	public static int FreeUnits => Math.Max(0, PantryCapacity - UsedUnits);

	private static bool PantryAdd(string key, int n)
	{
		var list = Pantry;
		if (list.Sum(kv => kv.Value) + n > PantryCapacity)
		{
			return false;
		}
		int i = list.FindIndex(kv => kv.Key == key);
		if (i < 0)
		{
			list.Add(new KeyValuePair<string, int>(key, n));
		}
		else
		{
			list[i] = new KeyValuePair<string, int>(key, list[i].Value + n);
		}
		SavePantry(list);
		return true;
	}

	private static bool PantryTake(string key, int n)
	{
		var list = Pantry;
		int i = list.FindIndex(kv => kv.Key == key);
		if (i < 0 || list[i].Value < n)
		{
			return false;
		}
		list[i] = new KeyValuePair<string, int>(key, list[i].Value - n);
		SavePantry(list);
		return true;
	}

	// =====================================================================
	// Food: a steak / feather, from the bags or from the Pantry
	// =====================================================================
	private static readonly FieldInfo SteakMonster = AccessTools.Field(typeof(dmEnemyBirdSteak), "archetypeThatIAmAChunkOf");
	private static readonly FieldInfo SteakLevel = AccessTools.Field(typeof(dmEnemyBirdSteak), "level");
	private static readonly FieldInfo SteakTier = AccessTools.Field(typeof(dmEnemyBirdSteak), "champTier");
	private static readonly FieldInfo SteakGene = AccessTools.Field(typeof(dmEnemyBirdSteak), "powerGene");
	private static readonly FieldInfo SteakDescription = AccessTools.Field(typeof(dmItem), "strSubDescription");
	private static readonly MethodInfo SteakSetName = AccessTools.Method(typeof(dmEnemyBirdSteak), "SetMeatNameAndText");

	public sealed class Food
	{
		public string Key;          // pantry key
		public string Name;
		public int Tvs, Mvr, Tier;  // tough(-)/swift(+), ranged(-)/melee(+), pluck gained
		public string Gene;
		public bool IsPlumage;
		public dmEnemyBirdSteak BagItem; // set when the food is in the hero's bags
		public int Count;
		// Worth feeding on purpose: raises Pluck (rare steaks) or teaches a power for sure (Plumage).
		public bool Useful => Tier > 0 || IsPlumage;
	}

	public static Food FoodFromItem(dmEnemyBirdSteak s)
	{
		bool plume = s is dmPlumageOfPower;
		string gene = s.GetPowerGene();
		string key = plume ? "p|" + gene : "s|" + SteakMonster.GetValue(s) + "|" + SteakTier.GetValue(s) + "|" + (gene ?? "none");
		return new Food
		{
			Key = key, Name = s.GetName(), IsPlumage = plume, Gene = gene, BagItem = s, Count = 1,
			Tvs = s.GetAdjustmentForBirdStat(EBirdStatAxis.tough_vs_swift),
			Mvr = s.GetAdjustmentForBirdStat(EBirdStatAxis.ranged_vs_melee),
			Tier = s.GetChampionPoints(),
		};
	}

	public static Food FoodFromKey(string key, int count)
	{
		string[] p = key.Split('|');
		if (p[0] == "p")
		{
			return new Food { Key = key, IsPlumage = true, Gene = p[1], Count = count, Name = "Plumage of Power: " + PowerName(p[1]) };
		}
		if (p[0] != "s" || !dmPoultryGuide.BirdSteakMenu.TryGetValue(p[1], out SteakDataStruct data))
		{
			return null;
		}
		int tier = int.Parse(p[2]);
		string monster = Game.GetActorArchetype(p[1])?.Name ?? p[1];
		string prefix = tier == 1 ? "Rare " : tier == 2 ? "Aged Rare " : tier >= 3 ? "Hella Aged Rare " : "";
		return new Food
		{
			Key = key, Name = prefix + monster + " Steak", Gene = p[3], Tier = tier, Count = count,
			Tvs = data.toughOrSwift, Mvr = data.meleeOrRanged,
		};
	}

	public static List<Food> BagFoods() =>
		Hero.GetInventoryList().OfType<dmEnemyBirdSteak>().Where(s => !s.bEquipped).Select(FoodFromItem).ToList();

	public static List<Food> PantryFoods() =>
		Pantry.Where(kv => kv.Key.StartsWith("s|") || kv.Key.StartsWith("p|"))
			.Select(kv => FoodFromKey(kv.Key, kv.Value)).Where(f => f != null).ToList();

	public static string PowerName(string arch)
	{
		if (string.IsNullOrEmpty(arch) || arch == "none")
		{
			return "-";
		}
		try
		{
			return Game.ReadUniqueGenerationDataArchetype<dmSpecialPower>(arch)?.strPowerName ?? arch;
		}
		catch
		{
			return arch;
		}
	}

	public static int GeneChancePerGene(string gene) =>
		dmPoultryGuide.BirdGeneticsSpecialChanceData.TryGetValue(gene, out int v) ? v : 20;

	// Chance (0-100) that eating one more of this gene teaches it, mirroring dmPetBird.AbsorbPowerGene.
	public static int LearnChance(BirdStatInfo info, string gene)
	{
		if (string.IsNullOrEmpty(gene) || gene == "none" || info.sealedPowers.Contains(gene) || info.powersUnlockedArchetypes.Contains(gene)
			|| info.powersUnlockedArchetypes.Count >= 4)
		{
			return 0;
		}
		info.powerGenesCollected.TryGetValue(gene, out int g);
		return Math.Min(100, (g + 1) * GeneChancePerGene(gene));
	}

	// =====================================================================
	// Birds
	// =====================================================================
	private static readonly FieldInfo PlacedBird = AccessTools.Field(typeof(dmBirdEgg), "birdPlacedInWorld");

	// The bird out in the world for this egg. The game's own lookup (what feeding uses): the egg's
	// birdPlacedInWorld field still points at the bird after it dies, until the egg is unequipped.
	public static dmPetBird Placed(dmBirdEgg egg) => IsEquipped(egg) ? Hero.GetActivePet() : null;

	public static List<dmBirdEgg> BagBirds() => Hero.GetInventoryList().OfType<dmBirdEgg>().ToList();

	public static List<dmBirdEgg> AllBirds() => BagBirds().Concat(dmGazetteer.eggsStoredAtAcademy).Distinct().ToList();

	public static bool InBag(dmBirdEgg e) => Hero.GetInventoryList().Contains(e);
	public static bool IsEquipped(dmBirdEgg e) => Hero.GetItemForSlot(ESlot.ES_BirdEggPocket) == e;

	// Looked up once per frame: the Coop asks many times a frame.
	private static int hatchFrame = -1;
	private static NPCAcademyHatch hatch;

	public static NPCAcademyHatch Hatch
	{
		get
		{
			if (hatchFrame != Ui.Frame)
			{
				hatchFrame = Ui.Frame;
				hatch = Game.allActors.OfType<NPCAcademyHatch>().FirstOrDefault();
			}
			return hatch;
		}
	}

	public static bool AtAcademy => Hatch != null;

	// The Pantry can be used: switched on, and you are at the Academy where it is kept.
	public static bool PantryOpen => Config.On(Config.Pantry) && AtAcademy;

	// While the Pantry is on, no score goes to the Steam leaderboards (stored poultices and steaks are an edge).
	public static void RegisterLeaderboardBlock(Harmony h)
	{
		HarmonyMethod block = Mod.Hook(typeof(PoultryCore), nameof(UploadPrefix));
		h.Patch(Patching.Method(typeof(SteamLeaderboards), nameof(SteamLeaderboards.UploadScore), new[] { typeof(Leaderboard), typeof(int) }), prefix: block);
		h.Patch(Patching.Method(typeof(SteamLeaderboards), nameof(SteamLeaderboards.UploadScore), new[] { typeof(Leaderboard), typeof(int), typeof(int[]) }), prefix: block);
	}

	private static bool UploadPrefix() => !Config.On(Config.Pantry);

	private static void RefreshAcademy()
	{
		Hatch?.UpdateWanderingPoultryList(Game.theCurrentLevel as dmDungeonLevel);
	}

	private static void Resync(dmBirdEgg egg)
	{
		dmPetBird placed = Placed(egg);
		if (placed != null)
		{
			egg.FillBirdStatsFromInfo(placed);
		}
	}

	public static string Store(dmBirdEgg egg)
	{
		if (!InBag(egg))
		{
			return null;
		}
		if (IsEquipped(egg))
		{
			if (Hero.CanRemoveItemFromSlot(ESlot.ES_BirdEggPocket) != RemoveItemResult.CanRemove)
			{
				return egg.BirdName + " can't leave its pocket right now.";
			}
			Hero.RemoveItemFromSlot(ESlot.ES_BirdEggPocket, Game);
			if (IsEquipped(egg))
			{
				return "Could not unequip " + egg.BirdName + ".";
			}
		}
		Hero.RemoveItemFromInventory(egg, bAndDestroy: false);
		dmGazetteer.eggsStoredAtAcademy.Add(egg);
		RefreshAcademy();
		return egg.BirdName + " now lives at the Academy.";
	}

	public static string Take(dmBirdEgg egg)
	{
		if (!dmGazetteer.eggsStoredAtAcademy.Contains(egg))
		{
			return null;
		}
		dmGazetteer.eggsStoredAtAcademy.Remove(egg);
		Hero.GiveItemToCharacter(egg);
		RefreshAcademy();
		return egg.BirdName + " joins your bags.";
	}

	public static string Equip(dmBirdEgg egg)
	{
		if (!InBag(egg))
		{
			Take(egg);
		}
		bool ok = Hero.EquipItemInSlot(egg, ESlot.ES_BirdEggPocket);
		return ok ? egg.BirdName + " is equipped. Blow the Battlebird Whistle to call it." : "Could not equip " + egg.BirdName + ".";
	}

	// ---- summon / unsummon: what the Battlebird Whistle does, without needing one ----
	public static bool IsOut(dmBirdEgg egg) => IsEquipped(egg) && Placed(egg) != null;

	public static string Summon(dmBirdEgg egg)
	{
		if (!IsEquipped(egg))
		{
			string r = Equip(egg);
			if (!IsEquipped(egg))
			{
				return r;
			}
		}
		if (!(Game.theCurrentLevel is dmDungeonLevel level))
		{
			return "Birds can't be called here.";
		}
		if (Hero.GetScriptVar_String("bird_banished") == "true")
		{
			return "Your bird is banished for now and can't be called.";
		}
		if (Hero.IsPetBirdActive())
		{
			return null;
		}
		LocValue loc = level.FindClearSpaceAdjacentTo(Hero.Location, false, false);
		if (!loc.IsValid())
		{
			return "No room next to you for " + egg.BirdName + ".";
		}
		new dmSpecialPower().dmscript_summon_pet_bird(Hero, Hero.Location, loc);
		return null; // the game logs "<name> flaps into battle!" or shows why not
	}

	public static string Unsummon(dmBirdEgg egg)
	{
		if (!IsOut(egg))
		{
			return null;
		}
		dmItemScripts.bird_egg_unequipped(Hero, egg, ESlot.ES_BirdEggPocket);
		dmAudio.PlayFX("poultry_chill");
		return egg.BirdName + " is back in its egg.";
	}

	public static string HealAll()
	{
		int n = 0;
		foreach (dmBirdEgg e in AllBirds().Where(e => e.GetStatInfo().bWounded))
		{
			e.GetStatInfo().bWounded = false;
			n++;
		}
		return n == 0 ? "No wounded birds." : "Healed " + n + " wounded bird" + (n > 1 ? "s" : "") + ".";
	}

	// ---- poultices: Pantry first (only at the Academy, where it is), then bags ----
	private static int PantryPoultices(string archetype) => PantryOpen ? PantryCount("i|" + archetype) : 0;

	public static int Count(string archetype) => PantryPoultices(archetype) + Hero.GetNumberOfThisItem(archetype);

	private static bool Consume(string archetype, int n)
	{
		if (Count(archetype) < n)
		{
			return false;
		}
		int fromPantry = Math.Min(n, PantryPoultices(archetype));
		if (fromPantry > 0)
		{
			PantryTake("i|" + archetype, fromPantry);
		}
		for (int i = fromPantry; i < n; i++)
		{
			Hero.RemoveItemFromInventory(archetype, bAndDestroy: true);
		}
		return true;
	}

	// ---- feeding (works on birds at the Academy too) ----
	public static string Feed(dmBirdEgg egg, Food food)
	{
		dmEnemyBirdSteak item = food.BagItem;
		bool fromPantry = item == null;
		if (fromPantry)
		{
			if (!PantryOpen || PantryCount(food.Key) < 1)
			{
				return "That food is gone.";
			}
			item = SpawnFood(food.Key);
			if (item == null)
			{
				return "That food is gone.";
			}
			PantryTake(food.Key, 1);
		}
		else
		{
			Hero.RemoveItemFromInventory(item, bAndDestroy: true);
		}
		BirdStatInfo before = Snapshot(egg.GetStatInfo());
		dmPetBird placed = Placed(egg);
		if (placed != null)
		{
			placed.FeedBird(item);
		}
		else
		{
			ApplyFood(egg.GetStatInfo(), item);
		}
		if (fromPantry)
		{
			item.RemoveMeFromGame(Game); // spawned only to be eaten
		}
		dmAudio.PlayFX("poultry_excite");
		BirdStatInfo after = egg.GetStatInfo();
		string learned = after.powersUnlockedArchetypes.Except(before.powersUnlockedArchetypes).Select(PowerName).FirstOrDefault();
		return egg.BirdName + " ate " + food.Name + "." + (learned != null ? " ^3Learned " + learned + "!^7" : "");
	}

	private static BirdStatInfo Snapshot(BirdStatInfo info)
	{
		var copy = new BirdStatInfo();
		copy.CopyFrom(info);
		return copy;
	}

	// Mirror of dmPetBird.FeedBird + AbsorbPowerGene for a bird that isn't out in the world.
	private static void ApplyFood(BirdStatInfo info, IBirdFood food)
	{
		info.championPointsEarned += food.GetChampionPoints();
		for (int a = 0; a < 2; a++)
		{
			info.birdStats[a] = Math.Max(-5, Math.Min(5, info.birdStats[a] + food.GetAdjustmentForBirdStat((EBirdStatAxis)a)));
		}
		string gene = food.GetPowerGene();
		if (string.IsNullOrEmpty(gene) || info.sealedPowers.Contains(gene))
		{
			return;
		}
		info.powerGenesCollected.TryGetValue(gene, out int g);
		g++;
		info.powerGenesCollected[gene] = g;
		if (info.powersUnlockedArchetypes.Count < 4 && !info.powersUnlockedArchetypes.Contains(gene) && gene != "none"
			&& dmUtilities.RollDice("1d100") <= g * GeneChancePerGene(gene))
		{
			info.powersUnlockedArchetypes.Add(gene);
		}
	}

	private static dmEnemyBirdSteak SpawnFood(string key)
	{
		string[] p = key.Split('|');
		if (p[0] == "p")
		{
			var plume = Game.SpawnItemFromArchetype("plumage_of_power") as dmPlumageOfPower;
			plume?.SetPowerGene(p[1]);
			return plume;
		}
		if (p[0] != "s")
		{
			return null;
		}
		var steak = Game.SpawnItemFromArchetype("enemy_bird_steak") as dmEnemyBirdSteak;
		if (steak == null)
		{
			return null;
		}
		SteakMonster.SetValue(steak, p[1]);
		SteakLevel.SetValue(steak, Hero.iLevel);
		SteakTier.SetValue(steak, int.Parse(p[2]));
		SteakSetName.Invoke(steak, null);
		SteakGene.SetValue(steak, p[3]); // keep the gene this steak rolled when it dropped
		// SetMeatNameAndText described the gene it rolled itself: describe the kept one instead.
		string desc = SteakDescription.GetValue(steak) as string ?? "";
		int at = desc.IndexOf("This steak may teach your pet", StringComparison.Ordinal);
		if (at >= 0)
		{
			desc = desc.Substring(0, at);
		}
		dmSpecialPower power = string.IsNullOrEmpty(p[3]) || p[3] == "none" ? null : Game.ReadUniqueGenerationDataArchetype<dmSpecialPower>(p[3]);
		SteakDescription.SetValue(steak, desc + (power != null ? "This steak may teach your pet ^n^3" + power.strPowerName + "^7." : ""));
		return steak;
	}

	// ---- prune ----
	public static string Prune(dmBirdEgg egg, string power)
	{
		if (!Consume(Pruning, 1))
		{
			return "You need a Power Pruning Poultice.";
		}
		egg.SealPower(power);
		dmAudio.PlayFX("poof_disappear");
		return egg.BirdName + " forgot " + PowerName(power) + " for good.";
	}

	// ---- ascend: +1 generation for N Passionate Poultices, Pluck -= Pluck * 0.5^N ----
	public static int PluckAfterAscend(int pluck, int n) => pluck - (int)Math.Floor(pluck * Math.Pow(0.5, n));

	public static string Ascend(dmBirdEgg egg, int n)
	{
		if (n < 1 || !Consume(Passionate, n))
		{
			return "Not enough Passionate Poultry Poultices.";
		}
		BirdStatInfo info = egg.GetStatInfo();
		int before = info.championPointsEarned;
		info.generation = Math.Max(1, info.generation) + 1;
		info.championPointsEarned = PluckAfterAscend(before, n);
		Resync(egg);
		dmAudio.PlayFX("poultry_excite");
		return egg.BirdName + " ascends to Generation " + info.generation + " (Pluck " + before + " -> " + info.championPointsEarned + ").";
	}

	// ---- breed ----
	public sealed class BreedPreview
	{
		public int Generation, Pluck, Tvs, Mvr;
		public List<(string gene, int count, double chance)> Powers = new List<(string, int, double)>();
	}

	// Exact mirror of CombineTwoBirds' deterministic part, plus the probability of each power being rolled.
	public static BreedPreview Preview(dmBirdEgg a, dmBirdEgg b)
	{
		var p = new BreedPreview
		{
			Generation = Math.Max(a.GetGeneration(), b.GetGeneration()) + (a.GetGeneration() == b.GetGeneration() ? 1 : 0),
			Pluck = (a.GetChampionPoints() + b.GetChampionPoints()) / 2,
			Tvs = (a.GetStatAxis().X + b.GetStatAxis().X) / 2,
			Mvr = (a.GetStatAxis().Y + b.GetStatAxis().Y) / 2,
		};
		var genes = new Dictionary<string, int>(a.GetAllGenes());
		foreach (var kv in b.GetAllGenes())
		{
			genes[kv.Key] = genes.TryGetValue(kv.Key, out int v) ? v + kv.Value : kv.Value;
		}
		genes.Remove("none");
		var ordered = genes.Select(kv => (gene: kv.Key, count: Math.Max(kv.Value / 2, 1))).OrderByDescending(g => g.count).ToList();
		// dist[k] = probability that exactly k powers were picked so far (max 4)
		double[] dist = { 1, 0, 0, 0, 0 };
		foreach (var (gene, count) in ordered)
		{
			double roll = Math.Max(0, Math.Min(99, count * 20 - 1)) / 100.0; // RollDice("1d100") < count*20
			double chance = (1 - dist[4]) * roll;
			p.Powers.Add((gene, count, chance));
			for (int k = 4; k >= 1; k--)
			{
				dist[k] = dist[k] + dist[k - 1] * roll - (k < 4 ? dist[k] * roll : 0);
			}
			dist[0] *= 1 - roll;
		}
		return p;
	}

	public static string Breed(dmBirdEgg a, dmBirdEgg b)
	{
		if (!Consume(Passionate, 1))
		{
			return "You need a Passionate Poultry Poultice.";
		}
		dmBirdEgg egg = dmPetBird.CombineTwoBirds(a, b);
		Hero.GiveItemToCharacter(egg);
		dmAudio.PlayFX("poultry_excite");
		return "A new egg from " + a.BirdName + " and " + b.BirdName + " is in your bags (Generation " + egg.GetGeneration() + "). Equip it and blow the whistle to hatch it.";
	}

	// ---- release: vanilla release (Plumage of Power drops) + Feathers ----
	private static readonly MethodInfo HatchRelease = AccessTools.Method(typeof(NPCAcademyHatch), "OnTrulySetBirdFreeForever");

	public static string Release(dmBirdEgg egg)
	{
		NPCAcademyHatch hatch = Hatch;
		if (hatch == null)
		{
			return "Professor Hatch must be nearby to set a bird free.";
		}
		int feathers = ReleaseFeathers(egg);
		if (InBag(egg) && !IsEquipped(egg))
		{
			Hero.RemoveItemFromInventory(egg, bAndDestroy: false);
		}
		hatch.SetScriptVar_Object_WillNotSerialize("set_free_target", egg);
		HatchRelease.Invoke(hatch, new object[] { 0, null });
		Feathers += feathers;
		return egg.BirdName + " flies free. +" + feathers + " Feathers (and any Plumage of Power it dropped).";
	}

	// =====================================================================
	// Pantry transfers + shop
	// =====================================================================
	public static bool IsPantryItem(dmItem item) =>
		item is dmEnemyBirdSteak || PantryArchetypes.Contains(item.ArchetypeName);

	private static string KeyOf(dmItem item) => item is dmEnemyBirdSteak s ? FoodFromItem(s).Key : "i|" + item.ArchetypeName;

	private static int StackSize(dmItem item) => item.IsBulkItem() ? Math.Max(1, item.GetBulkCount()) : 1;

	public static string Deposit(dmItem item)
	{
		int stack = StackSize(item);
		int n = Math.Min(stack, FreeUnits);
		if (n <= 0 || !PantryAdd(KeyOf(item), n))
		{
			return "The Pantry is full. Buy more space with Feathers.";
		}
		string name = item.GetName();
		Hero.RemoveItemFromInventory(item, bAndDestroy: true, n);
		return "Stored " + name + (n > 1 ? " x" + n : "") + "." + (n < stack ? " ^1Pantry full^7, " + (stack - n) + " stayed in your bags." : "");
	}

	public static string DepositAll()
	{
		int moved = 0;
		bool full = false;
		foreach (dmItem item in Hero.GetInventoryList().Where(i => IsPantryItem(i) && !i.bEquipped && !i.InventoryLocked).ToList())
		{
			int stack = StackSize(item);
			int n = Math.Min(stack, FreeUnits);
			if (n > 0 && PantryAdd(KeyOf(item), n))
			{
				Hero.RemoveItemFromInventory(item, bAndDestroy: true, n);
				moved += n;
			}
			if (n < stack)
			{
				full = true;
			}
		}
		return "Stored " + moved + " item" + (moved == 1 ? "" : "s") + "." + (full ? " ^1Pantry full^7, some items stayed in your bags." : "");
	}

	public static string Withdraw(string key, int n)
	{
		int have = PantryCount(key);
		n = Math.Min(n, have);
		if (n <= 0)
		{
			return null;
		}
		for (int i = 0; i < n; i++)
		{
			dmItem item = key.StartsWith("i|") ? Game.SpawnItemFromArchetype(key.Substring(2)) : SpawnFood(key);
			if (item == null)
			{
				return "Could not rebuild " + key + ".";
			}
			item.bIsIdentified = true;
			Hero.GiveItemToCharacter(item);
		}
		PantryTake(key, n);
		return "Took " + n + " from the Pantry.";
	}

	public static string Render(string key)
	{
		Food f = FoodFromKey(key, 1);
		if (f == null || !PantryTake(key, 1))
		{
			return null;
		}
		int gain = RenderFeathers(f);
		Feathers += gain;
		return "Sold " + f.Name + " for " + gain + " Feathers.";
	}

	public static string BuySpace()
	{
		int price = SpacePrice;
		if (Feathers < price)
		{
			return "Need " + price + " Feathers.";
		}
		Feathers -= price;
		PantryCapacity += UnitsPerPurchase;
		return "The Pantry now holds " + PantryCapacity + " items.";
	}

	public static string BuyPoultice(string archetype)
	{
		if (Feathers < PoulticeFeatherPrice)
		{
			return "Need " + PoulticeFeatherPrice + " Feathers.";
		}
		if (!PantryAdd("i|" + archetype, 1))
		{
			return "The Pantry is full.";
		}
		Feathers -= PoulticeFeatherPrice;
		return "Bought a " + (archetype == Passionate ? "Passionate" : "Power Pruning") + " Poultry Poultice (stored in the Pantry).";
	}

	public static string SellPoultice()
	{
		if (!Consume(Passionate, 1))
		{
			return "No Passionate Poultry Poultice to sell.";
		}
		Feathers += PoulticeSellFeathers;
		return "Sold a Passionate Poultry Poultice for " + PoulticeSellFeathers + " Feathers.";
	}

	public static string ItemName(string key)
	{
		if (key.StartsWith("i|"))
		{
			switch (key.Substring(2))
			{
			case Passionate: return "Passionate Poultry Poultice";
			case Pruning: return "Power Pruning Poultice";
			case "poultry_protection_potion": return "Poultry Protection Potion";
			case "bird_rename_card": return "Bird Rename Card";
			default: return key.Substring(2);
			}
		}
		return FoodFromKey(key, 1)?.Name ?? key;
	}

	// =====================================================================
	// Favorite bird (auto taken + equipped by Prepare hero)
	// =====================================================================
	public static string FavoriteName
	{
		get => dmGazetteer.GetMapScriptVar_String("qol_favorite_bird", MapName);
		set => dmGazetteer.SetMapScriptVar_String("qol_favorite_bird", value ?? "", MapName);
	}

	public static dmBirdEgg Favorite => AllBirds().FirstOrDefault(e => !string.IsNullOrEmpty(FavoriteName) && e.BirdName == FavoriteName);
}
