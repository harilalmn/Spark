`spark pkg list` reconciles a graph's package folder against what its file records and exits 1 when
something is missing, and `spark pkg restore` fetches what is absent.

Restoring downloads; it does not agree to load anything, so `run` and `check` still need
`--trust-packages` or a visit to the desktop window.
