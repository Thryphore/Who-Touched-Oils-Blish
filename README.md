# Who Touched Oils (Blish HUD)

A [Blish HUD](https://blishhud.com/) module for Guild Wars 2 that summarizes **who stepped in Deimos Rapid Decay (oils)** after each pull — from ArcDPS combat logs.

**Website:** [https://thryphore.github.io/Who-Touched-Oils/](https://thryphore.github.io/Who-Touched-Oils/)

## Features

- After-fight summary from ArcDPS `.zevtc` / `.evtc` logs (no ArcDPS ↔ Blish bridge required)
- **First to touch each oil** (per oil puddle)
- **Total oil hits** per player
- Ignores hits while **downed or dead**
- Auto-watches new Deimos logs; manual “Parse latest” button

## Requirements

- Guild Wars 2
- [Blish HUD](https://blishhud.com/) 1.2+
- [ArcDPS](https://www.deltaconnected.com/arcdps/) with combat logging enabled

## Install

1. Download the latest `DeimosOilSummary.bhm` from [Releases](https://github.com/Thryphore/Who-Touched-Oils-Blish/releases) (or build it — see below).
2. Copy it into your Blish `modules` folder, e.g.  
   `Documents\Guild Wars 2\addons\blishhud\modules\`
3. Restart Blish HUD → Settings → Modules → enable **Deimos Oil Summary**.

## Build

```powershell
dotnet build DeimosOilSummary\DeimosOilSummary.csproj -c Release
```

Output: `DeimosOilSummary\bin\Release\net48\DeimosOilSummary.bhm`

## Use

1. Run a Deimos pull (kill or wipe) so ArcDPS writes a log under  
   `…/addons/arcdps/arcdps.cbtlogs/Deimos (17154)/`
2. The panel opens with first-touches and hit totals
3. Corner icon toggles the panel; **Parse latest Deimos log** re-reads the newest file

## License

MIT
