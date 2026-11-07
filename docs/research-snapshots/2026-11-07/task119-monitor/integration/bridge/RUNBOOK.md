# Task119 live bridge

This prepared package reads the two existing M116 and S115-S2 observer directories. One process holds a nonblocking flock in its private state directory. It checks observations every 90 seconds. The receiver version remains the greatest saved per-run observation timestamp in milliseconds. Equal versions are idempotent when both runs match. A late peer with an unchanged global version is retained in `state.json` as pending. The journal emits one notice on a changed pending pair and a final notice if the pair could not be sent before the stop boundary.

The bridge checks the authenticated GET before a possible POST. After a write, it reads back and matches the complete projected pair and version. It fsyncs and atomically replaces the local last confirmed state only after verification. Lost acknowledgements are reconciled by GET. Unavailable observations are reported without changing statuses. Network failures retain last good state and back off HTTP attempts by 90, 180, then at most 360 seconds. Observations continue on their own 90-second schedule. Every HTTP operation has a bounded wall alarm and socket timeout. It makes no retry past its local deadline.

The deadline is the earlier of the absolute epoch `1794035543` and 180 seconds after both existing observers first report a returned call. The terminal timestamp is durable across restarts. A missing file or unknown provider state does not count as terminal. An unsent delayed peer remains pending on exit. This version covers exactly the two existing calls. Extending it to another call needs an explicit receiver and projector configuration update.

## Owner launch after receiver release

The Raven inspection verified systemd 255, original service DynamicUser, existing `site_access` credential source `/etc/sts2-modal-monitor/site-access` with root:root 0600, and both observers root:raven 2700. The independent transient unit uses root UID to read the original directories and removes capabilities. It mounts just those two observer paths inside the research data subtree, makes the main filesystem read-only, and writes to a separate state directory. The unit also has its own private temporary filesystem. The existing credential is passed by systemd LoadCredential. Python reads it from the systemd credential directory and retains it only in memory. There is no credential argument, environment value, output, or payload copy.

Stage only the three reviewed Python files in the root-owned directory `/opt/sts2-monitor-task119-live-bridge` (0755) with root-owned read-only files (0444) before starting. The separate readback DynamicUser needs to read these source files. Do not change source observer permissions. Run the following from the authorized Raven administration session after deployment and root acceptance.

```sh
deadline=1794035543
runtime_seconds=$((deadline - $(date +%s)))
test "$runtime_seconds" -gt 0 || exit 1
systemd-run --unit=task119-live-bridge --collect --service-type=exec \
  --property="RuntimeMaxSec=${runtime_seconds}s" --property=TimeoutStopSec=1s \
  --property=Restart=no --property=User=root --property=Group=root --property=UMask=0077 \
  --property=StateDirectory=task119-live-bridge --property=StateDirectoryMode=0700 \
  --property=LoadCredential=site_access:/etc/sts2-modal-monitor/site-access \
  --property=ProtectSystem=strict --property=ProtectHome=yes --property=PrivateTmp=yes \
  --property=PrivateDevices=yes --property=NoNewPrivileges=yes --property=CapabilityBoundingSet= \
  --property="RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6" \
  --property="InaccessiblePaths=/etc/sts2-modal-monitor /var/lib/sts2-modal-production /var/lib/sts2-modal-runner" \
  --property=TemporaryFileSystem=/data/raven/hack-sts2-modal-research:ro \
  --property="BindReadOnlyPaths=/data/raven/hack-sts2-modal-research/task116-M-ops/observer-v2 /data/raven/hack-sts2-modal-research/task115-second-segment-stage/observer" \
  -- /usr/bin/python3 -E -B /opt/sts2-monitor-task119-live-bridge/live_bridge.py
```

The service runs in its own transient unit. It creates no timer or observer and does not alter any existing unit. Startup and logging do not print credential content. Check the launch using `systemctl show task119-live-bridge -p ActiveState,SubState,ExecMainStatus` and the safe `site_verified` journal entry. This command is prepared only and was not exercised in this task.

## One authenticated readback

```sh
systemd-run --unit=task119-live-bridge-readback --collect --wait --pipe --service-type=exec \
  --property=RuntimeMaxSec=20s --property=DynamicUser=yes \
  --property=LoadCredential=site_access:/etc/sts2-modal-monitor/site-access \
  --property=ProtectSystem=strict --property=ProtectHome=yes --property=PrivateTmp=yes \
  --property=NoNewPrivileges=yes \
  -- /usr/bin/python3 -E -B /opt/sts2-monitor-task119-live-bridge/live_bridge.py --readback
```

The readback prints version, observation timestamps, status, dose and checkpoint evidence for both calls. It makes one GET and does not take the writer lock or touch state. The Python entry point also supports `--credential-fd` when an authorized launch environment already supplies the same credential descriptor.

Tests use a simulated clock, fake Site, local flock and synthetic inherited descriptor. No HTTP, SDK, credential file access, unit start, deploy, or paid compute was executed by the test suite.

```sh
python3 -m unittest -v test_live_bridge.py
```
