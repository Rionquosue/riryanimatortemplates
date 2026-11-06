# Riry's Animator Templates

Animator templates for [YGDR Animator Editor](https://github.com/YerGodDamnRight/YGDR-Animator-Editor).

## Adding templates

Put templates in `Templates/`, with their `.meta` files:

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

## Licences

- `Editor/YgdrTemplateBridge.cs`: GNU General Public License v3.0 or later
  (`Editor/GPL-3.0.md`), as it patches YGDR Animator Editor (GPL-3.0).
- Everything else, the templates included: MIT (`LICENSE` at the repository root).
