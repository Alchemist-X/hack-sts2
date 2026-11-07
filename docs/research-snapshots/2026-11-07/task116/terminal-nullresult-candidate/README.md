# M116 terminal null-result compatibility candidate

Preparation only. The accepted original collector and pins stay unchanged at `task116/terminal-collection-candidate`. This candidate changes exactly one expression: `terminal.get('result',{})` becomes `terminal.get('result') or {}`.

A verified terminal exception may legally carry raw `result:null`; it is inspected as an empty result mapping for the optional attempt check. The original observation is neither deleted nor rewritten. Normal-final selection, identity, byte/hash bounds, deadlines/absence/advance rules and funding/runtime/science semantics remain byte-identical. No raw result workaround is required.

The one targeted regression checks normal behavior then verified timed_out/null/double-absent atomic latest using the original fixture. No SDK/Torch/model/native/cloud access or full suite is run. The new collector SHA must be explicitly accepted/bound by future root/Ops before use; no accepted or staged source is overwritten here.
