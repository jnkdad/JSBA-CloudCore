\# Zone Extraction Guide – EAV-111a (Test 06)



To simplify debugging, EAV-111a should be processed in zones instead of

treating the plan as one monolithic dataset.



\## Zone A – Admin / Office Wing (bottom-left)



\- Mostly orthogonal geometry.

\- Light to moderate annotation noise.

\- Recommended first zone for tuning performance and stability.



\## Zone B – Circulation Spine (central connector)



\- Corridors with many tags and leaders.

\- Dotted/dashed lines and path indicators.

\- Good for validating leader filtering and text handling.



\## Zone C – Instructional Block (upper-left / mid-left)



\- Mix of classrooms and support spaces.

\- Dense grid lines and tags.

\- Useful for grid filtering logic.



\## Zone D – Theater + Stage (bottom-right)



\- Heaviest noise: seating arcs, risers, symbols.

\- High risk for accidental room creation from arcs.

\- Final stress test once earlier zones are stable.



\## Zone E – Upper Instruction / Mechanical (upper-right)



\- Lightly rotated geometry.

\- Complex adjacency between rooms.

\- Good for final validation of rotated-room handling.



\### Recommended order



Process zones in this order:



\*\*A → B → C → E → D\*\*



Save the theater (Zone D) for last.



