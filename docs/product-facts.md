# Fan Selector — product facts

Plain-text source of truth for **all** product copy: the website, the Autodesk
App Store listing, and anything else. No HTML, no store-specific formatting —
each consumer styles this its own way.

- The App Store submission is `appstore-listing.md`, filled in from this file.
  When a fact changes, change it **here first**, then re-derive that one.
- Technical facts here are read from the code, not written from memory:
  `version.json`, `branding.props`, `App.cs`, `Core/CatalogFile.cs`,
  `Core/FanSearch.cs`, `Core/FanPlacer.cs`, `Core/DuctStub.cs`,
  `Core/Silencers.cs`, `Core/FanSettings.cs`, `Installer/Product.wxs`.

---

## Identity

- **Name:** Fan Selector
- **Publisher:** Vixeldorf
- **Category:** Revit add-in — MEP / HVAC equipment selection
- **Current version:** 2.0.0
- **Platform:** Autodesk Revit 2022, 2023, 2024, 2025, 2026 (Windows, 64-bit)
- **Interface language:** English
- **Website:** https://www.vixeldorf.com
- **Support:** yoav@vixeldorf.com, answered within 5 business days
- **Source:** https://github.com/ioavant/FanSelector

## One-liner

Select a fan by duty inside Revit — the manufacturer's own type catalogue is the
database.

## Short description (one sentence)

Fan Selector searches a fan family's type catalogue by air flow and pressure,
then places the type that delivers it — loading it from the family file when the
project does not have that size yet.

## What it does

State the duty: air flow and pressure, in the project's own units. Fan Selector
reads the manufacturer's type catalogue — the `.csv` that already sits beside the
`.rfa` — and lists every duty point that can deliver it, ranked, with the motor
power, speed, specific fan power and sound power beside each one. Pick a row,
click a point in the view, and that exact type is placed with the figures written
onto it.

The type does not have to be in the project. A catalogue holds a couple of
hundred duty points; a project typically holds a handful of sizes. When the
chosen size is missing, the family is loaded from its own file so that Revit
builds the type from the catalogue line — nothing already in the model is
touched or overwritten.

Nothing has to be prepared beyond one pass through Options per family: which
catalogue column carries which figure, and which family parameter each figure is
written onto. There is no separate database to maintain and no proprietary
family to adopt.

## Feature bullets

- Searches the whole catalogue — every duty point in the file, not the few sizes
  already loaded into the model.
- Works with any Mechanical Equipment fan family that has a Revit type catalogue.
- Two-part mapping per family: read a figure from a catalogue column, write it
  onto a family parameter. Both dropdowns are filtered by unit and pre-filled
  with the likely match, so setting up a new family is mostly confirming what was
  proposed.
- Six figures: air flow, pressure, motor power, speed, efficiency / specific fan
  power, sound power. The first two drive the search; the rest are there to judge
  the choice by.
- Any number of extra columns — frame size, weight, order code, whatever the file
  carries.
- Ranking, not just filtering: by closeness to the requested duty, by lowest
  specific fan power, by quietest, or by lowest motor power.
- Units come from the catalogue's own header and are displayed in the project's
  units. A file written in CFM and inches of water works exactly like one in m³/h
  and Pa. An unrecognised unit token is reported, never assumed.
- Adjustable tolerance on both figures.
- Optional duct stub: the fan arrives with a short round duct off one connector,
  at that connector's own diameter, capped by an air terminal carrying the air
  flow that was searched for — a fan with a real connected duty instead of an
  unconnected object. The duct system type can be chosen on the main window, or
  left to follow the fan's own connector.
- Optional inlet and outlet attenuators, in duct diameters (0, 1 or 2), for
  families that build them themselves. The control appears only for a family that
  declares the two parameters, so it stays out of the way of every other family.
- A photograph of the fan beside the results — axial, centrifugal or in-line,
  chosen from the family name, with a general one for anything else. All four
  ship inside the add-in; there is nothing to pick.
- When a catalogue cannot be mapped, the dialog says which column or parameter is
  the problem instead of showing an empty list, and one button copies the
  catalogue's columns and the family's parameters to the clipboard.
- The mapping is stored once per machine and shared by every Revit version and
  every project.
- Three example fan families with their catalogues are installed alongside.

## How it works (the mechanic worth explaining once)

A Revit type catalogue is a `.csv` beside a `.rfa`, one line per duty point, its
header declaring the unit of every column. What makes fan catalogues awkward is
that **a line is not a Revit type**: the designation `710/9/30/5Z` is a duty
point, while the family holds one type per physical size — `710` — and several
duty points share it.

So Fan Selector keeps the two apart. It searches the catalogue lines, resolves
each matching line to the type that carries it, and writes the line's figures
onto the fan it places. Which column names the type is a per-family setting,
guessed by testing each column against the types the project already has and
reported as "this column matched 15 of 203 lines" so the guess can be checked.

The second half of the mapping exists because real fan families declare air flow
and pressure as instance parameters that hold nothing until something fills them.
Writing the selected duty onto the placed instance is what turns a catalogue
lookup into a fan that schedules and connects correctly.

## Who it is for

MEP engineers and HVAC modellers who select fans in Revit and are tired of doing
the selection in a manufacturer's separate program and then hunting for the
matching Revit type by hand. Especially useful for an office with its own fan
library: point each family at its catalogue once, and the whole library becomes
searchable by duty.

## Requirements

- Autodesk Revit 2022–2026.
- A fan family (`.rfa`) in the Mechanical Equipment category, with its Revit type
  catalogue (`.csv`) beside it under the same name.
- For the duct stub: an air terminal family loaded in the project, plus a duct
  type and a duct system type.

## Options reference

| Option | Default | Effect |
|---|---|---|
| Fan family | not set — Options opens by itself on the first run | Which family the catalogue belongs to; several families can be set up and switched between on the main window |
| Catalogue file | the `.csv` beside the `.rfa` | Where the duty points are read from; pre-filled for the example families |
| Type column | guessed, with the match count shown | Which catalogue column names the Revit type |
| Figure mapping | guessed per figure | Per figure: the catalogue column it is read from, and the family parameter it is written onto |
| Extra columns | none | Further catalogue columns to show in the results |
| Duct stub | off | Whether this family's fans grow a duct stub by default, the air terminal family that caps it, its connection-size parameter, and the stub length |
| Duct type | first suitable | The duct type used for the stub |
| Tolerance | 10 % | How far from the requested duty a result may be |

Options are stored in a settings file in the add-in's installation folder, so
they are set once for every Revit version and every project on the machine.

## Where data lives

- The mapping and all options: a readable JSON file next to the installed add-in
  (`FanSelector.settings.json`), falling back to the user's own AppData folder
  when that location is not writable.
- In the model: nothing beyond the fan the user asked for and the figures mapped
  onto it.

No Extensible Storage, no shared parameters added to the template, no external
database and no cloud service. Models stay fully usable if the add-in is
removed — what it leaves behind is ordinary Revit mechanical equipment. The only
network request the add-in ever makes is the optional check for a newer published
version.

## What it never does

- Never overwrites a type the project already has; an existing size is used as it
  stands.
- Never edits the catalogue or the family file.
- Never guesses a unit it does not recognise — it reports it instead.
- Never installs an update by itself: a notice appears, the download is yours to
  start.

## Install / uninstall

Windows Installer package (MSI), not Autodesk App Manager packaging. The library
installs to `C:\ProgramData\Vixeldorf\FanSelector\`, the example families to
`Sample Families` beneath it, and one manifest per selected Revit version to
`C:\ProgramData\Autodesk\Revit\Addins\<year>\`. The installer asks which Revit
versions to register. Administrator rights are required. Uninstall through
Windows Settings > Apps; fans already placed are ordinary Revit mechanical
equipment and stay untouched.

## Known limitations

- Only families in the Mechanical Equipment category are offered.
- Placing a size the project lacks loads the family from its `.rfa`, so that file
  must sit beside the `.csv` under the same name.
- An unrecognised unit token in the catalogue header leaves those values exactly
  as written, and says so.
- Listing the family parameters to write onto opens the family definition
  briefly; in-place families and system families cannot be read that way.
- The mapping is stored per machine, not per project.
- Fans are placed hosted on a level; face-hosted fan families are not supported.
- The duct stub needs its air terminal family loaded. An air terminal whose flow
  Revit calculates from the system cannot be set directly, and that is reported.
- Attenuators appear only for a family that builds them itself from the two
  parameters `D silencer In` and `D silencer Out`; of the shipped families that
  is the axial fan.
- Revit 2027 is not supported yet — it needs a separate .NET 10 build.

## Assets

What exists, in `FanSelector/Resources/`:

- Ribbon icon, 16×16 and 32×32 (`fanselector_16.png`, `fanselector_32.png`).
- The four fan photographs shown beside the results, 640 px
  (`fan_axial.jpg`, `fan_centrifugal.jpg`, `fan_inline.jpg`, `fan_generic.jpg`).

What a listing or a product page still needs, and cannot be derived from the
code:

- A 200×200 product icon. AC Link has `ac_icon_200_vixeldorf.png`; Fan Selector
  has nothing above 32×32.
- Screenshots from a real project: the results table with a duty entered, the
  Options mapping dialog, and a placed fan with its duct stub and attenuators.

## Version history

### 2.0.0

- Any fan family can be used, through a mapping set up once per family in the new
  Options dialog. The previous generation understood only its own bundled
  families and their fixed parameter names.
- The search covers every duty point in the catalogue rather than only the sizes
  already loaded, and placing a missing size loads the family so Revit builds the
  type from the catalogue.
- Efficiency / specific fan power and sound power added to the figures shown,
  plus any number of user-chosen extra columns.
- Results can be ranked by closeness to the duty, specific fan power, sound power
  or motor power.
- Optional duct stub with an air terminal carrying the selected air flow, and a
  duct system type chosen on the main window.
- Optional inlet and outlet attenuators, in duct diameters, for families that
  build them.
- Rebuilt interface, and fans are placed on the correct level instead of at a
  fixed height.
