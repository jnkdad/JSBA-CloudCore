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
    /// Extract inner boundaries from double wall lines.
    /// For parallel wall pairs, keeps the inner line (closer to room center) to help form separate room boundaries.
    /// IMPORTANT: Preserves and duplicates dividing walls so each room gets its own complete boundary.
    /// This is useful when CollapseParallelWalls loses information needed to separate adjacent rooms.
    /// </summary>
    public List<RawPath> ExtractInnerBoundaries(List<RawPath> paths, double wallThickness)
    {
        if (wallThickness <= 0 || paths.Count < 2)
            return paths;

        _logger.LogInformation("NTS: Extracting inner boundaries from {Count} paths with wall thickness tolerance {Thickness}",
            paths.Count, wallThickness);

        var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
        var otherPaths = paths.Where(p => p.Points.Count != 2).ToList();

        if (lineSegments.Count < 4)
        {
            _logger.LogInformation("NTS: Not enough line segments, returning paths as-is");
            return paths;
        }

        // First, identify dividing walls BEFORE processing parallel pairs
        // This ensures we preserve them even if they're part of parallel pairs
        var horizontals = new List<RawPath>();
        var verticals = new List<RawPath>();

        foreach (var line in lineSegments)
        {
            var dx = Math.Abs(line.Points[1].X - line.Points[0].X);
            var dy = Math.Abs(line.Points[1].Y - line.Points[0].Y);

            if (dx > dy * 3)
                horizontals.Add(line);
            else if (dy > dx * 3)
                verticals.Add(line);
        }

        var dividingWalls = new List<RawPath>();
        if (horizontals.Count >= 2 && verticals.Count >= 2)
        {
            var topY = horizontals.Min(h => (h.Points[0].Y + h.Points[1].Y) / 2);
            var bottomY = horizontals.Max(h => (h.Points[0].Y + h.Points[1].Y) / 2);
            var allX = verticals.Select(v => (v.Points[0].X + v.Points[1].X) / 2).ToList();
            var minX = allX.Min();
            var maxX = allX.Max();

            foreach (var v in verticals)
            {
                var vX = (v.Points[0].X + v.Points[1].X) / 2;
                var vMinY = Math.Min(v.Points[0].Y, v.Points[1].Y);
                var vMaxY = Math.Max(v.Points[0].Y, v.Points[1].Y);
                
                bool connectsTop = Math.Abs(vMinY - topY) < wallThickness * 2;
                bool connectsBottom = Math.Abs(vMaxY - bottomY) < wallThickness * 2;
                bool isNotAtEdge = (vX - minX) > wallThickness && (maxX - vX) > wallThickness;
                
                if (connectsTop && connectsBottom && isNotAtEdge)
                {
                    dividingWalls.Add(v);
                    _logger.LogInformation("NTS: Identified dividing wall at X={X:F1} for inner boundary extraction", vX);
                }
            }
        }

        var usedIndices = new HashSet<int>();
        var result = new List<RawPath>(otherPaths);
        var parallelPairs = new List<(int i, int j, RawPath inner, RawPath outer, bool isDividingWall)>();

        // First pass: identify parallel line pairs, but preserve dividing walls
        for (int i = 0; i < lineSegments.Count; i++)
        {
            if (usedIndices.Contains(i))
                continue;

            var path1 = lineSegments[i];
            bool path1IsDividing = dividingWalls.Contains(path1);

            // If this is a dividing wall, don't collapse it - keep it and we'll duplicate it later
            if (path1IsDividing)
            {
                result.Add(path1);
                usedIndices.Add(i);
                _logger.LogInformation("NTS: Preserving dividing wall (path {I}) without collapsing", i);
                continue;
            }

            bool foundPair = false;
            for (int j = i + 1; j < lineSegments.Count; j++)
            {
                if (usedIndices.Contains(j))
                    continue;

                var path2 = lineSegments[j];
                bool path2IsDividing = dividingWalls.Contains(path2);

                // Don't collapse if either is a dividing wall
                if (path2IsDividing)
                    continue;

                // Check if parallel and close
                if (AreParallelWallLines(path1, path2, wallThickness, out var centerPath))
                {
                    // Determine which is inner vs outer
                    var (inner, outer) = DetermineInnerOuter(path1, path2, lineSegments);
                    parallelPairs.Add((i, j, inner, outer, false));
                    usedIndices.Add(i);
                    usedIndices.Add(j);
                    foundPair = true;
                    break;
                }
            }

            if (!foundPair && !path1IsDividing)
            {
                // No parallel pair found, keep as-is
                result.Add(path1);
                usedIndices.Add(i);
            }
        }

        // Keep inner lines from parallel pairs
        foreach (var (i, j, inner, outer, isDiv) in parallelPairs)
        {
            result.Add(inner);
            _logger.LogInformation("NTS: Keeping inner line from parallel pair (paths {I} and {J})", i, j);
        }

        // Split horizontal lines at dividing walls and duplicate dividing walls
        // This ensures each room has its own complete boundary
        var finalHorizontals = new List<RawPath>();
        foreach (var h in horizontals)
        {
            // Check if this horizontal was kept (not collapsed)
            bool wasKept = result.Contains(h) || parallelPairs.Any(p => p.inner == h || p.outer == h && !dividingWalls.Contains(h));
            if (!wasKept)
                continue; // This horizontal was part of a collapsed pair, skip it

            bool needsSplitting = false;
            double divX = 0;

            foreach (var dividingWall in dividingWalls)
            {
                divX = (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2;
                var hMinX = Math.Min(h.Points[0].X, h.Points[1].X);
                var hMaxX = Math.Max(h.Points[0].X, h.Points[1].X);
                
                if (hMinX < divX && hMaxX > divX)
                {
                    needsSplitting = true;
                    break;
                }
            }

            if (needsSplitting)
            {
                var hMinX = Math.Min(h.Points[0].X, h.Points[1].X);
                var hMaxX = Math.Max(h.Points[0].X, h.Points[1].X);
                // Use exact Y coordinate - both points should have same Y for horizontal line, but use the actual Y
                // This ensures proper alignment at top and bottom
                var hY = h.Points[0].Y; // Use first point's Y (should be same as second for horizontal)
                
                // Remove the original horizontal from result if it's there
                result.Remove(h);
                
                // Use the same offset as the dividing wall duplication
                const double horizOffsetEpsilon = 0.001;
                
                // Split into left and right parts
                // Left part connects to original dividing wall at divX
                // Right part connects to offset dividing wall at divX + horizOffsetEpsilon
                // IMPORTANT: Use exact same Y coordinate for both parts to ensure alignment
                var leftStart = h.Points[0].X < h.Points[1].X ? h.Points[0] : h.Points[1];
                var rightEnd = h.Points[0].X > h.Points[1].X ? h.Points[0] : h.Points[1];
                
                var leftPart = new RawPath
                {
                    Points = new List<Point2D>
                    {
                        new Point2D { X = leftStart.X, Y = leftStart.Y }, // Keep original Y
                        new Point2D { X = divX, Y = leftStart.Y } // Connects to original wall, same Y
                    },
                    LineWidth = h.LineWidth,
                    IsStroked = h.IsStroked,
                    IsFilled = h.IsFilled,
                    SegmentCount = 1,
                    PathLength = divX - hMinX,
                    PathType = h.PathType,
                    PathTypeEnum = h.PathTypeEnum,
                    WallThickness = h.WallThickness,
                    ObjectIndex = h.ObjectIndex
                };
                var rightPart = new RawPath
                {
                    Points = new List<Point2D>
                    {
                        new Point2D { X = divX + horizOffsetEpsilon, Y = rightEnd.Y }, // Connects to offset wall, same Y as right end
                        new Point2D { X = rightEnd.X, Y = rightEnd.Y } // Keep original Y
                    },
                    LineWidth = h.LineWidth,
                    IsStroked = h.IsStroked,
                    IsFilled = h.IsFilled,
                    SegmentCount = 1,
                    PathLength = hMaxX - divX,
                    PathType = h.PathType,
                    PathTypeEnum = h.PathTypeEnum,
                    WallThickness = h.WallThickness,
                    ObjectIndex = h.ObjectIndex
                };
                finalHorizontals.Add(leftPart);
                finalHorizontals.Add(rightPart);
                _logger.LogInformation("NTS: Split horizontal line at dividing wall X={X:F1} (Y={Y:F1}, left connects to {X1:F4}, right to {X2:F4})", 
                    divX, leftStart.Y, divX, divX + horizOffsetEpsilon);
            }
            else
            {
                finalHorizontals.Add(h);
            }
        }

        // Replace horizontals in result with split versions
        foreach (var h in horizontals)
        {
            result.Remove(h);
        }
        result.AddRange(finalHorizontals);

        // Duplicate dividing walls so each room gets its own copy
        // IMPORTANT: Offset the duplicated walls in X direction (horizontal) so NTS treats them as distinct
        // We use a small offset (0.001 units) that's below visual threshold but prevents merging
        // The offset is in X direction (horizontal) because the dividing walls are vertical
        const double offsetEpsilon = 0.001;
        foreach (var dividingWall in dividingWalls)
        {
            var divX = (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2;
            var divMinY = Math.Min(dividingWall.Points[0].Y, dividingWall.Points[1].Y);
            var divMaxY = Math.Max(dividingWall.Points[0].Y, dividingWall.Points[1].Y);
            
            // Original wall - keep as-is (will be used by left room)
            // Ensure it's exactly at divX for proper alignment
            var originalPoints = dividingWall.Points.Select(p => new Point2D 
            { 
                X = divX, // Ensure exact X coordinate for alignment
                Y = p.Y // Keep original Y to ensure bottom/top alignment
            }).ToList();
            result.Add(new RawPath
            {
                Points = originalPoints,
                LineWidth = dividingWall.LineWidth,
                IsStroked = dividingWall.IsStroked,
                IsFilled = dividingWall.IsFilled,
                SegmentCount = dividingWall.SegmentCount,
                PathLength = dividingWall.PathLength,
                PathType = dividingWall.PathType,
                PathTypeEnum = dividingWall.PathTypeEnum,
                WallThickness = dividingWall.WallThickness,
                ObjectIndex = dividingWall.ObjectIndex
            });
            
            // Duplicate wall - offset to the right (X direction) so NTS treats it as distinct
            // This ensures right room gets its own copy that won't be merged with the original
            // Keep same Y coordinates to ensure alignment at top and bottom
            var offsetPoints = dividingWall.Points.Select(p => new Point2D 
            { 
                X = divX + offsetEpsilon, // Offset to the right (X direction)
                Y = p.Y // Keep same Y to ensure bottom/top alignment
            }).ToList();
            
            result.Add(new RawPath
            {
                Points = offsetPoints,
                LineWidth = dividingWall.LineWidth,
                IsStroked = dividingWall.IsStroked,
                IsFilled = dividingWall.IsFilled,
                SegmentCount = dividingWall.SegmentCount,
                PathLength = dividingWall.PathLength,
                PathType = dividingWall.PathType,
                PathTypeEnum = dividingWall.PathTypeEnum,
                WallThickness = dividingWall.WallThickness,
                ObjectIndex = dividingWall.ObjectIndex + 10000 // Different index to help distinguish
            }); // Duplicate with X offset
            _logger.LogInformation("NTS: Duplicated dividing wall at X={X:F1} (Y range {Y1:F1} to {Y2:F1}) with {Offset:F4} X-offset for separate room boundaries", 
                divX, divMinY, divMaxY, offsetEpsilon);
        }

        _logger.LogInformation("NTS: Extracted inner boundaries: {Original} → {Result} paths ({Pairs} parallel pairs, {DivWalls} dividing walls duplicated, {SplitHoriz} horizontals split)",
            paths.Count, result.Count, parallelPairs.Count, dividingWalls.Count, finalHorizontals.Count - horizontals.Count);

        return result;
    }

    /// <summary>
    /// Determine which of two parallel lines is the inner boundary (closer to room centers)
    /// Uses heuristic: inner line is typically closer to the centroid of all paths
    /// </summary>
    private (RawPath inner, RawPath outer) DetermineInnerOuter(RawPath path1, RawPath path2, List<RawPath> allPaths)
    {
        // Calculate centroid of all paths to approximate structure center
        double sumX = 0, sumY = 0;
        int count = 0;
        foreach (var path in allPaths)
        {
            foreach (var point in path.Points)
            {
                sumX += point.X;
                sumY += point.Y;
                count++;
            }
        }
        var centroidX = sumX / count;
        var centroidY = sumY / count;

        // Calculate distance from each line's midpoint to centroid
        var mid1X = (path1.Points[0].X + path1.Points[1].X) / 2;
        var mid1Y = (path1.Points[0].Y + path1.Points[1].Y) / 2;
        var dist1 = Math.Sqrt(Math.Pow(mid1X - centroidX, 2) + Math.Pow(mid1Y - centroidY, 2));

        var mid2X = (path2.Points[0].X + path2.Points[1].X) / 2;
        var mid2Y = (path2.Points[0].Y + path2.Points[1].Y) / 2;
        var dist2 = Math.Sqrt(Math.Pow(mid2X - centroidX, 2) + Math.Pow(mid2Y - centroidY, 2));

        // Closer to centroid is typically inner boundary
        return dist1 < dist2 ? (path1, path2) : (path2, path1);
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

        // The actual wall thickness is the perpendicular distance between the two parallel lines
        // We already calculated this as 'dist' above
        double actualWallThickness = dist;

        centerPath = new RawPath
        {
            Points = new List<Point2D> { centerStart, centerEnd },
            LineWidth = (path1.LineWidth + path2.LineWidth) / 2,
            IsStroked = path1.IsStroked,
            IsFilled = path1.IsFilled,
            SegmentCount = 1,
            PathLength = Math.Sqrt(Math.Pow(centerEnd.X - centerStart.X, 2) + Math.Pow(centerEnd.Y - centerStart.Y, 2)),
            PathType = "Centerline", // Legacy string type
            PathTypeEnum = PathTypeEnum.Centerline, // New enum type
            WallThickness = actualWallThickness, // Store the actual measured wall thickness
            ObjectIndex = path1.ObjectIndex
        };

        return true;
    }

    /// <summary>
    /// Merge collinear line segments that are on the same line (within tolerance).
    /// This reduces multiple short segments on the same wall to a single longer segment.
    /// </summary>
    public List<RawPath> MergeCollinearSegments(List<RawPath> paths, double yTolerance)
    {
        if (paths.Count < 2)
            return paths;

        _logger.LogInformation("NTS: Merging collinear segments with Y tolerance {Tolerance} for {Count} paths",
            yTolerance, paths.Count);

        var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
        var otherPaths = paths.Where(p => p.Points.Count != 2).ToList();

        // Separate horizontal and vertical lines
        var horizontals = new List<RawPath>();
        var verticals = new List<RawPath>();
        var others = new List<RawPath>();

        foreach (var line in lineSegments)
        {
            var dx = Math.Abs(line.Points[1].X - line.Points[0].X);
            var dy = Math.Abs(line.Points[1].Y - line.Points[0].Y);

            if (dx > dy * 3)
                horizontals.Add(line);
            else if (dy > dx * 3)
                verticals.Add(line);
            else
                others.Add(line);
        }

        // Get vertical line X positions to use as split points for horizontal merging
        var verticalXPositions = verticals
            .Select(v => (v.Points[0].X + v.Points[1].X) / 2)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        // Merge horizontal lines with same Y (within tolerance), but don't merge across vertical walls
        var mergedHorizontals = MergeHorizontalLines(horizontals, yTolerance, verticalXPositions);

        // Get horizontal line Y positions to use as split points for vertical merging
        var horizontalYPositions = horizontals
            .Select(h => (h.Points[0].Y + h.Points[1].Y) / 2)
            .Distinct()
            .OrderBy(y => y)
            .ToList();

        // Merge vertical lines with same X (within tolerance), but don't merge across horizontal walls
        var mergedVerticals = MergeVerticalLines(verticals, yTolerance, horizontalYPositions);

        var result = new List<RawPath>();
        result.AddRange(mergedHorizontals);
        result.AddRange(mergedVerticals);
        result.AddRange(others);
        result.AddRange(otherPaths);

        _logger.LogInformation("NTS: Merged collinear segments: {Before} → {After} paths (H:{H}, V:{V}, Other:{O})",
            paths.Count, result.Count, mergedHorizontals.Count, mergedVerticals.Count, others.Count + otherPaths.Count);

        // Log each resulting path for debugging
        for (int i = 0; i < result.Count; i++)
        {
            var p = result[i];
            if (p.Points.Count == 2)
            {
                _logger.LogInformation("NTS: Path {I}: ({X1:F1},{Y1:F1}) -> ({X2:F1},{Y2:F1})",
                    i, p.Points[0].X, p.Points[0].Y, p.Points[1].X, p.Points[1].Y);
            }
        }

        return result;
    }

    private List<RawPath> MergeHorizontalLines(List<RawPath> lines, double yTolerance, List<double> verticalXPositions)
    {
        if (lines.Count < 2)
            return lines;

        // First, group by Y coordinate (within tolerance)
        var yGroups = new List<List<RawPath>>();
        var used = new HashSet<int>();

        for (int i = 0; i < lines.Count; i++)
        {
            if (used.Contains(i)) continue;

            var group = new List<RawPath> { lines[i] };
            used.Add(i);
            var avgY = (lines[i].Points[0].Y + lines[i].Points[1].Y) / 2;

            for (int j = i + 1; j < lines.Count; j++)
            {
                if (used.Contains(j)) continue;

                var otherY = (lines[j].Points[0].Y + lines[j].Points[1].Y) / 2;
                if (Math.Abs(avgY - otherY) <= yTolerance)
                {
                    group.Add(lines[j]);
                    used.Add(j);
                }
            }

            yGroups.Add(group);
        }

        // For each Y group, further split into X-overlapping subgroups, respecting vertical walls
        var result = new List<RawPath>();
        foreach (var yGroup in yGroups)
        {
            if (yGroup.Count == 1)
            {
                result.Add(yGroup[0]);
                continue;
            }

            // Sort by minX
            var sorted = yGroup.OrderBy(l => Math.Min(l.Points[0].X, l.Points[1].X)).ToList();

            // Merge overlapping/adjacent segments, but split at vertical wall positions
            var currentGroup = new List<RawPath> { sorted[0] };
            double currentMinX = Math.Min(sorted[0].Points[0].X, sorted[0].Points[1].X);
            double currentMaxX = Math.Max(sorted[0].Points[0].X, sorted[0].Points[1].X);

            for (int i = 1; i < sorted.Count; i++)
            {
                var line = sorted[i];
                var lineMinX = Math.Min(line.Points[0].X, line.Points[1].X);
                var lineMaxX = Math.Max(line.Points[0].X, line.Points[1].X);

                // Check if there's a vertical wall between current group and this line
                bool crossesVerticalWall = verticalXPositions.Any(vx =>
                    vx > currentMaxX - yTolerance && vx < lineMinX + yTolerance);

                // Check if this line overlaps or is adjacent to current group (within tolerance)
                if (lineMinX <= currentMaxX + yTolerance * 2 && !crossesVerticalWall)
                {
                    // Overlapping or adjacent and no vertical wall between - add to current group
                    currentGroup.Add(line);
                    currentMaxX = Math.Max(currentMaxX, lineMaxX);
                }
                else
                {
                    // Gap too large or crosses vertical wall - finalize current group and start new one
                    result.Add(CreateMergedHorizontalLine(currentGroup));
                    currentGroup = new List<RawPath> { line };
                    currentMinX = lineMinX;
                    currentMaxX = lineMaxX;
                }
            }

            // Finalize last group
            result.Add(CreateMergedHorizontalLine(currentGroup));
        }

        return result;
    }

    private RawPath CreateMergedHorizontalLine(List<RawPath> group)
    {
        if (group.Count == 1)
            return group[0];

        double minX = double.MaxValue, maxX = double.MinValue;
        double sumY = 0;
        double maxThickness = 0;

        foreach (var line in group)
        {
            minX = Math.Min(minX, Math.Min(line.Points[0].X, line.Points[1].X));
            maxX = Math.Max(maxX, Math.Max(line.Points[0].X, line.Points[1].X));
            sumY += (line.Points[0].Y + line.Points[1].Y) / 2;
            maxThickness = Math.Max(maxThickness, line.WallThickness);
        }

        double avgY = sumY / group.Count;

        _logger.LogDebug("NTS: Merged {Count} horizontal lines at Y≈{Y:F1} into single line from X={MinX:F1} to X={MaxX:F1}",
            group.Count, avgY, minX, maxX);

        return new RawPath
        {
            Points = new List<Point2D>
            {
                new Point2D { X = minX, Y = avgY },
                new Point2D { X = maxX, Y = avgY }
            },
            LineWidth = group.Max(l => l.LineWidth),
            IsStroked = group[0].IsStroked,
            IsFilled = group[0].IsFilled,
            SegmentCount = 1,
            PathLength = maxX - minX,
            PathType = group[0].PathType,
            PathTypeEnum = group[0].PathTypeEnum,
            WallThickness = maxThickness,
            ObjectIndex = group.Min(l => l.ObjectIndex)
        };
    }

    private List<RawPath> MergeVerticalLines(List<RawPath> lines, double xTolerance, List<double> horizontalYPositions)
    {
        if (lines.Count < 2)
            return lines;

        // First, group by X coordinate (within tolerance)
        var xGroups = new List<List<RawPath>>();
        var used = new HashSet<int>();

        for (int i = 0; i < lines.Count; i++)
        {
            if (used.Contains(i)) continue;

            var group = new List<RawPath> { lines[i] };
            used.Add(i);
            var avgX = (lines[i].Points[0].X + lines[i].Points[1].X) / 2;

            for (int j = i + 1; j < lines.Count; j++)
            {
                if (used.Contains(j)) continue;

                var otherX = (lines[j].Points[0].X + lines[j].Points[1].X) / 2;
                if (Math.Abs(avgX - otherX) <= xTolerance)
                {
                    group.Add(lines[j]);
                    used.Add(j);
                }
            }

            xGroups.Add(group);
        }

        // For each X group, further split into Y-overlapping subgroups, respecting horizontal walls
        var result = new List<RawPath>();
        foreach (var xGroup in xGroups)
        {
            if (xGroup.Count == 1)
            {
                result.Add(xGroup[0]);
                continue;
            }

            // Sort by minY
            var sorted = xGroup.OrderBy(l => Math.Min(l.Points[0].Y, l.Points[1].Y)).ToList();

            // Merge overlapping/adjacent segments, but split at horizontal wall positions
            var currentGroup = new List<RawPath> { sorted[0] };
            double currentMinY = Math.Min(sorted[0].Points[0].Y, sorted[0].Points[1].Y);
            double currentMaxY = Math.Max(sorted[0].Points[0].Y, sorted[0].Points[1].Y);

            for (int i = 1; i < sorted.Count; i++)
            {
                var line = sorted[i];
                var lineMinY = Math.Min(line.Points[0].Y, line.Points[1].Y);
                var lineMaxY = Math.Max(line.Points[0].Y, line.Points[1].Y);

                // Check if there's a horizontal wall between current group and this line
                bool crossesHorizontalWall = horizontalYPositions.Any(hy =>
                    hy > currentMaxY - xTolerance && hy < lineMinY + xTolerance);

                // Check if this line overlaps or is adjacent to current group (within tolerance)
                if (lineMinY <= currentMaxY + xTolerance * 2 && !crossesHorizontalWall)
                {
                    // Overlapping or adjacent and no horizontal wall between - add to current group
                    currentGroup.Add(line);
                    currentMaxY = Math.Max(currentMaxY, lineMaxY);
                }
                else
                {
                    // Gap too large or crosses horizontal wall - finalize current group and start new one
                    result.Add(CreateMergedVerticalLine(currentGroup));
                    currentGroup = new List<RawPath> { line };
                    currentMinY = lineMinY;
                    currentMaxY = lineMaxY;
                }
            }

            // Finalize last group
            result.Add(CreateMergedVerticalLine(currentGroup));
        }

        return result;
    }

    private RawPath CreateMergedVerticalLine(List<RawPath> group)
    {
        if (group.Count == 1)
            return group[0];

        double minY = double.MaxValue, maxY = double.MinValue;
        double sumX = 0;
        double maxThickness = 0;

        foreach (var line in group)
        {
            minY = Math.Min(minY, Math.Min(line.Points[0].Y, line.Points[1].Y));
            maxY = Math.Max(maxY, Math.Max(line.Points[0].Y, line.Points[1].Y));
            sumX += (line.Points[0].X + line.Points[1].X) / 2;
            maxThickness = Math.Max(maxThickness, line.WallThickness);
        }

        double avgX = sumX / group.Count;

        _logger.LogDebug("NTS: Merged {Count} vertical lines at X≈{X:F1} into single line from Y={MinY:F1} to Y={MaxY:F1}",
            group.Count, avgX, minY, maxY);

        return new RawPath
        {
            Points = new List<Point2D>
            {
                new Point2D { X = avgX, Y = minY },
                new Point2D { X = avgX, Y = maxY }
            },
            LineWidth = group.Max(l => l.LineWidth),
            IsStroked = group[0].IsStroked,
            IsFilled = group[0].IsFilled,
            SegmentCount = 1,
            PathLength = maxY - minY,
            PathType = group[0].PathType,
            PathTypeEnum = group[0].PathTypeEnum,
            WallThickness = maxThickness,
            ObjectIndex = group.Min(l => l.ObjectIndex)
        };
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

                // Check distance to left endpoint - vertical must be to the LEFT of line center
                var leftDist = Math.Abs(vX - hMinX);
                var hCenterX = (hMinX + hMaxX) / 2;
                if (leftDist < bestLeftDist && leftDist < snapTolerance * 3 && vX <= hCenterX)
                {
                    bestLeftDist = leftDist;
                    bestLeftVIdx = vIdx;
                    bestLeftVX = vX;
                }

                // Check distance to right endpoint - vertical must be to the RIGHT of line center
                var rightDist = Math.Abs(vX - hMaxX);
                if (rightDist < bestRightDist && rightDist < snapTolerance * 3 && vX >= hCenterX)
                {
                    bestRightDist = rightDist;
                    bestRightVIdx = vIdx;
                    bestRightVX = vX;
                }
            }

            // Ensure we don't extend both endpoints to the same vertical line
            if (bestLeftVIdx >= 0 && bestLeftVIdx == bestRightVIdx)
            {
                // Only extend the closer endpoint
                if (bestLeftDist <= bestRightDist)
                    bestRightVIdx = -1;
                else
                    bestLeftVIdx = -1;
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
                    _logger.LogInformation("NTS: Extended V{V} top to ({X:F1},{Y:F1})",
                        bestLeftVIdx, intersection.X, intersection.Y);
                }
                else if (Math.Abs(hY - currentVLine.Points[bottomIdx].Y) < snapTolerance * 2)
                {
                    vNewPoints[bottomIdx] = intersection;
                    modified[bestLeftVIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                    _logger.LogInformation("NTS: Extended V{V} bottom to ({X:F1},{Y:F1})",
                        bestLeftVIdx, intersection.X, intersection.Y);
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
                    _logger.LogInformation("NTS: Extended V{V} top to ({X:F1},{Y:F1})",
                        bestRightVIdx, intersection.X, intersection.Y);
                }
                else if (Math.Abs(hY - currentVLine.Points[bottomIdx].Y) < snapTolerance * 2)
                {
                    vNewPoints[bottomIdx] = intersection;
                    modified[bestRightVIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                    _logger.LogInformation("NTS: Extended V{V} bottom to ({X:F1},{Y:F1})",
                        bestRightVIdx, intersection.X, intersection.Y);
                }

                _logger.LogInformation("NTS: Extended H{H} right to V{V} at ({X:F1},{Y:F1})",
                    hIdx, bestRightVIdx, intersection.X, intersection.Y);
            }
        }

        // Also extend vertical lines to meet horizontal lines (symmetric extension)
        // This handles cases where vertical lines don't quite reach horizontal lines
        foreach (var (vIdx, vLine) in verticals)
        {
            if (modified.ContainsKey(vIdx))
                continue; // Already modified
            
            var vStart = vLine.Points[0];
            var vEnd = vLine.Points[1];
            var vX = (vStart.X + vEnd.X) / 2;
            var vMinY = Math.Min(vStart.Y, vEnd.Y);
            var vMaxY = Math.Max(vStart.Y, vEnd.Y);
            
            // Find horizontal lines that this vertical line should connect to
            foreach (var (hIdx, hLine) in horizontals)
            {
                var hStart = hLine.Points[0];
                var hEnd = hLine.Points[1];
                var hY = (hStart.Y + hEnd.Y) / 2;
                var hMinX = Math.Min(hStart.X, hEnd.X);
                var hMaxX = Math.Max(hStart.X, hEnd.X);
                
                // Check if vertical line's X is within horizontal line's X range (within tolerance)
                if (vX < hMinX - snapTolerance || vX > hMaxX + snapTolerance)
                    continue;
                
                // Check if horizontal line's Y is close to vertical line's endpoints
                var distToTop = Math.Abs(hY - vMaxY);
                var distToBottom = Math.Abs(hY - vMinY);
                
                // Extend top endpoint if close
                if (distToTop < snapTolerance * 2 && distToTop > 0.01)
                {
                    var currentVLine = modified.ContainsKey(vIdx) ? modified[vIdx] : vLine;
                    var vNewPoints = new List<Point2D>(currentVLine.Points);
                    var topIdx = currentVLine.Points[0].Y > currentVLine.Points[1].Y ? 0 : 1;
                    vNewPoints[topIdx] = new Point2D { X = vX, Y = hY };
                    modified[vIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                    _logger.LogInformation("NTS: Extended V{V} top to meet H{H} at Y={Y:F1} (distance was {Dist:F2})",
                        vIdx, hIdx, hY, distToTop);
                }
                
                // Extend bottom endpoint if close
                if (distToBottom < snapTolerance * 2 && distToBottom > 0.01)
                {
                    var currentVLine = modified.ContainsKey(vIdx) ? modified[vIdx] : vLine;
                    var vNewPoints = new List<Point2D>(currentVLine.Points);
                    var bottomIdx = currentVLine.Points[0].Y < currentVLine.Points[1].Y ? 0 : 1;
                    vNewPoints[bottomIdx] = new Point2D { X = vX, Y = hY };
                    modified[vIdx] = ClonePathWithNewPoints(currentVLine, vNewPoints);
                    _logger.LogInformation("NTS: Extended V{V} bottom to meet H{H} at Y={Y:F1} (distance was {Dist:F2})",
                        vIdx, hIdx, hY, distToBottom);
                }
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

    /// <summary>
    /// Filter out open lines (lines where at least one endpoint doesn't connect to any other line).
    /// Open lines interfere with polygon reconstruction.
    /// </summary>
    public List<RawPath> FilterOpenLines(List<RawPath> paths, double tolerance)
    {
        if (paths.Count < 2)
            return paths;

        _logger.LogInformation("NTS: Filtering open lines with tolerance {Tolerance} for {Count} paths",
            tolerance, paths.Count);

        var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
        var otherPaths = paths.Where(p => p.Points.Count != 2).ToList();

        // Collect all endpoints
        var allEndpoints = new List<(int pathIdx, int pointIdx, Point2D point)>();
        for (int i = 0; i < lineSegments.Count; i++)
        {
            allEndpoints.Add((i, 0, lineSegments[i].Points[0]));
            allEndpoints.Add((i, 1, lineSegments[i].Points[1]));
        }

        // For each line, check if both endpoints connect to another line
        var connectedLines = new List<RawPath>();
        var removedCount = 0;

        for (int i = 0; i < lineSegments.Count; i++)
        {
            var line = lineSegments[i];
            var p0 = line.Points[0];
            var p1 = line.Points[1];

            // Check if endpoint 0 connects to any other line's endpoint
            bool p0Connected = allEndpoints.Any(ep =>
                ep.pathIdx != i &&
                Math.Abs(ep.point.X - p0.X) < tolerance &&
                Math.Abs(ep.point.Y - p0.Y) < tolerance);

            // Check if endpoint 1 connects to any other line's endpoint
            bool p1Connected = allEndpoints.Any(ep =>
                ep.pathIdx != i &&
                Math.Abs(ep.point.X - p1.X) < tolerance &&
                Math.Abs(ep.point.Y - p1.Y) < tolerance);

            if (p0Connected && p1Connected)
            {
                connectedLines.Add(line);
            }
            else
            {
                removedCount++;
                _logger.LogInformation("NTS: Removed open line {I}: ({X1:F1},{Y1:F1}) -> ({X2:F1},{Y2:F1}) [p0:{P0}, p1:{P1}]",
                    i, p0.X, p0.Y, p1.X, p1.Y, p0Connected ? "connected" : "open", p1Connected ? "connected" : "open");
            }
        }

        var result = new List<RawPath>();
        result.AddRange(connectedLines);
        result.AddRange(otherPaths);

        _logger.LogInformation("NTS: Filtered open lines: {Before} → {After} paths (removed {Removed})",
            paths.Count, result.Count, removedCount);

        return result;
    }

    /// <summary>
    /// Duplicate internal dividing walls to create separate closed boundaries for adjacent rooms.
    /// Internal dividing walls are vertical lines that connect top and bottom horizontal lines
    /// but are not at the leftmost or rightmost edge. By duplicating them, each room can form
    /// its own closed boundary using the dividing wall.
    /// </summary>
    public List<RawPath> DuplicateDividingWalls(List<RawPath> paths, double tolerance)
    {
        if (paths.Count < 4) // Need at least 4 paths for rooms with dividing walls
            return paths;

        _logger.LogInformation("NTS: Duplicating dividing walls with tolerance {Tolerance} for {Count} paths",
            tolerance, paths.Count);

        var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
        var otherPaths = paths.Where(p => p.Points.Count != 2).ToList();

        if (lineSegments.Count < 4)
        {
            _logger.LogInformation("NTS: Not enough line segments to identify dividing walls");
            return paths;
        }

        // Classify lines as horizontal or vertical
        var horizontals = new List<RawPath>();
        var verticals = new List<RawPath>();

        foreach (var line in lineSegments)
        {
            var dx = Math.Abs(line.Points[1].X - line.Points[0].X);
            var dy = Math.Abs(line.Points[1].Y - line.Points[0].Y);

            if (dx > dy * 3) // More horizontal than vertical
                horizontals.Add(line);
            else if (dy > dx * 3) // More vertical than horizontal
                verticals.Add(line);
        }

        if (horizontals.Count < 2 || verticals.Count < 2)
        {
            _logger.LogInformation("NTS: Not enough horizontal/vertical lines to identify dividing walls");
            return paths;
        }

        // Find top and bottom horizontal lines (by Y coordinate)
        var topHorizontal = horizontals.OrderBy(h => (h.Points[0].Y + h.Points[1].Y) / 2).First();
        var bottomHorizontal = horizontals.OrderByDescending(h => (h.Points[0].Y + h.Points[1].Y) / 2).First();
        var topY = (topHorizontal.Points[0].Y + topHorizontal.Points[1].Y) / 2;
        var bottomY = (bottomHorizontal.Points[0].Y + bottomHorizontal.Points[1].Y) / 2;

        // Find leftmost and rightmost X positions
        var allXPositions = new List<double>();
        foreach (var v in verticals)
        {
            allXPositions.Add((v.Points[0].X + v.Points[1].X) / 2);
        }
        foreach (var h in horizontals)
        {
            allXPositions.Add(Math.Min(h.Points[0].X, h.Points[1].X));
            allXPositions.Add(Math.Max(h.Points[0].X, h.Points[1].X));
        }
        var minX = allXPositions.Min();
        var maxX = allXPositions.Max();

        // Identify internal dividing walls: vertical lines that:
        // 1. Connect top and bottom (Y range covers both)
        // 2. Are not at the leftmost or rightmost edge
        var dividingWalls = new List<RawPath>();
        var result = new List<RawPath>(otherPaths);

        foreach (var vertical in verticals)
        {
            var vX = (vertical.Points[0].X + vertical.Points[1].X) / 2;
            var vMinY = Math.Min(vertical.Points[0].Y, vertical.Points[1].Y);
            var vMaxY = Math.Max(vertical.Points[0].Y, vertical.Points[1].Y);

            // Check if this vertical line connects top and bottom
            bool connectsTop = Math.Abs(vMinY - topY) < tolerance * 2;
            bool connectsBottom = Math.Abs(vMaxY - bottomY) < tolerance * 2;

            // Check if it's not at the edge (with some tolerance)
            bool isNotAtEdge = (vX - minX) > tolerance && (maxX - vX) > tolerance;

            if (connectsTop && connectsBottom && isNotAtEdge)
            {
                dividingWalls.Add(vertical);
                _logger.LogInformation("NTS: Found dividing wall at X={X:F1}, Y range: {MinY:F1} to {MaxY:F1}",
                    vX, vMinY, vMaxY);
            }
            else
            {
                // Not a dividing wall, keep original
                result.Add(vertical);
            }
        }

        // Split horizontal lines at dividing walls and duplicate dividing walls
        // This creates separate line segments for each room, allowing separate closed loops
        foreach (var dividingWall in dividingWalls)
        {
            var divX = (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2;
            _logger.LogInformation("NTS: Processing dividing wall at X={X:F1}, checking {Count} horizontal lines for splitting",
                divX, horizontals.Count);
            
            // Split each horizontal line at the dividing wall
            var newHorizontals = new List<RawPath>();
            foreach (var h in horizontals)
            {
                var hMinX = Math.Min(h.Points[0].X, h.Points[1].X);
                var hMaxX = Math.Max(h.Points[0].X, h.Points[1].X);
                var hY = (h.Points[0].Y + h.Points[1].Y) / 2;
                
                _logger.LogInformation("NTS: Checking horizontal line: X range [{MinX:F1} to {MaxX:F1}], Y={Y:F1}, crosses divX={DivX:F1}? {Crosses}",
                    hMinX, hMaxX, hY, divX, hMinX < divX && hMaxX > divX);
                
                // Check if this horizontal line crosses the dividing wall
                if (hMinX < divX && hMaxX > divX)
                {
                    // Split the horizontal line at the dividing wall
                    var leftPart = new RawPath
                    {
                        Points = new List<Point2D>
                        {
                            h.Points[0].X < h.Points[1].X ? h.Points[0] : h.Points[1],
                            new Point2D { X = divX, Y = hY }
                        },
                        LineWidth = h.LineWidth,
                        IsStroked = h.IsStroked,
                        IsFilled = h.IsFilled,
                        SegmentCount = 1,
                        PathLength = divX - hMinX,
                        PathType = h.PathType,
                        PathTypeEnum = h.PathTypeEnum,
                        WallThickness = h.WallThickness,
                        ObjectIndex = h.ObjectIndex
                    };
                    var rightPart = new RawPath
                    {
                        Points = new List<Point2D>
                        {
                            new Point2D { X = divX, Y = hY },
                            h.Points[0].X > h.Points[1].X ? h.Points[0] : h.Points[1]
                        },
                        LineWidth = h.LineWidth,
                        IsStroked = h.IsStroked,
                        IsFilled = h.IsFilled,
                        SegmentCount = 1,
                        PathLength = hMaxX - divX,
                        PathType = h.PathType,
                        PathTypeEnum = h.PathTypeEnum,
                        WallThickness = h.WallThickness,
                        ObjectIndex = h.ObjectIndex
                    };
                    newHorizontals.Add(leftPart);
                    newHorizontals.Add(rightPart);
                    _logger.LogInformation("NTS: Split horizontal line at X={X:F1} into left (X={MinX:F1} to {DivX:F1}) and right (X={DivX:F1} to {MaxX:F1})",
                        divX, hMinX, divX, divX, hMaxX);
                }
                else
                {
                    // Doesn't cross dividing wall, keep as-is
                    newHorizontals.Add(h);
                }
            }
            horizontals = newHorizontals;
        }

        // Add all horizontal lines (now split at dividing walls)
        foreach (var h in horizontals)
        {
            result.Add(h);
        }

        // Duplicate each dividing wall - one copy for each room
        foreach (var dividingWall in dividingWalls)
        {
            // Add two identical copies of the dividing wall
            // Each room will use one copy as part of its boundary
            result.Add(dividingWall);
            result.Add(new RawPath
            {
                Points = new List<Point2D>(dividingWall.Points),
                LineWidth = dividingWall.LineWidth,
                IsStroked = dividingWall.IsStroked,
                IsFilled = dividingWall.IsFilled,
                SegmentCount = dividingWall.SegmentCount,
                PathLength = dividingWall.PathLength,
                PathType = dividingWall.PathType,
                PathTypeEnum = dividingWall.PathTypeEnum,
                WallThickness = dividingWall.WallThickness,
                ObjectIndex = dividingWall.ObjectIndex
            });
            _logger.LogInformation("NTS: Duplicated dividing wall at X={X:F1}",
                (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2);
        }

        _logger.LogInformation("NTS: Duplicated {Count} dividing walls: {Original} → {Result} paths",
            dividingWalls.Count, paths.Count, result.Count);

        return result;
    }

    /// <summary>
    /// Custom polygon reconstruction that explicitly handles dividing walls to create separate room boundaries.
    /// This builds polygons by following connected paths and using dividing walls as boundaries.
    /// </summary>
    private List<RoomBoundary> ReconstructPolygonsCustom(List<RawPath> paths, double tolerance)
    {
        _logger.LogInformation("NTS: Custom polygon reconstruction from {Count} paths", paths.Count);

        if (paths.Count < 3)
            return new List<RoomBoundary>();

        var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
        if (lineSegments.Count < 3)
            return new List<RoomBoundary>();

        // Build a graph of connected endpoints
        var endpointConnections = new Dictionary<string, List<(RawPath path, int pointIdx)>>();
        
        foreach (var path in lineSegments)
        {
            var p0 = path.Points[0];
            var p1 = path.Points[1];
            
            var key0 = $"{Math.Round(p0.X / tolerance) * tolerance:F1},{Math.Round(p0.Y / tolerance) * tolerance:F1}";
            var key1 = $"{Math.Round(p1.X / tolerance) * tolerance:F1},{Math.Round(p1.Y / tolerance) * tolerance:F1}";
            
            if (!endpointConnections.ContainsKey(key0))
                endpointConnections[key0] = new List<(RawPath, int)>();
            if (!endpointConnections.ContainsKey(key1))
                endpointConnections[key1] = new List<(RawPath, int)>();
            
            endpointConnections[key0].Add((path, 0));
            endpointConnections[key1].Add((path, 1));
        }

        // Classify lines and identify dividing walls
        var horizontals = new List<RawPath>();
        var verticals = new List<RawPath>();
        var dividingWalls = new List<RawPath>();

        foreach (var line in lineSegments)
        {
            var dx = Math.Abs(line.Points[1].X - line.Points[0].X);
            var dy = Math.Abs(line.Points[1].Y - line.Points[0].Y);

            if (dx > dy * 3)
                horizontals.Add(line);
            else if (dy > dx * 3)
                verticals.Add(line);
        }

        // Find dividing walls (vertical lines not at edges)
        if (horizontals.Count >= 2 && verticals.Count >= 2)
        {
            var topY = horizontals.Min(h => (h.Points[0].Y + h.Points[1].Y) / 2);
            var bottomY = horizontals.Max(h => (h.Points[0].Y + h.Points[1].Y) / 2);
            var allX = verticals.Select(v => (v.Points[0].X + v.Points[1].X) / 2).ToList();
            var minX = allX.Min();
            var maxX = allX.Max();

            foreach (var v in verticals)
            {
                var vX = (v.Points[0].X + v.Points[1].X) / 2;
                var vMinY = Math.Min(v.Points[0].Y, v.Points[1].Y);
                var vMaxY = Math.Max(v.Points[0].Y, v.Points[1].Y);
                
                bool connectsTop = Math.Abs(vMinY - topY) < tolerance * 2;
                bool connectsBottom = Math.Abs(vMaxY - bottomY) < tolerance * 2;
                bool isNotAtEdge = (vX - minX) > tolerance && (maxX - vX) > tolerance;
                
                if (connectsTop && connectsBottom && isNotAtEdge)
                {
                    dividingWalls.Add(v);
                }
            }
        }

        _logger.LogInformation("NTS: Custom reconstruction - {H} horizontal, {V} vertical, {D} dividing walls",
            horizontals.Count, verticals.Count, dividingWalls.Count);

        // Build polygons by following connected paths, treating dividing walls as boundaries
        var result = new List<RoomBoundary>();
        var usedPaths = new HashSet<RawPath>();
        
        // If we have dividing walls, build polygons on each side separately
        // Use separate usedPaths sets for left and right to avoid conflicts
        if (dividingWalls.Count > 0)
        {
            _logger.LogInformation("NTS: Building polygons on each side of {Count} dividing walls", dividingWalls.Count);
            
            // Group dividing walls by X position (they might be duplicates)
            var uniqueDividingWalls = dividingWalls
                .GroupBy(dw => Math.Round((dw.Points[0].X + dw.Points[1].X) / 2 / tolerance) * tolerance)
                .Select(g => g.First())
                .ToList();
            
            _logger.LogInformation("NTS: Processing {Count} unique dividing walls", uniqueDividingWalls.Count);
            
            foreach (var dividingWall in uniqueDividingWalls)
            {
                var divX = (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2;
                var divY1 = Math.Min(dividingWall.Points[0].Y, dividingWall.Points[1].Y);
                var divY2 = Math.Max(dividingWall.Points[0].Y, dividingWall.Points[1].Y);
                _logger.LogInformation("NTS: Processing dividing wall at X={X:F1}, Y range: {Y1:F1} to {Y2:F1}",
                    divX, divY1, divY2);
                
                // Use separate usedPaths for left and right to allow both to be built
                var leftUsedPaths = new HashSet<RawPath>();
                var rightUsedPaths = new HashSet<RawPath>();
                
                // Build polygon on left side of dividing wall
                var leftPolygon = BuildPolygonOnSideOfDividingWall(
                    dividingWall, lineSegments, endpointConnections, leftUsedPaths, tolerance, dividingWalls, isLeftSide: true);
                if (leftPolygon != null && leftPolygon.Count >= 3)
                {
                    // Ensure closed
                    var first = leftPolygon[0];
                    var last = leftPolygon[leftPolygon.Count - 1];
                    if (Math.Abs(first.X - last.X) > 0.01 || Math.Abs(first.Y - last.Y) > 0.01)
                    {
                        leftPolygon.Add(new Point2D { X = first.X, Y = first.Y });
                    }
                    result.Add(new RoomBoundary { Polygon = leftPolygon });
                    _logger.LogInformation("NTS: Built left polygon with {Count} points", leftPolygon.Count);
                    usedPaths.UnionWith(leftUsedPaths);
                }
                else
                {
                    _logger.LogWarning("NTS: Failed to build left polygon for dividing wall at X={X:F1}", divX);
                }
                
                // Build polygon on right side of dividing wall
                var rightPolygon = BuildPolygonOnSideOfDividingWall(
                    dividingWall, lineSegments, endpointConnections, rightUsedPaths, tolerance, dividingWalls, isLeftSide: false);
                if (rightPolygon != null && rightPolygon.Count >= 3)
                {
                    // Ensure closed
                    var first = rightPolygon[0];
                    var last = rightPolygon[rightPolygon.Count - 1];
                    if (Math.Abs(first.X - last.X) > 0.01 || Math.Abs(first.Y - last.Y) > 0.01)
                    {
                        rightPolygon.Add(new Point2D { X = first.X, Y = first.Y });
                    }
                    result.Add(new RoomBoundary { Polygon = rightPolygon });
                    _logger.LogInformation("NTS: Built right polygon with {Count} points", rightPolygon.Count);
                    usedPaths.UnionWith(rightUsedPaths);
                }
                else
                {
                    _logger.LogWarning("NTS: Failed to build right polygon for dividing wall at X={X:F1}", divX);
                }
            }
        }
        
        // Also try building polygons from remaining unused paths
        foreach (var startPath in lineSegments)
        {
            if (usedPaths.Contains(startPath))
                continue;

            var polygon = BuildPolygonFromPath(startPath, endpointConnections, usedPaths, tolerance, dividingWalls);
            if (polygon != null && polygon.Count >= 3)
            {
                // Check if polygon is closed
                var first = polygon[0];
                var last = polygon[polygon.Count - 1];
                var dist = Math.Sqrt(Math.Pow(first.X - last.X, 2) + Math.Pow(first.Y - last.Y, 2));
                
                if (dist < tolerance)
                {
                    // Ensure closed
                    if (polygon.Count > 0 && 
                        (Math.Abs(polygon[0].X - polygon[polygon.Count - 1].X) > 0.01 || 
                         Math.Abs(polygon[0].Y - polygon[polygon.Count - 1].Y) > 0.01))
                    {
                        polygon.Add(new Point2D { X = polygon[0].X, Y = polygon[0].Y });
                    }
                    
                    result.Add(new RoomBoundary { Polygon = polygon });
                    _logger.LogInformation("NTS: Built custom polygon with {Count} points", polygon.Count);
                }
            }
        }

        _logger.LogInformation("NTS: Custom reconstruction found {Count} polygons", result.Count);
        return result;
    }

    private List<Point2D>? BuildPolygonFromPath(RawPath startPath, 
        Dictionary<string, List<(RawPath path, int pointIdx)>> endpointConnections,
        HashSet<RawPath> usedPaths, double tolerance, List<RawPath> dividingWalls)
    {
        var polygon = new List<Point2D>();
        var currentPath = startPath;
        var currentPoint = currentPath.Points[1]; // Start from end of first path
        polygon.Add(currentPath.Points[0]);
        polygon.Add(currentPath.Points[1]);
        usedPaths.Add(currentPath);

        int maxIterations = 100;
        int iterations = 0;

        while (iterations < maxIterations)
        {
            iterations++;
            
            // Find next connected path
            var key = $"{Math.Round(currentPoint.X / tolerance) * tolerance:F1},{Math.Round(currentPoint.Y / tolerance) * tolerance:F1}";
            
            if (!endpointConnections.ContainsKey(key))
                break;

            RawPath? nextPath = null;
            foreach (var (path, pointIdx) in endpointConnections[key])
            {
                if (usedPaths.Contains(path))
                    continue;
                
                // Prefer non-dividing walls, but allow dividing walls if no other option
                if (dividingWalls.Contains(path))
                {
                    // Only use dividing wall if we've exhausted other options
                    if (nextPath == null)
                        nextPath = path;
                    continue;
                }
                
                nextPath = path;
                break;
            }

            if (nextPath == null)
                break;

            // Add next path to polygon
            var p0 = nextPath.Points[0];
            var p1 = nextPath.Points[1];
            var otherPoint = (Math.Abs(p0.X - currentPoint.X) < tolerance && 
                             Math.Abs(p0.Y - currentPoint.Y) < tolerance)
                ? p1 
                : p0;
            
            polygon.Add(otherPoint);
            usedPaths.Add(nextPath);
            currentPath = nextPath;
            currentPoint = otherPoint;

            // Check if we've closed the loop
            var first = polygon[0];
            if (Math.Abs(currentPoint.X - first.X) < tolerance && 
                Math.Abs(currentPoint.Y - first.Y) < tolerance)
            {
                return polygon;
            }
        }

        return null;
    }

    /// <summary>
    /// Build a polygon on one side of a dividing wall by following connected paths
    /// </summary>
    private List<Point2D>? BuildPolygonOnSideOfDividingWall(
        RawPath dividingWall,
        List<RawPath> allPaths,
        Dictionary<string, List<(RawPath path, int pointIdx)>> endpointConnections,
        HashSet<RawPath> usedPaths,
        double tolerance,
        List<RawPath> allDividingWalls,
        bool isLeftSide)
    {
        var divX = (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2;
        var polygon = new List<Point2D>();
        var localUsedPaths = new HashSet<RawPath>();
        
        // Start from the dividing wall
        var currentPath = dividingWall;
        var currentPoint = currentPath.Points[1];
        polygon.Add(currentPath.Points[0]);
        polygon.Add(currentPath.Points[1]);
        localUsedPaths.Add(currentPath);

        int maxIterations = 100;
        int iterations = 0;

        while (iterations < maxIterations)
        {
            iterations++;
            
            // Find next connected path
            var key = $"{Math.Round(currentPoint.X / tolerance) * tolerance:F1},{Math.Round(currentPoint.Y / tolerance) * tolerance:F1}";
            
            if (!endpointConnections.ContainsKey(key))
                break;

            RawPath? nextPath = null;
            foreach (var (path, pointIdx) in endpointConnections[key])
            {
                if (localUsedPaths.Contains(path) || usedPaths.Contains(path))
                    continue;
                
                // Skip other dividing walls (we only want to use this one)
                if (path != dividingWall && allDividingWalls.Contains(path))
                    continue;
                
                // For left side, prefer paths to the left of dividing wall
                // For right side, prefer paths to the right of dividing wall
                var pathX = (path.Points[0].X + path.Points[1].X) / 2;
                if (isLeftSide && pathX > divX + tolerance)
                    continue; // Skip paths on right side
                if (!isLeftSide && pathX < divX - tolerance)
                    continue; // Skip paths on left side
                
                nextPath = path;
                break;
            }

            if (nextPath == null)
                break;

            // Add next path to polygon
            var p0 = nextPath.Points[0];
            var p1 = nextPath.Points[1];
            var otherPoint = (Math.Abs(p0.X - currentPoint.X) < tolerance && 
                             Math.Abs(p0.Y - currentPoint.Y) < tolerance)
                ? p1 
                : p0;
            
            polygon.Add(otherPoint);
            localUsedPaths.Add(nextPath);
            currentPath = nextPath;
            currentPoint = otherPoint;

            // Check if we've closed the loop (back to dividing wall)
            var first = polygon[0];
            if (Math.Abs(currentPoint.X - first.X) < tolerance && 
                Math.Abs(currentPoint.Y - first.Y) < tolerance)
            {
                // Mark all used paths as used
                foreach (var p in localUsedPaths)
                {
                    usedPaths.Add(p);
                }
                return polygon;
            }
        }

        return null;
    }

    /// <summary>
    /// Split a large polygon into multiple polygons using dividing walls.
    /// This is used when NTS finds one large polygon that should be split into separate rooms.
    /// </summary>
    /// <summary>
    /// Split a polygon using NTS by creating a cutting line from the dividing wall and using difference operation
    /// </summary>
    private List<RoomBoundary> SplitPolygonUsingDividingWalls(RoomBoundary largePolygon, List<RawPath> paths, double tolerance)
    {
        _logger.LogInformation("NTS: Attempting to split polygon with {PointCount} points using NTS operations", largePolygon.Polygon.Count);
        
        try
        {
            // Convert polygon to NTS
            var coords = largePolygon.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToList();
            if (!coords[0].Equals2D(coords[^1]))
            {
                coords.Add(new Coordinate(coords[0].X, coords[0].Y));
            }
            var ring = _geometryFactory.CreateLinearRing(coords.ToArray());
            var polygon = _geometryFactory.CreatePolygon(ring);
            
            if (!polygon.IsValid)
            {
                _logger.LogWarning("NTS: Invalid polygon, cannot split");
                return new List<RoomBoundary> { largePolygon };
            }

            // Find dividing walls
            var lineSegments = paths.Where(p => p.Points.Count == 2).ToList();
            var horizontals = new List<RawPath>();
            var verticals = new List<RawPath>();

            foreach (var line in lineSegments)
            {
                var dx = Math.Abs(line.Points[1].X - line.Points[0].X);
                var dy = Math.Abs(line.Points[1].Y - line.Points[0].Y);

                if (dx > dy * 3)
                    horizontals.Add(line);
                else if (dy > dx * 3)
                    verticals.Add(line);
            }

            if (horizontals.Count < 2 || verticals.Count < 2)
                return new List<RoomBoundary> { largePolygon };

            var topY = horizontals.Min(h => (h.Points[0].Y + h.Points[1].Y) / 2);
            var bottomY = horizontals.Max(h => (h.Points[0].Y + h.Points[1].Y) / 2);
            var allX = verticals.Select(v => (v.Points[0].X + v.Points[1].X) / 2).ToList();
            var minX = allX.Min();
            var maxX = allX.Max();

            var dividingWalls = new List<RawPath>();
            foreach (var v in verticals)
            {
                var vX = (v.Points[0].X + v.Points[1].X) / 2;
                var vMinY = Math.Min(v.Points[0].Y, v.Points[1].Y);
                var vMaxY = Math.Max(v.Points[0].Y, v.Points[1].Y);
                
                bool connectsTop = Math.Abs(vMinY - topY) < tolerance * 2;
                bool connectsBottom = Math.Abs(vMaxY - bottomY) < tolerance * 2;
                bool isNotAtEdge = (vX - minX) > tolerance && (maxX - vX) > tolerance;
                
                if (connectsTop && connectsBottom && isNotAtEdge)
                {
                    dividingWalls.Add(v);
                }
            }

            if (dividingWalls.Count == 0)
            {
                _logger.LogInformation("NTS: No dividing walls found for splitting");
                return new List<RoomBoundary> { largePolygon };
            }

            _logger.LogInformation("NTS: Found {Count} dividing walls, attempting to split polygon", dividingWalls.Count);

            // For each dividing wall, create a cutting polygon and use it to split
            var result = new List<RoomBoundary>();
            
            foreach (var dividingWall in dividingWalls)
            {
                var divX = (dividingWall.Points[0].X + dividingWall.Points[1].X) / 2;
                var divY1 = Math.Min(dividingWall.Points[0].Y, dividingWall.Points[1].Y);
                var divY2 = Math.Max(dividingWall.Points[0].Y, dividingWall.Points[1].Y);
                
                // Extend the dividing wall line to create a cutting plane
                // Create a very thin rectangle along the dividing wall to use as a cutter
                var envelope = polygon.EnvelopeInternal;
                var cutterWidth = tolerance * 2;
                
                // Create left and right polygons by clipping the original polygon
                var leftCoords = new List<Coordinate>();
                var rightCoords = new List<Coordinate>();
                
                // Split the polygon's coordinates at the dividing wall
                foreach (var coord in polygon.ExteriorRing.Coordinates)
                {
                    if (coord.X < divX)
                    {
                        leftCoords.Add(coord);
                    }
                    else
                    {
                        rightCoords.Add(coord);
                    }
                }
                
                // Add intersection points with dividing wall
                // Find where polygon edges cross the dividing wall
                var prevCoord = polygon.ExteriorRing.Coordinates[0];
                foreach (var coord in polygon.ExteriorRing.Coordinates.Skip(1))
                {
                    // Check if edge crosses dividing wall
                    if ((prevCoord.X < divX && coord.X > divX) || (prevCoord.X > divX && coord.X < divX))
                    {
                        // Calculate intersection
                        var t = (divX - prevCoord.X) / (coord.X - prevCoord.X);
                        var intersectY = prevCoord.Y + t * (coord.Y - prevCoord.Y);
                        var intersect = new Coordinate(divX, intersectY);
                        
                        if (intersectY >= divY1 - tolerance && intersectY <= divY2 + tolerance)
                        {
                            leftCoords.Add(intersect);
                            rightCoords.Add(intersect);
                        }
                    }
                    prevCoord = coord;
                }
                
                // Try to form valid polygons from the split coordinates
                if (leftCoords.Count >= 3)
                {
                    // Ensure closed and try to create polygon
                    if (!leftCoords[0].Equals2D(leftCoords[^1]))
                        leftCoords.Add(new Coordinate(leftCoords[0].X, leftCoords[0].Y));
                    
                    try
                    {
                        var leftRing = _geometryFactory.CreateLinearRing(leftCoords.ToArray());
                        if (leftRing.IsValid)
                        {
                            var leftPoly = _geometryFactory.CreatePolygon(leftRing);
                            if (leftPoly.IsValid && leftPoly.Area > 0)
                            {
                                result.Add(ToRoomBoundary(leftPoly));
                                _logger.LogInformation("NTS: Created left polygon with area {Area:F2}", leftPoly.Area);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("NTS: Failed to create left polygon: {Message}", ex.Message);
                    }
                }
                
                if (rightCoords.Count >= 3)
                {
                    if (!rightCoords[0].Equals2D(rightCoords[^1]))
                        rightCoords.Add(new Coordinate(rightCoords[0].X, rightCoords[0].Y));
                    
                    try
                    {
                        var rightRing = _geometryFactory.CreateLinearRing(rightCoords.ToArray());
                        if (rightRing.IsValid)
                        {
                            var rightPoly = _geometryFactory.CreatePolygon(rightRing);
                            if (rightPoly.IsValid && rightPoly.Area > 0)
                            {
                                result.Add(ToRoomBoundary(rightPoly));
                                _logger.LogInformation("NTS: Created right polygon with area {Area:F2}", rightPoly.Area);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("NTS: Failed to create right polygon: {Message}", ex.Message);
                    }
                }
            }

            if (result.Count > 1)
            {
                _logger.LogInformation("NTS: Successfully split polygon into {Count} polygons", result.Count);
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Failed to split polygon using dividing walls");
        }

        return new List<RoomBoundary> { largePolygon };
    }

    /// <summary>
    /// Calculate the wall thickness to use for polygon insetting.
    /// Returns the minimum wall thickness from all centerline paths, or 0 if no centerlines found.
    /// </summary>
    public double CalculateWallThicknessForInset(List<RawPath> paths)
    {
        var centerlineThicknesses = paths
            .Where(p => p.PathTypeEnum == PathTypeEnum.Centerline && p.WallThickness > 0)
            .Select(p => p.WallThickness)
            .ToList();

        if (centerlineThicknesses.Count == 0)
        {
            _logger.LogDebug("NTS: No centerline paths with wall thickness found");
            return 0;
        }

        // Use minimum thickness (most conservative - won't over-inset thin walls)
        double minThickness = centerlineThicknesses.Min();
        double maxThickness = centerlineThicknesses.Max();
        double avgThickness = centerlineThicknesses.Average();

        _logger.LogInformation("NTS: Wall thickness from centerlines - Min: {Min:F2}, Max: {Max:F2}, Avg: {Avg:F2}, using Min",
            minThickness, maxThickness, avgThickness);

        return minThickness;
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
            PathTypeEnum = original.PathTypeEnum,
            WallThickness = original.WallThickness,
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
                PathTypeEnum = p.PathTypeEnum,
                WallThickness = p.WallThickness,
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
            PathTypeEnum = PathTypeEnum.Merged, // Mark as merged
            WallThickness = Math.Max(path1.WallThickness, path2.WallThickness), // Preserve wall thickness if present
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
                    PathLength = lineString.Length,
                    PathTypeEnum = PathTypeEnum.Original
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

        // Filter out zero-length and degenerate paths that can't form polygons
        var validPaths = paths.Where(p => 
        {
            if (p.Points.Count < 2)
                return false;
            
            // Check if path has non-zero length
            if (p.Points.Count >= 2)
            {
                // For 2-point paths, check distance between endpoints
                if (p.Points.Count == 2)
                {
                    var p1 = p.Points[0];
                    var p2 = p.Points[1];
                    var dist = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
                    if (dist < 0.1) // Very short paths (less than 0.1 units)
                    {
                        _logger.LogInformation("NTS: Filtering out near-zero-length path: ({X1:F1},{Y1:F1}) -> ({X2:F1},{Y2:F1}), length={Length:F2}",
                            p1.X, p1.Y, p2.X, p2.Y, dist);
                        return false;
                    }
                }
                else
                {
                    // For multi-point paths, check total length
                    double totalLength = 0;
                    for (int i = 0; i < p.Points.Count - 1; i++)
                    {
                        var p1 = p.Points[i];
                        var p2 = p.Points[i + 1];
                        totalLength += Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
                    }
                    if (totalLength < 0.1)
                    {
                        _logger.LogInformation("NTS: Filtering out near-zero-length multi-point path with {Count} points, total length={Length:F2}",
                            p.Points.Count, totalLength);
                        return false;
                    }
                }
            }
            
            return true;
        }).ToList();

        if (validPaths.Count != paths.Count)
        {
            _logger.LogInformation("NTS: Filtered {Removed} degenerate paths, {Remaining} valid paths remain",
                paths.Count - validPaths.Count, validPaths.Count);
        }

        try
        {
            var result = new List<RoomBoundary>();
            
            // First, try to create polygons directly from closed paths
            // This avoids the noding issue where shared walls create many small faces
            foreach (var path in validPaths)
            {
                if (path.Points.Count < 4) continue; // Need at least 4 points for a polygon
                
                var coords = path.Points.Select(p => new Coordinate(p.X, p.Y)).ToList();
                
                // Check if path is closed (start == end within tolerance)
                var first = coords[0];
                var last = coords[^1];
                var distance = Math.Sqrt(Math.Pow(first.X - last.X, 2) + Math.Pow(first.Y - last.Y, 2));
                
                // Only log closed paths to reduce noise
                if (distance < 1.0)
                {
                    _logger.LogInformation("NTS: Found closed path with {Points} points, distance from start to end: {Distance:F2}", coords.Count, distance);
                }
                
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
            
            // Log what we found from closed paths
            if (result.Count > 0)
            {
                _logger.LogInformation("NTS: Found {Count} polygons directly from closed paths", result.Count);
                foreach (var boundary in result)
                {
                    var area = GetArea(boundary);
                    _logger.LogInformation("NTS:   - Polygon area: {Area:F2}, points: {PointCount}", area, boundary.Polygon.Count);
                }
            }
            
            // Always run polygonizer with noding to find all polygons
            // This is important for adjacent rooms that share walls - the polygonizer can find
            // separate polygons even when closed paths exist
            _logger.LogInformation("NTS: Running polygonizer with noding to find all polygons");
            
            // Convert valid paths to NTS LineStrings (use filtered paths)
            var lineStrings = validPaths.Select(ToLineString).ToList();

            // Log path information for debugging
            int totalPoints = lineStrings.Sum(ls => ls.Coordinates.Length);
            _logger.LogInformation("NTS: Total points in all paths: {PointCount}", totalPoints);
            _logger.LogInformation("NTS: Path count: {PathCount}, Path lengths: {Lengths}", 
                lineStrings.Count, string.Join(", ", lineStrings.Select(ls => ls.Length.ToString("F2"))));
            
            // Log each path's endpoints for debugging
            for (int i = 0; i < Math.Min(lineStrings.Count, 10); i++)
            {
                var ls = lineStrings[i];
                if (ls.Coordinates.Length >= 2)
                {
                    var start = ls.Coordinates[0];
                    var end = ls.Coordinates[ls.Coordinates.Length - 1];
                    _logger.LogInformation("NTS: Path {Index}: start=({X1:F1},{Y1:F1}), end=({X2:F1},{Y2:F1}), length={Length:F2}",
                        i, start.X, start.Y, end.X, end.Y, ls.Length);
                }
            }

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
            var polygonizerResults = polygons
                .Cast<Polygon>()
                .Select(ToRoomBoundary)
                .ToList();

            // Log polygonizer results in detail
            _logger.LogInformation("NTS: Polygonizer found {Count} polygons", polygonizerResults.Count);
            foreach (var boundary in polygonizerResults)
            {
                var area = GetArea(boundary);
                _logger.LogInformation("NTS:   - Polygonizer polygon area: {Area:F2}, points: {PointCount}", area, boundary.Polygon.Count);
            }

            // Combine results from both methods (closed paths and polygonizer)
            // Use polygonizer results if they found more polygons (better separation)
            // Otherwise, use the direct closed path results
            if (polygonizerResults.Count > result.Count)
            {
                _logger.LogInformation("NTS: Polygonizer found {PolygonizerCount} polygons vs {DirectCount} from closed paths - using polygonizer results",
                    polygonizerResults.Count, result.Count);
                result = polygonizerResults;
            }
            else if (result.Count > 0 && polygonizerResults.Count > 0)
            {
                _logger.LogInformation("NTS: Using {DirectCount} polygons from closed paths (polygonizer found {PolygonizerCount})",
                    result.Count, polygonizerResults.Count);
                // Still prefer polygonizer if it found the same number - it might have better separation
                if (polygonizerResults.Count == result.Count)
                {
                    _logger.LogInformation("NTS: Both methods found same count, using polygonizer results for better separation");
                    result = polygonizerResults;
                }
            }
            else if (polygonizerResults.Count > 0)
            {
                _logger.LogInformation("NTS: Using {PolygonizerCount} polygons from polygonizer", polygonizerResults.Count);
                result = polygonizerResults;
            }

            // Log final polygon details
            _logger.LogInformation("NTS: Final reconstructed {Count} polygons", result.Count);
            foreach (var boundary in result)
            {
                var area = GetArea(boundary);
                _logger.LogInformation("NTS:   - Final polygon area: {Area:F2}, points: {PointCount}", area, boundary.Polygon.Count);
            }

            // If NTS polygonizer only found 1 polygon but we have dividing walls, try splitting it
            _logger.LogInformation("NTS: Checking fallback - result.Count={ResultCount}, validPaths.Count={ValidCount}",
                result.Count, validPaths.Count);
            if (result.Count == 1 && validPaths.Count >= 4)
            {
                _logger.LogInformation("NTS: Only 1 polygon found, trying to split using dividing walls");
                try
                {
                    // Try to split the single polygon using dividing walls
                    var splitResult = SplitPolygonUsingDividingWalls(result[0], validPaths, tolerance: 1.0);
                    if (splitResult.Count > result.Count)
                    {
                        _logger.LogInformation("NTS: Split polygon into {Count} polygons vs {OriginalCount} from NTS - using split result",
                            splitResult.Count, result.Count);
                        return splitResult;
                    }
                    
                    // If splitting didn't work, try custom reconstruction
                    _logger.LogInformation("NTS: Splitting didn't work, trying custom reconstruction with dividing wall handling");
                    var customResult = ReconstructPolygonsCustom(validPaths, tolerance: 1.0);
                    _logger.LogInformation("NTS: Custom reconstruction returned {Count} polygons", customResult.Count);
                    if (customResult.Count > result.Count)
                    {
                        _logger.LogInformation("NTS: Custom reconstruction found {Count} polygons vs {NtsCount} from NTS - using custom",
                            customResult.Count, result.Count);
                        return customResult;
                    }
                    else if (customResult.Count > 0)
                    {
                        _logger.LogInformation("NTS: Custom reconstruction found same or fewer polygons ({Count}), keeping NTS result",
                            customResult.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NTS: Custom reconstruction failed, using NTS result");
                }
            }
            else
            {
                _logger.LogInformation("NTS: Fallback condition not met - result.Count={ResultCount}, validPaths.Count={ValidCount}",
                    result.Count, validPaths.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NTS: Polygon reconstruction failed");
            return new List<RoomBoundary>();
        }
    }

    /// <summary>
    /// Inset (shrink) polygons by half the wall thickness to get inner boundaries.
    /// This is used after polygon reconstruction to convert centerline-based polygons
    /// into proper inner boundary polygons.
    /// </summary>
    public List<RoomBoundary> InsetPolygons(List<RoomBoundary> boundaries, double wallThickness)
    {
        if (wallThickness <= 0 || boundaries.Count == 0)
            return boundaries;

        double insetAmount = wallThickness / 2.0;
        _logger.LogInformation("NTS: Insetting {Count} polygons by {Inset:F2} (half of wall thickness {Thickness:F2})",
            boundaries.Count, insetAmount, wallThickness);

        var result = new List<RoomBoundary>();

        foreach (var boundary in boundaries)
        {
            try
            {
                // Convert to NTS Polygon
                var coords = boundary.Polygon.Select(p => new Coordinate(p.X, p.Y)).ToList();

                // Ensure closed ring
                if (coords.Count > 0 && !coords[0].Equals2D(coords[^1]))
                {
                    coords.Add(new Coordinate(coords[0].X, coords[0].Y));
                }

                if (coords.Count < 4)
                {
                    _logger.LogDebug("NTS: Skipping polygon with too few points: {Count}", coords.Count);
                    continue;
                }

                var ring = _geometryFactory.CreateLinearRing(coords.ToArray());
                var polygon = _geometryFactory.CreatePolygon(ring);

                if (!polygon.IsValid)
                {
                    _logger.LogDebug("NTS: Skipping invalid polygon before inset");
                    continue;
                }

                // Use negative buffer to shrink the polygon
                var insetGeometry = polygon.Buffer(-insetAmount);

                if (insetGeometry.IsEmpty)
                {
                    _logger.LogDebug("NTS: Polygon became empty after inset (was too thin), original area: {Area:F2}", polygon.Area);
                    continue;
                }

                // Handle both Polygon and MultiPolygon results (buffer can split a polygon)
                if (insetGeometry is Polygon insetPolygon)
                {
                    if (insetPolygon.IsValid && insetPolygon.Area > 0)
                    {
                        result.Add(ToRoomBoundary(insetPolygon));
                        _logger.LogDebug("NTS: Inset polygon from area {Original:F2} to {Inset:F2}",
                            polygon.Area, insetPolygon.Area);
                    }
                }
                else if (insetGeometry is MultiPolygon multiPolygon)
                {
                    // Buffer can sometimes create multiple polygons (e.g., if polygon was very thin in the middle)
                    for (int i = 0; i < multiPolygon.NumGeometries; i++)
                    {
                        if (multiPolygon.GetGeometryN(i) is Polygon p && p.IsValid && p.Area > 0)
                        {
                            result.Add(ToRoomBoundary(p));
                            _logger.LogDebug("NTS: Inset multipolygon part {Index}, area: {Area:F2}", i, p.Area);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NTS: Failed to inset polygon");
            }
        }

        _logger.LogInformation("NTS: Inset complete: {Original} polygons -> {Result} polygons",
            boundaries.Count, result.Count);

        return result;
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

