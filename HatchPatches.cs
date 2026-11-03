using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DungeonMans;
using DungeonmansModKit;
using HarmonyLib;

namespace DungeonmansPoultry;

// Professor Hatch (Academy poultry corral).
//  - Bumping Hatch opens the work menu directly instead of "Hello there!" -> "Yeah let's get to it!".
//  - The Ovomansy FAQ that lived behind that greeting moves into the work menu.
//  - Deposit / withdraw lists get "Deposit all" / "Withdraw all" side actions.
internal static class HatchPatches
{
	private const int FaqResponse = 7;

	private static readonly AccessTools.FieldRef<NPCAcademyHatch, Dictionary<string, string>> ResponseTable =
		AccessTools.FieldRefAccess<NPCAcademyHatch, Dictionary<string, string>>("dialogResponseTable");

	private static readonly AccessTools.FieldRef<NPCAcademyHatch, string> CurrentDialogNext =
		AccessTools.FieldRefAccess<NPCAcademyHatch, string>("currentDialogNext");

	private static readonly MethodInfo CheckItemsBeforeOpeningMenu =
		Mod.Target(typeof(NPCAcademyHatch), "CheckItemsBeforeOpeningMenu");

	private static readonly MethodInfo ShowCurrentDialogNextBox =
		Mod.Target(typeof(NPCAcademyHatch), "ShowCurrentDialogNextBox");

	public static void RegisterSkipGreeting(Harmony h)
	{
		h.Patch(Mod.Target(typeof(NPCAcademyHatch), nameof(NPCAcademyHatch.EncroachedBy)),
			prefix: Mod.Hook(typeof(HatchPatches), nameof(EncroachedByPrefix)));
	}

	public static void RegisterFaqInMenu(Harmony h)
	{
		h.Patch(AccessTools.Constructor(typeof(NPCAcademyHatch), Type.EmptyTypes),
			postfix: Mod.Hook(typeof(HatchPatches), nameof(ConstructorPostfix)));
		h.Patch(Mod.Target(typeof(NPCAcademyHatch), "HandleDialogResponse"),
			prefix: Mod.Hook(typeof(HatchPatches), nameof(HandleDialogResponsePrefix)));
	}

	private const string CoopEntry = "Open ^3the Coop^7: every bird, feeding, breeding, Pantry.";


	public static void RegisterBulkTransfer(Harmony h)
	{
		h.Patch(Mod.Target(typeof(NPCAcademyHatch), "ShowDepositBirdsBox"),
			postfix: Mod.Hook(typeof(HatchPatches), nameof(DepositBoxPostfix)));
		h.Patch(Mod.Target(typeof(NPCAcademyHatch), "ShowWithdrawBirdsBox"),
			postfix: Mod.Hook(typeof(HatchPatches), nameof(WithdrawBoxPostfix)));
	}

	private static bool EncroachedByPrefix(NPCAcademyHatch __instance, dmActor encroacher, dmGame theGame, ref bool __result)
	{
		// First meeting keeps the tutorial chain.
		if (encroacher != theGame.dungeonMans || theGame.dungeonMans.GetScriptVar_String("met_hatch") != "true")
		{
			return true;
		}
		// Same path as answering "Yeah let's get to it!" (whistle / first-egg checks, then the main menu).
		CheckItemsBeforeOpeningMenu.Invoke(__instance, null);
		__result = false;
		return false;
	}

	private static void ConstructorPostfix(NPCAcademyHatch __instance)
	{
		Dictionary<string, string> responses = ResponseTable(__instance);
		string done = responses["main_menu_" + FaqResponse];
		responses["main_menu_" + FaqResponse] = "I have some questions about the specifics of ^3Ovomansy^7.";
		responses["main_menu_" + (FaqResponse + 1)] = CoopEntry;
		responses["main_menu_" + (FaqResponse + 2)] = done;
	}

	private static bool HandleDialogResponsePrefix(NPCAcademyHatch __instance, int iResponseNum)
	{
		if (CurrentDialogNext(__instance) != "reply_to_main_menu")
		{
			return true;
		}
		if (iResponseNum == FaqResponse + 1)
		{
			CoopPanel.Open();
			return false;
		}
		if (iResponseNum != FaqResponse)
		{
			return true;
		}
		CurrentDialogNext(__instance) = "ovomansy_faq_main";
		ShowCurrentDialogNextBox.Invoke(__instance, null);
		return false;
	}

	private static void DepositBoxPostfix()
	{
		AddBulkAction("SelectBirdToPlaceInAcademy", "Deposit all (keeps equipped egg)", skipEquipped: true);
	}

	private static void WithdrawBoxPostfix()
	{
		AddBulkAction("SelectBirdToPlaceInInventory", "Withdraw all", skipEquipped: false);
	}

	// The vanilla methods push the scrollbox and return nothing; find it by its select callback.
	// (When there are no birds they show a dialog instead and no box is found.)
	private static void AddBulkAction(string selectMethodName, string label, bool skipEquipped)
	{
		ui_scrollbox box = dmUtilities.theGame.theRenderer.activeScrollboxes
			.LastOrDefault(sb => sb.actionOnSelect?.Method.Name == selectMethodName);
		if (box == null)
		{
			return;
		}
		box.AddSideAction(label, sb => MoveAll(sb, skipEquipped), isRadio: false);
	}

	private static void MoveAll(ui_scrollbox sb, bool skipEquipped)
	{
		// Non-radio side actions get highlighted when clicked; it's a one-shot button, so un-highlight it.
		foreach (ScrollboxSideAction action in sb.sideActions.Where(a => !a.bIsRadio))
		{
			action.bIsActive = false;
		}
		dmItem equipped = dmUtilities.theGame.dungeonMans.GetItemForSlot(ESlot.ES_BirdEggPocket);
		// The vanilla select callback moves one bird and removes it from sb.dataObjects; run it per bird.
		foreach (object bird in sb.dataObjects.ToList())
		{
			if (skipEquipped && (bird == equipped || (bird as dmItem)?.bEquipped == true))
			{
				continue;
			}
			sb.actionOnSelect(bird, 0, sb);
		}
		sb.idxCurrentBox = 0;
		sb.iTopBoxIdx = 0;
		dmAudio.PlayUIFX("ui_confirm");
		// If the list is now empty the scrollbox closes itself next frame and returns to Hatch's menu.
	}
}
