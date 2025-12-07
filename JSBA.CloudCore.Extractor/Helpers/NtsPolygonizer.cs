using JSBA.CloudCore.Contracts.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Polygonize;
using NetTopologySuite.Operation.Buffer;

namespace JSBA.CloudCore.Extractor.Helpers;

/// <summary>
/// Uses NetTopologySuite for robust polygon reconstruction from line segments
/// </summary>
public class NtsPolygonizer
{
    private readonly ILogger<NtsPolygonizer> _logger;
    private readonly GeometryFactory _geometryFactory;

    public NtsPolygonizer(ILogger<NtsPolygonizer> logger)
    {
        _logger = logger;
        _geometryFactory = new GeometryFactory();
    }

    /// <summary>
    /// Convert RawPath to NTS LineString
    /// </summary>
    private LineString ToLineString(RawPath path)
    {
        var coordinates = path.Points.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        return _geometryFactory.CreateLineString(coordinates);
    }

    /// <summary>
    /// Convert NTS Polygon to RoomBoundary
    /// </summary>
    private RoomBoundary ToRoomBoundary(Polygon polygon)
    {
        var points = polygon.ExteriorRing.Coordinates
            .Select(c => new Point2D { X = c.X, Y = c.Y })
            .ToList();

        return new RoomBoundary
        {
            Polygon = points
        };
    }

    /// <summary>
    /// Get area of a RoomBoundary
    /// </summary>
    private double GetArea(RoomBoundary boundary)
    {
        var coords = boundary.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        if (!coords[0].Equals2D(coords[^1]))
        {
            coords = coords.Append(coords[0]).ToArray();
        }
        var ring = _geometryFactory.CreateLinearRing(coords);
        var polygon = _geometryFactory.CreatePolygon(ring);
        return Math.Abs(polygon.Area);
    }

    /// <summary>
    /// Collapse parallel wall lines into centerlines.
    /// This handles the case where walls are drawn with thickness (double lines).
    /// </summary>
    public List<RawPath> CollapseParallelWalls(List<RawPath> paths, double wallThickness)
    {
        if (wallThickness <= 0 || paths.Count < 2)
            return paths;

        _logger.LogInformation("NTS: Collapsing parallel walls with thickness tolerance {Thickness} for {Count} paths",
            wallThickness, paths.Count);

        var usedIndices = new HashSet<int>();
        var result = new List<RawPath>();

        for (int i = 0; i < paths.Count; i++)
        {
            if (usedIndices.Contains(i))
                continue;

            var path1 = paths[i];
            bool foundPair = false;

            for (int j = i + 1; j < paths.Count; j++)
            {
                if (usedIndices.Contains(j))
                    continue;

                var path2 = paths[j];

                // Check if these are parallel lines close together
                if (AreParallelWallLines(path1, path2, wallThickness, out var centerPath))
                {
                    _logger.LogInformation("NTS: Collapsed parallel paths {I} and {J} into centerline", i, j);
                    result.Add(centerPath);
                    usedIndices.Add(i);
                    usedIndices.Add(j);
                    foundPair = true;
                    break;
                }
            }

            if (!foundPair)
            {
                result.Add(path1);
                usedIndices.Add(i);
            }
        }

        _logger.LogInformation("NTS: Collapsed parallel walls: {Original} → {Result} paths", paths.Count, result.Count);
        return result;
    }

    /// <summary>
    /// Check if two paths are parallel wall lines and compute their centerline
    /// </summary>
    private bool AreParallelWallLines(RawPath path1, RawPath path2, double maxDistance, out RawPath centerPath)
    {
        centerPath = null!;

        // Only works for simple 2-point line segments
        if (path1.Points.Count != 2 || path2.Points.Count != 2)
            return false;

        var start1 = path1.Points[0];
        var end1 = path1.Points[1];
        var start2 = path2.Points[0];
        var end2 = path2.Points[1];

        // Calculate direction vectors
        double dx1 = end1.X - start1.X;
        double dy1 = end1.Y - start1.Y;
        double dx2 = end2.X - start2.X;
        double dy2 = end2.Y - start2.Y;

        // Normalize
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);

        if (len1 < 0.001 || len2 < 0.001)
            return false;

        dx1 /= len1; dy1 /= len1;
        dx2 /= len2; dy2 /= len2;

        // Check if parallel (dot product of directions should be near 1 or -1)
        double dot = Math.Abs(dx1 * dx2 + dy1 * dy2);
        if (dot < 0.98) // Allow ~11 degree tolerance
            return false;

        // Check if the lines are close together (perpendicular distance)
        // Use distance from midpoint of line2 to line1
        double midX = (start2.X + end2.X) / 2;
        double midY = (start2.Y + end2.Y) / 2;

        // Distance from point to line: |ax + by + c| / sqrt(a² + b²)
        // Line equation: (y1-y0)x - (x1-x0)y + (x1-x0)y0 - (y1-y0)x0 = 0
        double a = dy1; // (end1.Y - start1.Y) normalized
        double b = -dx1; // -(end1.X - start1.X) normalized
        double c = dx1 * start1.Y - dy1 * start1.X;
        double dist = Math.Abs(a * midX + b * midY + c);

        if (dist > maxDistance)
            return false;

        // Check that lines overlap (project endpoints onto line)
        // Project line2's endpoints onto line1's direction
        double proj2Start = (start2.X - start1.X) * dx1 + (start2.Y - start1.Y) * dy1;
        double proj2End = (end2.X - start1.X) * dx1 + (end2.Y - start1.Y) * dy1;

        // Line1 goes from 0 to len1
        double min2 = Math.Min(proj2Start, proj2End);
        double max2 = Math.Max(proj2Start, proj2End);

        // Check for overlap
        if (max2 < -len1 * 0.1 || min2 > len1 * 1.1)
            return false; // No overlap

        // Create centerline
        // Find the centerpoint of each pair of endpoints
        // We need to match the endpoints correctly based on projection
        Point2D centerStart, centerEnd;

        // Determine which endpoints to pair
        double distStartToStart = Math.Sqrt(Math.Pow(start1.X - start2.X, 2) + Math.Pow(start1.Y - start2.Y, 2));
        double distStartToEnd = Math.Sqrt(Math.Pow(start1.X - end2.X, 2) + Math.Pow(start1.Y - end2.Y, 2));

        if (distStartToStart < distStartToEnd)
        {
            // start1-start2 are close, end1-end2 are close
            centerStart = new Point2D { X = (start1.X + start2.X) / 2, Y = (start1.Y + start2.Y) / 2 };
            centerEnd = new Point2D { X = (end1.X + end2.X) / 2, Y = (end1.Y + end2.Y) / 2 };
        }
        else
        {
            // start1-end2 are close, end1-start2 are close
            centerStart = new Point2D { X = (start1.X + end2.X) / 2, Y = (start1.Y + end2.Y) / 2 };
            centerEnd = new Point2D { X = (end1.X + start2.X) / 2, Y = (end1.Y + start2.Y) / 2 };
        }

        centerPath = new RawPath
        {
            Points = new List<Point2D> { centerStart, centerEnd },
            LineWidth = (path1.LineWidth + path2.LineWidth) / 2,
            IsStroked = path1.IsStroked,
            IsFilled = path1.IsFilled,
            SegmentCount = 1,
            PathLength = Math.Sqrt(Math.Pow(centerEnd.X - centerStart.X, 2) + Math.Pow(centerEnd.Y - centerStart.Y, 2)),
            PathType = path1.PathType,
            ObjectIndex = path1.ObjectIndex
        };

        return true;
    }

    /// <summary>
    /// Extends line segments to meet perpendicular lines at their intersection points.
    /// For each horizontal line, extends it to meet vertical lines that are within tolerance.
    /// For each vertical line, extends it to meet horizontal lines that are within tolerance.
    /// </summary>
    public List<RawPath> ExtendLinesToIntersections(List<RawPath> paths, double snapTolerance)
    {
        _logger.LogInformation("NTS: Extending lines to intersections with snap tolerance {Tolerance} for {Count} paths",
            snapTolerance, paths.Count);

        if (paths.Count < 2)
            return paths;

        // Filter to only 2-point line segments
        var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
        var otherPaths = paths.Where(p => p.Points.Count != 2).ToList();

        // Log each line segment for debugging
        for (int i = 0; i < lineSegments.Count; i++)
        {
            var line = lineSegments[i];
            _logger.LogInformation("NTS: Line {I}: ({X1:F1},{Y1:F1}) -> ({X2:F1},{Y2:F1})",
                i, line.Points[0].X, line.Points[0].Y, line.Points[1].X, line.Points[1].Y);
        }

        if (lineSegments.Count < 2)
        {
            _logger.LogInformation("NTS: Not enough line segments to extend");
            return paths;
        }

        // Classify lines as horizontal or vertical
        var horizontals = new List<(int idx, RawPath path)>();
        var verticals = new List<(int idx, RawPath path)>();

        for (int i = 0; i < lineSegments.Count; i++)
        {
            var line = lineSegments[i];
            var dx = Math.Abs(line.Points[1].X - line.Points[0].X);
            var dy = Math.Abs(line.Points[1].Y - line.Points[0].Y);

            if (dx > dy * 3) // More horizontal than vertical (allow some tolerance)
                horizontals.Add((i, line));
            else if (dy > dx * 3) // More vertical than horizontal
                verticals.Add((i, line));
        }

        _logger.LogInformation("NTS: Found {H} horizontal and {V} vertical lines", horizontals.Count, verticals.Count);

        var modified = new Dictionary<int, RawPath>();

        // For each horizontal line, find the nearest vertical line for each endpoint
        foreach (var (hIdx, hLine) in horizontals)
        {
            var hStart = hLine.Points[0];
            var hEnd = hLine.Points[1];
            var hY = (hStart.Y + hEnd.Y) / 2; // Y coordinate of horizontal line
            var hMinX = Math.Min(hStart.X, hEnd.X);
            var hMaxX = Math.Max(hStart.X, hEnd.X);

            // Find best vertical line for left endpoint
            int bestLeftVIdx = -1;
            double bestLeftDist = double.MaxValue;
            double bestLeftVX = 0;

            // Find best vertical line for right endpoint
            int bestRightVIdx = -1;
            double bestRightDist = double.MaxValue;
            double bestRightVX = 0;

            foreach (var (vIdx, vLine) in verticals)
            {
                var vStart = vLine.Points[0];
                var vEnd = vLine.Points[1];
                var vX = (vStart.X + vEnd.X) / 2;
                var vMinY = Math.Min(vStart.Y, vEnd.Y);
                var vMaxY = Math.Max(vStart.Y, vEnd.Y);

                // Check if vertical line's Y range covers the horizontal line's Y (within tolerance)
                if (hY < vMinY - snapTolerance || hY > vMaxY + snapTolerance)
                    continue;

                // Check distance to left endpoint
                var leftDist = Math.Abs(vX - hMinX);
                if (leftDist < bestLeftDist && leftDist < snapTolerance * 3)
                {
                    bestLeftDist = leftDist;
                    bestLeftVIdx = vIdx;
                    bestLeftVX = vX;
                }

                // Check distance to right endpoint
                var rightDist = Math.Abs(vX - hMaxX);
                if (rightDist < bestRightDist && rightDist < snapTolerance * 3)
                {
                    bestRightDist = rightDist;
                    bestRightVIdx = vIdx;
                    bestRightVX = vX;
                }
            }

            // Extend left endpoint to best vertical line
            if (bestLeftVIdx >= 0)
            {
                var intersection = new Point2D { X = bestLeftVX, Y = hY };
                var currentLine = modified.ContainsKey(hIdx) ? modified[hIdx] : hLine;
                var newPoints = new List<Point2D>(currentLine.Points);
                var leftIdx = currentLine.Points[0].X < currentLine.Points[1].X ? 0 : 1;
                newPoints[leftIdx] = intersection;
                modified[hIdx] = ClonePathWithNewPoints(currentLine, newPoints);

                // Also extend the vertical line
                var vLine = verticals.First(v => v.idx == bestLeftVIdx).path;
                var currentVLine = modified.ContainsKey(bestLeftVIdx) ? modified[bestLeftVIdx] : vLine;
                var vNewPoints = new List<Point2D>(currentVLine.Points);
                var topIdx = currentVLine.Points[0].Y > currentVLine.Points[1].Y ? 0 : 1;
                var bottomIdx = 1 - topIdx;
                if (Math.Abs(hY - currentVLine.Points[topIdx].Y) < snapTolerance * 2)
                {
                    vNewPoints[topIdx] = intersection;
                    modified[bestLeftVIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                }
                else if (Math.Abs(hY - currentVLine.Points[bottomIdx].Y) < snapTolerance * 2)
                {
                    vNewPoints[bottomIdx] = intersection;
                    modified[bestLeftVIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                }

                _logger.LogInformation("NTS: Extended H{H} left to V{V} at ({X:F1},{Y:F1})",
                    hIdx, bestLeftVIdx, intersection.X, intersection.Y);
            }

            // Extend right endpoint to best vertical line
            if (bestRightVIdx >= 0)
            {
                var intersection = new Point2D { X = bestRightVX, Y = hY };
                var currentLine = modified.ContainsKey(hIdx) ? modified[hIdx] : hLine;
                var newPoints = new List<Point2D>(currentLine.Points);
                var rightIdx = currentLine.Points[0].X > currentLine.Points[1].X ? 0 : 1;
                newPoints[rightIdx] = intersection;
                modified[hIdx] = ClonePathWithNewPoints(currentLine, newPoints);

                // Also extend the vertical line
                var vLine = verticals.First(v => v.idx == bestRightVIdx).path;
                var currentVLine = modified.ContainsKey(bestRightVIdx) ? modified[bestRightVIdx] : vLine;
                var vNewPoints = new List<Point2D>(currentVLine.Points);
                var topIdx = currentVLine.Points[0].Y > currentVLine.Points[1].Y ? 0 : 1;
                var bottomIdx = 1 - topIdx;
                if (Math.Abs(hY - currentVLine.Points[topIdx].Y) < snapTolerance * 2)
                {
                    vNewPoints[topIdx] = intersection;
                    modified[bestRightVIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                }
                else if (Math.Abs(hY - currentVLine.Points[bottomIdx].Y) < snapTolerance * 2)
                {
                    vNewPoints[bottomIdx] = intersection;
                    modified[bestRightVIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                }

                _logger.LogInformation("NTS: Extended H{H} right to V{V} at ({X:F1},{Y:F1})",
                    hIdx, bestRightVIdx, intersection.X, intersection.Y);
            }
        }

        // Build result: use modified lines where available, original otherwise
        var result = new List<RawPath>();
        for (int i = 0; i < lineSegments.Count; i++)
        {
            result.Add(modified.ContainsKey(i) ? modified[i] : lineSegments[i]);
        }
        result.AddRange(otherPaths);

        _logger.LogInformation("NTS: Modified {Count} lines", modified.Count);
        return result;
    }

    private static double Distance(Point2D p1, Point2D p2)
    {
        return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    private Point2D? ComputeIntersection(Point2D a1, Point2D a2, Point2D b1, Point2D b2)
    {
        // Line 1: P = a1 + t * (a2 - a1)
        // Line 2: P = b1 + s * (b2 - b1)
        double dx1 = a2.X - a1.X;
        double dy1 = a2.Y - a1.Y;
        double dx2 = b2.X - b1.X;
        double dy2 = b2.Y - b1.Y;

        double cross = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(cross) < 1e-10) // Lines are parallel
            return null;

        double t = ((b1.X - a1.X) * dy2 - (b1.Y - a1.Y) * dx2) / cross;

        return new Point2D
        {
            X = a1.X + t * dx1,
            Y = a1.Y + t * dy1
        };
    }

    private RawPath ClonePathWithNewPoints(RawPath original, List<Point2D> newPoints)
    {
        var start = newPoints[0];
        var end = newPoints[newPoints.Count - 1];
        return new RawPath
        {
            Points = newPoints,
            LineWidth = original.LineWidth,
            IsStroked = original.IsStroked,
            IsFilled = original.IsFilled,
            SegmentCount = original.SegmentCount,
            PathLength = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)),
            PathType = original.PathType,
            ObjectIndex = original.ObjectIndex
        };
    }

    /// <summary>
    /// Bridge small gaps by merging line segments that have endpoints within gapTolerance distance
    /// Uses iterative merging to connect all segments that should be bridged
    /// </summary>
    public List<RawPath> BridgeGaps(List<RawPath> paths, double gapTolerance)
    {
        if (gapTolerance <= 0 || paths.Count == 0)
            return paths;

        // Log path details before bridging
            foreach (var p in paths)
            {
                var start = p.Points[0];
                var end = p.Points[p.Points.Count - 1];
                _logger.LogInformation("NTS: Input path {Index}: {PointCount} points, start=({StartX:F1},{StartY:F1}), end=({EndX:F1},{EndY:F1})",
                    paths.IndexOf(p), p.Points.Count, start.X, start.Y, end.X, end.Y);
            }

            _logger.LogInformation("NTS: Bridging gaps with tolerance {Tolerance} for {Count} paths",
            gapTolerance, paths.Count);

        try
        {
            // Work with a mutable list of paths
            var workingPaths = paths.Select(p => new RawPath
            {
                Points = new List<Point2D>(p.Points),
                LineWidth = p.LineWidth,
                IsStroked = p.IsStroked,
                IsFilled = p.IsFilled,
                SegmentCount = p.SegmentCount,
                PathLength = p.PathLength,
                PathType = p.PathType,
                ObjectIndex = p.ObjectIndex
            }).ToList();

            bool merged = true;
            int iterations = 0;
            int maxIterations = workingPaths.Count * 2; // Prevent infinite loops

            // Iteratively merge paths until no more merges are possible
            while (merged && iterations < maxIterations)
            {
                merged = false;
                iterations++;

                for (int i = 0; i < workingPaths.Count; i++)
                {
                    if (workingPaths[i].Points.Count < 2)
                        continue;

                    var path1 = workingPaths[i];
                    var start1 = path1.Points[0];
                    var end1 = path1.Points[path1.Points.Count - 1];

                    for (int j = i + 1; j < workingPaths.Count; j++)
                    {
                        if (workingPaths[j].Points.Count < 2)
                            continue;

                        var path2 = workingPaths[j];
                        var start2 = path2.Points[0];
                        var end2 = path2.Points[path2.Points.Count - 1];

                        // Check all endpoint combinations
                        if (TryMergePaths(path1, path2, start1, end1, start2, end2, gapTolerance, out var mergedPath))
                        {
                            workingPaths[i] = mergedPath;
                            workingPaths.RemoveAt(j);
                            merged = true;
                            break; // Restart search after merge
                        }
                    }

                    if (merged)
                        break; // Restart outer loop
                }
            }

            // Close paths that are almost closed (start and end points are very close)
            // This helps polygon reconstruction by ensuring paths form closed loops
            double closeTolerance = Math.Max(gapTolerance, 1.0); // Close paths if endpoints are within 1 unit
            foreach (var path in workingPaths)
            {
                if (path.Points.Count >= 3)
                {
                    var start = path.Points[0];
                    var end = path.Points[path.Points.Count - 1];
                    double distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
                    
                    if (distance <= closeTolerance && distance > 0.001) // Almost closed but not exactly
                    {
                        // Close the path by adding the first point at the end (creates a closed ring)
                        path.Points.Add(new Point2D { X = start.X, Y = start.Y });
                        _logger.LogInformation("NTS: Closed path with {PointCount} points (endpoints were {Distance:F2} apart, tolerance {Tolerance:F2})", 
                            path.Points.Count, distance, closeTolerance);
                    }
                }
            }

            // Update path metadata
            foreach (var path in workingPaths)
            {
                path.SegmentCount = path.Points.Count;
                path.PathLength = CalculatePathLength(path.Points);
            }

            _logger.LogInformation("NTS: Gap bridging complete after {Iterations} iterations: {Original} → {Merged} paths",
                iterations, paths.Count, workingPaths.Count);

            return workingPaths;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Gap bridging failed, returning original paths");
            return paths;
        }
    }

    /// <summary>
    /// Try to merge two paths if their endpoints are within gapTolerance
    /// Handles both collinear lines (extend in same direction) and perpendicular lines (L/T joints)
    /// </summary>
    private bool TryMergePaths(RawPath path1, RawPath path2, Point2D start1, Point2D end1, 
        Point2D start2, Point2D end2, double gapTolerance, out RawPath mergedPath)
    {
        mergedPath = path1;

        // Get direction vectors for each path at endpoints
        var dir1End = GetDirectionAtEndpoint(path1, end1, true);
        var dir1Start = GetDirectionAtEndpoint(path1, start1, false);
        var dir2End = GetDirectionAtEndpoint(path2, end2, true);
        var dir2Start = GetDirectionAtEndpoint(path2, start2, false);

        // Check all 4 endpoint combinations
        // Case 1: end1 connects to start2
        if (ArePointsClose(end1, start2, gapTolerance) && 
            CanBridgeEndpoints(end1, dir1End, start2, dir2Start, gapTolerance, out var connectionPoint1))
        {
            var mergedPoints = new List<Point2D>(path1.Points);
            // Add connection point if found (for L/T joints), otherwise add start2 directly
            if (connectionPoint1 != null)
            {
                mergedPoints.Add(connectionPoint1);
            }
            else if (!ArePointsClose(end1, start2, 0.1))
            {
                mergedPoints.Add(start2);
            }
            for (int i = 1; i < path2.Points.Count; i++)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        // Case 2: end1 connects to end2 (reverse path2)
        if (ArePointsClose(end1, end2, gapTolerance) && 
            CanBridgeEndpoints(end1, dir1End, end2, dir2End, gapTolerance, out var connectionPoint2))
        {
            var mergedPoints = new List<Point2D>(path1.Points);
            // Add connection point if found
            if (connectionPoint2 != null)
            {
                mergedPoints.Add(connectionPoint2);
            }
            else if (!ArePointsClose(end1, end2, 0.1))
            {
                mergedPoints.Add(end2);
            }
            for (int i = path2.Points.Count - 2; i >= 0; i--)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        // Case 3: start1 connects to start2 (reverse path1)
        if (ArePointsClose(start1, start2, gapTolerance) && 
            CanBridgeEndpoints(start1, dir1Start, start2, dir2Start, gapTolerance, out var connectionPoint3))
        {
            var mergedPoints = new List<Point2D>();
            // Add path1 points in reverse
            for (int i = path1.Points.Count - 1; i >= 0; i--)
            {
                mergedPoints.Add(path1.Points[i]);
            }
            // Add connection point if found
            if (connectionPoint3 != null)
            {
                mergedPoints.Add(connectionPoint3);
            }
            else if (!ArePointsClose(start1, start2, 0.1))
            {
                mergedPoints.Add(start2);
            }
            for (int i = 1; i < path2.Points.Count; i++)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        // Case 4: start1 connects to end2 (reverse path1)
        if (ArePointsClose(start1, end2, gapTolerance) && 
            CanBridgeEndpoints(start1, dir1Start, end2, dir2End, gapTolerance, out var connectionPoint4))
        {
            var mergedPoints = new List<Point2D>();
            // Add path1 points in reverse
            for (int i = path1.Points.Count - 1; i >= 0; i--)
            {
                mergedPoints.Add(path1.Points[i]);
            }
            // Add connection point if found
            if (connectionPoint4 != null)
            {
                mergedPoints.Add(connectionPoint4);
            }
            else if (!ArePointsClose(start1, end2, 0.1))
            {
                mergedPoints.Add(end2);
            }
            for (int i = path2.Points.Count - 2; i >= 0; i--)
            {
                mergedPoints.Add(path2.Points[i]);
            }
            mergedPath = CreateMergedPath(path1, path2, mergedPoints);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get the direction vector at an endpoint of a path
    /// </summary>
    private (double dx, double dy) GetDirectionAtEndpoint(RawPath path, Point2D endpoint, bool isEnd)
    {
        if (path.Points.Count < 2)
            return (0, 0);

        Point2D otherPoint;
        if (isEnd)
        {
            // Direction from second-to-last point to last point
            otherPoint = path.Points.Count > 1 ? path.Points[path.Points.Count - 2] : path.Points[0];
        }
        else
        {
            // Direction from first point to second point
            otherPoint = path.Points.Count > 1 ? path.Points[1] : path.Points[path.Points.Count - 1];
        }

        double dx = endpoint.X - otherPoint.X;
        double dy = endpoint.Y - otherPoint.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        
        if (len < 0.001)
            return (0, 0);

        return (dx / len, dy / len);
    }

    /// <summary>
    /// Check if two endpoints can be bridged - handles both collinear and perpendicular lines
    /// Returns true if they can be bridged, and optionally returns the connection point (for L/T joints)
    /// </summary>
    private bool CanBridgeEndpoints(Point2D p1, (double dx, double dy) dir1, 
        Point2D p2, (double dx, double dy) dir2, double gapTolerance, out Point2D? connectionPoint)
    {
        connectionPoint = null;

        // Check if endpoints are very close - just connect directly
        double distance = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
        if (distance < 0.1)
        {
            return true; // Very close, connect directly
        }

        // Check if lines are collinear (nearly parallel) - use existing collinearity check
        double linesDot = Math.Abs(dir1.dx * dir2.dx + dir1.dy * dir2.dy);
        if (linesDot > 0.95) // Lines are collinear
        {
            // Use the existing collinearity logic
            double gapDx = p2.X - p1.X;
            double gapDy = p2.Y - p1.Y;
            double gapLen = Math.Sqrt(gapDx * gapDx + gapDy * gapDy);
            if (gapLen < 0.001)
                return true;

            gapDx /= gapLen;
            gapDy /= gapLen;

            double gapDot1 = gapDx * dir1.dx + gapDy * dir1.dy;
            double gapDot2 = gapDx * dir2.dx + gapDy * dir2.dy;

            // Gap must align with both directions
            return Math.Abs(gapDot1) > 0.95 && Math.Abs(gapDot2) > 0.95;
        }

        // Lines are not collinear - check if they're perpendicular (L or T joint)
        // For perpendicular lines, dot product should be near 0
        if (Math.Abs(linesDot) < 0.1) // Lines are perpendicular (within ~5 degrees of 90)
        {
            // Find intersection point by extending both lines
            // Line 1: p1 + t * dir1
            // Line 2: p2 + s * dir2
            // Solve for intersection: p1 + t * dir1 = p2 + s * dir2
            // Rearranging: t * dir1 - s * dir2 = p2 - p1

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;

            // Solve using Cramer's rule for:
            // t * dir1.dx - s * dir2.dx = dx
            // t * dir1.dy - s * dir2.dy = dy
            double det = dir1.dx * (-dir2.dy) - dir1.dy * (-dir2.dx);
            
            if (Math.Abs(det) > 0.001) // Lines are not parallel
            {
                double t = (dx * (-dir2.dy) - dy * (-dir2.dx)) / det;
                double s = (dir1.dx * dy - dir1.dy * dx) / det;

                // Check if intersection is within reasonable extension distance
                if (t >= -gapTolerance && t <= gapTolerance && s >= -gapTolerance && s <= gapTolerance)
                {
                    // Calculate intersection point
                    double ix = p1.X + t * dir1.dx;
                    double iy = p1.Y + t * dir1.dy;

                    // Check if intersection point is within tolerance of both endpoints
                    double dist1 = Math.Sqrt(Math.Pow(ix - p1.X, 2) + Math.Pow(iy - p1.Y, 2));
                    double dist2 = Math.Sqrt(Math.Pow(ix - p2.X, 2) + Math.Pow(iy - p2.Y, 2));

                    if (dist1 <= gapTolerance && dist2 <= gapTolerance)
                    {
                        connectionPoint = new Point2D { X = ix, Y = iy };
                        return true;
                    }
                }
            }
        }

        // For other angles, if endpoints are close enough, allow connection
        // This handles cases where lines are at angles other than 0, 90, or 180 degrees
        if (distance <= gapTolerance * 0.5)
        {
            return true; // Close enough, connect directly
        }

        return false;
    }

    /// <summary>
    /// Create a merged path from two paths
    /// </summary>
    private RawPath CreateMergedPath(RawPath path1, RawPath path2, List<Point2D> mergedPoints)
    {
        return new RawPath
        {
            Points = mergedPoints,
            LineWidth = Math.Max(path1.LineWidth, path2.LineWidth), // Use thicker line width
            IsStroked = path1.IsStroked || path2.IsStroked,
            IsFilled = path1.IsFilled || path2.IsFilled,
            SegmentCount = mergedPoints.Count,
            PathLength = CalculatePathLength(mergedPoints),
            PathType = "Polyline", // Merged paths are polylines
            ObjectIndex = Math.Min(path1.ObjectIndex, path2.ObjectIndex) // Keep original index
        };
    }

    /// <summary>
    /// Check if two points are close within tolerance
    /// </summary>
    private bool ArePointsClose(Point2D p1, Point2D p2, double tolerance)
    {
        double distance = Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
        return distance <= tolerance;
    }

    /// <summary>
    /// Check if endpoints can be bridged by extending lines in their current direction
    /// Rule: Only bridge if lines are collinear (nearly parallel) AND gap aligns with both directions
    /// This prevents bridging parallel lines at corners (like ||)
    /// </summary>
    private bool AreEndpointsCollinear(Point2D endpoint1, Point2D directionPoint1, Point2D endpoint2, Point2D directionPoint2)
    {
        // Calculate direction vectors for each line
        double dx1 = directionPoint1.X - endpoint1.X;
        double dy1 = directionPoint1.Y - endpoint1.Y;
        double dx2 = directionPoint2.X - endpoint2.X;
        double dy2 = directionPoint2.Y - endpoint2.Y;

        // Calculate gap direction
        double gapDx = endpoint2.X - endpoint1.X;
        double gapDy = endpoint2.Y - endpoint1.Y;

        // Normalize vectors
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        double len2 = Math.Sqrt(dx2 * dx2 + dy2 * dy2);
        double gapLen = Math.Sqrt(gapDx * gapDx + gapDy * gapDy);

        if (len1 < 0.001 || len2 < 0.001 || gapLen < 0.001)
            return true; // Degenerate case (points are very close), allow connection

        dx1 /= len1;
        dy1 /= len1;
        dx2 /= len2;
        gapDx /= gapLen;
        gapDy /= gapLen;

        // First check: Are the two lines collinear (nearly parallel and pointing in same direction)?
        // Use dot product to check if line directions are aligned
        double linesDot = Math.Abs(dx1 * dx2 + dy1 * dy2);
        if (linesDot < 0.95) // Lines are not collinear (more than ~18 degrees apart)
        {
            return false; // Don't bridge non-collinear lines
        }

        // Second check: Does the gap direction align with BOTH line directions?
        // The gap should be in the same direction as both lines (we can extend either line to bridge)
        double gapDot1 = gapDx * dx1 + gapDy * dy1;
        double gapDot2 = gapDx * dx2 + gapDy * dy2;

        // Gap must align with both directions (within ~18 degrees)
        // We use the absolute value to allow bridging in either direction along the line
        return Math.Abs(gapDot1) > 0.95 && Math.Abs(gapDot2) > 0.95;
    }

    /// <summary>
    /// Calculate total length of a path
    /// </summary>
    private double CalculatePathLength(List<Point2D> points)
    {
        if (points.Count < 2)
            return 0;

        double totalLength = 0;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            double dy = points[i + 1].Y - points[i].Y;
            totalLength += Math.Sqrt(dx * dx + dy * dy);
        }

        return totalLength;
    }

    /// <summary>
    /// Recursively extract LineStrings from a geometry
    /// </summary>
    private void ExtractLineStrings(Geometry geometry, List<RawPath> result, double lineWidth)
    {
        if (geometry is LineString lineString)
        {
            var points = lineString.Coordinates.Select(c => new Point2D { X = c.X, Y = c.Y }).ToList();
            if (points.Count >= 2)
            {
                result.Add(new RawPath
                {
                    Points = points,
                    LineWidth = lineWidth,
                    PathLength = lineString.Length
                });
            }
        }
        else if (geometry is GeometryCollection collection)
        {
            foreach (var geom in collection.Geometries)
            {
                ExtractLineStrings(geom, result, lineWidth);
            }
        }
    }

    /// <summary>
    /// Reconstruct closed polygons from line segments using NTS Polygonizer
    /// This is MUCH faster and more robust than the custom implementation
    /// </summary>
    public List<RoomBoundary> ReconstructPolygons(List<RawPath> paths)
    {
        _logger.LogInformation("NTS: Reconstructing polygons from {Count} paths", paths.Count);

        if (paths.Count == 0)
        {
            _logger.LogWarning("NTS: No paths provided for polygon reconstruction");
            return new List<RoomBoundary>();
        }

        try
        {
            var result = new List<RoomBoundary>();
            
            // First, try to create polygons directly from closed paths
            // This avoids the noding issue where shared walls create many small faces
            foreach (var path in paths)
            {
                if (path.Points.Count < 4) continue; // Need at least 4 points for a polygon
                
                var coords = path.Points.Select(p => new Coordinate(p.X, p.Y)).ToList();
                
                // Check if path is closed (start == end within tolerance)
                var first = coords[0];
                var last = coords[^1];
                var distance = Math.Sqrt(Math.Pow(first.X - last.X, 2) + Math.Pow(first.Y - last.Y, 2));
                
                _logger.LogInformation("NTS: Path has {Points} points, distance from start to end: {Distance:F2}", coords.Count, distance);
                
                if (distance < 1.0) // Path is closed
                {
                    // Ensure exactly closed
                    if (!first.Equals2D(last))
                    {
                        coords.Add(new Coordinate(first.X, first.Y));
                    }
                    
                    try
                    {
                        var ring = _geometryFactory.CreateLinearRing(coords.ToArray());
                        
                        // Check if ring is valid (not self-intersecting)
                        if (ring.IsValid && ring.IsSimple)
                        {
                            var polygon = _geometryFactory.CreatePolygon(ring);
                            if (polygon.IsValid && polygon.Area > 0)
                            {
                                result.Add(ToRoomBoundary(polygon));
                                _logger.LogInformation("NTS: Created polygon directly from closed path (area: {Area:F2})", polygon.Area);
                            }
                        }
                        else
                        {
                            // Ring is self-intersecting - use Polygonizer on this single ring
                            _logger.LogInformation("NTS: Ring is self-intersecting, using Polygonizer on single ring");
                            var lineString = _geometryFactory.CreateLineString(coords.ToArray());
                            var nodedRing = lineString.Union(); // Node the self-intersecting ring
                            
                            var singlePolygonizer = new Polygonizer();
                            singlePolygonizer.Add(nodedRing);
                            var singlePolygons = singlePolygonizer.GetPolygons();
                            
                            _logger.LogInformation("NTS: Single ring polygonizer found {Count} polygons", singlePolygons.Count);
                            
                            foreach (Polygon p in singlePolygons.Cast<Polygon>())
                            {
                                if (p.IsValid && p.Area > 0)
                                {
                                    result.Add(ToRoomBoundary(p));
                                    _logger.LogInformation("NTS: Added polygon from single ring, area: {Area:F2}", p.Area);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("NTS: Failed to create polygon directly: {Message}", ex.Message);
                    }
                }
            }
            
            // If we successfully created polygons directly, return them
            if (result.Count > 0)
            {
                _logger.LogInformation("NTS: Reconstructed {Count} polygons directly from closed paths", result.Count);
                return result;
            }
            
            // Fall back to polygonizer with noding for complex cases
            _logger.LogInformation("NTS: Falling back to polygonizer with noding");
            
            // Convert paths to NTS LineStrings
            var lineStrings = paths.Select(ToLineString).ToList();

            // Log path information for debugging
            int totalPoints = lineStrings.Sum(ls => ls.Coordinates.Length);
            _logger.LogDebug("NTS: Total points in all paths: {PointCount}", totalPoints);
            _logger.LogDebug("NTS: Path lengths: {Lengths}", 
                string.Join(", ", lineStrings.Select(ls => ls.Length.ToString("F2"))));

            // Node the geometry - this splits lines at their intersection points
            // This is essential for handling adjacent rooms that share walls
            var multiLine = _geometryFactory.CreateMultiLineString(lineStrings.ToArray());
            var nodedGeometry = multiLine.Union(); // Union with itself nodes the geometry
            
            // Extract all line segments from the noded result
            var nodedLines = new List<Geometry>();
            for (int i = 0; i < nodedGeometry.NumGeometries; i++)
            {
                nodedLines.Add(nodedGeometry.GetGeometryN(i));
            }
            
            _logger.LogInformation("NTS: After noding: {Original} lines -> {Noded} lines", 
                lineStrings.Count, nodedLines.Count);
            
            // Use NTS Polygonizer to find all closed polygons
            var polygonizer = new Polygonizer();
            polygonizer.Add(nodedLines);

            // Get all results from polygonizer
            var polygons = polygonizer.GetPolygons();
            var cutEdges = polygonizer.GetCutEdges();
            var dangles = polygonizer.GetDangles();
            var invalidRingLines = polygonizer.GetInvalidRingLines();

            _logger.LogInformation("NTS: Polygonizer results - Polygons: {PolygonCount}, Cut Edges: {CutEdgeCount}, Dangles: {DangleCount}, Invalid Rings: {InvalidRingCount}",
                polygons.Count, cutEdges.Count, dangles.Count, invalidRingLines.Count);

            if (polygons.Count == 0)
            {
                _logger.LogWarning("NTS: No polygons found. This may indicate:");
                _logger.LogWarning("  - Paths do not form closed loops");
                _logger.LogWarning("  - Gaps are too large to form closed rings");
                _logger.LogWarning("  - Paths need additional gap bridging");
                
                // Log some diagnostic information
                if (cutEdges.Count > 0)
                {
                    _logger.LogDebug("NTS: Found {Count} cut edges (edges that are not part of any polygon)", cutEdges.Count);
                }
                if (dangles.Count > 0)
                {
                    _logger.LogDebug("NTS: Found {Count} dangles (edges that don't connect to form rings)", dangles.Count);
                }
            }

            // Convert to RoomBoundary
            result = polygons
                .Cast<Polygon>()
                .Select(ToRoomBoundary)
                .ToList();

            _logger.LogInformation("NTS: Reconstructed {Count} polygons", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Polygon reconstruction failed");
            return new List<RoomBoundary>();
        }
    }

    /// <summary>
    /// Remove outer polygons (keep only inner boundaries)
    /// Uses spatial containment checks - much faster than custom implementation
    /// </summary>
    public List<RoomBoundary> RemoveOuterPolygons(List<RoomBoundary> boundaries)
    {
        if (boundaries.Count <= 1)
            return boundaries;

        _logger.LogInformation("NTS: Removing outer polygons from {Count} boundaries", boundaries.Count);

        try
        {
            // Convert to NTS Polygons
            var ntsPolygons = boundaries.Select(b =>
            {
                var coords = b.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                // Ensure closed ring
                if (!coords[0].Equals2D(coords[^1]))
                {
                    coords = coords.Append(coords[0]).ToArray();
                }
                var ring = _geometryFactory.CreateLinearRing(coords);
                return _geometryFactory.CreatePolygon(ring);
            }).ToList();

            // Find polygons that ARE contained by at least one other polygon
            // These are the room interiors (contained within the outer wall boundary)
            var innerPolygons = new List<Polygon>();

            for (int i = 0; i < ntsPolygons.Count; i++)
            {
                bool isContained = false;

                for (int j = 0; j < ntsPolygons.Count; j++)
                {
                    if (i == j) continue;

                    // Check if polygon i is contained within polygon j
                    if (ntsPolygons[j].Contains(ntsPolygons[i]))
                    {
                        isContained = true;
                        break;
                    }
                }

                // Keep polygons that ARE contained (room interiors inside outer walls)
                if (isContained)
                {
                    innerPolygons.Add(ntsPolygons[i]);
                }
            }

            _logger.LogInformation("NTS: Removed outer polygons: {Original} → {Inner}",
                boundaries.Count, innerPolygons.Count);

            // Convert back to RoomBoundary
            return innerPolygons.Select(ToRoomBoundary).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Remove outer polygons failed, returning original");
            return boundaries;
        }
    }

    /// <summary>
    /// Remove nested polygons (keep only outermost polygons)
    /// </summary>
    public List<RoomBoundary> RemoveNestedPolygons(List<RoomBoundary> boundaries)
    {
        if (boundaries.Count <= 1)
            return boundaries;

        _logger.LogInformation("NTS: Removing nested polygons from {Count} boundaries", boundaries.Count);

        try
        {
            // Convert to NTS Polygons
            var ntsPolygons = boundaries.Select(b =>
            {
                var coords = b.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
                if (!coords[0].Equals2D(coords[^1]))
                {
                    coords = coords.Append(coords[0]).ToArray();
                }
                var ring = _geometryFactory.CreateLinearRing(coords);
                return _geometryFactory.CreatePolygon(ring);
            }).ToList();

            // Keep only polygons that don't contain any other polygon
            var outerPolygons = new List<Polygon>();

            for (int i = 0; i < ntsPolygons.Count; i++)
            {
                bool containsOther = false;

                for (int j = 0; j < ntsPolygons.Count; j++)
                {
                    if (i == j) continue;

                    // Check if polygon i contains polygon j
                    if (ntsPolygons[i].Contains(ntsPolygons[j]))
                    {
                        containsOther = true;
                        break;
                    }
                }

                // Keep polygons that don't contain others
                if (!containsOther)
                {
                    outerPolygons.Add(ntsPolygons[i]);
                }
            }

            _logger.LogInformation("NTS: Removed nested polygons: {Original} → {Outer}",
                boundaries.Count, outerPolygons.Count);

            return outerPolygons.Select(ToRoomBoundary).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Remove nested polygons failed, returning original");
            return boundaries;
        }
    }

    /// <summary>
    /// Filter polygons by minimum area
    /// </summary>
    public List<RoomBoundary> FilterByMinArea(List<RoomBoundary> boundaries, double minArea)
    {
        if (minArea <= 0)
            return boundaries;

        // Log areas for debugging
        foreach (var b in boundaries)
        {
            var area = GetArea(b);
            _logger.LogInformation("NTS: Polygon area: {Area:F2}", area);
        }

        var filtered = boundaries.Where(b => GetArea(b) >= minArea).ToList();

        _logger.LogInformation("NTS: Filtered by min area {MinArea}: {Original} → {Filtered}",
            minArea, boundaries.Count, filtered.Count);

        return filtered;
    }

    /// <summary>
    /// Filter out thin strip polygons based on minimum width.
    /// Calculates the minimum width of the polygon's bounding box
    /// and filters out polygons where the width is less than the threshold.
    /// </summary>
    /// <param name="boundaries">List of boundaries to filter</param>
    /// <param name="minWidth">Minimum width in units. Polygons narrower than this are filtered.</param>
    public List<RoomBoundary> FilterByMinWidth(List<RoomBoundary> boundaries, double minWidth)
    {
        if (boundaries.Count == 0 || minWidth <= 0)
            return boundaries;

        _logger.LogInformation("NTS: Filtering by min width {MinWidth:F2}", minWidth);

        var result = new List<RoomBoundary>();

        foreach (var boundary in boundaries)
        {
            var coords = boundary.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToArray();
            if (!coords[0].Equals2D(coords[^1]))
            {
                coords = coords.Append(coords[0]).ToArray();
            }
            var ring = _geometryFactory.CreateLinearRing(coords);
            var polygon = _geometryFactory.CreatePolygon(ring);

            var envelope = polygon.EnvelopeInternal;
            var width = Math.Min(envelope.Width, envelope.Height);

            _logger.LogInformation("NTS: Polygon - Min dimension: {Width:F2}, BBox: {EnvWidth:F2} x {EnvHeight:F2}",
                width, envelope.Width, envelope.Height);

            if (width >= minWidth)
            {
                result.Add(boundary);
            }
            else
            {
                _logger.LogInformation("NTS: Removed thin strip polygon (width {Width:F2} < {Min:F2})",
                    width, minWidth);
            }
        }

        _logger.LogInformation("NTS: Filtered by min width: {Original} → {Filtered}",
            boundaries.Count, result.Count);

        return result;
    }
}

