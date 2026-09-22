#!/bin/bash
# Moves the panel of a compose project onto the image of a release, and back where it does not come up healthy.
set -u

work=$AMNEZIAGEO_UPDATE_WORK
tag=$AMNEZIAGEO_UPDATE_TAG
mkdir -p "$work"
exec >> "$work/$tag.log" 2>&1

/opt/amneziageo-server/AmneziaGeo.Server.Api handover
rc=$?
echo "$rc" > "$work/$tag.rc"
exit "$rc"
