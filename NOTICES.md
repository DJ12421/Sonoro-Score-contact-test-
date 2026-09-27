# Third-Party Notices

Sonoro-Score incorporates code, data, and algorithms adapted from the following
open-source projects. We are grateful to their authors.

---

## Tacet-Lab

- **Repository:** https://github.com/DJ12421/Tacet-Lab
- **License:** GNU General Public License v3.0 (GPL-3.0)
- **Copyright:** © Tacet-Lab contributors (DJ12421)

> ⚠️ License note: Tacet-Lab is GPL-3.0, not MIT. The Sonoro-Score files below
> are adapted from GPL-3.0 sources; the combined scanner work that links them
> must be distributed under GPL-compatible terms. Sonoro-Score's own MIT
> `LICENSE` covers original code only — seek legal review before publishing
> binaries that bundle the adapted matcher/signatures/data tables.

Portions adapted from Tacet-Lab (GPL-3.0):

- OCR pipeline strategy (region definitions and scan layout).
- Sonata icon pixel-signature matcher (`src/scanner/visual.ts`).
- Sonata icon signature data (`src/game-data/scanner-signatures.generated.ts`,
  `scannerSignatureVersion` 3.6), extracted to
  `src/SonoroScore.Scanner/sonata_signatures.json`.
- Stat alias tables, fuzzy matching approach, and tunable substat roll tables.
- Tacet-Lab backup envelope (`src/storage/database.ts` `exportAccount`,
  `src/domain/types.ts` `AccountDocument`, `src/game-data/core.ts`
  `GAME_DATA_VERSION`) mirrored by `TacetLabExporter.cs`.

Files carrying adapted code are marked with a header comment crediting Tacet-Lab
(GPL-3.0). See NOTICES.md.

---

## GPL-3.0 (Tacet-Lab adapted portions)

The adapted Tacet-Lab portions above are governed by the GNU General Public
License v3.0. Full text: <https://www.gnu.org/licenses/gpl-3.0.html>.
Source: <https://github.com/DJ12421/Tacet-Lab> (LICENSE file in that repo).