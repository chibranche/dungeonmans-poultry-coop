# Dungeonmans Poultry

Battle Poultry mod for Dungeonmans (Steam, .NET 8): the Coop window, the bird status window, the breeding gene fix and
Professor Hatch shortcuts. Requires [Dungeonmans ModKit](https://github.com/chibranche/dungeonmans-mod-toolkit) (included in the release zip).
It changes the game through Harmony patches at runtime; `Dungeonmans.dll` is never modified.

It works on its own, without the QoL mod. Its **Coop button** sits in the top bar that ModKit shares between mods
(drag it sideways to reorder the bar).

## Features

**Professor Hatch (poultry corral)**
- Bumping into Hatch opens the work menu directly. The first-meeting tutorial is unchanged.
- The Ovomansy FAQ that used to sit behind the greeting is now an entry in the work menu.
- The deposit list has a **Deposit all (keeps equipped egg)** side action, and the withdraw list has a **Withdraw all** side action. Press Right or click to reach them.
**The Coop (poultry)**
- Open it from Professor Hatch's menu ("Open the Coop"), the Coop button in the top bar, or the poultry status window. Away from the Academy, it only works with the birds and items in your bags.
- **Birds:** every hatched bird with its details: stat map, Pluck, Generation, Level, powers (hover for gene counts), genes not learned yet and forgotten powers.
  - One-click Store, Take, Equip, **Summon / Unsummon** (works like the Battlebird Whistle, no whistle needed), Heal all, and Set free (click twice). The heart next to the name sets the favorite.
  - "forget" next to each power costs 1 Pruning Poultice.
  - Feed list: rare steaks and Plumage from your bags and the Pantry ("All foods" adds plain steaks), each showing its effect and the chance to learn its power.
- **Breed:** pick two parents. Before you breed, it shows the egg's exact generation, Pluck and stat scores, and the chance of each power (hover a power for its gene count). Both parents are kept.
- **Ascend:** raises a bird's generation without breeding (+20% damage), paid with 2 to 8 Passionate Poultices. Pluck after = Pluck − ⌊Pluck × 0.5^N⌋, and stats, powers and genes are kept.
- **Pantry** (a setting, turns off leaderboard uploads): Academy storage for steaks, Plumage of Power, poultices, rename cards and protection potions. It holds 30 items to start (single items, not stacks).
  - It's kept in the game's own save, so it survives hero deaths.
  - Store all, Take (Shift = whole stack), and Sell steaks for Feathers (1 / 5 / 25 / 125 by rarity). Hover an item for its description.
- **Feathers:** an Academy currency. You earn 5 for each bird you set free, 5 for each Passionate Poultice you sell, or by selling steaks.
  - Buy / Sell: +10 Pantry space (25, then 50, 75… Feathers), or a Passionate Poultry Poultice for 50 Feathers. Pruning Poultices can't be bought.
- **Breeding bug fix (a setting):** vanilla breeding overwrites the first parent's gene table with the egg's. With the fix, both parents keep their genes.
- **Favorite bird:** the heart next to a bird's name; it is listed first. If you also use the QoL mod, its Prepare hero equips this bird.

**Poultry status window** (bottom right, above your hero's vitals)
- Drawn like your hero's vitals: name, level, generation and Pluck, HP (or Wounded), then Block or Parry, Range and Damage (hover each for details).
- Drag it by the name plate. The arrow folds it. **Feed**, **Ascend** and **Coop** open the Coop on that bird.

## Settings

In **Mod Settings**, under "Dungeonmans Poultry":
- The mod's own switch turns everything on or off (at the next launch).
- **Bug fix: breeding genes bug**: on by default. Off gives the vanilla behavior.
- **Pantry (disables leaderboards)**: the Pantry tab, its storage and the Feathers shop. While it is on, no score is
  sent to the Steam leaderboards, since stored poultices and steaks are an edge. Switching it off keeps what is
  stored; it comes back when you switch it on again.

Settings are saved to `<game>\Mods\Poultry\config.json`. Your Pantry, Feathers and favorite bird are stored in the
game's own save.

## Install

Download `DungeonmansPoultry-<version>-with-ModKit.zip` from the Releases page, extract it into the Dungeonmans game
folder (Steam: right-click the game > Manage > Browse local files) and double-click `ModKit-Install.bat`.
Run it again after a Steam update. Details: `ModKit-README.txt` in the zip. If you already use the QoL mod, extracting
over it is fine: both zips carry the same ModKit.

Log: `<game>\ModKit\modkit.log`, where each part is listed as `[poultry] ok` or `[poultry] FAIL`.

## Building from source

Needs the .NET 8 SDK, the game, and [Dungeonmans ModKit](https://github.com/chibranche/dungeonmans-mod-toolkit)
installed in the game folder (the project references `<game>\ModKit\DungeonmansModKit.dll`).

```powershell
.\install.ps1 [-GameDir <path>]   # build and copy into <game>\Mods\Poultry
.\build-release.ps1               # dist\DungeonmansPoultry-<v>-with-ModKit.zip (uses the toolkit's release zip)
```

## License

MIT, see [LICENSE](LICENSE).
