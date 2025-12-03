\# Test 05 – NoiseStress\_1to2Rooms



\*\*PDF:\*\* `One or two rooms with noisy elements (grids, leaders, arrows, callouts).pdf`



\## Purpose



Validate noise filtering while preserving correct room boundaries.



\## Must pass



\- Room polygons match the equivalent clean geometry (from Test 03/04).

\- Grid lines, leaders, arrows, and callouts are \*\*not\*\* treated as walls.

\- No ghost rooms created by noise paths.

\- Room tags remain readable and mapped correctly.



\## Notes



This is the primary test for tuning thresholds and heuristics around:



\- Minimum segment length

\- Line pattern / layer filtering

\- Leader lines crossing walls

\- Small near-parallel noise segments at corners



