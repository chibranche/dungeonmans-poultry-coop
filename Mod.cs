using System;
using System.Reflection;
using DungeonMans;
using DungeonmansModKit;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace DungeonmansPoultry;

// Forwards to ModKit's shared log (ModKit\modkit.log), prefixed "[poultry]".
internal static class Log
{
	public static void Write(string msg) => PoultryMod.Ctx?.Log.Write(msg);
}

// ModKit entry point: registers settings, patches, windows and input.
public sealed class PoultryMod : IMod
{
	internal static ModContext Ctx;

	public void Load(ModContext ctx)
	{
		Ctx = ctx;
		Config.Register(ctx.Settings);

		ctx.Patch("Hatch: skip greeting", HatchPatches.RegisterSkipGreeting);
		ctx.Patch("Hatch: FAQ in main menu", HatchPatches.RegisterFaqInMenu);
		ctx.Patch("Hatch: deposit/withdraw all", HatchPatches.RegisterBulkTransfer);
		ctx.Patch("Poultry: breeding bug fix", PoultryCore.Register);
		ctx.Patch("Pantry: leaderboard block", PoultryCore.RegisterLeaderboardBlock);

		// The Coop counts as an open menu for every mod.
		Ui.AddModal(() => CoopPanel.IsOpen);

		Events.Input += g =>
		{
			if (!CoopPanel.IsOpen)
			{
				return false;
			}
			CoopPanel.HandleInput(g);
			return true;
		};
		Events.HeroChanged += (g, hero) => CoopPanel.Reset();
		// Screenshot scenes (developer tool, inactive for players). poultry_enter_academy goes first so the Coop shows
		// the whole Academy flock (capture sessions never save).
		DevCapture.Scene("poultry_enter_academy", EnterAcademy);
		DevCapture.Scene("poultry_academy", () => { });
		DevCapture.Scene("poultry_birds", () => CoopPanel.Open(0), CoopPanel.ForceClose);
		DevCapture.Scene("poultry_breed", () => { CoopPanel.Open(1); CoopPanel.DevPickParents(); }, CoopPanel.ForceClose);
		DevCapture.Scene("poultry_ascend", () => CoopPanel.Open(2), CoopPanel.ForceClose);
		DevCapture.Scene("poultry_pantry", () => CoopPanel.Open(3), CoopPanel.ForceClose);
		DevCapture.Scene("poultry_hud", () => { });
		DevCapture.Scene("poultry_docs", () => Ui.OpenModDocs("poultry"), Ui.CloseModSettings);
		Events.DrawOverlay += g =>
		{
			if (g.dungeonMans == null || g.bShowMainMenu)
			{
				return;
			}
			PoultryHud.Draw(g);
			if (!CoopPanel.IsOpen && !Ui.IsBusy(g))
			{
				// The Coop button in ModKit's shared top bar.
				TopBar.Add("poultry.coop", 64, (r, clickable) =>
				{
					g.theRenderer.BeginSpriteBatch();
					Ui.HangingButton("poultry_coop_open", r, "", clickable ? () => CoopPanel.Open() : (Action)null,
						tooltipText: "The Coop\nBirds, feeding, breeding, Ascend, Pantry\nDrag to move", stackLevel: 6);
					Ui.IconTrimmed("petbird_center", new Rectangle(r.X + 14, r.Y + 2, 36, 28));
					g.theRenderer.EndSpriteBatch();
				}, priority: 40);
			}
			if (CoopPanel.IsOpen)
			{
				CoopPanel.Draw(g);
			}
		};
	}

	// Same gateway the game builds when it sends a hero home after a new overworld.
	private static void EnterAcademy()
	{
		dmGame g = Dm.Game;
		if (Dm.InAcademy || g.dungeonMans == null)
		{
			return;
		}
		dmGateway gate = (dmGateway)g.SpawnActorFromArchetype("default_gateway");
		gate.worldArchetypeToLoad = "dmAcademy";
		gate.areaGenerationData = "academy_data";
		gate.saveName = "dungeonmans_academy";
		g.currentActivatedGateway = gate;
		g.bChangeMapThisFrame = true;
	}
}

internal static class Mod
{
	public static HarmonyMethod Hook(Type type, string name) => Patching.Hook(type, name);

	public static MethodInfo Target(Type type, string name) => Patching.Method(type, name);
}
