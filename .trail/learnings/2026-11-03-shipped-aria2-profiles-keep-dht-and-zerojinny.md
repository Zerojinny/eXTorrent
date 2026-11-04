---
title: Shipped aria2 profiles keep DHT and peer exchange on
author: Zerojinny
date: 2026-11-03
branch: main
cost: { minutes: 29, tokens: 359147 }
id: l-be775859
scope: repo
status: active
src: agent
touches:
  - ".gitattributes"
  - CLAUDE.md
  - LICENSE
  - README.ko.md
  - README.md
  - bin/aria2.conf.100
---

## Decided
Keep DHT and peer exchange ON in both shipped aria2 profiles, as the repo owner set them on GitHub on 2026-10-11. Resume support (check-integrity=true) is added on top.

Why: torrents without a working tracker still find peers; the owner chose this deliberately before the rewrite.

## Tried and ruled out
- DHT/PEX off (what the rewrite had locally): would have silently reverted the owner's earlier commit and broken trackerless torrents. Only private-tracker setups benefit.

## Watch out
- With DHT on, a local-tracker test is no longer offline. For offline tests, copy the builder to a separate folder and turn DHT/PEX off only in that copy's profiles.
- Before overwriting shipped settings, diff against origin/main: the owner also edits files directly on GitHub.

In the code:
- bin/aria2.conf.100, bin/aria2.conf.1G: enable-dht=true, enable-peer-exchange=true, bt-enable-lpd=false, check-integrity=true
- README.md / README.ko.md "aria2 settings" section describes these defaults
