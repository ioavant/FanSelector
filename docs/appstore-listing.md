# Fan Selector — Autodesk App Store listing (Vixeldorf build, v2.0.0)

Facts pulled from `version.json`, `FanSelector/App.cs`, `Installer/Product.wxs`,
`branding.props` and the command/dialog code. Support email and SLA are the
standing Vixeldorf defaults — confirm before submitting.

## App Name
Fan Selector

## App Short Description
Search the fan families already loaded in your Revit project by air flow and pressure, then place the matching type. Works with any family — you map its parameters once.

## App Description
Fan Selector turns the fan families you already have into a searchable selection tool. State the duty — air flow and pressure — and it lists the types of the chosen family that can deliver it, ranked, with the figures that matter beside them. Pick one and it places that exact type.

It ships with no fan database and requires no proprietary families. Everything it knows, it reads from your own families.

HOW IT WORKS
A fan family already carries its performance data: every type has an air flow, a pressure, usually a motor power and a speed. Fan Selector only needs to be told which parameter is which. You do that once per family in Options: each figure gets a dropdown listing that family's own parameters, filtered to the ones whose unit actually fits — air flow parameters for air flow, pressure parameters for pressure — with the likely match pre-selected. From then on, searching that family is instant, and nothing about any particular manufacturer's naming is baked into the add-in.

It reads the family's own definition, so it does not care whether the figures are declared as type parameters or as instance parameters with a per-type value — a distinction most fan libraries make one way and most selection tools cannot see past.

KEY FEATURES
- Works with any Mechanical Equipment fan family loaded in the project. No bundled catalogue, no required families, no CSV files to maintain.
- Per-family parameter mapping with unit-aware dropdowns and automatic guessing, so setting up a new family usually means confirming what it already proposed. Type parameters and instance parameters are both offered, and each is labelled.
- When a family has nothing suitable to map, the dialog says so and why, rather than showing an empty list. A "Copy parameter list" button puts the family's whole parameter table on the clipboard.
- Six mapped figures: air flow, pressure, motor power, speed, efficiency / specific fan power, and sound power. Air flow and pressure drive the search; the rest are shown so you can judge the choice.
- Any number of extra columns: point them at any other type parameter you want to see next to the results — frame size, weight, order code, whatever your library carries.
- Ranking, not just filtering. Sort the matches by closeness to the requested duty, by lowest specific fan power, by quietest, or by lowest motor power — so the shortlist is ordered by whatever actually decides the selection.
- Adjustable tolerance, as a percentage on both figures, with a default you set once.
- Your project's units throughout. Type 5000 m³/h and 400 Pa, or 3000 CFM and 1.6 inWG — whatever the project is set to. Values are never converted behind your back; they are compared exactly as Revit stores them.
- Places the matching type on the correct level, with an optional mounting offset above the point you click.
- Optionally writes the selected air flow onto an instance parameter, for families whose duct connector is driven by an instance override rather than by the type.
- Live type preview from Revit, so you can see what you are about to place.
- The mapping is stored once per machine and shared by every Revit version and every project on it.
- Three sample fan families with type catalogues are included, so there is something to try it against immediately.

WHO IT'S FOR
MEP engineers and HVAC modellers who select fans in Revit and are tired of doing it in a manufacturer's separate selection program and then hunting for the matching type by hand. Especially useful for offices with their own fan family library: map each family once and the whole library becomes searchable.

Supported on Revit 2022 through 2026 (single installer, pick your versions during setup).

## App Version
**Version Number:** 2.0.0

**Version Description:** Fan selection now works with any fan family loaded in the project, through a parameter mapping you set up in the new Options dialog — the previous version only understood its own bundled families and their fixed parameter names. Adds efficiency / specific fan power and sound power to the figures shown, unlimited user-chosen extra columns, and ranking by closeness, SFP, noise or motor power. Air flow and pressure are now entered and shown in the project's own units. Rebuilt interface, and placement now hosts the fan on the correct level instead of a fixed height.

## General Usage Instructions
1. Load the fan family you want to select from into your project, along with the types you want to choose between (if the family has a type catalogue, pick the types there when loading).
2. Click Options on the Vixeldorf ribbon tab, pick that family from the list of Mechanical Equipment families in the project, and press Add. Fan Selector proposes a parameter for each figure; check them and adjust anything it guessed wrong. Air flow and pressure are required, the rest are optional. Add any extra parameters you want shown as columns, then click OK.
3. Click Fan Selector. Choose the family, enter the air flow and pressure you need — in the project's own units, shown beside each box — set the tolerance, and press Search.
4. The matching types are listed with their figures and how far each is off the requested duty. Change "Sort by" to order them by efficiency, noise or motor power instead of by closeness.
5. Select a row and click Insert fan (or double-click the row), then click the point in the view where the fan goes. That exact type is placed there.
6. To add another family, or to change a mapping, reopen Options — either from the ribbon or from the button inside the Fan Selector window.

## Installation/Uninstallation
Fan Selector installs via a standard Windows Installer (MSI) package. Files are copied to %ProgramData%\Vixeldorf\FanSelector\, and a .addin manifest is registered for each supported Revit version you select during setup, under %ProgramData%\Autodesk\Revit\Addins\{year} for 2022 through 2026. The optional sample fan families are installed to %ProgramData%\Vixeldorf\FanSelector\Sample Families\. Restart Revit after installing to load the "Vixeldorf" ribbon tab.

To uninstall, use Windows Settings > Apps > Installed apps (or Control Panel > Programs and Features), select "Vixeldorf Fan Selector", and click Uninstall. This removes the DLL and all per-version .addin manifests. Restart Revit afterward to confirm the ribbon tab is gone.

## Support Information
For support, contact us at yoav@vixeldorf.com or via <a href="https://www.vixeldorf.com">vixeldorf.com</a>. Please include your Revit version, the Fan Selector version (shown in the Fan Selector window title), the fan family you are selecting from, and a short description or screenshot of the issue. We aim to respond within 5 business days.

## Additional Information
<b>Supported Revit versions:</b> 2022, 2023, 2024, 2025, 2026 (single build, 64-bit).<br>
<b>Category:</b> Mechanical / HVAC.<br>
<b>Requirements:</b> No third-party dependencies. At least one fan family loaded in the project.<br>
<b>Data storage:</b> Fan Selector stores nothing in your model. It reads performance figures from the type parameters of your own families, and the only thing it ever writes is the family instance you asked it to place — plus, if you explicitly map one, the air flow value on that instance. The parameter mapping itself lives in a small readable JSON file next to the add-in, shared by every Revit version on the machine, so nothing is added to the project file and models stay fully usable if the add-in is removed.<br>
<b>Units:</b> All input and output follow the project's own unit settings; no internal conversion is applied to catalogue values.<br>
<b>Publisher:</b> <a href="https://www.vixeldorf.com">Vixeldorf</a>.

## Known Issues
- Only families in the Mechanical Equipment category are offered. A fan modelled as an Air Terminal or Generic Model will not appear in the family list.<br>
- The family and the types you want to select from must already be loaded in the project. Fan Selector does not load families, or additional types, from .rfa files on disk.<br>
- The first read of a very large family takes a moment while its definition is opened; it is cached for the rest of the session. In-place and system families cannot be read at all.<br>
- Revit has no unit for sound power, and efficiency is not always stored in a recognisable one. Tick "Show every parameter" in Options to map such a figure.<br>
- The mapping is stored per machine, not per project: on another machine it has to be set up again, or the settings file copied across.<br>
- Fans are placed as free-standing instances hosted on a level. Face-hosted fan families are not supported.

## Learn More Url
https://www.vixeldorf.com
