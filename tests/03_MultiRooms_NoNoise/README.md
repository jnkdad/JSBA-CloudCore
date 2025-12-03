\# Test 03 – MultiRooms\_NoNoise



\*\*PDF:\*\* `Multiple rooms with room tags (no noise).pdf`



\## Purpose



Validate clean multi-room boundary detection and basic room-tag extraction

with no annotation noise.



\## Must pass



\- One closed polygon per room.

\- All rooms found.

\- Room name + number parsed correctly.

\- Dimension strings yield consistent scale (±0.5%).



\## Notes



This is the baseline multi-room test. If this test is not green, the later

noise/rotation tests will be much harder to debug.



