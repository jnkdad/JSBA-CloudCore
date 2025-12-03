\# Rotated Room Debugging Guide



When extracting rooms in Test 06, pay special attention to rotated areas.



\## Checklist per rotated room



1\. \*\*Orientation vector\*\*

&nbsp;  - Compute dominant wall direction from polygon edges.

&nbsp;  - Verify angle is non-zero and consistent with the drawing.



2\. \*\*Polygon stability\*\*

&nbsp;  - After gap-closing, corners should stay on the rotated grid.

&nbsp;  - Watch for subtle drift where edges become slightly off-angle.



3\. \*\*Tag behavior\*\*

&nbsp;  - Confirm that room tags inside rotated rooms still map correctly.

&nbsp;  - Tag orientation may differ from room angle; mapping is by position.



4\. \*\*Noise merging\*\*

&nbsp;  - Check that leaders or callouts crossing a wall do not become part of

&nbsp;    the room polygon.

&nbsp;  - Short segments near corners should either be merged cleanly or dropped.



5\. \*\*Transitions\*\*

&nbsp;  - At interfaces between orthogonal and rotated groups, verify that

&nbsp;    polygons do not bleed across the boundary or leave gaps.



Document any failure patterns here so we can refine heuristics and

thresholds.



