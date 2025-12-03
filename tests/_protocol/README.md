\# JSBA CloudCore – Room Extraction Test Protocol (v1)



Author: Jerrold / JSBA  

Primary user: Yang (boundary extraction)



\## Overview



This protocol defines how to validate the PDF → Room Boundary extraction

pipeline against a set of reference PDFs, in increasing complexity:



\- \*\*Test 03 – MultiRooms\_NoNoise\*\*

\- \*\*Test 04 – MultiRooms\_WithTags\*\*

\- \*\*Test 05 – NoiseStress\_1to2Rooms\*\*

\- \*\*Test 06 – FullPlan\_RotatedRooms\_Noise\*\*



Earlier single-room tests (01/02) are assumed to be handled separately.



---



\## Test 03 – MultiRooms\_NoNoise



\*\*PDF:\*\* `Multiple rooms with room tags (no noise).pdf`  

\*\*Folder:\*\* `tests/03\_MultiRooms\_NoNoise`



\### Purpose



Verify basic multi-room boundary detection and room-tag extraction with no

noise elements.



\### Required behavior



\- All rooms detected as separate polygons.

\- All boundaries closed (no gaps).

\- Room name + number extracted for each tag.

\- Dimension strings provide ground-truth scale; extracted lengths should

&nbsp; match within ±0.5%.



---



\## Test 04 – MultiRooms\_WithTags



\*\*PDF:\*\* `Multiple rooms with room tags (no noise).pdf`  

\*\*Folder:\*\* `tests/04\_MultiRooms\_WithTags`



\### Purpose



Same geometry as Test 03, but used to focus on text + tag handling and scale

verification.



\### Required behavior



\- Tag bounding boxes correctly associated to nearest room polygon.

\- Tag orientation and baseline recognized (even if not used yet).

\- Scale derived from dimensions is consistent with Test 03.



---



\## Test 05 – NoiseStress\_1to2Rooms



\*\*PDF:\*\* `One or two rooms with noisy elements (grids, leaders, arrows, callouts).pdf`  

\*\*Folder:\*\* `tests/05\_NoiseStress\_1to2Rooms`



\### Purpose



Validate noise filtering without corrupting valid room boundaries.



\### Required behavior



\- Room polygons remain intact.

\- Grid lines, leaders, arrows, and callouts are \*not\* treated as boundaries.

\- No ghost rooms created by leftover noise paths.

\- Tag extraction still works even if leaders cross walls.



---



\## Test 06 – FullPlan\_RotatedRooms\_Noise



\*\*PDF:\*\* `EAV-111a - AUDIO VIDEO PLAN – FIRST FLOOR LEVEL AREA A.pdf`  

\*\*Folder:\*\* `tests/06\_FullPlan\_RotatedRooms\_Noise`



\### Purpose



End-to-end stress test using a real architectural A/V plan with:



\- Rotated room groups

\- Mixed orthogonal + skewed geometry

\- Heavy annotation noise (grids, tags, leaders, seating, symbols)

\- Multiple lineweights and patterns



\### Required behavior



\- Valid rooms extracted as closed polygons (no gaps/overlaps).

\- Rotated rooms handled correctly (orientation vector preserved).

\- Noise elements fully filtered (no grids or leaders becoming walls).

\- Performance remains acceptable (no hangs or runaway memory).

\- Room tags correctly mapped to polygons.



---



\## Recommended execution order



1\. Run extraction on Test 03 until all pass criteria are met.

2\. Repeat for Test 04 with focus on tags + scaling.

3\. Move to Test 05 and tune noise filtering until stable.

4\. Finally, run Test 06 using the zone-based approach described in

&nbsp;  `ZONE\_EXTRACTION\_GUIDE.md`.



Record observations and failures in commit messages or a separate log as

needed.



