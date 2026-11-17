# TaskBarGame

A little farming game that lives just above your Windows taskbar while you work.

A farmer walks along a strip of crop plots, harvesting whatever is ripe. You earn coins, level up
your **Farming** skill and your **Garden**, unlock new crops and buy more plots. You can also click
crops yourself to farm faster.

## Running it

You need **Windows 10 or 11** and the free [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet run --project src/TaskBarGame.App
```

Or open `TaskBarGame.sln` in Visual Studio 2022/2026 and press **F5**.

To make a standalone `TaskBarGame.exe` you can share:

```bash
dotnet publish src/TaskBarGame.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

GitHub Actions also builds the `.exe` on every push. Download it from the **Actions** tab under **Artifacts**.

## How to play

| Do this | What happens |
|---|---|
| Click a **ripe** crop (it sparkles) | Harvest it yourself: +25 Harvest Fortune and you see the XP you earned |
| Click a **growing** crop | Tend it: it grows 4% faster per click |
| Right-click a plot | Choose what to plant there |
| Click the **farmer's head** or the **coins** | Open the garden panel |
| Right-click the farmer or the strip | Quick menu: sell crops, strip size, position, show-on-hover, quit |
| Click the **lock** | Buy the next plot (when you have the coins and Garden level) |

The farmer harvests ripe crops automatically, even while the game is closed: at half speed, for up to
12 hours. When you come back, a popup tells you what they did.

### Progression

- **Farming skill (levels 0-50):** XP from every harvest. Each level gives **+4 Harvest Fortune** and coins.
- **Harvest Fortune:** every 100 Fortune is one extra set of crops per harvest (150 = 2.5x crops).
- **Garden level (1-15):** Garden XP from every harvest and every collection tier. Garden levels unlock
  new crops and let you buy more plots (up to 12).
- **Collections:** every crop item you harvest counts. Each of the 10 tiers per crop gives +2 Fortune for
  that crop and Garden XP.
- **Market:** sell crops for coins, or turn on auto-sell.

| Crop | Garden level | Grows in | Sells for |
|---|---|---|---|
| Wheat | 1 | 30s | 2 |
| Carrot | 2 | 40s | 2 |
| Potato | 3 | 50s | 3 |
| Sugar Cane | 4 | 1m | 5 |
| Pumpkin | 5 | 1m 30s | 18 |
| Melon | 6 | 1m 40s | 5 |
| Cocoa Beans | 8 | 2m | 15 |
| Cactus | 10 | 2m 30s | 16 |
| Mushroom | 12 | 3m | 35 |
| Moonbloom | 15 | 8m | 160 |

### Settings

In the panel's **Settings** tab (or the right-click menu):

- **Only show it when I hover:** the strip tucks away and only the farmer stays visible, with a badge
  showing how many crops are ready. Hover over the farmer to show the strip.
- **Size:** Small / Normal / Large.
- **Position:** Left / Center / Right of the screen.
- Farmer auto-harvest, auto-sell, floating numbers, hide during fullscreen apps, start with Windows.

Your save file is `%APPDATA%\TaskBarGame\save.json` (with a `.bak` backup). If something breaks,
details are written to `%APPDATA%\TaskBarGame\error.log`.

## Changing the game

| I want to change... | Edit |
|---|---|
| Crop speed, price, XP, unlock level | `src/TaskBarGame.Core/Crops.cs` |
| Level XP tables, plot prices, collection tiers | `src/TaskBarGame.Core/Progression.cs` |
| Harvest/tend/offline rules | `src/TaskBarGame.Core/GameEngine.cs` |
| How the farmer walks | `src/TaskBarGame.Core/FarmerAgent.cs` |
| **Pixel art** (crops, farmer, icons) | `src/TaskBarGame.Core/Assets/sprites.txt`, a plain text file with one letter per pixel |
| The strip above the taskbar | `src/TaskBarGame.App/StripWindow.xaml.cs` |
| The garden panel | `src/TaskBarGame.App/PanelWindow.xaml` + `ViewModels/` |
| Colours and button styles | `src/TaskBarGame.App/Theme.xaml` |

Run the tests after changing game rules:

```bash
dotnet test
```

### Project layout

```
src/TaskBarGame.Core/     Game rules, saving and sprite loading. No Windows code, fully unit tested.
src/TaskBarGame.App/      The Windows (WPF) app: the taskbar strip, the panel, Windows integration.
tests/                    xUnit tests for TaskBarGame.Core.
```

## Coming next

- **Visitors:** characters who stop by the garden and ask for crops in exchange for rewards.
- **Composter and upgrades:** turn spare crops into compost, buy better tools and crop upgrades.
