# Fan Selector — Autodesk App Store listing (Vixeldorf build, v2.0.0)

Facts pulled from `version.json`, `FanSelector/App.cs`, `Installer/Product.wxs`,
`branding.props` and the command/dialog code. Support email and SLA are the
standing Vixeldorf defaults — confirm before submitting.

## App Name
Fan Selector

## App Short Description
Search a fan manufacturer's type catalogue by air flow and pressure inside Revit, then place the matching type — creating it from the catalogue if the project does not have it yet.

## App Description
Fan Selector turns the type catalogue that already comes with a fan family into a searchable selection tool. State the duty — air flow and pressure — and it lists the types that can deliver it, ranked, with the figures that matter beside them. Pick one and that exact type is placed, built from its catalogue line first if the project does not have it yet.

No separate database to maintain, no proprietary family to adopt: the catalogue is the database.

HOW IT WORKS
A Revit type catalogue is the .csv beside a .rfa that tells Revit which types the family offers. It already holds the numbers: one row per type, columns for air flow, pressure, motor power, speed. Fan Selector reads that same file, so the selection data and the family's type list cannot drift apart.

Setting up a family takes one pass through Options and has two halves. First, which column carries which figure: each gets a dropdown of the catalogue's columns, filtered to those whose declared unit fits, with the likely match pre-selected. Second — optional — which family parameter each figure is written onto when a fan is placed. That second half is what makes it work with real fan families, which normally declare air flow and pressure as instance parameters holding nothing until something fills them.

KEY FEATURES
- Searches the WHOLE catalogue, not only the types already loaded. A family may offer two hundred types where a project has a handful; inserting a match creates just that one type from its catalogue line, exactly as Revit would have on load.
- Works with any Mechanical Equipment fan family that has a type catalogue.
- Two-part mapping per family — read from a catalogue column, write onto a family parameter — with unit-aware dropdowns and automatic guessing, so a new family usually means confirming what was proposed.
- Six figures: air flow, pressure, motor power, speed, efficiency / specific fan power, sound power. The first two drive the search; the rest are shown so you can judge the choice.
- Any number of extra columns: frame size, weight, order code, whatever the file carries.
- Ranking, not just filtering. Sort by closeness to the duty, by lowest specific fan power, by quietest, or by lowest motor power — whatever actually decides the selection.
- Units come from the catalogue's own header and are shown in the project's units, so a file in CFM and inches of water works as well as one in m³/h and Pa. An unrecognised unit is reported, not assumed.
- Adjustable tolerance on both figures, with a default you set once.
- Places the matching type on the correct level, with an optional mounting offset above the point you click.
- A picture per family beside the results — a photograph of the machine reads faster than a wireframe. Revit's type preview stands in when a family has none.
- Optional duct stub, per family: placing the fan also grows a short round duct off one connector, at that connector's own diameter, and caps it with an air terminal carrying the air flow you searched for — so the fan arrives with a real connected duty instead of as an unconnected object. The system type follows the fan's connector unless you pick one.
- When a catalogue has nothing suitable to map, the dialog says so and why instead of showing an empty list, and a "Copy details" button puts its columns and the family's parameters on the clipboard.
- The mapping is stored once per machine, shared by every Revit version and project on it.
- Three example fan families with catalogues and pictures are installed with it.

WHO IT'S FOR
MEP engineers and HVAC modellers who select fans in Revit and are tired of doing it in a manufacturer's separate program and then hunting for the matching type by hand. Especially useful for an office with its own fan library: point each family at its catalogue once and the whole library becomes searchable.

Supported on Revit 2022 through 2026 (single installer, pick your versions during setup).

## App Version
**Version Number:** 2.0.0

**Version Description:** Fan selection now works with any fan family and its type catalogue, through a mapping you set up in the new Options dialog — the previous version understood only its own bundled families and their fixed parameter names. The search covers every type in the catalogue rather than only the ones already loaded, and inserting a match creates that single type from its catalogue line. Adds efficiency / specific fan power and sound power to the figures shown, unlimited user-chosen extra columns, and ranking by closeness, SFP, noise or motor power. Rebuilt interface, and placement now hosts the fan on the correct level instead of a fixed height.

## General Usage Instructions
1. Click Fan Selector on the Vixeldorf ribbon tab. On the very first run it opens Options by itself, because no family is set up yet; after that, Options is a button inside the window.
2. In Options, pick a fan family from the list of Mechanical Equipment families loaded in the project and press Add.
3. Point it at the family's type catalogue — the .csv beside the .rfa — and, if you like, at a picture of the fan. For the families that come with the add-in both are filled in already; otherwise use Browse.
4. Check the mapping. For each figure, the left dropdown is the catalogue column it is read from and the right one is the family parameter it is written onto when a fan is placed. Air flow and pressure columns are required, everything else is optional. Add any extra columns you want shown.
5. Optionally switch on "Grow a duct stub and cap it" for this family, and name the air terminal that caps it. Click OK.
6. Back in the window: choose the family, enter the air flow and pressure you need — in the project's own units, shown beside each box — set the tolerance, and press Search. The matching types are listed with their figures, how far each is off the requested duty, and whether it is in the model yet. Change "Sort by" to order them by efficiency, noise or motor power instead of by closeness.
7. Select a row and click Insert fan (or double-click the row), then click the point in the view where the fan goes. If the type is not in the model yet it is created from its catalogue line first, then placed, with the mapped figures written onto it — and with the stub and its terminal if that is switched on.

## Installation/Uninstallation
Fan Selector installs via a standard Windows Installer (MSI) package. Files are copied to %ProgramData%\Vixeldorf\FanSelector\, and a .addin manifest is registered for each supported Revit version you select during setup, under %ProgramData%\Autodesk\Revit\Addins\{year} for 2022 through 2026. The example fan families, their type catalogues and their pictures are installed to %ProgramData%\Vixeldorf\FanSelector\Sample Families\. Restart Revit after installing to load the "Vixeldorf" ribbon tab.

To uninstall, use Windows Settings > Apps > Installed apps (or Control Panel > Programs and Features), select "Vixeldorf Fan Selector", and click Uninstall. This removes the DLL, the example families and all per-version .addin manifests. Restart Revit afterward to confirm the ribbon tab is gone.

## Support Information
For support, contact us at yoav@vixeldorf.com or via <a href="https://www.vixeldorf.com">vixeldorf.com</a>. Please include your Revit version, the Fan Selector version (shown in the Fan Selector window title), the fan family and catalogue you are selecting from, and a short description or screenshot of the issue. We aim to respond within 5 business days.

## Additional Information
<b>Supported Revit versions:</b> 2022, 2023, 2024, 2025, 2026 (single build, 64-bit).<br>
<b>Category:</b> Mechanical / HVAC.<br>
<b>Requirements:</b> No third-party dependencies. A fan family with a Revit type catalogue (.csv) beside it.<br>
<b>Data storage:</b> Fan Selector adds nothing to your model. It reads performance figures from the family's own type catalogue file, and the only things it writes are the family instance you asked it to place and the figures you mapped onto that instance. The mapping itself lives in a small readable JSON file next to the add-in, shared by every Revit version on the machine, so models stay fully usable if the add-in is removed.<br>
<b>Units:</b> Catalogue units are read from the file's own header and converted by Revit; everything shown follows the project's unit settings.<br>
<b>Publisher:</b> <a href="https://www.vixeldorf.com">Vixeldorf</a>.

## Known Issues
- Only families in the Mechanical Equipment category are offered.<br>
- An unrecognised unit token leaves values exactly as written; the dialog says so rather than guessing.<br>
- A type the project does not have is created by copying a loaded type of the family and applying the catalogue line, so the family needs at least one type loaded. Columns naming an instance rather than a type parameter cannot be set on the new type, and are listed when that happens.<br>
- Listing the family parameters to write onto opens the family definition briefly; in-place and system families cannot be read that way.<br>
- The mapping is stored per machine, not per project.<br>
- Fans are placed hosted on a level; face-hosted families are not supported.<br>
- The duct stub needs its air terminal family loaded, and a duct type and system type to exist. If the terminal's Flow turns out to be calculated by the system rather than settable, the stub is still built and it is reported.

## Learn More Url
https://www.vixeldorf.com
