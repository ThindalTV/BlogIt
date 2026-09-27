# History

Past audit reports, kept as a record of what was found, what was fixed, and which
findings were accepted as by-design. Every finding in them is closed.

They are snapshots: file paths and namespaces are as they were on the date of each
report, before the repository was restructured (`src/BlogIt` is now
`src/BlogIt.Core`, the admin clients live under `src/admin/`, and the satellite
providers under `src/providers/`).

The accepted risks — admin-authored HTML and upload content types are trusted, and
every administrator is fully privileged — still stand. The current statement of
them is in the [administrator guide](../administrator-guide.md) and the
[technical guide](../technical-guide.md).
