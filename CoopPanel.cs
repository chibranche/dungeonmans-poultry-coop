using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMans;
using DungeonmansModKit;
using Microsoft.Xna.Framework;

namespace DungeonmansPoultry;

// "The Coop": one window for all poultry management (game logic in PoultryCore). Drawn with the game's own UI
// pieces (parchment window, text boxes, steel buttons, vitals bars, sliders, item sprites, Official Bird Paper)
// on the game's 32-pixel grid.
//   Birds  - bird cards on the left; on the right the selected bird: actions, stat map, powers & genes, feeding.
//   Breed  - pick two parents; exact preview of the egg (generation, Pluck, stats, chance of each power).
//   Ascend - +1 generation for N Passionate Poultices; Pluck after = Pluck - floor(Pluck * 0.5^N).
//   Pantry - Academy storage for steaks, Plumage and poultices; sell steaks for Feathers; Feathers shop.
internal static class CoopPanel
{
	public static bool IsOpen;
	private static int tab;
	private static readonly string[] Tabs = { "Birds", "Breed", "Ascend", "Pantry" };

	private static dmBirdEgg selected;
	private static dmBirdEgg parentA, parentB;
	private static dmBirdEgg releaseConfirm;
	private static int ascendN = 2;
	private static int birdScroll, feedScroll, bagScroll, pantryScroll;
	private static bool feedAll;
	private static int sortMode; // 0 generation, 1 pluck, 2 name
	private static string message = "";
	private static PoultryCore.Food previewFood, previewFoodLast;

	private static dmGame Game => dmUtilities.theGame;
	public static bool AtAcademy => PoultryCore.AtAcademy;

	public static void Open(int startTab = 0, dmBirdEgg focus = null)
	{
		if (Game?.dungeonMans == null)
		{
			return;
		}
		tab = 0;
		SetTab(Math.Clamp(startTab, 0, Tabs.Length - 1)); // the Pantry tab only opens at the Academy
		selected = focus ?? PoultryCore.AllBirds().FirstOrDefault(PoultryCore.IsEquipped) ?? PoultryCore.AllBirds().FirstOrDefault();
		releaseConfirm = null;
		message = AtAcademy ? "" : "Away from the Academy: only your bags.";
		IsOpen = true;
		dmAudio.PlayUIFX("open_dialog");
	}

	public static void ForceClose() => IsOpen = false;

	// Screenshots: the two best hatched birds as parents.
	public static void DevPickParents()
	{
		List<dmBirdEgg> birds = VisibleBirds();
		parentA = birds.ElementAtOrDefault(0);
		parentB = birds.ElementAtOrDefault(1);
	}

	// A new hero or a loaded save: nothing picked before belongs to it.
	public static void Reset()
	{
		IsOpen = false;
		selected = parentA = parentB = releaseConfirm = null;
		message = "";
	}

	private static void Close()
	{
		IsOpen = false;
		dmAudio.PlayUIFX("ui_back_02");
	}

	private static void Say(string s)
	{
		if (!string.IsNullOrEmpty(s))
		{
			message = s;
			Game.AddStringToLog("^3Coop:^7 " + s);
			string plain = Ui.StripCodes(s);
			Ui.Toast(plain.Length > 70 ? plain.Substring(0, 67) + "..." : s);
		}
	}

	private static void Do(Func<string> action)
	{
		try
		{
			Say(action());
		}
		catch (Exception e)
		{
			Log.Write("Coop action failed: " + e);
			message = "^1That failed^7 (see ModKit\\modkit.log).";
		}
	}

	// ---------------- input ----------------

	private static Rectangle feedRect, bagRect, pantryRect;

	public static void HandleInput(dmGame g)
	{
		List<ActionButton> a = g.theInput.ExecutedActions;
		List<dmBirdEgg> birds = VisibleBirds();
		int wheel = a.Contains(ActionButton.MouseWheel_Up) ? -1 : a.Contains(ActionButton.MouseWheel_Down) ? 1 : 0;
		if (a.Contains(ActionButton.Close))
		{
			Close();
		}
		else if (wheel != 0)
		{
			Point m = Ui.Mouse;
			if (feedRect.Contains(m))
			{
				feedScroll += wheel;
			}
			else if (bagRect.Contains(m))
			{
				bagScroll += wheel * 2;
			}
			else if (pantryRect.Contains(m))
			{
				pantryScroll += wheel * 2;
			}
			else
			{
				birdScroll += wheel;
			}
		}
		else if (Ui.KeyFocusActive)
		{
			// Tab / arrows / Enter move and press the focused button (ModKit keyboard navigation).
		}
		else if (a.Contains(ActionButton.Left) || a.Contains(ActionButton.ArrowKey_Left))
		{
			SetTab((tab + ShownTabs - 1) % ShownTabs);
		}
		else if (a.Contains(ActionButton.Right) || a.Contains(ActionButton.ArrowKey_Right))
		{
			SetTab((tab + 1) % ShownTabs);
		}
		else if ((a.Contains(ActionButton.Up) || a.Contains(ActionButton.ArrowKey_Up)) && birds.Count > 0)
		{
			selected = birds[Math.Max(0, birds.IndexOf(selected) - 1)];
		}
		else if ((a.Contains(ActionButton.Down) || a.Contains(ActionButton.ArrowKey_Down)) && birds.Count > 0)
		{
			selected = birds[Math.Min(birds.Count - 1, birds.IndexOf(selected) + 1)];
		}
		a.Clear();
	}

	// The Pantry tab (last) only counts when it can open, so Left/Right wrap over the tabs you can use.
	private static int ShownTabs => PoultryCore.PantryOpen ? Tabs.Length : Tabs.Length - 1;

	private static void SetTab(int t)
	{
		if (t == 3 && !PoultryCore.PantryOpen)
		{
			return;
		}
		tab = t;
		releaseConfirm = null;
	}

	private static List<dmBirdEgg> VisibleBirds()
	{
		IOrderedEnumerable<dmBirdEgg> birds = (AtAcademy ? PoultryCore.AllBirds() : PoultryCore.BagBirds())
			.Where(e => e.BirdHasEverHatched)
			.OrderByDescending(PoultryCore.IsEquipped)
			.ThenByDescending(e => e.BirdName == PoultryCore.FavoriteName);
		birds = sortMode == 1 ? birds.ThenByDescending(e => e.GetChampionPoints()).ThenByDescending(e => e.GetGeneration())
			: sortMode == 2 ? birds.ThenBy(e => e.BirdName)
			: birds.ThenByDescending(e => e.GetGeneration()).ThenByDescending(e => e.GetChampionPoints());
		return birds.ToList();
	}

	// ---------------- item icons ----------------

	private static readonly Dictionary<string, dmSprite> itemSprites = new Dictionary<string, dmSprite>();

	private static readonly Dictionary<string, dmItem> sampleItems = new Dictionary<string, dmItem>();

	// One spawned copy per archetype, for its icon and description.
	private static dmItem Sample(string archetype)
	{
		if (!sampleItems.TryGetValue(archetype, out dmItem it))
		{
			try
			{
				it = Game.SpawnItemFromArchetype(archetype);
			}
			catch
			{
				it = null;
			}
			sampleItems[archetype] = it;
		}
		return it;
	}

	private static dmSprite ArchetypeSprite(string archetype)
	{
		if (!itemSprites.TryGetValue(archetype, out dmSprite sp))
		{
			sp = Sample(archetype)?.sprite ?? default;
			itemSprites[archetype] = sp;
		}
		return sp;
	}

	private static string ItemText(dmItem it)
	{
		if (it == null)
		{
			return "";
		}
		string s = "";
		try
		{
			s = it.GetDescription();
			if (string.IsNullOrEmpty(s))
			{
				s = it.GetFlavorText();
			}
		}
		catch
		{
		}
		return string.IsNullOrEmpty(s) ? "" : Wrap(Ui.StripCodes(s), 54);
	}

	// Hover text for a pantry key or bag item: name, then what it does.
	private static string Describe(string key, dmItem bagItem)
	{
		PoultryCore.Food f = bagItem is dmEnemyBirdSteak st ? PoultryCore.FoodFromItem(st) : key != null && !key.StartsWith("i|") ? PoultryCore.FoodFromKey(key, 1) : null;
		if (f != null)
		{
			string fx = FoodEffects(f, null);
			return "^3" + f.Name + "^7" + (fx.Length > 0 ? "\n" + fx.Replace("    ", "\n") : "");
		}
		dmItem it = bagItem ?? (key != null ? Sample(key.Substring(2)) : null);
		string text = ItemText(it);
		return "^3" + (it?.GetName() ?? PoultryCore.ItemName(key)) + "^7" + (text.Length > 0 ? "\n" + text : "");
	}

	// "Pluck +1    Tough +2    Shields Up 40%" (info = the bird it would be fed to, or null).
	private static string FoodEffects(PoultryCore.Food f, BirdStatInfo info)
	{
		var parts = new List<string>();
		if (f.Tier > 0)
		{
			parts.Add("^3Pluck +" + f.Tier + "^7");
		}
		if (f.Tvs != 0)
		{
			parts.Add(f.Tvs < 0 ? "Tough +" + -f.Tvs : "Swift +" + f.Tvs);
		}
		if (f.Mvr != 0)
		{
			parts.Add(f.Mvr < 0 ? "Ranged +" + -f.Mvr : "Melee +" + f.Mvr);
		}
		if (f.Gene != null && f.Gene != "none")
		{
			if (info == null)
			{
				parts.Add("^2" + PoultryCore.PowerName(f.Gene) + " gene^7");
			}
			else
			{
				bool known = info.powersUnlockedArchetypes.Contains(f.Gene);
				int chance = f.IsPlumage ? 100 : PoultryCore.LearnChance(info, f.Gene);
				parts.Add(known ? PoultryCore.PowerName(f.Gene) + " (known)" : "^2" + PoultryCore.PowerName(f.Gene) + " " + chance + "%^7");
			}
		}
		return string.Join("    ", parts);
	}

	// Pantry key: s|... steak, p|... Plumage, i|archetype item.
	private static dmSprite KeySprite(string key) =>
		key.StartsWith("s|") ? Ui.GameSprite("enemy_bird_steak_sprite")
		: key.StartsWith("p|") ? Ui.GameSprite("plumage_of_power_sprite")
		: ArchetypeSprite(key.Substring(2));

	private static dmSprite FoodSprite(PoultryCore.Food f) =>
		f.BagItem != null ? f.BagItem.sprite : f.IsPlumage ? Ui.GameSprite("plumage_of_power_sprite") : Ui.GameSprite("enemy_bird_steak_sprite");

	// ---------------- drawing ----------------

	private const int WUnits = 38, HUnits = 22;
	private const int ListW = 384, CardH = 64;

	public static void Draw(dmGame g)
	{
		wg2DRenderer tr = g.theRenderer;
		List<dmBirdEgg> visible = VisibleBirds();
		if (selected == null || !visible.Contains(selected))
		{
			selected = visible.FirstOrDefault();
		}
		// A parent set free, or left at the Academy when the Coop is opened elsewhere, can't be bred.
		if (parentA != null && !visible.Contains(parentA))
		{
			parentA = null;
		}
		if (parentB != null && !visible.Contains(parentB))
		{
			parentB = null;
		}
		previewFood = null;
		tr.BeginSpriteBatch();
		tr.bForceDropShadows = true;
		Rectangle c = Ui.ModalWindow("poultry_coop", WUnits, HUnits, "The Coop", Close, AtAcademy ? null : "bags only");

		// The Pantry tab is gone when the Pantry is switched off, and grayed away from the Academy.
		if (tab == 3 && !PoultryCore.PantryOpen)
		{
			tab = 0;
		}
		Ui.Tabs("poultry_coop", c.X, c.Y, Config.On(Config.Pantry) ? Tabs : Tabs.Take(3).ToArray(), tab, SetTab, new[] { true, true, true, AtAcademy },
			tooltips: new[] { null, null, null, AtAcademy ? "Academy storage for steaks, Plumage and poultices" : "Only at the Academy: the Pantry is stored there.\nOpen the Coop in the Academy to store or take items." });
		int cx = c.Right;
		cx = Currency(cx, c.Y + 4, ArchetypeSprite(PoultryCore.Pruning), PoultryCore.Count(PoultryCore.Pruning), "Power Pruning Poultices: make a bird forget a power");
		cx = Currency(cx, c.Y + 4, ArchetypeSprite(PoultryCore.Passionate), PoultryCore.Count(PoultryCore.Passionate), "Passionate Poultry Poultices: breeding and Ascend");
		Currency(cx, c.Y + 4, Ui.GameSprite(FeatherSprite), PoultryCore.Feathers, "Feathers: from freeing birds, selling steaks and poultices");

		var body = new Rectangle(c.X, c.Y + 44, c.Width, c.Height - 44 - 32);
		var right = new Rectangle(body.X + ListW + 16, body.Y, 768, body.Height);
		switch (tab)
		{
		case 0:
			DrawBirdList(body, BirdClick.Select);
			DrawBirdDetails(right);
			break;
		case 1:
			DrawBirdList(body, BirdClick.Parent);
			DrawBreed(right);
			break;
		case 2:
			DrawBirdList(body, BirdClick.Select);
			DrawAscend(right);
			break;
		case 3:
			DrawPantry(body);
			break;
		}

		Ui.Separator(c.X, c.Bottom - 26, c.Width);
		Ui.Text(string.IsNullOrEmpty(message) ? "^3Left/Right^7 tabs   ^3Up/Down^7 birds   ^3Wheel^7 scroll   ^3Esc^7 close" : message,
			c.X + 4, c.Bottom - 20, Palette.TextDim);
		tr.EndSpriteBatch();
		previewFoodLast = previewFood;
	}

	private const string FeatherSprite = "feather_gibs_sprite";

	// Icon + amount, right-aligned at 'right'. Returns the x for the next one.
	private static int Currency(int right, int y, dmSprite icon, int value, string tip)
	{
		string text = "^3" + value;
		int w = 32 + (int)Ui.Measure(text, UiFont.Medium).X;
		int x = right - w;
		ui_mouseFrame f = Ui.Area("poultry_coop_cur_" + tip.Substring(0, 6), new Rectangle(x, y, w, 28), null, Ui.WindowLevel + 1);
		Ui.Sprite(icon, new Rectangle(x, y, 28, 28));
		Ui.Text(text, x + 32, y + 2, Palette.Text, UiFont.Medium);
		if (Ui.Hovered(f))
		{
			Ui.Tooltip(tip);
		}
		return x - 20;
	}

	// ---------- bird list (left column) ----------

	private enum BirdClick { Select, Parent }

	private static void DrawBirdList(Rectangle body, BirdClick mode)
	{
		List<dmBirdEgg> birds = VisibleBirds();
		int x = body.X, y = body.Y;
		string[] sorts = { "Gen", "Pluck", "Name" };
		for (int i = 0; i < sorts.Length; i++)
		{
			int s = i;
			Ui.BoxButton("poultry_coop_sort" + i, new Rectangle(x + i * 68, y, 64, 32), sorts[i], () => sortMode = s, selected: sortMode == i,
				tooltipText: "Sort by " + sorts[i].ToLowerInvariant());
		}
		Ui.TextRight(birds.Count + " bird" + (birds.Count == 1 ? "" : "s"), x + ListW, y + 6, Palette.TextDim);

		var list = new Rectangle(x, y + 40, ListW, body.Height - 40);
		int rows = list.Height / CardH;
		birdScroll = Math.Max(0, Math.Min(Math.Max(0, birds.Count - rows), birdScroll));
		for (int vi = 0; vi < rows && birdScroll + vi < birds.Count; vi++)
		{
			DrawBirdCard(birds[birdScroll + vi], new Rectangle(x, list.Y + vi * CardH, ListW, CardH), mode, vi);
		}
		if (birds.Count == 0)
		{
			Ui.TextCentered("No birds here.", list, Palette.TextDim, UiFont.Medium);
		}
		if (birds.Count > rows)
		{
			Ui.TextRight((birdScroll + 1) + "-" + Math.Min(birds.Count, birdScroll + rows) + " of " + birds.Count + "  (wheel)", x + ListW, list.Y + rows * CardH + 2, Palette.TextMuted);
		}
	}

	private static void DrawBirdCard(dmBirdEgg e, Rectangle r, BirdClick mode, int slot)
	{
		bool isSel = mode == BirdClick.Select ? e == selected : e == parentA || e == parentB;
		ui_mouseFrame f = Ui.Area("poultry_coop_card" + slot, r, () => ClickBird(e, mode), Ui.WindowLevel + 1);
		bool hot = Ui.Hovered(f);
		Ui.Box(r, isSel || hot);
		Ui.Sprite(e.GetBirdSprite(), new Rectangle(r.X + 10, r.Y + 8, 40, 48), e.BirdHasEverHatched ? (Color?)null : new Color(120, 120, 120));

		int tx = r.X + 60;
		if (e.BirdName == PoultryCore.FavoriteName)
		{
			Ui.Icon("ui_favorite_heart", new Rectangle(tx, r.Y + 9, 18, 18));
			tx += 22;
		}
		Ui.Text(Ui.Fit(e.BirdName, r.Right - 70 - tx, UiFont.Medium), tx, r.Y + 6, isSel ? Palette.Accent : Palette.Text, UiFont.Medium);
		string where = PoultryCore.IsEquipped(e) ? "^5Equipped" : PoultryCore.InBag(e) ? "^7Bag" : "^7Academy";
		string extra = (e.GetStatInfo().bWounded ? "   ^1Hurt" : "") + (!e.BirdHasEverHatched ? "   ^7Egg" : "")
			+ (mode == BirdClick.Parent && e == parentA ? "   ^3P1" : mode == BirdClick.Parent && e == parentB ? "   ^3P2" : "");
		Ui.Text("Gen ^3" + e.GetGeneration() + "^7  Pluck ^3" + e.GetChampionPoints() + "^7   " + where + "^7" + extra, r.X + 60, r.Y + 34, Palette.TextDim);

		Point s = e.GetStatAxis();
		StatMap(new Rectangle(r.Right - 56, r.Y + 10, 44, 44), new[] { (s.X, s.Y, Color.White) }, mini: true);
	}

	private static void ClickBird(dmBirdEgg e, BirdClick mode)
	{
		releaseConfirm = null;
		if (mode == BirdClick.Select)
		{
			selected = e;
			return;
		}
		if (e == parentA)
		{
			parentA = null;
		}
		else if (e == parentB)
		{
			parentB = null;
		}
		else if (parentA == null)
		{
			parentA = e;
		}
		else
		{
			parentB = e;
		}
	}

	// ---------- shared pieces ----------

	private static readonly (string power, string name, int x0, int x1, int y0, int y1)[] StatZones =
	{
		("sp_bird_shields_up", "Shields Up", -5, -3, -5, -3),
		("sp_ai_bird_stick_and_move", "Stick and Move", 3, 5, -5, -3),
		("sp_bird_intercept", "Intercept", -5, -3, 3, 5),
		("sp_bird_dashing_charge", "Dashing Charge", 3, 5, 3, 5),
	};

	// 11x11 map of the two stat axes: x = Tough(-5) .. Swift(+5), y = Ranged(-5, top) .. Melee(+5, bottom).
	// Gold corners: stat powers. Red band (Melee 3+): Press the Attack.
	private static void StatMap(Rectangle r, IEnumerable<(int x, int y, Color c)> dots, bool mini = false, (int x, int y)? ghost = null)
	{
		Ui.Fill(r, new Color(14, 10, 8, 230));
		float cw = r.Width / 11f, ch = r.Height / 11f;
		Rectangle Cell(int sx, int sy) => new Rectangle(r.X + (int)((sx + 5) * cw), r.Y + (int)((sy + 5) * ch), (int)Math.Ceiling(cw), (int)Math.Ceiling(ch));
		for (int sx = -5; sx <= 5; sx++)
		{
			for (int sy = -5; sy <= 5; sy++)
			{
				bool zone = StatZones.Any(z => sx >= z.x0 && sx <= z.x1 && sy >= z.y0 && sy <= z.y1);
				Color c = zone ? new Color(220, 170, 70, 110) : sy >= 3 ? new Color(200, 80, 60, 80) : (sx + sy) % 2 == 0 ? new Color(70, 56, 42, 90) : new Color(50, 40, 30, 90);
				Rectangle cell = Cell(sx, sy);
				Ui.Fill(mini ? cell : new Rectangle(cell.X, cell.Y, cell.Width - 1, cell.Height - 1), c);
			}
		}
		Ui.Fill(new Rectangle(r.X + r.Width / 2, r.Y, 1, r.Height), new Color(255, 240, 210, 70));
		Ui.Fill(new Rectangle(r.X, r.Y + r.Height / 2, r.Width, 1), new Color(255, 240, 210, 70));
		Ui.Outline(r, Palette.Border);
		if (ghost.HasValue)
		{
			Rectangle gc = Cell(ghost.Value.x, ghost.Value.y);
			Ui.Outline(new Rectangle(gc.X - 1, gc.Y - 1, gc.Width + 2, gc.Height + 2), Color.White, mini ? 1 : 2);
		}
		foreach (var d in dots)
		{
			Rectangle dc = Cell(Math.Clamp(d.x, -5, 5), Math.Clamp(d.y, -5, 5));
			Ui.Fill(new Rectangle(dc.X - 1, dc.Y - 1, dc.Width + 2, dc.Height + 2), Color.Black);
			Ui.Fill(dc, d.c);
		}
	}

	private static void StatMapWithLabels(string id, Rectangle r, IEnumerable<(int x, int y, Color c)> dots, (int x, int y)? ghost = null)
	{
		StatMap(r, dots, false, ghost);
		Ui.TextCentered("Ranged", new Rectangle(r.X, r.Y - 20, r.Width, 18), PoultryHud.Ranged);
		Ui.TextCentered("Melee", new Rectangle(r.X, r.Bottom + 2, r.Width, 18), PoultryHud.Melee);
		Ui.TextRight("Tough", r.X - 6, r.Y + r.Height / 2 - 10, PoultryHud.Tough);
		Ui.Text("Swift", r.Right + 6, r.Y + r.Height / 2 - 10, PoultryHud.Swift);
		ui_mouseFrame f = Ui.Area(id, r, null, Ui.WindowLevel + 1);
		if (Ui.Hovered(f))
		{
			Point m = Ui.Mouse;
			int sx = (int)((m.X - r.X) / (r.Width / 11f)) - 5, sy = (int)((m.Y - r.Y) / (r.Height / 11f)) - 5;
			string zone = StatZones.Where(z => sx >= z.x0 && sx <= z.x1 && sy >= z.y0 && sy <= z.y1).Select(z => z.name).FirstOrDefault();
			Ui.Tooltip("Tough / Swift across, Ranged / Melee down."
				+ "\nGold corners give a stat power" + (zone != null ? ": here ^3" + zone + "^7" : ".")
				+ "\nRed band (Melee 3+): Press the Attack." + (ghost.HasValue ? "\nWhite square: after the hovered food." : ""));
		}
	}

	private static void StatRows(int x, int y, int w, int t, int m, int pluck, int gen, string id)
	{
		PoultryHud.StatRow(id + "_tvs", x, y, w, "Tough", "Swift", t, PoultryHud.Tough, PoultryHud.Swift, PoultryHud.ToughSwiftHelp(t, pluck));
		PoultryHud.StatRow(id + "_mvr", x, y + PoultryHud.StatRowHeight, w, "Ranged", "Melee", m, PoultryHud.Ranged, PoultryHud.Melee, PoultryHud.RangedMeleeHelp(m, gen));
	}

	private static Rectangle Section(Rectangle r, string title)
	{
		Rectangle b = Ui.Box(r);
		Ui.Text(title, b.X + 12, b.Y + 6, Palette.Accent, UiFont.Medium);
		return b;
	}

	// The bird's portrait on the game's Official Bird Paper.
	private static void Portrait(dmBirdEgg e, Rectangle r)
	{
		Ui.Icon("ui_official_bird_paper", r);
		Ui.Sprite(e.GetBirdSprite(), new Rectangle(r.X + r.Width / 6, r.Y + r.Height / 10, r.Width * 2 / 3, r.Height * 7 / 10),
			e.BirdHasEverHatched ? (Color?)null : new Color(120, 120, 120));
	}

	// ---------- Birds tab ----------

	private static void DrawBirdDetails(Rectangle area)
	{
		dmBirdEgg e = selected;
		if (e == null)
		{
			Ui.TextCentered("Pick a bird on the left.", area, Palette.TextDim, UiFont.Medium);
			return;
		}
		BirdStatInfo info = e.GetStatInfo();
		dmPetBird placed = PoultryCore.Placed(e);
		int x = area.X, y = area.Y;

		// Header: portrait on bird paper, name, facts, actions, HP.
		Portrait(e, new Rectangle(x, y, 198, 120));
		bool fav = e.BirdName == PoultryCore.FavoriteName;
		int hx = x + 212;
		var heart = new Rectangle(hx, y + 6, 26, 26);
		ui_mouseFrame hf = Ui.Area("poultry_coop_favheart", heart, () =>
		{
			dmAudio.PlayUIFX("ui_click_01");
			PoultryCore.FavoriteName = fav ? "" : e.BirdName;
			Say(fav ? "No favorite bird." : e.BirdName + " is your favorite.");
		}, Ui.WindowLevel + 1);
		Ui.Icon("ui_favorite_heart", heart, fav ? (Color?)null : Ui.Hovered(hf) ? new Color(200, 170, 160) : new Color(80, 70, 66));
		if (Ui.Hovered(hf))
		{
			Ui.Tooltip(fav ? "Favorite: Prepare hero equips it. Click to unset." : "Make it your favorite (Prepare hero equips it)");
		}
		hx += 34;
		Ui.Text(e.BirdName, hx, y, Palette.Text, UiFont.Large);
		string where = PoultryCore.IsEquipped(e) ? "^5Equipped^7" : PoultryCore.InBag(e) ? "In your bags" : "At the Academy";
		Ui.Text("Generation ^3" + e.GetGeneration() + "^7    Pluck ^3" + e.GetChampionPoints() + "^7    Level ^3" + e.BirdLevel + "^7    " + where
			+ (info.bWounded ? "    ^1Wounded^7" : "") + (!e.BirdHasEverHatched ? "    Unhatched" : ""), x + 212, y + 38, Palette.TextDim);
		if (placed != null && placed.maxHealth > 0)
		{
			Ui.GameBar(BarStyle.Health, new Rectangle(area.Right - 200, y + 4, 200, 26), placed.health / (float)placed.maxHealth, placed.health + "/" + placed.maxHealth);
		}

		int bx = x + 212, by = y + 72;
		bool inBag = PoultryCore.InBag(e);
		bx = Act("store", bx, by, inBag ? "Store" : "Take", () => Do(() => inBag ? PoultryCore.Store(e) : PoultryCore.Take(e)), AtAcademy,
			inBag ? "Leave it at the Academy" : "Put it in your bags");
		bx = Act("equip", bx, by, "Equip", () => Do(() => PoultryCore.Equip(e)), !PoultryCore.IsEquipped(e) && (AtAcademy || inBag), "Put it in your egg pocket");
		bool isOut = PoultryCore.IsOut(e);
		bool inLevel = Game.theCurrentLevel is dmDungeonLevel;
		bool canCall = inLevel && (AtAcademy || inBag) && !info.bWounded;
		bx = Act("summon", bx, by, isOut ? "Unsummon" : "Summon", () => Do(() => isOut ? PoultryCore.Unsummon(e) : PoultryCore.Summon(e)), isOut || canCall,
			isOut ? "Back into its egg" : info.bWounded ? "Wounded: heal it first" : !inLevel ? "Not on the overworld" : "Call it to your side (like the whistle; equips it)");
		bx = Act("heal", bx, by, "Heal all", () => Do(PoultryCore.HealAll), AtAcademy, "Heal every wounded bird");
		if (releaseConfirm == e)
		{
			Act("free", bx, by, "Confirm", () =>
			{
				releaseConfirm = null;
				Do(() => PoultryCore.Release(e));
			}, true, "Set it free for good: +" + PoultryCore.ReleaseFeathers(e) + " Feathers", Palette.Bad);
		}
		else
		{
			Act("free", bx, by, "Set free", () => releaseConfirm = e, AtAcademy && !e.InventoryLocked && !fav,
				fav ? "Unfavorite it first" : e.InventoryLocked ? "It's locked" : "Gone for good: +" + PoultryCore.ReleaseFeathers(e) + " Feathers (asks again)", Palette.Bad);
		}
		y += 128;

		// Stats.
		int t = info.birdStats[0], m = info.birdStats[1];
		Rectangle stats = Section(new Rectangle(x, y, 416, 224), "Stats");
		(int, int)? ghost = previewFoodLast != null ? (Math.Clamp(t + previewFoodLast.Tvs, -5, 5), Math.Clamp(m + previewFoodLast.Mvr, -5, 5)) : null;
		StatMapWithLabels("poultry_coop_map", new Rectangle(stats.X + 56, stats.Y + 58, 132, 132), new[] { (t, m, Color.White) }, ghost);
		int rx = stats.X + 252;
		int hpPct = (t < 0 ? -10 * t : -4 * t) + e.GetChampionPoints();
		int ly = stats.Y + 46;
		Fact(rx, ly, t < 0 ? "Tough" : t > 0 ? "Swift" : "Tough/Swift", Math.Abs(t).ToString(), PoultryHud.ToughSwiftHelp(t, e.GetChampionPoints()), t < 0 ? PoultryHud.Tough : PoultryHud.Swift);
		Fact(rx, ly + 24, m < 0 ? "Ranged" : m > 0 ? "Melee" : "Rng/Melee", Math.Abs(m).ToString(), PoultryHud.RangedMeleeHelp(m, e.GetGeneration()), m < 0 ? PoultryHud.Ranged : PoultryHud.Melee);
		Fact(rx, ly + 56, "Max HP", (hpPct >= 0 ? "+" : "") + hpPct + "%", "Tough +10%/pt, Swift -4%/pt, Pluck +1%/pt");
		Fact(rx, ly + 80, t <= 0 ? "Block" : "Parry", (t <= 0 ? -8 * t : 6 * t).ToString(), "Block 8 per Tough point, Parry 6 per Swift point");
		Fact(rx, ly + 104, "Attack", m < 0 ? (2 - m) + " tiles" : "melee", m < 0 ? "Ranged birds shoot from 2 + Ranged tiles" : "Melee birds fight up close");
		Fact(rx, ly + 128, "Damage", "+" + 20 * (e.GetGeneration() - 1) + "%", "+20% per generation after the first");

		// Powers & genes.
		Rectangle pw = Section(new Rectangle(x + 416, y, 352, 224), "Powers  " + info.powersUnlockedArchetypes.Count + "/4");
		int py = pw.Y + 38;
		foreach (string p in info.powersUnlockedArchetypes.ToList())
		{
			info.powerGenesCollected.TryGetValue(p, out int g);
			PowerLine(p, pw.X + 14, py, pw.Width - 28, null, () => Do(() => PoultryCore.Prune(e, p)),
				"Genes: " + g + "  (more genes = better chance to pass it on when breeding)");
			py += 34;
		}
		foreach (string p in info.powersViaStatsArchetypes)
		{
			PowerLine(p, pw.X + 14, py, pw.Width - 28, "from stats", null, "Comes from its stats; lost if they change.");
			py += 26;
		}
		if (info.powersUnlockedArchetypes.Count + info.powersViaStatsArchetypes.Count == 0)
		{
			Ui.Text("None yet: feed steaks with a power gene.", pw.X + 14, py, Palette.TextMuted);
			py += 26;
		}
		var genes = info.powerGenesCollected.Where(kv => kv.Key != "none" && !info.powersUnlockedArchetypes.Contains(kv.Key) && !info.sealedPowers.Contains(kv.Key))
			.OrderByDescending(kv => kv.Value).ToList();
		if (genes.Count > 0 && py < pw.Bottom - 24)
		{
			string list = string.Join(", ", genes.Select(kv => PoultryCore.PowerName(kv.Key) + " " + kv.Value));
			ui_mouseFrame gf = Ui.Area("poultry_coop_genes", new Rectangle(pw.X + 14, py, pw.Width - 28, 20), null, Ui.WindowLevel + 1);
			Ui.Text(Ui.Fit("Genes: " + list, pw.Width - 28), pw.X + 14, py, Palette.TextDim);
			if (Ui.Hovered(gf))
			{
				Ui.Tooltip("Genes collected but not learned yet:\n" + string.Join("\n", genes.Select(kv => PoultryCore.PowerName(kv.Key) + ": " + kv.Value
					+ " (next steak " + PoultryCore.LearnChance(info, kv.Key) + "%)")));
			}
		}
		if (info.sealedPowers.Count > 0)
		{
			Ui.Text(Ui.Fit("Forgotten: " + string.Join(", ", info.sealedPowers.Select(PoultryCore.PowerName)), pw.Width - 28), pw.X + 14, pw.Bottom - 28, Palette.TextMuted);
		}
		y += 232;

		DrawFeedList(e, new Rectangle(x, y, area.Width, area.Bottom - y));
	}

	private static int Act(string id, int x, int y, string label, Action onClick, bool enabled, string tip, Color? accent = null)
	{
		int w = Ui.ButtonWidth(label);
		Ui.BoxButton("poultry_coop_act_" + id, new Rectangle(x, y, w, 32), label, onClick, enabled, tooltipText: tip, accent: accent);
		return x + w + 6;
	}

	private static void Fact(int x, int y, string label, string value, string tip, Color? labelColor = null)
	{
		ui_mouseFrame f = Ui.Area("poultry_coop_fact_" + label, new Rectangle(x, y, 150, 20), null, Ui.WindowLevel + 1);
		Ui.Text(label, x, y, labelColor ?? Palette.TextDim);
		Ui.Text("^3" + value, x + 84, y, Palette.Text);
		if (Ui.Hovered(f))
		{
			Ui.Tooltip(tip);
		}
	}

	private static void PowerLine(string power, int x, int y, int w, string note, Action forget, string extra)
	{
		ui_mouseFrame f = Ui.Area("poultry_coop_pt_" + power, new Rectangle(x, y, w - 74, 28), null, Ui.WindowLevel + 1);
		Ui.Text(PoultryCore.PowerName(power), x, y + 4, forget != null ? Palette.Text : Palette.Info);
		if (note != null)
		{
			Ui.TextRight(note, x + w - (forget != null ? 76 : 0), y + 4, Palette.TextDim);
		}
		if (Ui.Hovered(f))
		{
			dmSpecialPower sp = Ui.Power(power);
			string text = sp != null ? PowerText(sp) : null;
			Ui.Tooltip("^3" + PoultryCore.PowerName(power) + "^7" + (!string.IsNullOrEmpty(text) ? "\n" + Wrap(Ui.StripCodes(text), 60) : "") + "\n" + extra);
		}
		if (forget != null)
		{
			bool can = PoultryCore.Count(PoultryCore.Pruning) > 0;
			Ui.BoxButton("poultry_coop_forget_" + power, new Rectangle(x + w - 64, y, 64, 32), "Forget", forget, can,
				tooltipText: can ? "Costs 1 Pruning Poultice" : "Needs a Pruning Poultice", accent: Palette.Warn);
		}
	}

	private static string PowerText(dmSpecialPower sp)
	{
		try
		{
			return sp.GetTooltipMainText();
		}
		catch
		{
			return sp.strFlavorText;
		}
	}

	private static string Wrap(string s, int width)
	{
		var lines = new List<string>();
		string line = "";
		foreach (string word in s.Split(' '))
		{
			if (line.Length + word.Length + 1 > width && line.Length > 0)
			{
				lines.Add(line);
				line = "";
			}
			line += (line.Length > 0 ? " " : "") + word;
		}
		lines.Add(line);
		return string.Join("\n", lines.Take(6));
	}

	private static void DrawFeedList(dmBirdEgg e, Rectangle area)
	{
		BirdStatInfo info = e.GetStatInfo();
		List<PoultryCore.Food> all = PoultryCore.BagFoods()
			.GroupBy(f => f.Key).Select(gr => { var f = gr.First(); f.Count = gr.Count(); return f; })
			.Concat(PoultryCore.PantryOpen ? PoultryCore.PantryFoods() : new List<PoultryCore.Food>())
			.ToList();
		List<PoultryCore.Food> foods = (feedAll ? all : all.Where(f => f.Useful))
			.OrderByDescending(f => f.IsPlumage).ThenByDescending(f => f.Tier).ThenBy(f => f.Name).ToList();
		Rectangle r = Section(new Rectangle(area.X, area.Y, area.Width, area.Height / 32 * 32), "Feed");
		Ui.BoxButton("poultry_coop_feedfilter", new Rectangle(r.X + 80, r.Y + 2, 160, 32), feedAll ? "All foods" : "Rare only",
			() => feedAll = !feedAll, tooltipText: feedAll ? "Show only foods that raise Pluck or teach a power" : "Also show plain steaks (they only shift stats)");
		Ui.TextRight(foods.Sum(f => f.Count) + " shown" + (PoultryCore.PantryOpen ? " (bags + Pantry)" : " (bags)"), r.Right - 14, r.Y + 8, Palette.TextDim);

		feedRect = new Rectangle(r.X, r.Y + 38, r.Width, r.Height - 42);
		const int rowH = 32;
		int rows = Math.Max(1, feedRect.Height / rowH);
		feedScroll = Math.Max(0, Math.Min(Math.Max(0, foods.Count - rows), feedScroll));
		for (int vi = 0; vi < rows && feedScroll + vi < foods.Count; vi++)
		{
			PoultryCore.Food f = foods[feedScroll + vi];
			var row = new Rectangle(r.X + 10, feedRect.Y + vi * rowH, r.Width - 20, rowH);
			ui_mouseFrame hf = Ui.Area("poultry_coop_feedrow" + vi, row, null, Ui.WindowLevel + 1);
			if (Ui.Hovered(hf))
			{
				Ui.Fill(row, new Color(255, 230, 170, 30));
				previewFood = f;
				Ui.Tooltip(f.Name + "\n" + FoodEffects(f, info));
			}
			Ui.BoxButton("poultry_coop_feed" + vi, new Rectangle(row.X, row.Y, 64, 32), "Feed", () => Do(() => PoultryCore.Feed(e, f)),
				e.BirdHasEverHatched, tooltipText: e.BirdHasEverHatched ? null : "Hatch it first", stackLevel: Ui.WindowLevel + 2);
			Ui.Sprite(FoodSprite(f), new Rectangle(row.X + 72, row.Y + 2, 28, 28));
			Ui.Text(Ui.Fit(f.Name, 250), row.X + 106, row.Y + 6, f.IsPlumage ? Palette.Info : f.Tier > 0 ? Palette.Accent : Palette.Text);
			// Cut to the row so a long power name doesn't run past the panel (the full line is in the row's tooltip).
			Ui.Text(Ui.Fit("x" + f.Count + (f.BagItem == null ? " (Pantry)" : "") + "    " + FoodEffects(f, info), row.Right - (row.X + 370) - 8),
				row.X + 370, row.Y + 6, Palette.TextDim);
		}
		if (foods.Count == 0)
		{
			Ui.TextCentered(feedAll ? "No steaks or Plumage." : "No rare steaks or Plumage.", feedRect, Palette.TextMuted);
		}
		else if (foods.Count > rows)
		{
			Ui.TextRight((feedScroll + 1) + "-" + Math.Min(foods.Count, feedScroll + rows) + " of " + foods.Count, r.Right - 14, r.Bottom - 22, Palette.TextMuted);
		}
	}

	// ---------- Breed tab ----------

	private static void DrawBreed(Rectangle area)
	{
		int x = area.X, y = area.Y;
		Ui.Text("Click two birds on the left.", x, y + 6, Palette.TextDim);
		y += 40;

		ParentCard(new Rectangle(x, y, 224, 192), parentA, "Parent 1");
		Ui.TextCentered("+", new Rectangle(x + 224, y, 48, 192), Palette.Accent, UiFont.Title);
		ParentCard(new Rectangle(x + 272, y, 224, 192), parentB, "Parent 2");
		Ui.TextCentered("=", new Rectangle(x + 496, y, 48, 192), Palette.Accent, UiFont.Title);
		var egg = new Rectangle(x + 544, y, 224, 192);

		if (parentA == null || parentB == null)
		{
			Ui.Box(egg);
			Ui.TextCentered("The egg", egg, Palette.TextMuted, UiFont.Medium);
			return;
		}
		if (!parentA.BirdHasEverHatched || !parentB.BirdHasEverHatched)
		{
			Ui.Box(egg);
			Ui.TextCentered("Both must have hatched", egg, Palette.Bad);
			return;
		}
		PoultryCore.BreedPreview p = PoultryCore.Preview(parentA, parentB);
		Ui.Box(egg, true);
		Ui.Text("The egg", egg.X + 14, egg.Y + 8, Palette.Accent, UiFont.Medium);
		Ui.Text("Gen ^3" + p.Generation + "^7   Pluck ^3" + p.Pluck, egg.X + 14, egg.Y + 40, Palette.Text, UiFont.Medium);
		StatRows(egg.X + 14, egg.Y + 98, egg.Width - 28, p.Tvs, p.Mvr, p.Pluck, p.Generation, "poultry_coop_egg");
		y += 200;

		int lowH = (area.Bottom - y - 48) / 32 * 32;
		Rectangle map = Section(new Rectangle(x, y, 288, lowH), "Stats");
		Point a = parentA.GetStatAxis(), b = parentB.GetStatAxis();
		int ms = Math.Max(66, Math.Min(map.Height - 100, 132));
		StatMapWithLabels("poultry_coop_bmap", new Rectangle(map.X + (map.Width - ms) / 2, map.Y + 58, ms, ms),
			new[] { (a.X, a.Y, PoultryHud.Swift), (b.X, b.Y, PoultryHud.Ranged), (p.Tvs, p.Mvr, Color.White) });
		int lx = Ui.Chip(map.X + 14, map.Bottom - 26, "P1", PoultryHud.Swift);
		lx = Ui.Chip(lx, map.Bottom - 26, "P2", PoultryHud.Ranged);
		Ui.Chip(lx, map.Bottom - 26, "Egg (white)", Palette.Text);

		Rectangle odds = Section(new Rectangle(x + 288, y, 480, lowH), "Powers it can be born with (max 4)");
		int oy = odds.Y + 40;
		int gi = 0;
		foreach (var (gene, count, chance) in p.Powers.Take(Math.Max(1, (odds.Height - 48) / 30)))
		{
			ui_mouseFrame gf = Ui.Area("poultry_coop_odds" + gi++, new Rectangle(odds.X + 14, oy, odds.Width - 28, 28), null, Ui.WindowLevel + 1);
			Ui.Text(Ui.Fit(PoultryCore.PowerName(gene), 220), odds.X + 14, oy + 4, Palette.Text);
			int pct = (int)Math.Round(chance * 100);
			Ui.GameBar(chance >= 0.99 ? BarStyle.Xp : chance >= 0.5 ? BarStyle.Stamina : BarStyle.Ire, new Rectangle(odds.X + 250, oy, odds.Width - 264, 26), (float)chance, pct + "%");
			if (Ui.Hovered(gf))
			{
				Ui.Tooltip("^3" + PoultryCore.PowerName(gene) + "^7\nThe parents carry " + count + " gene" + (count == 1 ? "" : "s") + " for it (half their total).\nEach gene: +20% chance, rolled strongest first, max 4 powers.");
			}
			oy += 30;
		}
		if (p.Powers.Count == 0)
		{
			Ui.Text("No power genes in either parent.", odds.X + 14, oy, Palette.TextMuted);
		}

		int have = PoultryCore.Count(PoultryCore.Passionate);
		string breedTip = "Uses 1 Passionate Poultry Poultice (you have " + have + ")."
			+ "\nBoth parents are kept; the egg goes to your bags."
			+ "\n" + (parentA.GetGeneration() == parentB.GetGeneration() ? "Same generation: the egg gets +1 generation." : "Different generations: the egg keeps the higher one (no +1).");
		Ui.BoxButton("poultry_coop_breed", new Rectangle(area.Right - 192, area.Bottom - 40, 192, 32), "Breed",
			() => Do(() => PoultryCore.Breed(parentA, parentB)), have > 0, font: UiFont.Medium, tooltipText: have > 0 ? breedTip : "Needs a Passionate Poultry Poultice");
	}

	private static void ParentCard(Rectangle r, dmBirdEgg e, string label)
	{
		Rectangle b = Ui.Box(r);
		if (e == null)
		{
			Ui.TextCentered(label + ": pick a bird", b, Palette.TextMuted);
			return;
		}
		Ui.Sprite(e.GetBirdSprite(), new Rectangle(b.X + 10, b.Y + 10, 44, 60));
		Ui.Text(label, b.X + 62, b.Y + 8, Palette.TextDim);
		Ui.Text(Ui.Fit(e.BirdName, b.Width - 72, UiFont.Medium), b.X + 62, b.Y + 28, Palette.Text, UiFont.Medium);
		Ui.Text("Gen ^3" + e.GetGeneration() + "^7   Pluck ^3" + e.GetChampionPoints(), b.X + 62, b.Y + 56, Palette.TextDim);
		Point s = e.GetStatAxis();
		StatRows(b.X + 14, b.Y + 98, b.Width - 28, s.X, s.Y, e.GetChampionPoints(), e.GetGeneration(), "poultry_coop_par_" + label.Replace(" ", ""));
	}

	// ---------- Ascend tab ----------

	private static void DrawAscend(Rectangle area)
	{
		dmBirdEgg e = selected;
		int x = area.X, y = area.Y;
		if (e == null)
		{
			Ui.TextCentered("Pick a bird on the left.", area, Palette.TextDim, UiFont.Medium);
			return;
		}
		int have = PoultryCore.Count(PoultryCore.Passionate);
		ascendN = Math.Max(2, Math.Min(8, ascendN));
		int pluck = e.GetChampionPoints();
		int after = PoultryCore.PluckAfterAscend(pluck, ascendN);

		Portrait(e, new Rectangle(x, y, 198, 120));
		Ui.Text(e.BirdName, x + 212, y, Palette.Text, UiFont.Large);
		Ui.Text("Generation", x + 212, y + 48, Palette.TextDim, UiFont.Medium);
		Ui.Text(e.GetGeneration().ToString(), x + 340, y + 42, Palette.Text, UiFont.Title);
		Ui.Icon("ui_yellow_red_arrow", new Rectangle(x + 392, y + 44, 40, 34));
		Ui.Text((e.GetGeneration() + 1).ToString(), x + 444, y + 42, Palette.Good, UiFont.Title);
		ui_mouseFrame af = Ui.Area("poultry_coop_ascinfo", new Rectangle(x + 212, y + 88, 556, 20), null, Ui.WindowLevel + 1);
		Ui.Text("Raises the generation without breeding: +20% damage. Only Pluck drops.", x + 212, y + 88, Palette.TextDim);
		if (Ui.Hovered(af))
		{
			Ui.Tooltip("A bird deals +20% damage per generation after the first\n(from the game's bird code; Professor Hatch says 10%)."
				+ "\nStats, powers and genes are kept.");
		}
		y += 136;

		Rectangle sec = Section(new Rectangle(x, y, area.Width, 256), "Passionate Poultices to spend");
		int bx = sec.X + 14, by = sec.Y + 40;
		for (int n = 2; n <= 8; n++)
		{
			int nn = n;
			Ui.BoxButton("poultry_coop_asc" + n, new Rectangle(bx, by, 64, 32), n.ToString(), () => ascendN = nn, n <= have, n == ascendN, UiFont.Medium,
				n <= have ? "Keeps " + (100 - 100.0 * Math.Pow(0.5, n)).ToString("0.#") + "% of Pluck" : "You have " + have);
			bx += 72;
		}
		Ui.Text("You have ^3" + have + "^7", bx + 8, by + 6, Palette.TextDim, UiFont.Medium);

		int barX = sec.X + 140, barW = sec.Width - 160;
		int max = Math.Max(1, pluck);
		Ui.Text("Pluck now", sec.X + 14, by + 58, Palette.TextDim, UiFont.Medium);
		Ui.GameBar(BarStyle.Stamina, new Rectangle(barX, by + 54, barW, 30), 1f, pluck.ToString());
		Ui.Text("Pluck after", sec.X + 14, by + 98, Palette.TextDim, UiFont.Medium);
		Ui.GameBar(BarStyle.Stamina, new Rectangle(barX, by + 94, barW, 30), after / (float)max, after + "   (-" + (pluck - after) + ")");
		Ui.Text("Each extra poultice halves the loss. Rare steaks (Pluck +1 / +2 / +3) refill it.", sec.X + 14, by + 144, Palette.TextMuted);

		Ui.BoxButton("poultry_coop_ascend", new Rectangle(area.Right - 384, area.Bottom - 40, 384, 32),
			"Ascend to generation " + (e.GetGeneration() + 1) + "  (" + ascendN + " poultices)", () => Do(() => PoultryCore.Ascend(e, ascendN)),
			have >= ascendN && e.BirdHasEverHatched, font: UiFont.Medium);
	}

	// ---------- Pantry tab ----------

	// [icon] label ...... price [feather]   (a 256-wide steel button)
	private static void ShopButton(string id, int x, int y, string label, dmSprite icon, bool showIcon, int price, Action onClick, bool enabled, string tip, bool gain = false)
	{
		var r = new Rectangle(x, y, 256, 32);
		Ui.BoxButton(id, r, "", onClick, enabled, tooltipText: tip);
		Color? dim = enabled ? (Color?)null : new Color(120, 120, 120);
		int tx = r.X + 12;
		if (showIcon)
		{
			Ui.Sprite(icon, new Rectangle(tx, r.Y + 3, 26, 26), dim);
			tx += 32;
		}
		Ui.Text(label, tx, r.Y + 6, enabled ? Palette.Text : Palette.TextMuted);
		Ui.TextRight("^3" + (gain ? "+" : "") + price, r.Right - 40, r.Y + 6, enabled ? Palette.Text : Palette.TextMuted);
		Ui.Sprite(Ui.GameSprite(FeatherSprite), new Rectangle(r.Right - 34, r.Y + 6, 20, 20), dim);
	}

	private static void DrawPantry(Rectangle body)
	{
		const int colW = 576, rowH = 32;
		var bag = PoultryCore.BagFoods().GroupBy(f => f.Key).Select(gr => (name: gr.First().Name, item: (dmItem)gr.First().BagItem, n: gr.Count()))
			.Concat(Game.dungeonMans.GetInventoryList().Where(i => PoultryCore.IsPantryItem(i) && !(i is dmEnemyBirdSteak)).GroupBy(i => i.ArchetypeName)
				.Select(gr => (name: gr.First().GetName(), item: gr.First(), n: gr.Count())))
			.ToList();

		Rectangle left = Section(new Rectangle(body.X, body.Y, colW, body.Height / 32 * 32), "In your bags  (" + bag.Sum(b => b.n) + ")");
		Ui.BoxButton("poultry_coop_storeall", new Rectangle(left.Right - 138, left.Y + 2, 128, 32), "Store all", () => Do(PoultryCore.DepositAll), bag.Count > 0,
			tooltipText: "Move every steak, Plumage and poultice to the Pantry (locked items stay)");
		bagRect = new Rectangle(left.X, left.Y + 40, left.Width, left.Height - 44);
		int rows = bagRect.Height / rowH;
		bagScroll = Math.Max(0, Math.Min(Math.Max(0, bag.Count - rows), bagScroll));
		for (int vi = 0; vi < rows && bagScroll + vi < bag.Count; vi++)
		{
			var b = bag[bagScroll + vi];
			int ry = bagRect.Y + vi * rowH;
			ui_mouseFrame bf = Ui.Area("poultry_coop_bagrow" + vi, new Rectangle(left.X + 84, ry, left.Width - 96, rowH), null, Ui.WindowLevel + 1);
			if (Ui.Hovered(bf))
			{
				Ui.Fill(new Rectangle(left.X + 80, ry, left.Width - 92, rowH), new Color(255, 230, 170, 30));
				Ui.Tooltip(Describe(null, b.item));
			}
			Ui.BoxButton("poultry_coop_store" + vi, new Rectangle(left.X + 12, ry, 64, 32), "Store", () => Do(() => PoultryCore.Deposit(b.item)), !b.item.InventoryLocked,
				tooltipText: b.item.InventoryLocked ? "Locked" : null);
			Ui.Sprite(b.item.sprite, new Rectangle(left.X + 84, ry + 2, 28, 28));
			Ui.Text(Ui.Fit(b.name, left.Width - 200), left.X + 120, ry + 6, Palette.Text);
			if (b.item.InventoryLocked)
			{
				Ui.Icon("ui_lock", new Rectangle(left.Right - 70, ry + 6, 18, 18));
			}
			if (b.n > 1)
			{
				Ui.TextRight("x" + b.n, left.Right - 16, ry + 6, Palette.TextDim);
			}
		}
		if (bag.Count == 0)
		{
			Ui.TextCentered("Nothing to store.", bagRect, Palette.TextMuted);
		}
		else if (bag.Count > rows)
		{
			Ui.TextRight((bagScroll + 1) + "-" + Math.Min(bag.Count, bagScroll + rows) + " of " + bag.Count + " (wheel)", left.Right - 14, left.Bottom - 22, Palette.TextMuted);
		}

		int px = body.X + colW + 16;
		var pantry = PoultryCore.Pantry;
		Rectangle right = Section(new Rectangle(px, body.Y, colW, body.Height / 32 * 32 - 128), "Pantry");
		int used = PoultryCore.UsedUnits, cap = PoultryCore.PantryCapacity;
		Ui.GameBar(used >= cap ? BarStyle.Health : BarStyle.Mana, new Rectangle(right.X + 100, right.Y + 6, 200, 24),
			used / (float)Math.Max(1, cap), used + " / " + cap + " items");
		Ui.TextRight("Shift = whole stack", right.Right - 14, right.Y + 8, Palette.TextMuted);
		pantryRect = new Rectangle(right.X, right.Y + 40, right.Width, right.Height - 44);
		int prow = pantryRect.Height / rowH;
		pantryScroll = Math.Max(0, Math.Min(Math.Max(0, pantry.Count - prow), pantryScroll));
		for (int vi = 0; vi < prow && pantryScroll + vi < pantry.Count; vi++)
		{
			var kv = pantry[pantryScroll + vi];
			int ry = pantryRect.Y + vi * rowH;
			ui_mouseFrame pf = Ui.Area("poultry_coop_pantryrow" + vi, new Rectangle(right.X + 184, ry, right.Width - 196, rowH), null, Ui.WindowLevel + 1);
			if (Ui.Hovered(pf))
			{
				Ui.Fill(new Rectangle(right.X + 184, ry, right.Width - 196, rowH), new Color(255, 230, 170, 30));
				Ui.Tooltip(Describe(kv.Key, null));
			}
			Ui.BoxButton("poultry_coop_take" + vi, new Rectangle(right.X + 12, ry, 64, 32), "Take",
				() => Do(() => PoultryCore.Withdraw(kv.Key, Game.theInput.bShiftIsDown ? kv.Value : 1)), tooltipText: "Shift-click: the whole stack");
			int tx = right.X + 84;
			if (!kv.Key.StartsWith("i|"))
			{
				PoultryCore.Food f = PoultryCore.FoodFromKey(kv.Key, 1);
				int feathers = f != null ? PoultryCore.RenderFeathers(f) : 0;
				Ui.BoxButton("poultry_coop_render" + vi, new Rectangle(tx, ry, 96, 32), "Sell +" + feathers, () => Do(() => PoultryCore.Render(kv.Key)), f != null,
					tooltipText: "Sell one for " + feathers + " Feathers", accent: Palette.Warn);
				tx += 104;
			}
			Ui.Sprite(KeySprite(kv.Key), new Rectangle(tx, ry + 2, 28, 28));
			Ui.Text(Ui.Fit(PoultryCore.ItemName(kv.Key), right.Right - 70 - tx - 36), tx + 36, ry + 6, Palette.Text);
			Ui.TextRight("x" + kv.Value, right.Right - 16, ry + 6, Palette.TextDim);
		}
		if (pantry.Count == 0)
		{
			Ui.TextCentered("Empty: store steaks and poultices from your bags.", pantryRect, Palette.TextMuted);
		}

		// Feathers: buy on the left, sell on the right.
		const int half = colW / 2;
		Rectangle buy = Section(new Rectangle(px, right.Bottom, half, 128), "Buy");
		Ui.Sprite(Ui.GameSprite(FeatherSprite), new Rectangle(buy.Right - 70, buy.Y + 8, 22, 22));
		Ui.TextRight("^3" + PoultryCore.Feathers, buy.Right - 14, buy.Y + 8, Palette.Text);
		ShopButton("poultry_coop_buyslots", buy.X + 14, buy.Y + 44, "+" + PoultryCore.UnitsPerPurchase + " Pantry space", default, false,
			PoultryCore.SpacePrice, () => Do(PoultryCore.BuySpace), PoultryCore.Feathers >= PoultryCore.SpacePrice, "Room for " + PoultryCore.UnitsPerPurchase + " more items in the Pantry");
		ShopButton("poultry_coop_buypoult", buy.X + 14, buy.Y + 84, "Passionate", ArchetypeSprite(PoultryCore.Passionate), true,
			PoultryCore.PoulticeFeatherPrice, () => Do(() => PoultryCore.BuyPoultice(PoultryCore.Passionate)), PoultryCore.Feathers >= PoultryCore.PoulticeFeatherPrice,
			"One Passionate Poultry Poultice");
		Rectangle sell = Section(new Rectangle(px + half, right.Bottom, half, 128), "Sell");
		ShopButton("poultry_coop_sellpoult", sell.X + 14, sell.Y + 44, "Passionate", ArchetypeSprite(PoultryCore.Passionate), true,
			PoultryCore.PoulticeSellFeathers, () => Do(PoultryCore.SellPoultice), PoultryCore.Count(PoultryCore.Passionate) > 0,
			"Sell one Passionate Poultry Poultice", true);
		Ui.Text("Steaks: Sell, in the Pantry list.", sell.X + 14, sell.Y + 90, Palette.TextMuted);
	}
}
