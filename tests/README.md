\# JSBA CloudCore – Room Extraction Test Suite (v1)



This folder contains the reference PDFs and documentation used to validate

Yang's PDF → Room Boundary extraction pipeline.



\### Included test levels



\- \*\*03 – MultiRooms\_NoNoise\*\*  

&nbsp; Clean multi-room plan with room tags and dimensions.



\- \*\*04 – MultiRooms\_WithTags\*\*  

&nbsp; Same plan as Test 03, focused on tag extraction and scaling.



\- \*\*05 – NoiseStress\_1to2Rooms\*\*  

&nbsp; One–two rooms with grids, leaders, arrows, and callouts.



\- \*\*06 – FullPlan\_RotatedRooms\_Noise\*\*  

&nbsp; Real-world A/V plan with rotated rooms and heavy annotation noise.



Protocol documents live in `tests/\_protocol`.



For each test, see the local `README.md` inside its folder for purpose,

expected behavior, and known stress factors.



