# Riry's Animator Templates

Animator templates for [YGDR Animator Editor](https://github.com/YerGodDamnRight/YGDR-Animator-Editor).

## Adding templates

Put templates in `Templates/`, with their `.meta` files.

### YGDR Animator Editor: `Templates/YGDR/`

- **Layer templates**: animator controllers. They appear in the Animator window's
  layer **+** menu among YGDR's own templates.
- **Blend tree templates**: blend trees saved as their own asset. They appear in
  the blend tree **Import Template** list.

A `.` in a template's name makes a submenu: `Riry.Toggle.Simple` shows as
**Riry > Toggle > Simple**.

YGDR Animator Editor only looks in its own package and in
`Assets/YGDR Animator/User Templates`, so `Editor/YgdrTemplateBridge.cs` adds this
folder to its template lists. It needs YGDR Animator Editor and the VRChat SDK
(which brings Harmony); without them the templates still install, they are just
not in those menus. If an update of the editor stops the bridge working, a
warning says so once per session.

YGDR Animator Editor also lists every blend tree template under **Delete
Template**, which deletes the template's whole folder. For this package's
templates the bridge refuses that and logs an error, so the package's files stay
intact; remove the package to remove its templates.

### MACS: `Templates/MACS/`

Blend trees saved as their own asset. Right-clicking a blend tree node in the
Animator window shows them in a **Riry** submenu beneath MACS's **New**; a
subfolder makes a submenu. Choosing one opens a window to insert it into the
clicked blend tree:

- Each parameter the template uses gets a row. Type a name, or pick one from
  MACS's fuzzy list of the controller's parameters under the field.
- **Insert** asks before adding parameters the controller does not have.
- The template's AAP clips are made again for the chosen names with MACS's AAP
  clip generator (`AAP {parameter} {value}` in the controller's `Animations`
  folder; an existing one is reused).
- One **Ctrl+Z** removes everything the insert added to the controller: the
  parameters, the copied blend trees and the new child. AAP clips are kept;
  delete them yourself if they are not wanted.

It needs MACS and the VRChat SDK (Harmony). MACS keeps its AAP generator and
fuzzy list internal under names that can change in an update; if they are not
found, this package's copy of the same behaviour is used and a warning says so
once per session.

## Licences

- `Editor/YgdrTemplateBridge.cs`: GNU General Public License v3.0 or later
  (`Editor/GPL-3.0.md`), as it patches YGDR Animator Editor (GPL-3.0).
- Everything else, the templates and `Editor/MACS/` included: MIT (`LICENSE` at
  the repository root).
