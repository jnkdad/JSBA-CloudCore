\# Test 04 – MultiRooms\_WithTags



\*\*PDF:\*\* `Multiple rooms with room tags (no noise).pdf`  

(Same geometry as Test 03.)



\## Purpose



Focus on validating room-tag extraction, text handling, and scale

verification using the same clean layout as Test 03.



\## Must pass



\- Tag bounding boxes map correctly to room polygons.

\- No duplicate or unassigned tags.

\- Scale derived from dimensions matches Test 03 (within ±0.5%).

\- Tag orientation and baseline are correctly captured (even if not yet used).



\## Notes



Treat this as the "text and scale" variant of Test 03. Geometry results

should match 03; failures here point to tag parsing or mapping logic.



