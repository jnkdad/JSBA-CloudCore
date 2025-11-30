\# Test 06 – FullPlan\_RotatedRooms\_Noise



\*\*PDF:\*\* `EAV-111a - AUDIO VIDEO PLAN – FIRST FLOOR LEVEL AREA A.pdf`



\## Purpose



Full end-to-end stress test with rotated room groups and heavy annotation

noise from a real A/V construction drawing.



\## Must pass



\- Valid rooms extracted as closed polygons.

\- Rotated rooms handled correctly (non-zero orientation preserved).

\- Grids, seating arcs, and other annotations filtered out.

\- Performance remains acceptable (no hangs or runaway memory).

\- Room tags mapped to corresponding polygons.



\## Notes



Use the zone-based workflow described in

`tests/\_protocol/ZONE\_EXTRACTION\_GUIDE.md` rather than running the entire

sheet at once during debugging.



