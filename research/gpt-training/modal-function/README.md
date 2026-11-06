# Modal Function transport source

These four Python files are copied without edits from the frozen Task104 Function
package. The source manifest records their exact SHA-256s and original relative
paths. `function_app.py` defines isolated execution and immutable source validation;
`modal_research/function_worker.py` and `native_worker.py` are transport/runtime
overlays. The latter sets the 32-core native execution recipe.

This directory is **source for review**, not a provisioned deployment. The live
release descriptor, image/Volume bindings, provider IDs, credentials, claims,
signed activation and operations packets are not published. `deploy_metadata.py`
defaults to dry-run behavior, but an actual deployment requires a complete separate
release and authority; do not fabricate missing IDs or point this copy at production.

The underlying exact worker modules are under `../releases/task104/code/`.
Do not replace the host control plane with the worker's similarly named modules.
No Function was deployed or invoked as part of this publication.
