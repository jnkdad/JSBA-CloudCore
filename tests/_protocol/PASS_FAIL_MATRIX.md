\# Room Extraction – Pass / Fail Matrix



| Test | Area              | Pass condition                                               | Fail condition                                                |

|------|-------------------|--------------------------------------------------------------|---------------------------------------------------------------|

| 03   | Boundary accuracy | 100% rooms have a single closed polygon                     | Any room missing, unclosed, or duplicated                    |

| 03   | Tag extraction    | Name + number parsed for every room tag                     | Missing tags or tags assigned to the wrong room              |

| 03   | Scale             | Derived lengths within ±0.5% of dimension text              | >1% error or inconsistent scaling across edges               |

| 04   | Tag mapping       | Tag bounding boxes map to nearest room polygon              | Tags float or attach to wrong polygon                        |

| 05   | Noise filtering   | Grids/leaders/arrows removed while walls remain intact      | Noise creates ghost rooms or removes part of a wall          |

| 05   | Room integrity    | Same polygons as Test 03 (for equivalent geometry)          | Fragmented polygons or extra polygons                        |

| 06   | Rotation handling | Rotated rooms keep correct angle / orientation vector       | Rooms snapped back to 0° or mis-rotated                      |

| 06   | Path filtering    | All non-room annotation paths filtered                      | Grids, seating arcs, or callouts become room boundaries      |

| 06   | Performance       | Extraction completes without timeout or crash               | Engine hangs, crashes, or uses excessive memory              |

| 06   | Complex geometry  | Theater/stage zones still yield correct room polygons       | Seating geometry breaks polygon reconstruction               |



