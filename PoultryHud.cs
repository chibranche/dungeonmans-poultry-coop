using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMans;
using DungeonmansModKit;
using Microsoft.Xna.Framework;

namespace DungeonmansPoultry;

// Bird status window, drawn with the game's own vitals frame (the hero's HP/stamina "backpack") so it reads as part
// of the game: name plate, generation/Pluck on the ribbon, level in the LEVEL badge, the game's red HP bar,
// Block / Range / Damage, and Feed / Ascend / Coop. Stats and powers live in the Coop.
// Drag it by the name plate (position is remembered); the arrow folds it to just the header and HP.
internal static class PoultryHud
{
	public const string ExpandedKey = "poultry_hud_expanded";
	private const string PosX = "poultry_hud_x", PosY = "poultry_hud_y";
	private const int W = 268; // width of the game's vitals frame

	private static dmBirdEgg Equipped(dmGame g) => g.dungeonMans.GetItemForSlot(ESlot.ES_BirdEggPocket) as dmBirdEgg;

	private static bool Visible(dmGame g) =>
		g.dungeonMans != null && !g.bShowMainMenu && !Ui.IsBusy(g) && Equipped(g) != null;

	// Stat colors shared with the Coop.
	public static readonly Color Tough = new Color(236, 156, 72), Swift = new Color(110, 206, 240);
	public static readonly Color Ranged = new Color(190, 146, 246), Melee = new Color(240, 108, 92);

	public static void Draw(dmGame g)
	{
		if (!Visible(g))
		{
			return;
		}
		dmBirdEgg egg = Equipped(g);
		BirdStatInfo info = egg.GetStatInfo();
		dmPetBird pet = PoultryCore.Placed(egg);
		bool expanded = Config.On(ExpandedKey);
		int h = expanded ? 236 : 172;

		var size = new Point(W, h);
		var defaultPos = new Point(Ui.ScreenSize.X - W - 6, Ui.ScreenSize.Y - 222 - h);
		// Clamped: a position saved at a bigger resolution would be off screen.
		Point pos = Ui.ClampToScreen(new Point(PoultryMod.Ctx.Settings.GetInt(PosX, defaultPos.X), PoultryMod.Ctx.Settings.GetInt(PosY, defaultPos.Y)), size);

		wg2DRenderer tr = g.theRenderer;
		tr.BeginSpriteBatch();
		bool shadows = tr.bForceDropShadows;
		pos = Ui.Drag("poultry_hud_ui", new Rectangle(pos.X + 20, pos.Y + 8, W - 76, 40), pos, size, p =>
		{
			PoultryMod.Ctx.Settings.SetInt(PosX, p.X);
			PoultryMod.Ctx.Settings.SetInt(PosY, p.Y);
		});
		int x = pos.X, y = pos.Y;
		Ui.Area("poultry_hud_ui_bg", new Rectangle(x, y, W, h), null, Ui.WindowLevel - 2);

		// Frame: back (stretched), top, bottom, exactly like the hero's vitals.
		Ui.Stretch("ook_ui_bag_back", new Rectangle(x, y + 40, W, h - 40 - 30));
		Ui.Stretch("ook_ui_bag_top", new Rectangle(x, y, W, 117));
		Ui.Stretch("ook_ui_bag_bottom", new Rectangle(x, y + h - 65, W, 65));

		// Name plate (black text, like the hero's name), ribbon text, level badge.
		tr.bForceDropShadows = false;
		Ui.TextCentered(Ui.Fit(egg.BirdName, 190), new Rectangle(x, y + 22, W, 20), Color.Black);
		// A summoned bird has the hero's level (the egg keeps the level it had when last called).
		int level = pet?.iLevel ?? g.dungeonMans.iLevel;
		Ui.Text(level.ToString(), x + (level < 10 ? 219 : 216), y + 61, Color.Black);
		tr.bForceDropShadows = true;
		Ui.TextCentered("Gen ^3" + egg.GetGeneration() + "^7   Pluck ^3" + egg.GetChampionPoints(), new Rectangle(x + 22, y + 38, 218, 20), Color.White);

		// Fold arrow (the game's own draggable-window arrows).
		var fold = new Rectangle(x + 22, y + 62, 26, 26);
		ui_mouseFrame ff = Ui.Area("poultry_hud_ui_fold", fold, () =>
		{
			dmAudio.PlayUIFX("ui_click_01");
			Config.Toggle(ExpandedKey);
		}, Ui.WindowLevel + 1);
		Ui.Icon(expanded ? "ui_draggable_button_arrow_up" : "ui_draggable_button_arrow_down", fold, Ui.Hovered(ff) ? Color.White : new Color(220, 210, 190));
		if (Ui.Hovered(ff))
		{
			Ui.Tooltip(expanded ? "Fold" : "Show stats and powers");
		}

		// HP: the game's red bar where the hero's HP bar would be.
		DrawHp(new Rectangle(x + 6, y + 92, 256, 30), pet, info);
		if (!expanded)
		{
			tr.bForceDropShadows = shadows;
			tr.EndSpriteBatch();
			return;
		}

		int t = info.birdStats[0], m = info.birdStats[1], gen = egg.GetGeneration();
		int cy = y + 128;
		int cw = (W - 28) / 3;
		CombatCell("poultry_hud_ui_def", new Rectangle(x + 14, cy, cw, 22),
			t < 0 ? "Block ^3" + -8 * t : t > 0 ? "Parry ^3" + 6 * t : "Block ^30",
			t < 0 ? "^3Block " + -8 * t + "^7\nBlock rating, like the hero's (8 per Tough point)"
			: t > 0 ? "^3Parry " + 6 * t + "^7\nParry rating, like the hero's (6 per Swift point)"
			: "^3No Block or Parry^7\nTough gives Block, Swift gives Parry.");
		CombatCell("poultry_hud_ui_rng", new Rectangle(x + 14 + cw, cy, cw, 22),
			m < 0 ? "Range ^3" + (2 - m) : "Melee",
			m < 0 ? "^3Range " + (2 - m) + " tiles^7\nShoots from 2 + Ranged tiles" : "^3Melee^7\nFights up close" + (m >= 3 ? "\nPress the Attack (Melee 3+)" : ""));
		CombatCell("poultry_hud_ui_dmg", new Rectangle(x + 14 + 2 * cw, cy, cw, 22),
			"Dmg ^3+" + 20 * (gen - 1) + "%",
			"^3Damage +" + 20 * (gen - 1) + "%^7\n+20% per generation after the first (generation " + gen + ")");
		cy += 30;

		int steaks = PoultryCore.BagFoods().Count(f => f.Useful);
		Ui.BoxButton("poultry_hud_ui_feed", new Rectangle(x + 18, cy, 64, 32), "Feed " + steaks, () => CoopPanel.Open(0, egg), tooltipText: "Rare steaks and Plumage in your bags");
		Ui.BoxButton("poultry_hud_ui_ascend", new Rectangle(x + 102, cy, 64, 32), "Ascend", () => CoopPanel.Open(2, egg), tooltipText: "+1 generation for Passionate Poultices");
		Ui.BoxButton("poultry_hud_ui_coop", new Rectangle(x + 186, cy, 64, 32), "Coop", () => CoopPanel.Open(0, egg), tooltipText: "All your birds");
		tr.bForceDropShadows = shadows;
		tr.EndSpriteBatch();
	}

	private static void CombatCell(string id, Rectangle r, string text, string tip)
	{
		ui_mouseFrame f = Ui.Area(id, r, null);
		Ui.TextCentered(text, r, Palette.Text);
		if (Ui.Hovered(f))
		{
			Ui.Tooltip(tip);
		}
	}

	private static void DrawHp(Rectangle r, dmPetBird pet, BirdStatInfo info)
	{
		ui_mouseFrame f = Ui.Area("poultry_hud_ui_hp", r, null);
		string tip;
		if (pet != null && pet.maxHealth > 0)
		{
			Ui.GameBar(BarStyle.Health, r, pet.health / (float)pet.maxHealth, pet.health + "/" + pet.maxHealth);
			tip = "HP " + pet.health + " / " + pet.maxHealth;
		}
		else if (info.bWounded)
		{
			Ui.GameBar(BarStyle.Ire, r, 0f, "Wounded");
			tip = "Wounded: heal it at Professor Hatch (or with a poultice).";
		}
		else
		{
			Ui.GameBar(BarStyle.Health, r, 0f, "In its egg");
			tip = "Blow the bird whistle to call it.";
		}
		if (Ui.Hovered(f))
		{
			Ui.Tooltip(tip);
		}
	}

	public const int StatRowHeight = 44;

	// One stat axis: "Tough 5 ............ Swift" on one line, the game's slider (red gem = the value) below.
	public static void StatRow(string id, int x, int y, int w, string neg, string pos, int value, Color negC, Color posC, string help)
	{
		ui_mouseFrame f = Ui.Area(id, new Rectangle(x, y, w, StatRowHeight - 4), null);
		Ui.Text(neg + (value < 0 ? "  ^3" + -value : ""), x, y, value < 0 ? negC : Palette.TextMuted);
		Ui.TextRight((value > 0 ? "^3" + value + "^7  " : "") + pos, x + w, y, value > 0 ? posC : Palette.TextMuted);
		Ui.Slider(new Rectangle(x, y + 20, w, 18), value, -5, 5);
		if (Ui.Hovered(f))
		{
			Ui.Tooltip(help);
		}
	}

	public static string ToughSwiftHelp(int t, int pluck)
	{
		int hp = (t < 0 ? -10 * t : -4 * t) + pluck;
		return (t < 0 ? "^3Tough " + -t + "^7" : t > 0 ? "^3Swift " + t + "^7" : "^3Balanced^7")
			+ "\nMax HP " + (hp >= 0 ? "+" : "") + hp + "%"
			+ "\nBlock " + Math.Max(0, -8 * t) + "   Parry " + Math.Max(0, 6 * t)
			+ "\nAt 3+ on both axes the bird gains stat powers.";
	}

	public static string RangedMeleeHelp(int m, int gen) =>
		(m < 0 ? "^3Ranged " + -m + "^7\nShoots from " + (2 - m) + " tiles" : m > 0 ? "^3Melee " + m + "^7\nFights up close" : "^3Balanced^7\nFights up close")
		+ "\nDamage +" + (20 * (gen - 1)) + "% from generation"
		+ (m >= 3 ? "\nPress the Attack (Melee 3+)" : "");
}
