# Fan Selector — Autodesk App Store listing (Vixeldorf build, v2.0.0)

Facts pulled from `version.json`, `FanSelector/App.cs`, `Installer/Product.wxs`,
`branding.props` and the command/dialog code. Support email and SLA are the
standing Vixeldorf defaults — confirm before submitting.

## App Name
Fan Selector

## App Short Description
Search a fan manufacturer's type catalogue by air flow and pressure inside Revit, then place the matching type — loading it from the family file if the project does not have it yet.

## App Description
Fan Selector turns the type catalogue that already comes with a fan family into a searchable selection tool. State the duty — air flow and pressure — and it lists the types that can deliver it, ranked, with the figures that matter beside them. Pick one and that exact type is placed, loaded from the family file first if the project does not have it yet.

There is no separate database to maintain and no proprietary family to adopt. The catalogue is the database.

HOW IT WORKS
A Revit type catalogue is the .csv that sits beside a .rfa and tells Revit which types the family offers. It already holds the numbers: one row per type, columns for air flow, pressure, motor power, speed. Fan Selector reads that same file, so the selection data and the family's own type list can never drift apart — there is only one file.

Setting up a family takes one pass through Options and has two halves. First, which column carries which figure: each figure gets a dropdown of the catalogue's columns, filtered to the ones whose declared unit actually fits, with the likely match pre-selected. Second — optional — which parameter of the family each figure should be written onto when a fan is placed. That second half is what makes the add-in work with real fan families, which normally declare air flow and pressure as instance parameters that hold nothing until something fills them.

KEY FEATURES
- Searches the WHOLE catalogue, not only the types already loaded. A manufacturer's family may offer two hundred types and a project usually has a handful; inserting a match loads just that one type rather than all of them.
- Works with any Mechanical Equipment fan family that has a type catalogue. No bundled database, no required families, nothing to keep in step by hand.
- Two-part mapping per family — read from a catalogue column, write onto a family parameter — with unit-aware dropdowns and automatic guessing, so a new family usually means confirming what was already proposed.
- Six figures: air flow, pressure, motor power, speed, efficiency / specific fan power, and sound power. Air flow and pressure drive the search; the rest are shown so you can judge the choice.
- Any number of extra columns: point them at any other catalogue column you want beside the results — frame size, weight, order code, whatever the file carries.
- Ranking, not just filtering. Sort matches by closeness to the requested duty, by lowest specific fan power, by quietest, or by lowest motor power — whatever actually decides the selection.
- Units are read from the catalogue's own header and shown in the project's units, so a file in CFM and inches of water works as well as one in m³/h and Pa. A unit the add-in does not recognise is reported rather than quietly assumed.
- Adjustable tolerance, as a percentage on both figures, with a default you set once.
- Places the matching type on the correct level, with an optional mounting offset above the point you click.
- When a family or catalogue has nothing suitable to map, the dialog says so and why, instead of showing an empty list. A "Copy details" button puts the catalogue's columns and the family's parameters on the clipboard.
- The mapping is stored once per machine and shared by every Revit version and every project on it.
- Three example fan families with working type catalogues are installed with the add-in, so there is something to try it against immediately.

WHO IT'S FOR
MEP engineers and HVAC modellers who select fans in Revit and are tired of doing it in a manufacturer's separate selection program and then hunting for the matching type by hand. Especially useful for offices with their own fan library: point each family at its catalogue once and the whole library becomes searchable.

Supported on Revit 2022 through 2026 (single installer, pick your versions during setup).

## App Version
**Version Number:** 2.0.0

**Version Description:** Fan selection now works with any fan family and its type catalogue, through a mapping you set up in the new Options dialog — the previous version understood only its own bundled families and their fixed parameter names. The search covers every type in the catalogue rather than only the ones already loaded, and inserting a match loads that single type. Adds efficiency / specific fan power and sound power to the figures shown, unlimited user-chosen extra columns, and ranking by closeness, SFP, noise or motor power. Rebuilt interface, and placement now hosts the fan on the correct level instead of a fixed height.

## General Usage Instructions
1. Click Options on the Vixeldorf ribbon tab, pick a fan family from the list of Mechanical Equipment families loaded in the project, and press Add.
2. Point it at the family's type catalogue — the .csv beside the .rfa. If the family came with the add-in, this is filled in already; otherwise use Browse.
3. Check the mapping. For each figure, the left dropdown is the catalogue column it is read from and the right one is the family parameter it is written onto when a fan is placed. Air flow and pressure columns are required, everything else is optional. Add any extra columns you want shown, then click OK.
4. Click Fan Selector. Choose the family, enter the air flow and pressure you need — in the project's own units, shown beside each box — set the tolerance, and press Search.
5. The matching types are listed with their figures, how far each is off the requested duty, and whether it is in the model yet. Change "Sort by" to order them by efficiency, noise or motor power instead of by closeness.
6. Select a row and click Insert fan (or double-click the row), then click the point in the view where the fan goes. The type is loaded if needed and placed there, with the catalogue figures written onto it.

## Installation/Uninstallation
Fan Selector installs via a standard Windows Installer (MSI) package. Files are copied to %ProgramData%\Vixeldorf\FanSelector\, and a .addin manifest is registered for each supported Revit version you select during setup, under %ProgramData%\Autodesk\Revit\Addins\{year} for 2022 through 2026. The example fan families and their type catalogues are installed to %ProgramData%\Vixeldorf\FanSelector\Sample Families\. Restart Revit after installing to load the "Vixeldorf" ribbon tab.

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
- Only families in the Mechanical Equipment category are offered. A fan modelled as an Air Terminal or Generic Model will not appear in the family list.<br>
- A catalogue column whose unit token Revit's type-catalogue format does not commonly use may not be recognised. Its values are then used exactly as written, and the dialog says so — it does not guess.<br>
- Placing a type the project does not have yet needs the .rfa beside the .csv under the same name, which is what Revit requires of a catalogue anyway. Without it, only types already loaded can be placed.<br>
- Listing the family parameters to write onto opens the family definition briefly; in-place and system families cannot be read that way. The search itself is unaffected.<br>
- The mapping is stored per machine, not per project: on another machine it has to be set up again, or the settings file copied across.<br>
- Fans are placed as free-standing instances hosted on a level. Face-hosted fan families are not supported.

## Learn More Url
https://www.vixeldorf.com
